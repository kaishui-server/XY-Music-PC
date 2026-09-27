using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Services.Plugins;
using WinUIMusicPlayer.ViewModel;

namespace WinUIMusicPlayer.Services
{
    /// <summary>手机版备份导入结果(各分项计数, 由调用方组装本地化摘要)。</summary>
    public sealed class MobileBackupImportResult
    {
        public bool Ok { get; init; }
        public string? Error { get; init; }
        public int InstalledPlugins { get; set; }
        public int FailedPlugins { get; set; }
        public List<string> PluginErrors { get; } = [];
        public int AppliedUserVarPlugins { get; set; }
        public int CreatedPlaylists { get; set; }
        public int ReusedPlaylists { get; set; }
        public int ImportedOnlineSongs { get; set; }
        public int ImportedLocalSongs { get; set; }
        public int SkippedSongs { get; set; }
        public int LocalFavorites { get; set; }
        public int OnlineFavorites { get; set; }
    }

    /// <summary>
    /// 从手机版 XY Music 备份(xymusic-backup 格式)导入歌单、收藏、插件与用户变量。
    /// 备份中的主题/设置/本地曲库等其他内容忽略。
    /// 在线歌曲跨端兼容: 插件哈希按平台回退匹配、lx:// 路径还原 LX musicInfo(与云同步导入共用 NormalizeOnlineSong 逻辑)。
    /// </summary>
    public sealed class MobileBackupImportService
    {
        private const string LxPrefix = "lx://";
        private const string PluginPrefix = "plugin://";
        private const string MfPluginPrefix = "mfplugin://";

        private readonly PluginManagerService _plugins;
        private readonly MusicDatabaseService _db;
        private readonly AppViewModel _app;
        /// <summary>本次导入的手机插件ID → 桌面内容哈希(歌曲快照按手机ID引用插件, 播放需换算为桌面哈希)。</summary>
        private readonly Dictionary<string, string> _mobilePluginHashes = new(StringComparer.Ordinal);

        public MobileBackupImportService()
        {
            _plugins = App.Services.GetRequiredService<PluginManagerService>();
            _db = App.Services.GetRequiredService<MusicDatabaseService>();
            _app = App.Services.GetRequiredService<AppViewModel>();
        }

        public async Task<MobileBackupImportResult> ImportAsync(string backupJson)
        {
            _mobilePluginHashes.Clear();
            using var root = JsonDocument.Parse(backupJson);
            if (root.RootElement.ValueKind != JsonValueKind.Object
                || !TryGetString(root.RootElement, "format", out var format)
                || format != "xymusic-backup")
            {
                return new MobileBackupImportResult { Ok = false, Error = "invalid-format" };
            }
            var result = new MobileBackupImportResult { Ok = true };

            var prefs = TryGetObject(root.RootElement, "prefs");
            var pluginsEl = TryGetObject(root.RootElement, "plugins");
            var playlistsEl = ParsePrefValue(prefs, "mobilePlaylistsV1");
            var favoritePathsEl = ParsePrefValue(prefs, "favoritePaths");
            var favoriteMetaEl = ParsePrefValue(prefs, "favoriteSongMetadataV1");
            var userVarsEl = ParsePrefValue(prefs, "mobilePluginUserVariablesV1");

            // 先装插件再导歌单: NormalizeOnlineSong 按平台回退需要已安装的插件运行时
            if (pluginsEl is not null)
                await ImportPluginsAsync(pluginsEl.Value, userVarsEl, result);
            if (playlistsEl is { ValueKind: JsonValueKind.Array })
                await ImportPlaylistsAsync(playlistsEl.Value, result);
            if (favoritePathsEl is { ValueKind: JsonValueKind.Array })
                await ImportFavoritesAsync(favoritePathsEl.Value, favoriteMetaEl, result);
            return result;
        }

