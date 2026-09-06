using ATL;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Utils;

namespace WinUIMusicPlayer.Services.Plugins
{
    /// <summary>
    /// 在线歌曲下载服务(参考 BakaMusic / 弦予音乐):
    /// 插件解析直链 → 下载音频 → ATL 内嵌元数据(标题/艺术家/专辑/封面/歌词, 始终内嵌)
    /// → 按需额外保存独立 LRC 歌词文件与独立封面图片。
    /// </summary>
    public static class OnlineDownloadService
    {
        /// <summary>下载音质档位(与 LX 生态键一致)。</summary>
        public static readonly string[] Qualities = ["128k", "320k", "flac", "flac24bit"];

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(180) };

        /// <summary>默认下载目录: 用户"音乐"库下的 XY Music 子目录。</summary>
        public static string DefaultDownloadDir
        {
            get
            {
                var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                if (string.IsNullOrEmpty(music)) music = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music");
                return Path.Combine(music, "XY Music");
            }
        }

        /// <summary>解析当前播放歌曲对应的 OnlineSong(Path 或 OnlineVirtualPath 任一命中)。</summary>
        public static OnlineSong? ResolveOnlineSong(Model.Music music)
        {
            if (OnlineMusicRegistry.TryGet(music.Path, out var s)) return s;
            if (!string.IsNullOrEmpty(music.OnlineVirtualPath) && OnlineMusicRegistry.TryGet(music.OnlineVirtualPath, out var s2)) return s2;
            return null;
        }

        private static readonly string[] s_audioExts = [".mp3", ".flac", ".m4a", ".ogg", ".wav", ".ape", ".wma"];

