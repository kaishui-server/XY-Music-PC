using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Services;
using WinUIMusicPlayer.Services.Plugins;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.ViewModel;
using WinUIMusicPlayer.ViewModel.Pages;

namespace WinUIMusicPlayer.ViewModel.Pages
{
    /// <summary>我的歌单页列表项: 歌单 + 异步解析的封面哈希(取第一首歌封面)。</summary>
    public partial class MyPlayListItem : ObservableObject
    {
        public required PlayList PlayList { get; set; }
        [ObservableProperty]
        private string _coverHash = string.Empty;
    }

    /// <summary>
    /// 我的歌单页 ViewModel: 展示在线歌单(IsOnline=1), 支持创建/重命名/删除。
    /// 在线歌单仅存歌曲插件链接, 播放时才解析下载, 不占用本地空间。
    /// </summary>
    public partial class MyPlayListViewModel : ObservableObject
    {
        private static readonly JsonSerializerOptions SongJsonOpts = new() { PropertyNameCaseInsensitive = true };

        private readonly AppViewModel _appViewModel;
        private readonly MusicDatabaseService _db;

        public ObservableCollection<MyPlayListItem> PlayLists { get; } = new();

        [ObservableProperty]
        private bool _isEmpty = true;

        public MyPlayListViewModel()
        {
            _appViewModel = App.Services.GetRequiredService<AppViewModel>();
            _db = App.Services.GetRequiredService<MusicDatabaseService>();
        }

        public async Task RefreshAsync()
        {
            PlayLists.Clear();
            foreach (var pl in _appViewModel.OnlinePlayLists.OrderBy(p => p.Id))
                PlayLists.Add(new MyPlayListItem { PlayList = pl });
            IsEmpty = PlayLists.Count == 0;
            foreach (var item in PlayLists)
                _ = LoadCoverAsync(item);
        }

        /// <summary>取歌单第一首歌封面: 在线歌单下载封面图, 本地歌直接用 ImageHash。</summary>
        private async Task LoadCoverAsync(MyPlayListItem item)
        {
            try
            {
                var entries = await _db.GetOnlinePlayListMusicsAsync(item.PlayList.Id);
                if (entries.Count == 0) return;
                var first = entries[0];
                if (first.MusicId > 0)
                {
                    if (_appViewModel.TryFindById(first.MusicId, out var m) && m is not null)
                        item.CoverHash = m.ImageHash ?? string.Empty;
                }
                else if (!string.IsNullOrEmpty(first.SongJson))
                {
                    var song = DeserializeSong(first.SongJson);
                    if (song is not null)
                    {
                        var hash = await OnlineSearchViewModel.DownloadArtworkAsync(song.Artwork);
                        if (hash is not null) item.CoverHash = hash;
                    }
                }
            }
            catch { /* 封面失败不阻断列表 */ }
        }

        /// <summary>反序列化在线歌曲 JSON(供本页与详情页共用), 并做跨端兼容规范化(手机端同步歌曲插件哈希/平台/缺失字段修正)。</summary>
        internal static OnlineSong? DeserializeSong(string json)
        {
            try
            {
                var song = JsonSerializer.Deserialize<OnlineSong>(json, SongJsonOpts);
                // 修复历史/手机端数据: 插件哈希回退匹配本地插件, 回填缺失平台与歌曲 id
                if (song is not null)
                    App.Services.GetRequiredService<PluginManagerService>().NormalizeOnlineSong(song);
                return song;
            }
            catch { return null; }
        }

        /// <summary>创建在线歌单并加入 AppViewModel.OnlinePlayLists。</summary>
        public async Task<PlayList?> CreatePlayListAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var pl = new PlayList { Name = name, SongCount = 0, IsOnline = 1 };
            pl.Id = await _db.CreateOnlinePlayListAsync(name);
            _appViewModel.OnlinePlayLists.Add(pl);
            await RefreshAsync();
            return pl;
        }

        /// <summary>重命名在线歌单(名称经回调获取, 与 AppViewModel.EditPlayListName 同款模式; 取消时回调返回空串)。</summary>
        public async Task RenamePlayListAsync(PlayList pl, Func<Task<string>> getNameCallback)
        {
            if (pl is null || getNameCallback is null) return;
            var newName = await getNameCallback();
            if (string.IsNullOrWhiteSpace(newName)) return;
            pl.Name = newName;
            await _db.UpdatePlayList(pl);
        }