        private async Task ImportPluginsAsync(JsonElement pluginsEl, JsonElement? userVarsEl, MobileBackupImportResult result)
        {
            // 手机插件ID → 桌面端内容哈希(存入 _mobilePluginHashes 供歌曲导入换算)
            foreach (var prop in pluginsEl.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                var code = prop.Value.GetString();
                if (string.IsNullOrWhiteSpace(code)) continue;
                var (hash, error) = await _plugins.InstallFromCodeAsync(code);
                if (hash is not null)
                {
                    _mobilePluginHashes[prop.Name] = hash;
                    result.InstalledPlugins++;
                }
                else
                {
                    result.FailedPlugins++;
                    if (result.PluginErrors.Count < 10)
                        result.PluginErrors.Add($"{prop.Name}: {error}");
                }
            }

            if (userVarsEl is not { ValueKind: JsonValueKind.Object }) return;
            foreach (var prop in userVarsEl.Value.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                if (!_mobilePluginHashes.TryGetValue(prop.Name, out var hash)) continue;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kv in prop.Value.EnumerateObject())
                {
                    if (kv.Value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(kv.Name))
                        values[kv.Name] = kv.Value.GetString() ?? string.Empty;
                }
                if (values.Count == 0) continue;
                // LX 插件不支持用户变量, 安装成功的 MF 插件热重载使变量立即生效
                var (ok, _) = await _plugins.SetUserVariablesAsync(hash, values);
                if (ok) result.AppliedUserVarPlugins++;
            }
        }

        private async Task ImportPlaylistsAsync(JsonElement playlistsEl, MobileBackupImportResult result)
        {
            foreach (var pl in playlistsEl.EnumerateArray())
            {
                if (pl.ValueKind != JsonValueKind.Object) continue;
                var name = TryGetString(pl, "name", out var n) ? n : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var (localId, created) = await FindOrCreateOnlinePlayListAsync(name!);
                if (created) result.CreatedPlaylists++; else result.ReusedPlaylists++;

                var snapshots = pl.TryGetProperty("songSnapshots", out var snaps) && snaps.ValueKind == JsonValueKind.Object
                    ? snaps : (JsonElement?)null;
                if (!pl.TryGetProperty("songPaths", out var paths) || paths.ValueKind != JsonValueKind.Array) continue;
                foreach (var pathEl in paths.EnumerateArray())
                {
                    if (pathEl.ValueKind != JsonValueKind.String) continue;
                    var path = pathEl.GetString();
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    await AddSongToPlayListAsync(localId, path, snapshots, result);
                }
                await RefreshPlayListCountAsync(localId);
            }
        }

        private async Task ImportFavoritesAsync(JsonElement favPathsEl, JsonElement? favMetaEl, MobileBackupImportResult result)
        {
            // 收藏直达音乐库-收藏: 本地歌写入 Music 表收藏位, 在线歌写入在线收藏表(收藏视图合并展示)
            var localMatches = new List<Music>();
            var onlineSongs = new List<OnlineSong>();
            foreach (var pathEl in favPathsEl.EnumerateArray())
            {
                if (pathEl.ValueKind != JsonValueKind.String) continue;
                var path = pathEl.GetString();
                if (string.IsNullOrWhiteSpace(path)) continue;
                var song = TryGetSnapshot(favMetaEl, path, out var snap) ? ConvertSnapshotToOnlineSong(path, snap) : null;
                if (song is not null)
                    onlineSongs.Add(song);
                else
                {
                    var m = FindLocalMusicByFileName(path);
                    if (m is not null && !m.IsFavorite) localMatches.Add(m);
                }
            }
            if (onlineSongs.Count > 0)
                result.OnlineFavorites = await _db.AddOnlineFavoritesAsync(onlineSongs);
            if (localMatches.Count > 0)
            {
                await _db.AddMusicListToFavour(localMatches);
                result.LocalFavorites = localMatches.Count;
            }
        }

        private async Task AddSongToPlayListAsync(int playListId, string path, JsonElement? snapshots, MobileBackupImportResult result)
        {
            var song = TryGetSnapshot(snapshots, path, out var snap) ? ConvertSnapshotToOnlineSong(path, snap) : null;
            if (song is not null)
            {
                if (await _db.AddOnlineSongToPlayListAsync(playListId, song)) result.ImportedOnlineSongs++;
                return;
            }
            // 本地路径(手机存储路径): 按文件名匹配桌面本地库
            var m = FindLocalMusicByFileName(path);
            if (m is not null)
            {
                if (await _db.AddLocalMusicToOnlinePlayListAsync(playListId, m.Id)) result.ImportedLocalSongs++;
            }
            else
            {
                result.SkippedSongs++;
            }
        }

