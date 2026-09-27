using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Services.Plugins;
using WinUIMusicPlayer.ViewModel;

namespace WinUIMusicPlayer.Services
{
    /// <summary>手机版备份导出结果(各分项计数, 由调用方组装本地化摘要)。</summary>
    public sealed class MobileBackupExportResult
    {
        public bool Ok { get; init; }
        public string? Error { get; init; }
        public int Plugins { get; set; }
        public int UserVarPlugins { get; set; }
        public int Playlists { get; set; }
        public int OnlineSongs { get; set; }
        public int LocalSongs { get; set; }
        public int SkippedSongs { get; set; }
        public int OnlineFavorites { get; set; }
        public int LocalFavorites { get; set; }
    }

    /// <summary>
    /// 把收藏、歌单、插件与用户变量导出为手机版 XY Music 备份(xymusic-backup v3 格式)。
    /// 与手机版 BackupService.exportBackup 互通: 手机版「备份恢复」可直接读取导入;
    /// 本应用「从手机版导入备份」也可读回(端到端回环)。
    /// 只迁移用户数据, 不导出本地曲库、主题与设置; 也不导出 mobileEnabledPlugins
    /// 等设备级状态键, 避免手机端恢复时覆盖其自身插件的启用状态。
    /// 插件以内容哈希为导出 ID(手机端插件 id 即 plugins 目录文件名),
    /// MF 歌曲路径 plugin://ID/歌曲id、LX 歌曲路径 lx://音源/歌曲id, 均与手机端原生格式一致。
    /// </summary>
    public sealed class MobileBackupExportService
    {
        private const string FormatId = "xymusic-backup";
        private const int FormatVersion = 3;

        private static readonly JsonSerializerOptions OnlineSongJsonOpts = new() { PropertyNameCaseInsensitive = true };
        private static readonly JsonSerializerOptions IndentedOpts = new() { WriteIndented = true };

        private readonly PluginManagerService _plugins;
        private readonly MusicDatabaseService _db;

        public MobileBackupExportService()
        {
            _plugins = App.Services.GetRequiredService<PluginManagerService>();
            _db = App.Services.GetRequiredService<MusicDatabaseService>();
        }