        public async Task RemovePlayListAsync(PlayList pl)
        {
            if (pl is null) return;
            await _db.DeleteOnlinePlayListAsync(pl);
            _appViewModel.OnlinePlayLists.Remove(pl);
            await RefreshAsync();
        }

        /// <summary>一键删除全部在线歌单(含歌单曲目), 返回删除数量。</summary>
        public async Task<int> RemoveAllPlayListsAsync()
        {
            var count = _appViewModel.OnlinePlayLists.Count;
            if (count == 0) return 0;
            foreach (var pl in _appViewModel.OnlinePlayLists.ToList())
                await _db.DeleteOnlinePlayListAsync(pl);
            _appViewModel.OnlinePlayLists.Clear();
            await RefreshAsync();
            return count;
        }

        // ────────────────────────────────────────────────────────────
        //  歌单导入(新建歌单弹窗: 从网络导入 / 从本地导入)
        // ────────────────────────────────────────────────────────────

        private PluginManagerService PluginManager => App.Services.GetRequiredService<PluginManagerService>();

        /// <summary>声明支持 importMusicSheet 的已启用插件(网络导入来源下拉)。</summary>
        public IReadOnlyList<PluginRuntime> GetImportablePlugins()
            => [.. PluginManager.ActivePlugins
                .Where(r => r.SupportsMethod("importMusicSheet"))
                .OrderBy(r => r.Metadata.Platform, StringComparer.OrdinalIgnoreCase)];

        /// <summary>网络导入歌单: 经插件 importMusicSheet 解析链接, 创建歌单并逐首写入插件链接。</summary>
        public async Task<(bool Ok, string Message)> ImportNetworkAsync(string pluginHash, string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return (false, ToolUtils.GetString("CreatePlayListUrlRequired"));
            var result = await PluginManager.ImportMusicSheetAsync(pluginHash, url.Trim());
            if (result.Error is not null)
                return (false, string.Format(ToolUtils.GetString("CreatePlayListImportFailedReason"), result.Error));
            var name = string.IsNullOrWhiteSpace(result.Title)
                ? ToolUtils.GetString("CreatePlayListImportedDefaultName")
                : result.Title.Trim();
            var pl = await CreatePlayListAsync(name);
            if (pl is null)
                return (false, ToolUtils.GetString("CreatePlayListImportFailedPlain"));
            var added = 0;
            foreach (var song in result.Songs)
            {
                if (await _db.AddOnlineSongToPlayListAsync(pl.Id, song)) added++;
            }
            pl.SongCount = added;
            return (true, string.Format(ToolUtils.GetString("CreatePlayListImportSuccess"), name, added));
        }

        /// <summary>本地导入歌单: M3U/M3U8 匹配本地音乐库; MusicFree 备份 JSON 按插件/本地路径匹配。</summary>
        public async Task<(bool Ok, string Message)> ImportLocalFileAsync(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var matcher = LocalMusicIndex.Create(_appViewModel.SongsSource);
            return ext switch
            {
                ".m3u" or ".m3u8" => await ImportM3uAsync(filePath, matcher),
                ".json" => await ImportMusicFreeBackupAsync(filePath, matcher),
                _ => (false, string.Format(ToolUtils.GetString("CreatePlayListImportFailedReason"), ToolUtils.GetString("CreatePlayListUnsupportedFile"))),
            };
        }

        /// <summary>M3U/M3U8 导入: 解析本地音频路径并匹配音乐库, 匹配到的以 MusicId 引用写入歌单。</summary>
        private async Task<(bool Ok, string Message)> ImportM3uAsync(string filePath, LocalMusicIndex matcher)
        {
            List<string> lines;
            try { lines = [.. (await File.ReadAllLinesAsync(filePath)).Select(l => l.Trim())]; }
            catch (Exception ex) { return (false, string.Format(ToolUtils.GetString("CreatePlayListImportFailedReason"), ex.Message)); }
            var baseDir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? string.Empty;
            var paths = lines
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .Select(l => ResolveM3uPath(l, baseDir))
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (paths.Count == 0)
                return (false, ToolUtils.GetString("CreatePlayListM3uEmpty"));

            var matched = new List<Music>();
            var missing = 0;
            foreach (var path in paths)
            {
                var m = matcher.Match(path, null, null);
                if (m is null) missing++;
                else if (!matched.Contains(m)) matched.Add(m);
            }
            if (matched.Count == 0)
                return (false, ToolUtils.GetString("CreatePlayListLocalNoMatch"));

            var name = Path.GetFileNameWithoutExtension(filePath);
            var pl = await CreatePlayListAsync(name);
            if (pl is null)
                return (false, ToolUtils.GetString("CreatePlayListImportFailedPlain"));
            foreach (var m in matched)
                await _db.AddLocalMusicToOnlinePlayListAsync(pl.Id, m.Id);
            pl.SongCount = matched.Count;
            return missing == 0
                ? (true, string.Format(ToolUtils.GetString("CreatePlayListImportSuccess"), name, matched.Count))
                : (true, string.Format(ToolUtils.GetString("CreatePlayListLocalImportedWithMissing"), name, matched.Count, missing));
        }