        /// <summary>手机版歌曲快照 → OnlineSong。返回 null 表示非在线歌曲(本地路径)或数据不足。</summary>
        private OnlineSong? ConvertSnapshotToOnlineSong(string path, JsonElement snap)
        {
            var title = TryGetString(snap, "title", out var t) ? t : string.Empty;
            var artist = TryGetString(snap, "artist", out var a) ? a : string.Empty;
            var album = TryGetString(snap, "album", out var al) ? al : string.Empty;
            var artwork = (TryGetString(snap, "coverUrl", out var cu) ? cu : null)
                ?? (TryGetString(snap, "coverThumbPath", out var ct) ? ct : null)
                ?? string.Empty;
            var durationSec = snap.TryGetProperty("duration", out var d) && d.TryGetInt32(out var di) ? di : 0;
            var mobilePluginId = TryGetString(snap, "pluginId", out var pid) ? pid : string.Empty;
            var pluginData = snap.TryGetProperty("pluginData", out var pd) && pd.ValueKind == JsonValueKind.Object
                ? pd : (JsonElement?)null;

            // lx://source/id: 标准 LX musicInfo 保存在 pluginData.lx
            if (path.StartsWith(LxPrefix, StringComparison.Ordinal))
            {
                var rest = path[LxPrefix.Length..].Split('/');
                if (rest.Length < 2) return null;
                var source = rest[0];
                var id = rest[^1];
                string rawJson;
                if (pluginData is not null
                    && pluginData.Value.TryGetProperty("lx", out var mi) && mi.ValueKind == JsonValueKind.Object)
                {
                    rawJson = mi.GetRawText();
                }
                else
                {
                    var minimal = new Dictionary<string, object?>
                    {
                        ["songmid"] = id,
                        ["source"] = source,
                        ["name"] = title,
                        ["singer"] = artist,
                        ["albumName"] = album,
                        ["interval"] = FormatInterval(durationSec),
                    };
                    rawJson = JsonSerializer.Serialize(minimal);
                }
                var lxSong = new OnlineSong
                {
                    Id = id,
                    Title = title,
                    Artist = artist,
                    Album = album,
                    Artwork = artwork,
                    DurationSec = durationSec,
                    Platform = source,
                    PluginHash = mobilePluginId,
                    PluginName = LxSources.DisplayName(source),
                    RawJson = rawJson,
                    IsLx = true,
                };
                RemapMobilePluginHash(lxSong);
                return lxSong;
            }

            // plugin://pluginId/id 或 mfplugin://pluginId/platform/id
            var prefix = path.StartsWith(PluginPrefix, StringComparison.Ordinal) ? PluginPrefix
                : path.StartsWith(MfPluginPrefix, StringComparison.Ordinal) ? MfPluginPrefix : null;
            if (prefix is null) return null;
            var parts = path[prefix.Length..].Split('/');
            if (parts.Length < 2) return null;
            var songId = parts[^1];
            var platform = pluginData is not null
                && pluginData.Value.TryGetProperty("platform", out var pf)
                && pf.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(pf.GetString())
                ? pf.GetString()!
                : (parts.Length >= 3 ? parts[1] : string.Empty);
            var song = new OnlineSong
            {
                Id = songId,
                Title = title,
                Artist = artist,
                Album = album,
                Artwork = artwork,
                DurationSec = durationSec,
                Platform = platform,
                PluginHash = mobilePluginId.Length > 0 ? mobilePluginId : parts[0],
                RawJson = pluginData is not null ? pluginData.Value.GetRawText() : string.Empty,
            };
            // 手机插件ID → 桌面哈希换算: 安装成功即换算, 后续按平台回退仅处理未随备份携带的插件
            RemapMobilePluginHash(song);
            // 跨端规范化: platform/id 从 RawJson 回填 + 插件哈希按平台回退匹配桌面已装插件
            _plugins.NormalizeOnlineSong(song);
            if (string.IsNullOrEmpty(song.Id) || string.IsNullOrEmpty(song.Platform) || string.IsNullOrEmpty(song.RawJson))
                return null;
            return song;
        }