        public async Task<MobileBackupExportResult> ExportAsync(string filePath)
        {
            try
            {
                var result = new MobileBackupExportResult { Ok = true };
                var exportedAt = DateTimeOffset.Now;

                // 插件: 导出 ID = 内容哈希(手机端以此为插件文件名), 用户变量按插件 ID 归属
                var pluginCodes = new Dictionary<string, string>(StringComparer.Ordinal);
                var userVars = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                foreach (var (entry, code) in _plugins.GetPluginSyncItems())
                {
                    if (string.IsNullOrEmpty(entry.Hash)) continue;
                    pluginCodes[entry.Hash] = code;
                    // LX 插件不支持用户变量; MF/animemusic 变量按平台存, 导出时挂到插件 ID 上
                    if (entry.Format == "lx") continue;
                    var vars = _plugins.GetUserVariables(entry.Platform);
                    if (vars.Count > 0) userVars[entry.Hash] = vars;
                }
                result.Plugins = pluginCodes.Count;
                result.UserVarPlugins = userVars.Count;

                var localMusics = await _db.GetAllMusicsRawAsync();
                var localById = localMusics.Where(m => m.Id > 0).ToDictionary(m => m.Id);

                var favorites = await BuildFavoritesAsync(localMusics, result);
                var playlists = await BuildPlaylistsAsync(localById, exportedAt, result);

                var prefs = new JsonObject
                {
                    ["favoritePaths"] = StringListPref(favorites.Paths),
                };
                if (favorites.Metadata.Count > 0)
                    prefs["favoriteSongMetadataV1"] = StringPref(favorites.Metadata.ToJsonString());
                if (playlists.Count > 0)
                    prefs["mobilePlaylistsV1"] = StringPref(JsonSerializer.Serialize(playlists));
                if (userVars.Count > 0)
                    prefs["mobilePluginUserVariablesV1"] = StringPref(JsonSerializer.Serialize(userVars));

                var payload = new JsonObject
                {
                    ["format"] = FormatId,
                    ["version"] = FormatVersion,
                    ["exportedAt"] = exportedAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz"),
                    ["prefs"] = prefs,
                    ["plugins"] = new JsonObject(pluginCodes.Select(kv =>
                        KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
                    ["library"] = new JsonObject(),
                };

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
                File.WriteAllText(filePath, payload.ToJsonString(IndentedOpts), new UTF8Encoding(false));
                return result;
            }
            catch (Exception ex)
            {
                return new MobileBackupExportResult { Ok = false, Error = ex.Message };
            }
        }

        private async Task<(List<string> Paths, JsonObject Metadata)> BuildFavoritesAsync(
            List<Music> localMusics, MobileBackupExportResult result)
        {
            // 合并顺序与收藏视图一致: 在线收藏(Order 升序) + 本地收藏(Music.Order), 整体按 Order 倒序
            var online = new List<(string Path, JsonObject? Snapshot, int Order)>();
            foreach (var (song, order) in await _db.GetOnlineFavoriteSongsAsync())
            {
                var snap = BuildOnlineSnapshot(song);
                if (snap is null) { result.SkippedSongs++; continue; }
                online.Add((snap.Value.Path, snap.Value.Snapshot, order));
                result.OnlineFavorites++;
            }
            var local = localMusics
                .Where(m => m.IsFavorite && !string.IsNullOrEmpty(m.Path))
                // 本地收藏与手机端原生行为一致: 只存路径, 不建快照(文件在异机不存在时不显示)
                .Select(m => (Path: m.Path, Snapshot: (JsonObject?)null, Order: m.Order))
                .ToList();
            result.LocalFavorites = local.Count;
            var merged = online.Concat(local)
                .OrderByDescending(t => t.Order)
                .Select(t => (t.Path, t.Snapshot))
                .ToList();

            var paths = new List<string>(merged.Count);
            var metadata = new JsonObject();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (path, snapshot) in merged)
            {
                if (!seen.Add(path)) continue;
                paths.Add(path);
                if (snapshot is not null) metadata[path] = snapshot;
            }
            return (paths, metadata);
        }

        private async Task<List<JsonObject>> BuildPlaylistsAsync(
            Dictionary<int, Music> localById, DateTimeOffset exportedAt, MobileBackupExportResult result)
        {
            var playlists = new List<JsonObject>();
            foreach (var pl in await _db.GetAllPlayListsAsync())
            {
                if (string.IsNullOrWhiteSpace(pl.Name)) continue;
                var songPaths = new List<string>();
                var snapshots = new JsonObject();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                if (pl.IsOnline == 1)
                {
                    foreach (var entry in await _db.GetOnlinePlayListMusicsAsync(pl.Id))
                    {
                        string path;
                        JsonObject? snapshot;
                        if (entry.MusicId > 0)
                        {
                            if (!localById.TryGetValue(entry.MusicId, out var m) || string.IsNullOrEmpty(m.Path))
                            {
                                result.SkippedSongs++;
                                continue;
                            }
                            path = m.Path;
                            snapshot = BuildLocalSnapshot(m);
                            result.LocalSongs++;
                        }
                        else
                        {
                            OnlineSong? song = null;
                            if (!string.IsNullOrEmpty(entry.SongJson))
                            {
                                try { song = JsonSerializer.Deserialize<OnlineSong>(entry.SongJson, OnlineSongJsonOpts); }
                                catch { /* 损坏条目按不存在处理 */ }
                            }
                            var snap = song is null ? null : BuildOnlineSnapshot(song);
                            if (snap is null) { result.SkippedSongs++; continue; }
                            path = snap.Value.Path;
                            snapshot = snap.Value.Snapshot;
                            result.OnlineSongs++;
                        }
                        if (!seen.Add(path)) continue;
                        songPaths.Add(path);
                        snapshots[path] = snapshot;
                    }
                }
                else
                {
                    foreach (var entry in await _db.GetPlayListMusicsAsync(pl.Id))
                    {
                        if (!localById.TryGetValue(entry.MusicId, out var m) || string.IsNullOrEmpty(m.Path))
                        {
                            result.SkippedSongs++;
                            continue;
                        }
                        if (!seen.Add(m.Path)) continue;
                        songPaths.Add(m.Path);
                        snapshots[m.Path] = BuildLocalSnapshot(m);
                        result.LocalSongs++;
                    }
                }

                playlists.Add(new JsonObject
                {
                    ["id"] = pl.Id.ToString(),
                    ["name"] = pl.Name,
                    ["songPaths"] = new JsonArray(songPaths.Select(p => (JsonNode?)p).ToArray()),
                    ["createdAt"] = exportedAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz"),
                    ["songSnapshots"] = snapshots,
                });
                result.Playlists++;
            }
            return playlists;
        }

        /// <summary>在线歌曲 → 手机端歌曲路径 + 快照。返回 null 表示数据不足(损坏条目), 导出时跳过。</summary>
        private static (string Path, JsonObject Snapshot)? BuildOnlineSnapshot(OnlineSong song)
        {
            if (string.IsNullOrEmpty(song.Id) || string.IsNullOrEmpty(song.Platform)) return null;
            JsonNode? pluginData;
            if (song.IsLx)
            {
                // 手机端 LX 歌曲 pluginData = {'lx': musicInfo}(PC 端 RawJson 即 musicInfo JSON)
                var info = ParseJsonObject(song.RawJson);
                if (info is null) return null;
                pluginData = new JsonObject { ["lx"] = info };
            }
            else
            {
                // MF 歌曲插件 id 为导出键(哈希), 手机端播放按 pluginId 找插件、musicItem 喂 getMediaSource
                if (string.IsNullOrEmpty(song.PluginHash)) return null;
                var item = ParseJsonObject(song.RawJson);
                if (item is null) return null;
                // PC 端导入回读时优先从 pluginData.platform 取平台, 缺失时补齐保证跨端可解析
                var platformProp = item["platform"];
                var hasPlatform = platformProp is JsonValue pv
                    && pv.TryGetValue<string>(out var pf)
                    && !string.IsNullOrEmpty(pf);
                if (!hasPlatform && !string.IsNullOrEmpty(song.Platform))
                    item["platform"] = song.Platform;
                pluginData = item;
            }
            var path = song.IsLx
                ? $"lx://{song.Platform}/{Uri.EscapeDataString(song.Id)}"
                : $"plugin://{song.PluginHash}/{Uri.EscapeDataString(song.Id)}";
            var snapshot = new JsonObject
            {
                ["path"] = path,
                ["title"] = song.Title ?? string.Empty,
                ["artist"] = song.Artist ?? string.Empty,
                ["album"] = song.Album ?? string.Empty,
                ["duration"] = Math.Max(0, (int)Math.Round(song.DurationSec)),
                ["format"] = "网络",
                ["pluginId"] = song.PluginHash,
                ["pluginData"] = pluginData,
            };
            if (!string.IsNullOrEmpty(song.Artwork)) snapshot["coverUrl"] = song.Artwork;
            return (path, snapshot);
        }

        /// <summary>本地歌曲 → 手机端歌单快照(路径 + 元数据, 无插件归属; 异机按文件名匹配恢复)。</summary>
        private static JsonObject BuildLocalSnapshot(Music m) => new()
        {
            ["path"] = m.Path ?? string.Empty,
            ["title"] = m.Title ?? string.Empty,
            ["artist"] = m.Author ?? string.Empty,
            ["album"] = m.Album ?? string.Empty,
            ["duration"] = Math.Max(0, (int)Math.Round(m.Duration.TotalSeconds)),
            ["format"] = string.IsNullOrWhiteSpace(m.Extension) ? "本地" : m.Extension,
        };

        private static JsonNode? ParseJsonObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonNode.Parse(json) is JsonObject obj ? obj : null;
            }
            catch { return null; }
        }

        // prefs 条目编码: 与手机端 _encodeValue 一致 {'t': 's'|'sl', 'v': ...}
        private static JsonObject StringPref(string value) => new() { ["t"] = "s", ["v"] = value };

        private static JsonObject StringListPref(IEnumerable<string> values) => new()
        {
            ["t"] = "sl",
            ["v"] = new JsonArray(values.Select(v => (JsonNode?)v).ToArray()),
        };
    }
}