        /// <summary>MusicFree 备份 JSON 导入: 本地路径歌曲匹配音乐库, 网络歌曲按平台匹配已启用插件。</summary>
        private async Task<(bool Ok, string Message)> ImportMusicFreeBackupAsync(string filePath, LocalMusicIndex matcher)
        {
            string content;
            try { content = await File.ReadAllTextAsync(filePath); }
            catch (Exception ex) { return (false, string.Format(ToolUtils.GetString("CreatePlayListImportFailedReason"), ex.Message)); }
            JsonDocument doc;
            try { doc = JsonDocument.Parse(content); }
            catch { return (false, ToolUtils.GetString("CreatePlayListBackupInvalid")); }
            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return (false, ToolUtils.GetString("CreatePlayListBackupInvalid"));
                if (!root.TryGetProperty("musicSheets", out var sheets) || sheets.ValueKind != JsonValueKind.Array)
                {
                    // 兼容 { data: { musicSheets: [...] } } 形态
                    if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                        || !data.TryGetProperty("musicSheets", out sheets) || sheets.ValueKind != JsonValueKind.Array)
                        return (false, ToolUtils.GetString("CreatePlayListBackupInvalid"));
                }

                var runtimes = PluginManager.ActivePlugins.Where(r => r.SupportsMethod("getMediaSource")).ToList();
                int playlistCount = 0, importedSongs = 0, unmatchedPlugin = 0, skipped = 0, sheetIndex = 0;
                foreach (var sheet in sheets.EnumerateArray())
                {
                    sheetIndex++;
                    if (sheet.ValueKind != JsonValueKind.Object) continue;
                    var name = GetString(sheet, "title") ?? GetString(sheet, "name")
                        ?? string.Format(ToolUtils.GetString("CreatePlayListUnnamedSheet"), sheetIndex);
                    if (!sheet.TryGetProperty("musicList", out var musicList) || musicList.ValueKind != JsonValueKind.Array) continue;

                    PlayList? pl = null;
                    var sheetSongs = 0;
                    foreach (var raw in musicList.EnumerateArray())
                    {
                        if (raw.ValueKind != JsonValueKind.Object) { skipped++; continue; }
                        // 本地路径歌曲 → 匹配音乐库(MusicId 引用)
                        var localPath = GetBackupLocalPath(raw);
                        if (localPath.Length > 0)
                        {
                            var m = matcher.Match(localPath, GetString(raw, "title"), GetString(raw, "artist"));
                            if (m is not null)
                            {
                                pl ??= await CreatePlayListAsync(name);
                                if (pl is not null && await _db.AddLocalMusicToOnlinePlayListAsync(pl.Id, m.Id))
                                {
                                    sheetSongs++; importedSongs++;
                                }
                            }
                            else skipped++;
                            continue;
                        }
                        // 网络歌曲 → 按平台匹配已启用插件(存插件链接)
                        var platform = GetString(raw, "platform", "source", "sourceName", "source_name");
                        var id = GetBackupSongId(raw);
                        if (string.IsNullOrEmpty(platform) || string.IsNullOrEmpty(id)) { skipped++; continue; }
                        var runtime = MatchPluginByPlatform(platform, runtimes);
                        if (runtime is null)
                        {
                            unmatchedPlugin++;
                            continue;
                        }
                        var song = BuildBackupOnlineSong(raw, runtime, id);
                        if (song is null) { skipped++; continue; }
                        pl ??= await CreatePlayListAsync(name);
                        if (pl is not null && await _db.AddOnlineSongToPlayListAsync(pl.Id, song))
                        {
                            sheetSongs++; importedSongs++;
                        }
                    }
                    if (pl is not null && sheetSongs > 0)
                    {
                        pl.SongCount = sheetSongs;
                        playlistCount++;
                    }
                }
                if (playlistCount == 0)
                {
                    var hint = unmatchedPlugin > 0
                        ? " " + string.Format(ToolUtils.GetString("CreatePlayListBackupNoPlugin"), unmatchedPlugin)
                        : string.Empty;
                    return (false, ToolUtils.GetString("CreatePlayListBackupNoSong") + hint);
                }
                var message = string.Format(ToolUtils.GetString("CreatePlayListBackupImported"), playlistCount, importedSongs);
                if (skipped > 0)
                    message += string.Format(ToolUtils.GetString("CreatePlayListBackupSkippedSuffix"), skipped);
                if (unmatchedPlugin > 0)
                    message += " " + string.Format(ToolUtils.GetString("CreatePlayListBackupNoPlugin"), unmatchedPlugin);
                return (true, message);
            }
        }

        /// <summary>备份 musicItem → OnlineSong(RawJson 补全 id/platform 等字段后交给插件解析)。</summary>
        private static OnlineSong? BuildBackupOnlineSong(JsonElement raw, PluginRuntime runtime, string id)
        {
            var title = GetString(raw, "title", "name", "songname");
            if (string.IsNullOrEmpty(title)) return null;
            var artist = GetString(raw, "artist", "singer", "author") ?? string.Empty;
            var album = GetString(raw, "album", "albumName", "album_name") ?? string.Empty;
            var artwork = GetString(raw, "artwork", "coverUrl", "cover", "img") ?? string.Empty;
            double durationSec = 0;
            if (raw.TryGetProperty("duration", out var d))
            {
                if (d.ValueKind == JsonValueKind.Number) durationSec = d.GetDouble();
                else if (d.ValueKind == JsonValueKind.String && double.TryParse(d.GetString(), out var dd)) durationSec = dd;
                if (durationSec > 1000) durationSec /= 1000; // 毫秒 → 秒
            }
            var node = JsonNode.Parse(raw.GetRawText())?.AsObject();
            if (node is null) return null;
            // 插件 getMediaSource 依赖 musicItem 的 id/platform, 缺失字段补全
            node["id"] ??= id;
            node["title"] ??= title;
            node["artist"] ??= artist;
            node["album"] ??= album;
            node["platform"] ??= runtime.Metadata.Platform;
            return new OnlineSong
            {
                Id = id,
                Title = title,
                Artist = artist,
                Album = album,
                Artwork = artwork,
                DurationSec = durationSec,
                Platform = runtime.Metadata.Platform,
                PluginHash = runtime.Hash,
                PluginName = runtime.Metadata.Platform,
                RawJson = node.ToJsonString(),
            };
        }

        /// <summary>按平台匹配已启用插件: 精确匹配唯一插件; 否则宽松包含匹配(如 "酷狗音乐" vs "酷狗"), 多候选时不绑定。</summary>
        private static PluginRuntime? MatchPluginByPlatform(string platform, IReadOnlyList<PluginRuntime> runtimes)
        {
            PluginRuntime? exact = null;
            foreach (var r in runtimes)
            {
                if (!string.Equals(r.Metadata.Platform?.Trim(), platform.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (exact is not null) return null; // 多个同平台插件无法确定来源
                exact = r;
            }
            if (exact is not null) return exact;
            var canon = CanonicalPlatform(platform);
            if (canon.Length < 2) return null;
            PluginRuntime? loose = null;
            foreach (var r in runtimes)
            {
                var c = CanonicalPlatform(r.Metadata.Platform ?? string.Empty);
                if (c.Length >= 2 && (c.Contains(canon) || canon.Contains(c)))
                {
                    if (loose is not null) return null;
                    loose = r;
                }
            }
            return loose;
        }

        private static readonly Regex PlatformNoiseRegex = new(@"[\s_.\-—/\\()\[\]（）【】·]+", RegexOptions.Compiled);

        /// <summary>平台名归一化: 已知平台映射到标准音源 key, 其余去分隔符与"音乐/音源"等后缀。</summary>
        private static string CanonicalPlatform(string value)
        {
            var v = value.ToLowerInvariant();
            if (v.Contains("netease") || v.Contains("网易")) return "wy";
            if (v.Contains("qq") || v.Contains("腾讯")) return "tx";
            if (v.Contains("kuwo") || v.Contains("酷我")) return "kw";
            if (v.Contains("kugou") || v.Contains("酷狗")) return "kg";
            if (v.Contains("migu") || v.Contains("咪咕")) return "mg";
            if (v.Contains("bilibili") || v.Contains("哔哩")) return "bilibili";
            v = PlatformNoiseRegex.Replace(v, string.Empty);
            while (v.EndsWith("音乐") || v.EndsWith("音源") || v.EndsWith("source") || v.EndsWith("music") || v.EndsWith("plugin") || v.EndsWith("插件"))
                v = v[..^2];
            return v;
        }

        /// <summary>备份歌曲本地路径(localPath/local_path/url 中的本地文件路径), 网络地址返回空。</summary>
        private static string GetBackupLocalPath(JsonElement raw)
        {
            foreach (var name in (string[])["localPath", "local_path", "url"])
            {
                if (!raw.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String) continue;
                var text = v.GetString()?.Trim();
                if (string.IsNullOrEmpty(text)) continue;
                if (text.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    if (Uri.TryCreate(text, UriKind.Absolute, out var u) && u.IsFile)
                        return Uri.UnescapeDataString(u.LocalPath);
                    continue;
                }
                if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme.Length > 1 && !uri.IsFile)
                    continue; // http 等网络地址
                return text;
            }
            return string.Empty;
        }

        /// <summary>备份歌曲 id(字符串或数字, 兼容 musicId/songmid/songId/mid/hash)。</summary>
        private static string GetBackupSongId(JsonElement raw)
        {
            foreach (var name in (string[])["id", "musicId", "songmid", "songId", "songid", "mid", "hash"])
            {
                if (!raw.TryGetProperty(name, out var v)) continue;
                if (v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(s)) return s!;
                }
                else if (v.ValueKind == JsonValueKind.Number)
                    return v.GetRawText();
            }
            return string.Empty;
        }

        private static string? GetString(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (obj.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            return null;
        }

        /// <summary>M3U 路径解析: 相对路径基于歌单文件目录, file:// URI 转本地路径, 网络地址忽略。</summary>
        private static string ResolveM3uPath(string line, string baseDir)
        {
            try
            {
                if (line.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    return Uri.TryCreate(line, UriKind.Absolute, out var u) && u.IsFile
                        ? Uri.UnescapeDataString(u.LocalPath)
                        : string.Empty;
                if (Uri.TryCreate(line, UriKind.Absolute, out var uri) && uri.Scheme.Length > 1 && !uri.IsFile)
                    return string.Empty; // http 等网络地址: 本地歌单不支持
                return Path.IsPathRooted(line) ? line : Path.GetFullPath(Path.Combine(baseDir, line));
            }
            catch { return string.Empty; }
        }

        /// <summary>本地音乐库索引: 路径 → 文件名 → 标题+歌手 三级匹配。</summary>
        private sealed class LocalMusicIndex
        {
            private readonly Dictionary<string, Music> _byPath = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<Music>> _byStem = new(StringComparer.OrdinalIgnoreCase);
            private readonly List<Music> _musics = [];

            public static LocalMusicIndex Create(IEnumerable<Music>? musics)
            {
                var index = new LocalMusicIndex();
                if (musics is null) return index;
                foreach (var m in musics)
                {
                    if (string.IsNullOrEmpty(m.Path)) continue;
                    index._musics.Add(m);
                    index._byPath.TryAdd(NormalizePath(m.Path), m);
                    var stem = Path.GetFileNameWithoutExtension(m.Path);
                    if (stem.Length > 0)
                    {
                        if (!index._byStem.TryGetValue(stem, out var list)) index._byStem[stem] = list = [];
                        list.Add(m);
                    }
                }
                return index;
            }

            /// <summary>按路径匹配, 回退唯一同名文件, 再回退标题+歌手唯一匹配。</summary>
            public Music? Match(string path, string? title, string? artist)
            {
                if (_byPath.TryGetValue(NormalizePath(path), out var hit)) return hit;
                var stem = Path.GetFileNameWithoutExtension(path);
                if (stem.Length > 0 && _byStem.TryGetValue(stem, out var list) && list.Count == 1) return list[0];
                if (string.IsNullOrEmpty(title)) return null;
                var titleKey = NormalizeText(title);
                if (titleKey.Length == 0) return null;
                var artistKey = NormalizeText(artist ?? string.Empty);
                Music? matched = null;
                foreach (var m in _musics)
                {
                    if (NormalizeText(m.Title) != titleKey) continue;
                    if (artistKey.Length > 0 && NormalizeText(m.Author) != artistKey) continue;
                    if (matched is not null) return null; // 多个同名歌曲无法确定
                    matched = m;
                }
                return matched;
            }

            private static string NormalizePath(string value) => value.Trim().Replace('/', '\\').ToLowerInvariant();

            private static string NormalizeText(string value) =>
                Regex.Replace(value.ToLowerInvariant(), @"[（(].*?[）)]", string.Empty).Replace(" ", string.Empty).Trim();
        }
    }
}