        /// <summary>
        /// 按下载命名规则("歌手 - 歌名.ext")在上次下载目录中查找该歌曲已下载的文件。
        /// 与 DownloadDialog 的默认目录逻辑一致(持久化设置优先, 兜默认目录)。
        /// </summary>
        public static string? FindExistingDownload(OnlineSong song)
        {
            try
            {
                var dir = !string.IsNullOrEmpty(AppSettings.DownloadPath) && Directory.Exists(AppSettings.DownloadPath)
                    ? AppSettings.DownloadPath
                    : DefaultDownloadDir;
                if (!Directory.Exists(dir)) return null;

                var name = $"{song.Artist} - {song.Title}".Trim(' ', '-');
                if (name.Length == 0) name = song.Title.Length > 0 ? song.Title : "unknown";
                name = SanitizeFileName(name);
                if (name.Length > 150) name = name[..150];
                if (name.Length == 0) return null;

                foreach (var ext in s_audioExts)
                {
                    var file = Path.Combine(dir, name + ext);
                    if (File.Exists(file)) return file;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 下载在线歌曲到指定目录。返回 (成功, 保存路径/错误信息, 额外保存说明)。
        /// 不管 saveLrc/saveCover 是否勾选, 歌词与封面始终内嵌到歌曲文件。
        /// </summary>
        public static async Task<(bool Ok, string Message, bool LrcSaved, bool CoverSaved)> DownloadAsync(
            OnlineSong song, string quality, string targetDir, bool saveLrc, bool saveCover,
            Action<string>? onStatus = null, Action<double>? onProgress = null)
        {
            var pluginManager = App.Services.GetRequiredService<PluginManagerService>();
            try
            {
                Directory.CreateDirectory(targetDir);

                // 1. 解析音源直链
                onStatus?.Invoke(ToolUtils.GetString("DownloadStatusResolving"));
                var (source, error) = await pluginManager.GetMediaSourceAsync(song, quality);
                if (source is null) return (false, error ?? "解析音源失败", false, false);
                var url = source.Url;

                // 2. 下载音频(带进度, B 站等 CDN 需带插件返回的 Referer/Cookie)
                onStatus?.Invoke(ToolUtils.GetString("DownloadStatusDownloading"));
                var (bytes, audioError) = await DownloadBytesAsync(url, source.Headers, onProgress);
                if (bytes is null) return (false, audioError ?? "下载音频失败", false, false);

                // 3. 并行获取封面与歌词(失败不阻断下载)
                onStatus?.Invoke(ToolUtils.GetString("DownloadStatusFetchingExtras"));
                var coverTask = DownloadCoverBytesAsync(song);
                var lyricTask = pluginManager.GetLyricAsync(song);
                await Task.WhenAll(coverTask, lyricTask);
                var coverBytes = await coverTask;
                var (lrc, translation, _) = await lyricTask;
                var combinedLrc = CombineLrc(lrc, translation);

                // 4. 生成目标文件路径(去重)
                var file = BuildTargetPath(targetDir, song, url, quality, bytes);
                await File.WriteAllBytesAsync(file, bytes);

                // 5. 内嵌元数据: 标题/艺术家/专辑 + 封面 + 歌词(始终执行)
                onStatus?.Invoke(ToolUtils.GetString("DownloadStatusEmbedding"));
                EmbedMetadata(file, song, coverBytes, combinedLrc);

                // 6. 独立歌词 LRC 文件(可选)
                var lrcSaved = false;
                if (saveLrc && !string.IsNullOrWhiteSpace(combinedLrc))
                {
                    var lrcFile = Path.ChangeExtension(file, ".lrc");
                    await File.WriteAllTextAsync(lrcFile, combinedLrc);
                    lrcSaved = true;
                }

                // 7. 独立封面文件(可选)
                var coverSaved = false;
                if (saveCover && coverBytes is { Length: > 0 })
                {
                    var coverFile = Path.ChangeExtension(file, GetCoverExt(song.Artwork, coverBytes));
                    await File.WriteAllBytesAsync(coverFile, coverBytes);
                    coverSaved = true;
                }

                return (true, file, lrcSaved, coverSaved);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, false, false);
            }
        }

        /// <summary>合并原文与翻译歌词: 时间戳可对齐时逐行合并为双语 LRC, 否则翻译空一行后追加。</summary>
        private static string CombineLrc(string? lrc, string? translation)
        {
            var main = (lrc ?? string.Empty).Trim();
            var trans = (translation ?? string.Empty).Trim();
            if (main.Length == 0) return trans;
            if (trans.Length == 0) return main;
            return MergeLrc(main, trans);
        }

        /// <summary>双语 LRC 合并: 翻译行紧跟时间最近(±2s 内)的原文行, 头部标签仅保留原文侧。</summary>
        private static string MergeLrc(string main, string trans)
        {
            static List<(double Time, string Line)> ParseTimestamped(string text)
            {
                var result = new List<(double, string)>();
                foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"^\[(\d{1,2}):(\d{1,2})(?:[.:](\d{1,3}))?\]");
                    if (!m.Success) continue;
                    var time = int.Parse(m.Groups[1].Value) * 60 + (double)int.Parse(m.Groups[2].Value);
                    if (m.Groups[3].Success)
                        time += double.Parse("0." + m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                    result.Add((time, line));
                }
                return result;
            }

            var mainLines = ParseTimestamped(main);
            var transLines = ParseTimestamped(trans);
            // 任一侧无法解析时间轴(或翻译行数明显对不上)时退回拼接
            if (mainLines.Count == 0 || transLines.Count == 0)
                return main + "\r\n\r\n" + trans;

            // 保留原文侧头部标签([ti:] 等, 无时间戳的行)
            var header = string.Join("\r\n", main.Replace("\r\n", "\n").Split('\n')
                .Where(l => l.Trim().Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(l, @"^\[\d{1,2}:\d{1,2}")));
            var sb = new System.Text.StringBuilder();
            if (header.Length > 0) sb.AppendLine(header);

            int ti = 0;
            foreach (var (time, line) in mainLines)
            {
                sb.AppendLine(line);
                // 找未消费翻译中与该行时间最近的行
                int best = -1;
                double bestDiff = double.MaxValue;
                for (int i = ti; i < transLines.Count; i++)
                {
                    var diff = Math.Abs(transLines[i].Time - time);
                    if (diff < bestDiff) { bestDiff = diff; best = i; }
                    if (transLines[i].Time > time + 2) break;
                }
                if (best >= 0 && bestDiff <= 2.0)
                {
                    // 跳过 QQ 占位空行(文本为 // 或空)
                    var transText = transLines[best].Line.Substring(transLines[best].Line.IndexOf(']') + 1).Trim();
                    if (transText.Length > 0 && transText != "//")
                        sb.AppendLine(transLines[best].Line);
                    ti = best + 1;
                }
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>下载字节流, 支持按 Content-Length 汇报进度。跟随降级重定向(网易云 outer/url 302 到 http CDN)。</summary>
        private static async Task<(byte[]? Bytes, string? Error)> DownloadBytesAsync(string url, Dictionary<string, string>? headers, Action<double>? onProgress)
        {
            try
            {
                using var response = await OnlinePlaybackResolver.SendFollowingDowngradeRedirectsAsync(
                    _http, url, headers, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                // 网易云 VIP/版权受限歌曲会重定向到 404 版权页(HTML), 不能当音频下载
                var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (mediaType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                    return (null, "该歌曲暂无可用音源(可能为 VIP 或版权受限歌曲)");
                long? total = response.Content.Headers.ContentLength;
                await using var stream = await response.Content.ReadAsStreamAsync();
                using var ms = new MemoryStream();
                var buffer = new byte[81920];
                int read;
                long copied = 0;
                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    ms.Write(buffer, 0, read);
                    copied += read;
                    if (total is > 0) onProgress?.Invoke(Math.Min(100.0, copied * 100.0 / total.Value));
                }
                var bytes = ms.ToArray();
                if (bytes.Length == 0) return (null, "音源内容为空");
                // 部分音源代理(如酷狗 kg.php)故障时返回 HTTP 200 + JSON 错误体, 下载会得到无法播放的文件
                if (OnlinePlaybackResolver.DescribeNonAudioBody(bytes) is { } bodyError) return (null, bodyError);
                onProgress?.Invoke(100);
                return (bytes, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        /// <summary>下载封面图片字节(无封面或失败返回 null, 不阻断下载)。</summary>
        private static async Task<byte[]?> DownloadCoverBytesAsync(OnlineSong song)
        {
            if (string.IsNullOrWhiteSpace(song.Artwork) || !song.Artwork.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return null;
            try
            {
                var (bytes, error) = await DownloadBytesAsync(song.Artwork, null, null);
                return bytes;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>构造目标文件路径: "歌手 - 歌名.ext", 非法字符替换, 重名自动追加序号。</summary>
        private static string BuildTargetPath(string dir, OnlineSong song, string url, string quality, byte[] bytes)
        {
            var ext = GetAudioExt(url, quality, bytes);
            var name = $"{song.Artist} - {song.Title}".Trim(' ', '-');
            if (name.Length == 0) name = song.Title.Length > 0 ? song.Title : "unknown";
            name = SanitizeFileName(name);
            if (name.Length > 150) name = name[..150];
            var file = Path.Combine(dir, name + ext);
            var seq = 2;
            while (File.Exists(file))
            {
                file = Path.Combine(dir, $"{name} ({seq++}){ext}");
            }
            return file;
        }

        /// <summary>音频扩展名: URL 优先, 其次按音质档位(无损→flac), 兜底按内容嗅探。</summary>
        private static string GetAudioExt(string url, string quality, byte[] bytes)
        {
            var fileName = url.Split('?')[0];
            var dot = fileName.LastIndexOf('.');
            if (dot > 0 && fileName.Length - dot is >= 2 and <= 5)
            {
                var candidate = fileName[dot..].ToLowerInvariant();
                if (candidate.All(c => char.IsLetterOrDigit(c) || c == '.') && candidate is ".mp3" or ".flac" or ".m4a" or ".ogg" or ".wav" or ".ape" or ".wma")
                    return candidate;
            }
            if (quality is "flac" or "flac24bit" or "hires") return ".flac";
            // 内容嗅探: FLAC("fLaC") / OGG("OggS") / MP4(ftyp)
            if (bytes.Length > 12)
            {
                if (bytes[0] == 'f' && bytes[1] == 'L' && bytes[2] == 'a' && bytes[3] == 'C') return ".flac";
                if (bytes[0] == 'O' && bytes[1] == 'g' && bytes[2] == 'g' && bytes[3] == 'S') return ".ogg";
                if (bytes[4] == 'f' && bytes[5] == 't' && bytes[6] == 'y' && bytes[7] == 'p') return ".m4a";
            }
            return ".mp3";
        }

        /// <summary>封面扩展名: URL 优先(.png/.webp/.gif), 兜底按字节头判断, 默认 .jpg。</summary>
        private static string GetCoverExt(string artworkUrl, byte[] bytes)
        {
            if (artworkUrl.Contains(".png", StringComparison.OrdinalIgnoreCase)) return ".png";
            if (bytes.Length > 8)
            {
                if (bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G') return ".png";
                if (bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F') return ".webp";
            }
            return ".jpg";
        }

        /// <summary>替换文件名非法字符。</summary>
        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            return new string(chars).Trim();
        }

        /// <summary>ATL 内嵌元数据: 标题/艺术家/专辑 + 封面 + 歌词(始终执行, 独立文件选项不影响)。</summary>
        private static void EmbedMetadata(string file, OnlineSong song, byte[]? coverBytes, string? lyrics)
        {
            try
            {
                Settings.FileBufferSize = 1024 * 256;
                var track = new Track(file)
                {
                    Title = song.Title,
                    Artist = song.Artist,
                    Album = song.Album,
                };
                if (coverBytes is { Length: > 0 })
                {
                    track.EmbeddedPictures.Clear();
                    track.EmbeddedPictures.Add(PictureInfo.fromBinaryData(coverBytes));
                }
                if (!string.IsNullOrWhiteSpace(lyrics))
                {
                    track.Lyrics = new System.Collections.Generic.List<LyricsInfo> { new() { UnsynchronizedLyrics = lyrics } };
                }
                track.Save();
            }
            catch
            {
                // 元数据写入失败不阻断下载(文件本体已保存)
            }
        }
    }
}