        /// <summary>歌曲快照的 PluginHash 是手机插件ID, 换算为本次导入安装得到的桌面内容哈希。</summary>
        private void RemapMobilePluginHash(OnlineSong song)
        {
            if (string.IsNullOrEmpty(song.PluginHash)) return;
            if (_mobilePluginHashes.TryGetValue(song.PluginHash, out var hash) && hash != song.PluginHash)
            {
                song.PluginHash = hash;
                // MF 歌曲名称由 NormalizeOnlineSong 按桌面哈希回填; LX 歌曲名称是音源显示名, 保持不变
                if (!song.IsLx) song.PluginName = string.Empty;
            }
        }

        private static string FormatInterval(int durationSec)
        {
            var m = Math.Max(0, durationSec) / 60;
            var s = Math.Max(0, durationSec) % 60;
            return $"{m:00}:{s:00}";
        }

        private Music? FindLocalMusicByFileName(string path)
        {
            var fileName = Path.GetFileName(path.TrimEnd('/', '\\'));
            if (string.IsNullOrEmpty(fileName)) return null;
            foreach (var m in _app.SongsSource)
            {
                if (m is null || m.Id <= 0 || string.IsNullOrEmpty(m.Path)) continue;
                if (string.Equals(Path.GetFileName(m.Path), fileName, StringComparison.OrdinalIgnoreCase)) return m;
            }
            return null;
        }

        private async Task<(int Id, bool Created)> FindOrCreateOnlinePlayListAsync(string name)
        {
            var existing = _app.OnlinePlayLists.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
            if (existing is not null) return (existing.Id, false);
            var id = await _db.CreateOnlinePlayListAsync(name);
            var pl = new PlayList { Id = id, Name = name, SongCount = 0, IsOnline = 1 };
            RunOnUi(() => _app.OnlinePlayLists.Add(pl));
            return (id, true);
        }

        private async Task RefreshPlayListCountAsync(int playListId)
        {
            var count = (await _db.GetOnlinePlayListMusicsAsync(playListId)).Count;
            var pl = _app.OnlinePlayLists.FirstOrDefault(p => p.Id == playListId);
            if (pl is not null) pl.SongCount = count;
        }

        private static void RunOnUi(Action action)
        {
            if (App.MainWindow?.DispatcherQueue is { } dq)
                dq.TryEnqueue(() => action());
            else
                action();
        }

        // ─── 备份 JSON 解析辅助(prefs 各键为 {t:类型, v:字符串化JSON}) ──────────────

        private static bool TryGetSnapshot(JsonElement? snapshots, string path, out JsonElement snap)
        {
            snap = default;
            return snapshots is { ValueKind: JsonValueKind.Object }
                && snapshots.Value.TryGetProperty(path, out snap) && snap.ValueKind == JsonValueKind.Object;
        }

        private static JsonElement? ParsePrefValue(JsonElement? prefs, string key)
        {
            if (prefs is not { ValueKind: JsonValueKind.Object }) return null;
            if (!prefs.Value.TryGetProperty(key, out var pref) || pref.ValueKind != JsonValueKind.Object) return null;
            if (!pref.TryGetProperty("v", out var v)) return null;
            if (v.ValueKind is JsonValueKind.Array or JsonValueKind.Object) return v;
            if (v.ValueKind == JsonValueKind.String)
            {
                try
                {
                    using var doc = JsonDocument.Parse(v.GetString()!);
                    return doc.RootElement.ValueKind is JsonValueKind.Array or JsonValueKind.Object
                        ? doc.RootElement.Clone()
                        : null;
                }
                catch { return null; }
            }
            return null;
        }

        private static JsonElement? TryGetObject(JsonElement parent, string name)
            => parent.ValueKind == JsonValueKind.Object
               && parent.TryGetProperty(name, out var v)
               && v.ValueKind == JsonValueKind.Object
                ? v : (JsonElement?)null;

        private static bool TryGetString(JsonElement parent, string name, out string value)
        {
            value = string.Empty;
            return parent.ValueKind == JsonValueKind.Object
                && parent.TryGetProperty(name, out var v)
                && v.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(v.GetString())
                && (value = v.GetString()!) is not null;
        }
    }
}
