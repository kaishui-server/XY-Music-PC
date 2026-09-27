using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinUIMusicPlayer.Services.Plugins
{
    /// <summary>animemusic/1 插件 META 字段(const META = {...} 单行 JSON, 分发端点实时生成)。</summary>
    public class AnimemusicMeta
    {
        public string Name { get; set; } = string.Empty;
        public string Platform { get; set; } = "wy";
        public string Version { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Api { get; set; } = string.Empty;
        public List<string> Qualities { get; set; } = ["128k", "192k", "320k", "flac"];
    }

    /// <summary>
    /// animemusic/1 自有格式插件运行时(对齐手机版 plugin_runtime.dart 的 REST 直连实现)。
    /// 插件本体是 CommonJS Node 模块(依赖 http/https/zlib 内置模块), 无法在 Jint 中执行;
    /// 但插件契约只是对后端 REST 的薄封装({api}/music/search 等, 含 PATH_INFO / ?route=
    /// 两种路由风格自适应), 宿主直接实现等价调用, 插件文件仍保留在磁盘上以便更新与配置管理。
    /// </summary>
    public sealed class AnimemusicPluginRuntime : IDisposable
    {
        public string Hash { get; }
        public string Code { get; }
        public AnimemusicMeta Meta { get; }
        /// <summary>生效后端地址: 用户变量 api 优先, 否则 META.api 按安装源端口改写。</summary>
        public string Api { get; }

        private readonly ILogger _logger;
        private readonly object _gate = new();
        private bool _disposed;
        /// <summary>已确认可用的路由风格: null=未确认, false=PATH_INFO, true=?route=。</summary>
        private bool? _queryRoute;

        private static readonly Regex MetaRegex = new(@"const\s+META\s*=\s*(\{[^\n;]+\})\s*;", RegexOptions.Compiled);
        private static readonly Regex IndexPathRegex = new(@"/index\.php(\?.*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNameCaseInsensitive = true };

        private AnimemusicPluginRuntime(string code, string hash, AnimemusicMeta meta, string api, ILogger logger)
        {
            Code = code;
            Hash = hash;
            Meta = meta;
            Api = api;
            _logger = logger;
        }

        /// <summary>检测代码是否为 animemusic/1 插件(在 LX 检测之后调用):
        /// META.format 声明自有格式, 或 CommonJS 导出变量且代码含 animemusic/1 标记。</summary>
        public static bool IsAnimemusicScript(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            if (Regex.IsMatch(code, @"[""']format[""']\s*:\s*[""']animemusic/1[""']")) return true;
            return Regex.IsMatch(code, @"module\.exports\s*=\s*\w+") && code.Contains("animemusic/1");
        }

        /// <summary>提取 META 关键字段(对齐手机版 _extractAnimemusicMeta): META 保证是单行合法 JSON。</summary>
        public static AnimemusicMeta ParseMeta(string code)
        {
            var meta = new AnimemusicMeta();
            var match = MetaRegex.Match(code);
            if (!match.Success) return meta;
            try
            {
                using var doc = JsonDocument.Parse(match.Groups[1].Value);
                var root = doc.RootElement;
                string Str(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString()?.Trim() ?? string.Empty
                    : string.Empty;
                meta.Name = Str("name");
                var platform = Str("platform");
                if (platform.Length > 0) meta.Platform = platform;
                meta.Version = Str("version");
                meta.Author = Str("author");
                meta.Api = Str("api");
                if (root.TryGetProperty("qualities", out var q) && q.ValueKind == JsonValueKind.Array)
                {
                    var list = q.EnumerateArray()
                        .Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() ?? string.Empty : v.ToString())
                        .Where(s => s.Length > 0)
                        .ToList();
                    if (list.Count > 0) meta.Qualities = list;
                }
            }
            catch { /* META 损坏时保留默认值 */ }
            return meta;
        }

        /// <summary>animemusic 分发端点生成的 META.api 与脚本下载地址同主机但指向默认端口(80),
        /// 而后端实际运行在订阅源端口上; 同主机且 api 未带显式端口时, 把 api 端口改写成源端口。</summary>
        public static string RewriteApiPort(string api, string? sourceUrl)
        {
            if (string.IsNullOrWhiteSpace(api) || string.IsNullOrWhiteSpace(sourceUrl)) return api;
            if (!Uri.TryCreate(api, UriKind.Absolute, out var apiUri) ||
                !Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri)) return api;
            if (apiUri.Host.Length == 0 || !string.Equals(apiUri.Host, sourceUri.Host, StringComparison.OrdinalIgnoreCase))
                return api;
            var apiDefaultPort = apiUri.Port is 80 or -1;
            if (!apiDefaultPort || sourceUri.Port is 80 or -1) return api;
            try
            {
                return new UriBuilder(apiUri) { Port = sourceUri.Port }.ToString();
            }
            catch { return api; }
        }

        /// <summary>加载 animemusic 插件(纯 META 解析, 无需脚本引擎)。apiOverride 为用户变量 api。</summary>
        public static AnimemusicPluginRuntime Load(string code, string hash, string? apiOverride, string? sourceUrl, ILogger logger)
        {
            var meta = ParseMeta(code);
            var api = !string.IsNullOrWhiteSpace(apiOverride)
                ? apiOverride.Trim()
                : RewriteApiPort(meta.Api, sourceUrl);
            return new AnimemusicPluginRuntime(code, hash, meta, api, logger);
        }

        /// <summary>归一化音质档位(对齐手机版 _normalizeAnimemusicQuality)。</summary>
        public static string NormalizeQuality(string? quality)
        {
            var value = quality?.Trim().ToLowerInvariant() ?? string.Empty;
            if (value.Length == 0) return "320k";
            return value switch
            {
                "hires" or "hi-res" or "master" or "atmos" or "dolby" or "hifi" or "24bit" or "flac24bit" => "flac24bit",
                "flac" or "lossless" or "sq" or "ape" or "wav" => "flac",
                "128k" or "192k" or "320k" => value,
                _ => "320k",
            };
        }

        // ---------------- REST 直连 ----------------

        private static string ApiBase(string api)
        {
            // 与手机版一致: 先去掉结尾的 /index.php(含查询串), 再去尾部斜杠
            var trimmed = IndexPathRegex.Replace(api.Trim(), "");
            return trimmed.TrimEnd('/');
        }

        private static string BuildUrl(string baseAndPath, Dictionary<string, string> parameters)
        {
            var query = string.Join("&", parameters.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? string.Empty)}"));
            return query.Length == 0 ? baseAndPath : baseAndPath + "?" + query;
        }

        /// <summary>调用 animemusic 后端: 自动探测并记忆路由风格(PATH_INFO 或 ?route=), 404 时换另一种重试。</summary>
        private (JsonElement Body, string? Error) CallApi(string path, Dictionary<string, string> parameters)
        {
            lock (_gate)
            {
                if (_disposed) return (default, "插件已释放");
                if (string.IsNullOrWhiteSpace(Api)) return (default, "插件缺少后端接口地址");
                var base_ = ApiBase(Api);
                var confirmed = _queryRoute;
                var attempts = confirmed is null ? new[] { false, true } : new[] { confirmed.Value, !confirmed.Value };
                string? lastError = null;
                foreach (var useQuery in attempts)
                {
                    string url;
                    if (useQuery)
                    {
                        var query = new Dictionary<string, string> { ["route"] = path };
                        foreach (var kv in parameters) query[kv.Key] = kv.Value;
                        url = BuildUrl(base_ + "/index.php", query);
                    }
                    else
                    {
                        url = BuildUrl(base_ + "/" + path, parameters);
                    }
                    try
                    {
                        var raw = PluginHttpBridge.Shared.Request(url, JsonSerializer.Serialize(new PluginHttpBridge.RequestOptions
                        {
                            Method = "GET",
                            Headers = new Dictionary<string, string>
                            {
                                ["Accept"] = "application/json, text/plain, */*",
                                ["User-Agent"] = "animemusic-plugin/1.0.0",
                            },
                            TimeoutMs = 15000,
                        }));
                        var payload = JsonSerializer.Deserialize<PluginHttpBridge.ResponsePayload>(raw, PayloadOptions);
                        if (payload?.Error is { Length: > 0 } reqError)
                            throw new InvalidOperationException(reqError);
                        if (payload is null || payload.Body is null) throw new InvalidOperationException("后端返回空响应");
                        if (payload.StatusCode == 404)
                        {
                            // 路由风格不匹配, 换另一种再试(与插件 apiCall 一致)
                            lastError = "接口返回 404";
                            continue;
                        }
                        if (payload.StatusCode is < 200 or >= 300)
                            throw new InvalidOperationException($"接口返回 HTTP {payload.StatusCode}");
                        using var doc = JsonDocument.Parse(payload.Body);
                        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("接口返回异常");
                        var root = doc.RootElement;
                        var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
                        if (code == 404)
                        {
                            lastError = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                                ? m.GetString() ?? "接口 404"
                                : "接口 404";
                            continue;
                        }
                        if (code != 200)
                        {
                            var message = root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String
                                ? msg.GetString()?.Trim() ?? string.Empty
                                : string.Empty;
                            throw new InvalidOperationException(message.Length > 0 ? message : $"接口返回失败({code})");
                        }
                        _queryRoute = useQuery;
                        return (root.Clone(), null);
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                        // 已确认过路由风格后, 业务错误不再换风格重试
                        if (confirmed is not null) return (default, ex.Message);
                    }
                }
                return (default, lastError ?? "接口调用失败");
            }
        }

        /// <summary>歌词响应中的平台与歌曲 id(对齐手机版 _animemusicSongRef): 搜索结果 rawData 已带 platform 字段。</summary>
        private (string Platform, string Id) SongRef(OnlineSong song)
        {
            var id = song.Id ?? string.Empty;
            var platform = song.Platform ?? string.Empty;
            try
            {
                if (!string.IsNullOrEmpty(song.RawJson))
                {
                    using var doc = JsonDocument.Parse(song.RawJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        var root = doc.RootElement;
                        foreach (var key in new[] { "id", "songId", "musicId" })
                        {
                            if (!root.TryGetProperty(key, out var v)) continue;
                            var picked = v.ValueKind switch
                            {
                                JsonValueKind.String => v.GetString(),
                                JsonValueKind.Number => v.GetRawText(),
                                _ => null,
                            };
                            if (!string.IsNullOrWhiteSpace(picked)) { id = picked; break; }
                        }
                        if (string.IsNullOrWhiteSpace(id)
                            && root.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Object
                            && extra.TryGetProperty("songId", out var es)
                            && es.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(es.GetString()))
                            id = es.GetString()!;
                        if (root.TryGetProperty("platform", out var p) && p.ValueKind == JsonValueKind.String
                            && !string.IsNullOrEmpty(p.GetString()))
                            platform = p.GetString()!;
                    }
                }
            }
            catch { /* 损坏 JSON 忽略, 用歌曲快照字段 */ }
            if (string.IsNullOrWhiteSpace(platform)) platform = Meta.Platform;
            return (platform, id);
        }

        /// <summary>搜索: GET music/search?platform=&amp;keyword=&amp;page=&amp;limit=30, 结果 rawData 注入 platform。</summary>
        public (List<OnlineSong> Songs, bool IsEnd, string? Error) Search(string keyword, int page)
        {
            var trimmed = keyword?.Trim() ?? string.Empty;
            if (trimmed.Length == 0) return ([], true, null);
            var (body, error) = CallApi("music/search", new Dictionary<string, string>
            {
                ["platform"] = Meta.Platform,
                ["keyword"] = trimmed,
                ["page"] = page.ToString(),
                ["limit"] = "30",
            });
            if (error is not null) return ([], false, error);
            var songs = new List<OnlineSong>();
            if (body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var song = ParseSearchItem(item);
                    if (song is not null) songs.Add(song);
                }
            }
            return (songs, songs.Count < 30, null);
        }

        /// <summary>搜索结果条目 → OnlineSong(对齐手机版 _toSearchSong 的宽松字段回退)。</summary>
        private OnlineSong? ParseSearchItem(JsonElement item)
        {
            string Text(params string[] keys)
            {
                foreach (var key in keys)
                {
                    if (!item.TryGetProperty(key, out var v)) continue;
                    var s = v.ValueKind switch
                    {
                        JsonValueKind.String => v.GetString(),
                        JsonValueKind.Number => v.GetRawText(),
                        // 数组字段(多歌手)以 "/" 连接, 对齐手机版 valueText
                        JsonValueKind.Array => string.Join("/", v.EnumerateArray()
                            .Where(e => e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString()))
                            .Select(e => e.GetString()!.Trim())),
                        _ => null,
                    };
                    if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                }
                return string.Empty;
            }
            var id = Text("id", "songId", "musicId", "mid");
            if (id.Length == 0) return null;
            double duration = 0;
            foreach (var key in new[] { "duration", "interval", "dt", "time", "length", "timelength" })
            {
                if (!item.TryGetProperty(key, out var d) || d.ValueKind != JsonValueKind.Number) continue;
                var num = d.GetDouble();
                if (num <= 0) continue;
                duration = num > 1000 ? num / 1000.0 : num;
                break;
            }
            var artwork = Text("coverUrl", "cover", "picUrl", "pic", "artwork", "img", "albumImg");
            // rawData 注入 platform 供 musicUrl 使用, 与手机版一致
            var enriched = new Dictionary<string, object?>
            {
                ["platform"] = Meta.Platform,
            };
            foreach (var p in item.EnumerateObject())
                enriched[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : (object?)p.Value.Clone();
            return new OnlineSong
            {
                Id = id,
                Title = Text("title", "name", "songname", "songName"),
                Artist = Text("artist", "singer", "author", "artists", "ar"),
                Album = Text("albumName", "album_name", "albumname", "albumTitle", "album", "al"),
                Artwork = artwork,
                DurationSec = duration,
                Platform = Meta.Platform,
                PluginHash = Hash,
                PluginName = Meta.Name,
                RawJson = JsonSerializer.Serialize(enriched),
            };
        }

        /// <summary>解析直链: GET music/url?source=&amp;musicId=&amp;quality=。</summary>
        public (OnlineMediaSource? Source, string? Error) GetMediaSource(OnlineSong song, string quality)
        {
            var (platform, id) = SongRef(song);
            if (string.IsNullOrWhiteSpace(id)) return (null, "歌曲缺少 id");
            var (body, error) = CallApi("music/url", new Dictionary<string, string>
            {
                ["source"] = platform,
                ["musicId"] = id,
                ["quality"] = NormalizeQuality(quality),
            });
            if (error is not null) return (null, error);
            var url = body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String
                ? u.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            if (url.Length == 0) return (null, "插件没有返回可播放地址");
            return (new OnlineMediaSource(url, null), null);
        }

        /// <summary>歌词: 优先逐字(music/lyric/word, Enhanced LRC), 失败回退逐行(music/lyric)。
        /// 主歌词/翻译/罗马音按主/译/罗马顺序返回, 翻译与罗马音拼接为 Translation。</summary>
        public (string? Lrc, string? Translation, string? Error) GetLyric(OnlineSong song)
        {
            var (platform, id) = SongRef(song);
            if (string.IsNullOrWhiteSpace(id)) return (null, null, "歌曲缺少 id");
            var parameters = new Dictionary<string, string>
            {
                ["platform"] = platform,
                ["musicId"] = id,
                ["interval"] = "200",
            };
            var name = song.Title?.Trim();
            if (!string.IsNullOrEmpty(name)) parameters["name"] = name;

            var word = CallApi("music/lyric/word", parameters);
            var joined = JoinLyrics(word.Body);
            if (word.Error is null && joined is not null)
                return (joined.Value.Lrc, joined.Value.Translation, null);
            // 逐字失败自动回退逐行(与插件 lyricFallback 默认行为一致)
            var line = CallApi("music/lyric", parameters);
            var lineJoined = JoinLyrics(line.Body);
            if (line.Error is not null && lineJoined is null) return (null, null, line.Error);
            if (lineJoined is not null) return (lineJoined.Value.Lrc, lineJoined.Value.Translation, null);
            return (null, null, "歌词内容为空");
        }

        private (string Lrc, string? Translation)? JoinLyrics(JsonElement body)
        {
            if (body.ValueKind != JsonValueKind.Object) return null;
            string Pick(string key) => body.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            var lrc = Pick("lyric");
            var tlyric = Pick("tlyric");
            var rlyric = Pick("rlyric");
            if (lrc.Length == 0 && tlyric.Length == 0 && rlyric.Length == 0) return null;
            var translation = new[] { tlyric, rlyric }.Where(s => s.Length > 0).ToList();
            return (lrc, translation.Count > 0 ? string.Join("\n", translation) : null);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
            }
        }
    }
}
