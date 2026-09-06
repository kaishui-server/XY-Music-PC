using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Services.Plugins;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.ViewModel;

namespace WinUIMusicPlayer.Services.Account
{
    /// <summary>云同步频率, 与手机版一致。</summary>
    public enum CloudSyncFrequency
    {
        FiveMinutes,
        FifteenMinutes,
        ThirtyMinutes,
        OneHour,
        SixHours,
        TwelveHours,
        OneDay,
        Manual,
    }

    /// <summary>一次云同步的结果统计。</summary>
    public class CloudSyncResult
    {
        public bool NoChange { get; set; }
        public int UploadedPlaylists { get; set; }
        public int UploadedSongs { get; set; }
        public int DownloadedPlaylists { get; set; }
        public int DownloadedSongs { get; set; }
        public int DownloadedFavorites { get; set; }
        public int UploadedPlugins { get; set; }
        public int DownloadedPlugins { get; set; }
        public List<string> PluginErrors { get; set; } = [];
    }

    /// <summary>
    /// 账号云同步: 歌单+收藏+插件, 与手机版/服务端相同的分块上传协议。
    /// 本地歌曲仅同步元数据和路径(不上传文件); 在线歌曲仅同步插件链接(虚拟路径+原始数据)。
    /// 状态持久化到 %LOCALAPPDATA%\XYMusic\Account\cloud-sync-state.json。
    /// </summary>
    public class AccountCloudSyncService
    {
        private const int MaxSongsPerChunk = 500;
        private const int MaxPluginBytes = 5 * 1024 * 1024;

        private readonly AuthService _auth;
        private readonly PluginManagerService _plugins;
        private readonly MusicDatabaseService _db;
        private readonly ILogger<AccountCloudSyncService> _logger;

        private static string StateDir => Path.Combine(AppPaths.LocalFolder, "Account");
        private static string StateFile => Path.Combine(StateDir, "cloud-sync-state.json");
        private readonly object _stateGate = new();

        private System.Threading.Timer? _autoTimer;
        private bool _autoUploading;
        private int _autoStartGeneration;
        /// <summary>串行化同步操作, 避免手动/自动并发写云数据。</summary>
        private readonly SemaphoreSlim _syncGate = new(1, 1);

        /// <summary>同步完成(手动/自动), 参数: 是否手动, 结果(失败时为 null), 错误信息。</summary>
        public event Action<bool, CloudSyncResult?, string?>? SyncCompleted;

        public AccountCloudSyncService(
            AuthService auth,
            PluginManagerService plugins,
            MusicDatabaseService db,
            ILogger<AccountCloudSyncService> logger)
        {
            _auth = auth;
            _plugins = plugins;
            _db = db;
            _logger = logger;
            try { Directory.CreateDirectory(StateDir); } catch { }
        }

        // ─── 每账号状态 ─────────────────────────────────

        private sealed class AccountSyncState
        {
            public bool Enabled { get; set; }
            public bool Prompted { get; set; }
            public string Frequency { get; set; } = nameof(CloudSyncFrequency.ThirtyMinutes);
            public DateTime? LastManual { get; set; }
            public string? LastUploadHash { get; set; }
            public string? LastPluginsHash { get; set; }
            /// <summary>云端歌单 ID → 本地歌单 Id。</summary>
            public Dictionary<string, int> PlaylistLinks { get; set; } = [];
        }

        private sealed class SyncStateFile
        {
            public Dictionary<string, AccountSyncState> Accounts { get; set; } = [];
        }

        private Dictionary<string, AccountSyncState> _stateCache = new();
        private bool _stateLoaded;

        private void EnsureStateLoaded()
        {
            if (_stateLoaded) return;
            lock (_stateGate)
            {
                if (_stateLoaded) return;
                try
                {
                    if (File.Exists(StateFile))
                        _stateCache = JsonSerializer.Deserialize<SyncStateFile>(File.ReadAllText(StateFile))?.Accounts ?? [];
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "读取云同步状态失败");
                    _stateCache = [];
                }
                _stateLoaded = true;
            }
        }

        private void SaveStateLocked()
        {
            try
            {
                Directory.CreateDirectory(StateDir);
                File.WriteAllText(StateFile, JsonSerializer.Serialize(new SyncStateFile { Accounts = _stateCache }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "保存云同步状态失败");
            }
        }

        private AccountSyncState StateOf(string accountId)
        {
            EnsureStateLoaded();
            lock (_stateGate)
            {
                if (!_stateCache.TryGetValue(accountId, out var st))
                {
                    st = new AccountSyncState();
                    _stateCache[accountId] = st;
                }
                return st;
            }
        }

        // ─── 配置读写(供账号页/弹窗调用) ─────────────────

        public bool IsEnabled(string accountId)
        {
            EnsureStateLoaded();
            lock (_stateGate) return _stateCache.TryGetValue(accountId, out var st) && st.Enabled;
        }

        public void SetEnabled(string accountId, bool enabled)
        {
            var st = StateOf(accountId);
            lock (_stateGate)
            {
                st.Enabled = enabled;
                SaveStateLocked();
            }
        }

        public bool HasPrompted(string accountId)
        {
            EnsureStateLoaded();
            lock (_stateGate) return _stateCache.TryGetValue(accountId, out var st) && st.Prompted;
        }

        public void MarkPrompted(string accountId)
        {
            var st = StateOf(accountId);
            lock (_stateGate)
            {
                st.Prompted = true;
                SaveStateLocked();
            }
        }

        public CloudSyncFrequency GetFrequency(string accountId)
        {
            EnsureStateLoaded();
            string name;
            lock (_stateGate)
                name = _stateCache.TryGetValue(accountId, out var st) ? st.Frequency : nameof(CloudSyncFrequency.ThirtyMinutes);
            return Enum.TryParse<CloudSyncFrequency>(name, out var f) ? f : CloudSyncFrequency.ThirtyMinutes;
        }

        public void SetFrequency(string accountId, CloudSyncFrequency value)
        {
            var st = StateOf(accountId);
            lock (_stateGate)
            {
                st.Frequency = value.ToString();
                SaveStateLocked();
            }
        }

        public DateTime? GetLastManualSync(string accountId)
        {
            EnsureStateLoaded();
            lock (_stateGate) return _stateCache.TryGetValue(accountId, out var st) ? st.LastManual : null;
        }

        // ─── 对外同步入口 ─────────────────────────────────

        /// <summary>完整同步(手动): 并行拉取 → 本地合并 → 并行上传(歌单与插件两路并行)。返回错误信息(null=成功)。</summary>
        public async Task<(CloudSyncResult? Result, string? Error)> SyncAllAsync(bool manual)
        {
            if (string.IsNullOrEmpty(_auth.AccountId))
                return (null, "请先登录账号");
            await _syncGate.WaitAsync();
            try
            {
                var accountId = _auth.AccountId;
                var result = new CloudSyncResult();

                // 1) 并行拉取云端数据: 歌单快照 + 插件(下载并恢复缺失插件)
                var cloudTask = _auth.RequestActionAsync("file_sync_download", new { user_id = accountId }, 60000);
                var pluginDownloadTask = DownloadPluginsAsync();
                var cloudData = await cloudTask;
                var (downloadedPlugins, pluginDownloadErrors) = await pluginDownloadTask;

                // 2) 合并云端歌单/收藏到本地
                var downloaded = await MergeCloudDataAsync(cloudData, accountId);

                // 3) 构建合并后的本地快照(收藏透传复用已下载的云端数据, 避免重复整包请求)
                var payload = await BuildPlaylistPayloadAsync();
                var favorites = await BuildFavoritesPayloadAsync(cloudData);
                var hash = PayloadHash(payload, favorites);
                bool uploadSkipped;
                lock (_stateGate)
                    uploadSkipped = StateOf(accountId).LastUploadHash == hash;

                // 4) 并行上传: 歌单快照(分块并行) 与 插件逐个上传(服务端读改写需串行) 互不依赖, 两路并行
                var uploadTask = uploadSkipped
                    ? Task.FromResult(new CloudSyncResult { NoChange = true })
                    : UploadPayloadAsync(accountId, payload, favorites);
                var pluginUploadTask = UploadPluginsIfChangedAsync();
                await Task.WhenAll(uploadTask, pluginUploadTask);
                var uploaded = await uploadTask;
                var (pluginNoChange, uploadedPlugins, pluginUploadErrors) = await pluginUploadTask;

                result.NoChange = pluginNoChange && (uploadSkipped || uploaded.NoChange);
                result.UploadedPlaylists = uploaded.UploadedPlaylists;
                result.UploadedSongs = uploaded.UploadedSongs;
                result.DownloadedPlaylists = downloaded.DownloadedPlaylists;
                result.DownloadedSongs = downloaded.DownloadedSongs;
                result.DownloadedFavorites = downloaded.DownloadedFavorites;
                result.UploadedPlugins = uploadedPlugins;
                result.DownloadedPlugins = downloadedPlugins;
                result.PluginErrors = [.. pluginDownloadErrors, .. pluginUploadErrors];

                if (!uploadSkipped)
                {
                    lock (_stateGate)
                    {
                        StateOf(accountId).LastUploadHash = hash;
                        SaveStateLocked();
                    }
                }

                if (manual)
                {
                    StateOf(accountId).LastManual = DateTime.Now;
                    lock (_stateGate) SaveStateLocked();
                }
                SyncCompleted?.Invoke(manual, result, null);
                return (result, null);
            }
            catch (AuthException ex)
            {
                SyncCompleted?.Invoke(manual, null, ex.Message);
                return (null, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "云同步失败");
                SyncCompleted?.Invoke(manual, null, "同步失败：" + ex.Message);
                return (null, "同步失败：" + ex.Message);
            }
            finally
            {
                _syncGate.Release();
            }
        }

        // ─── 上传 ─────────────────────────────────

        /// <summary>上传完整快照(自动同步用, 不做哈希跳过, 自愈服务端不完整数据)。</summary>
        public async Task<CloudSyncResult> UploadAsync()
        {
            var accountId = RequireAccountId();
            await _syncGate.WaitAsync();
            try
            {
                var payload = await BuildPlaylistPayloadAsync();
                var favorites = await BuildFavoritesPayloadAsync();
                var result = await UploadPayloadAsync(accountId, payload, favorites);
                lock (_stateGate)
                {
                    StateOf(accountId).LastUploadHash = PayloadHash(payload, favorites);
                    SaveStateLocked();
                }
                return result;
            }
            finally
            {
                _syncGate.Release();
            }
        }

        private async Task<CloudSyncResult> UploadPayloadAsync(
            string accountId,
            List<Dictionary<string, object?>> payload,
            List<Dictionary<string, object?>> favorites)
        {
            await _auth.RequestActionAsync("file_sync_upload_start", new { user_id = accountId }, 30000);

            // 按每块最多 500 首歌分块
            var chunks = new List<List<Dictionary<string, object?>>>();
            var current = new List<Dictionary<string, object?>>();
            int songCount = 0;
            foreach (var item in payload)
            {
                var songs = (List<object?>)item["songs"]!;
                if (current.Count > 0 && songCount + songs.Count > MaxSongsPerChunk)
                {
                    chunks.Add(current);
                    current = [];
                    songCount = 0;
                }
                current.Add(item);
                songCount += songs.Count;
            }
            if (current.Count > 0 || chunks.Count == 0) chunks.Add(current);

            // 并行上传各分块: 服务端按 chunk_index 写独立文件, 互不依赖
            await Task.WhenAll(chunks.Select((chunk, index) => _auth.RequestActionAsync("file_sync_upload_chunk", new
            {
                user_id = accountId,
                chunk_index = index,
                total_chunks = chunks.Count,
                chunk_data = chunk,
            }, 60000)));

            var data = await _auth.RequestActionAsync("file_sync_upload_finish", new
            {
                user_id = accountId,
                favorites,
            }, 60000);
            return new CloudSyncResult
            {
                UploadedPlaylists = data.TryGetProperty("playlist_count", out var pc) ? pc.GetInt32() : payload.Count,
                UploadedSongs = data.TryGetProperty("song_total", out var st) ? st.GetInt32() : payload.Sum(p => ((List<object?>)p["songs"]!).Count),
            };
        }

        // ─── 构建上传快照 ─────────────────────────────────

        /// <summary>读取全部歌单并转为云端格式(本地+在线歌单都同步)。</summary>
        private async Task<List<Dictionary<string, object?>>> BuildPlaylistPayloadAsync()
        {
            var accountId = RequireAccountId();
            var allMusic = (await _db.GetAllMusicsRawAsync()).ToDictionary(m => m.Id, m => m);
            var playlists = await _db.GetAllPlayListsAsync();

            var result = new List<Dictionary<string, object?>>();
            foreach (var pl in playlists.OrderBy(p => p.Id))
            {
                // 反查歌单的云端 ID, 没有则分配并持久化
                string? cloudId = null;
                lock (_stateGate)
                {
                    cloudId = StateOf(accountId).PlaylistLinks.FirstOrDefault(kv => kv.Value == pl.Id).Key;
                    if (string.IsNullOrEmpty(cloudId))
                    {
                        cloudId = $"pl-{Guid.NewGuid():N}";
                        StateOf(accountId).PlaylistLinks[cloudId] = pl.Id;
                        SaveStateLocked();
                    }
                }

                var songs = new List<object?>();
                if (pl.IsOnline == 1)
                {
                    var entries = await _db.GetOnlinePlayListMusicsAsync(pl.Id);
                    foreach (var e in entries.OrderBy(e => e.Order))
                    {
                        if (e.MusicId > 0)
                        {
                            if (allMusic.TryGetValue(e.MusicId, out var m)) songs.Add(LocalSongPayload(m));
                        }
                        else if (!string.IsNullOrEmpty(e.SongJson))
                        {
                            try
                            {
                                var song = JsonSerializer.Deserialize<OnlineSong>(e.SongJson, OnlineSongJsonOpts);
                                if (song is not null) songs.Add(OnlineSongPayload(song));
                            }
                            catch { /* 损坏条目跳过 */ }
                        }
                    }
                }
                else
                {
                    var entries = await _db.GetPlayListMusicsAsync(pl.Id);
                    foreach (var e in entries.OrderBy(e => e.Order))
                    {
                        if (allMusic.TryGetValue(e.MusicId, out var m)) songs.Add(LocalSongPayload(m));
                    }
                }
                result.Add(new Dictionary<string, object?>
                {
                    ["id"] = cloudId,
                    ["name"] = pl.Name,
                    ["type"] = "mixed",
                    ["createdAt"] = DateTime.Now.ToString("o"),
                    ["cloudCoverUrl"] = null,
                    ["isFavorite"] = false,
                    ["songs"] = songs,
                });
            }
            return result;
        }

        private static readonly JsonSerializerOptions OnlineSongJsonOpts = new() { PropertyNameCaseInsensitive = true };

        /// <summary>收藏快照: 本地收藏 + 云端在线收藏透传(避免覆盖手机端的在线收藏)。cloudData 传入已下载的云端数据可省一次整包请求。</summary>
        private async Task<List<Dictionary<string, object?>>> BuildFavoritesPayloadAsync(JsonElement? cloudData = null)
        {
            var favorites = new List<Dictionary<string, object?>>();
            foreach (var m in (await _db.GetAllMusicsRawAsync()).Where(m => m.IsFavorite).OrderBy(m => m.Order))
                favorites.Add(LocalSongPayload(m));

            // 透传云端已有在线收藏(source_type=plugin)
            try
            {
                var data = cloudData ?? await _auth.RequestActionAsync("file_sync_download", new { user_id = RequireAccountId() }, 60000);
                if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("favorites", out var favs) && favs.ValueKind == JsonValueKind.Array)
                {
                    var localPaths = favorites.Select(f => (f["path"] as string)?.Trim()).ToHashSet();
                    foreach (var f in favs.EnumerateArray())
                    {
                        if (f.ValueKind != JsonValueKind.Object) continue;
                        var sourceType = f.TryGetProperty("source_type", out var st) ? st.GetString() : null;
                        var path = f.TryGetProperty("path", out var p) ? p.GetString() : null;
                        if (sourceType != "plugin" || string.IsNullOrWhiteSpace(path)) continue;
                        if (!localPaths.Add(path.Trim())) continue;
                        favorites.Add(f.EnumerateObject().ToDictionary(prop => prop.Name, prop => (object?)prop.Value.Clone()));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取云端收藏透传失败, 仅上传本地收藏");
            }
            return favorites;
        }

        private static Dictionary<string, object?> LocalSongPayload(Music m)
        {
            return new Dictionary<string, object?>
            {
                ["path"] = m.Path,
                ["name"] = m.Title,
                ["title"] = m.Title,
                ["artist"] = m.Author,
                ["album"] = m.Album,
                ["duration"] = (int)m.Duration.TotalSeconds,
                ["format"] = string.IsNullOrEmpty(m.Extension) ? "本地" : m.Extension,
                ["source_type"] = "local",
                ["syncType"] = "local",
            };
        }

        private static Dictionary<string, object?> OnlineSongPayload(OnlineSong s)
        {
            return new Dictionary<string, object?>
            {
                ["path"] = s.VirtualPath,
                ["name"] = s.Title,
                ["title"] = s.Title,
                ["artist"] = s.Artist,
                ["album"] = s.Album,
                ["duration"] = (int)s.DurationSec,
                ["format"] = "网络",
                ["source_type"] = "plugin",
                ["syncType"] = "online",
                ["pluginId"] = s.PluginHash,
                ["pluginName"] = s.PluginName,
                ["platform"] = s.Platform,
                ["pluginData"] = s.RawJson,
                ["coverUrl"] = s.Artwork,
                ["id"] = s.Id,
                ["isLx"] = s.IsLx,
            };
        }

        private static string PayloadHash(List<Dictionary<string, object?>> payload, List<Dictionary<string, object?>> favorites)
        {
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("playlists");
                JsonSerializer.Serialize(writer, payload);
                writer.WritePropertyName("favorites");
                JsonSerializer.Serialize(writer, favorites);
                writer.WriteEndObject();
            }
            return Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant();
        }

        // ─── 下载合并 ─────────────────────────────────

        public async Task<CloudSyncResult> DownloadAsync()
        {
            var accountId = RequireAccountId();
            var data = await _auth.RequestActionAsync("file_sync_download", new { user_id = accountId }, 60000);
            return await MergeCloudDataAsync(data, accountId);
        }

        /// <summary>把已下载的云端快照合并进本地(歌单+收藏)。</summary>
        private async Task<CloudSyncResult> MergeCloudDataAsync(JsonElement data, string accountId)
        {
            var result = new CloudSyncResult();

            var allMusic = await _db.GetAllMusicsRawAsync();
            var musicByPath = allMusic
                .Where(m => !string.IsNullOrEmpty(m.Path))
                .GroupBy(m => m.Path.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var allPlaylists = await _db.GetAllPlayListsAsync();

            // 1) 歌单合并(按云端歌单 ID 映射本地歌单, 缺失则新建在线歌单)
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("playlists", out var pls) && pls.ValueKind == JsonValueKind.Array)
            {
                foreach (var pl in pls.EnumerateArray())
                {
                    if (pl.ValueKind != JsonValueKind.Object) continue;
                    var cloudId = pl.TryGetProperty("id", out var idEl) ? idEl.GetString()?.Trim() : null;
                    if (string.IsNullOrEmpty(cloudId)) continue;
                    var name = pl.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                    name = string.IsNullOrWhiteSpace(name) ? "未命名歌单" : name!;

                    // 跳过手机版旧"我喜欢"伪装歌单(收藏独立同步)
                    if (pl.TryGetProperty("isFavorite", out var favEl) && favEl.ValueKind == JsonValueKind.True) continue;
                    if (name == "我喜欢的音乐" || name == "我喜欢") continue;

                    // 查映射: 云端 ID → 本地歌单
                    int localId = 0;
                    bool linked = false;
                    lock (_stateGate)
                    {
                        if (StateOf(accountId).PlaylistLinks.TryGetValue(cloudId, out localId))
                            linked = allPlaylists.Any(p => p.Id == localId);
                    }
                    PlayList target;
                    if (linked)
                    {
                        target = allPlaylists.First(p => p.Id == localId);
                        if (!string.Equals(target.Name, name, StringComparison.Ordinal))
                        {
                            target.Name = name;
                            await _db.UpdatePlayList(target);
                        }
                    }
                    else
                    {
                        localId = await _db.CreateOnlinePlayListAsync(name);
                        lock (_stateGate)
                        {
                            StateOf(accountId).PlaylistLinks[cloudId] = localId;
                            SaveStateLocked();
                        }
                        target = new PlayList { Id = localId, Name = name, SongCount = 0, IsOnline = 1 };
                        allPlaylists.Add(target);
                        AddOnlinePlayListToUi(target);
                    }
                    await MergeCloudPlaylistSongsAsync(target, pl, musicByPath, result);
                    result.DownloadedPlaylists++;
                }
            }

            // 2) 收藏合并(按本地路径匹配)
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("favorites", out var favsEl) && favsEl.ValueKind == JsonValueKind.Array)
            {
                var appVm = App.Services.GetRequiredService<AppViewModel>();
                foreach (var f in favsEl.EnumerateArray())
                {
                    if (f.ValueKind != JsonValueKind.Object) continue;
                    var sourceType = f.TryGetProperty("source_type", out var st) ? st.GetString() : null;
                    if (sourceType == "plugin") continue; // 在线收藏桌面端暂不落库, 透传保留在云端
                    var path = f.TryGetProperty("path", out var p) ? p.GetString() : null;
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    if (!musicByPath.TryGetValue(path.Trim(), out var m)) continue;
                    if (m.IsFavorite) continue;
                    m.IsFavorite = true;
                    await _db.UpdateMusicInfo(m);
                    if (appVm.TryFindById(m.Id, out var inMem) && inMem is not null) inMem.IsFavorite = true;
                    result.DownloadedFavorites++;
                }
            }
            return result;
        }

        /// <summary>把云端歌单的歌曲合并进本地歌单(去重: 在线按虚拟路径, 本地按 MusicId)。
        /// 在线歌单可同时收在线歌曲与本地歌曲; 本地歌单(队列式)仅收本地歌曲。</summary>
        private async Task MergeCloudPlaylistSongsAsync(
            PlayList target,
            JsonElement pl,
            Dictionary<string, Music> musicByPath,
            CloudSyncResult result)
        {
            if (!pl.TryGetProperty("songs", out var songsEl) || songsEl.ValueKind != JsonValueKind.Array) return;
            // 先清除旧版跨端同步遗留的损坏在线条目(手机插件 id/空 RawJson, 播放必失败), 由本次合并按修正后数据重建
            if (target.IsOnline == 1)
                await _db.RemoveBrokenOnlineSongsAsync(target.Id);
            var localSongs = new List<Music>();
            foreach (var song in songsEl.EnumerateArray())
            {
                if (song.ValueKind != JsonValueKind.Object) continue;
                var (isOnline, onlineSong, localPath) = ParseCloudSong(song);
                if (isOnline)
                {
                    if (target.IsOnline == 1 && onlineSong is not null)
                    {
                        if (await _db.AddOnlineSongToPlayListAsync(target.Id, onlineSong))
                            result.DownloadedSongs++;
                    }
                    // 本地歌单不支持在线歌曲条目, 跳过(云端数据保留)
                }
                else if (!string.IsNullOrWhiteSpace(localPath) && musicByPath.TryGetValue(localPath.Trim(), out var m))
                {
                    if (target.IsOnline == 1)
                    {
                        if (await _db.AddLocalMusicToOnlinePlayListAsync(target.Id, m.Id))
                            result.DownloadedSongs++;
                    }
                    else
                    {
                        localSongs.Add(m);
                    }
                }
            }
            if (target.IsOnline == 0 && localSongs.Count > 0)
            {
                await _db.AddMusicListToPlayList(localSongs, target.Id);
                result.DownloadedSongs += localSongs.Count;
            }
            if (target.IsOnline == 1)
                target.SongCount = (await _db.GetOnlinePlayListMusicsAsync(target.Id)).Count;
        }

        private static void AddOnlinePlayListToUi(PlayList pl)
        {
            App.MainWindow?.DispatcherQueue?.TryEnqueue(() =>
                App.Services.GetRequiredService<AppViewModel>().OnlinePlayLists.Add(pl));
        }

        /// <summary>解析云端歌曲条目: 在线歌曲 / 本地歌曲路径。在线歌曲做跨端兼容修正(插件哈希按平台回退、pluginData 支持嵌套对象)。</summary>
        private (bool IsOnline, OnlineSong? Song, string? LocalPath) ParseCloudSong(JsonElement song)
        {
            string? Str(params string[] keys)
            {
                foreach (var k in keys)
                    if (song.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.GetString()))
                        return v.GetString();
                return null;
            }
            // 手机端 pluginData 上传为嵌套 JSON 对象, 桌面端为序列化字符串, 两种形态都接受
            string RawJson(params string[] keys)
            {
                foreach (var k in keys)
                    if (song.TryGetProperty(k, out var v))
                    {
                        if (v.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.GetString()))
                            return v.GetString()!;
                        if (v.ValueKind == JsonValueKind.Object)
                            return v.GetRawText();
                    }
                return string.Empty;
            }
            var sourceType = Str("source_type");
            var syncType = Str("syncType");
            var path = Str("path");
            var isOnline = sourceType == "plugin" || syncType == "online"
                || (!string.IsNullOrEmpty(path) && OnlineMusicRegistry.IsOnlinePath(path));
            if (!isOnline)
            {
                return (false, null, path);
            }
            var s = new OnlineSong
            {
                Id = Str("id") ?? string.Empty,
                Title = Str("title", "name") ?? string.Empty,
                Artist = Str("artist") ?? string.Empty,
                Album = Str("album") ?? string.Empty,
                Artwork = Str("coverUrl", "cover_url", "coverThumbPath", "cover_thumb_path") ?? string.Empty,
                DurationSec = song.TryGetProperty("duration", out var d) && d.TryGetDouble(out var dv) ? dv : 0,
                Platform = Str("platform") ?? string.Empty,
                PluginHash = Str("pluginId", "plugin_id") ?? string.Empty,
                PluginName = Str("pluginName", "plugin_name") ?? string.Empty,
                RawJson = RawJson("pluginData", "rawData", "raw_data"),
                IsLx = song.TryGetProperty("isLx", out var lx) && lx.ValueKind == JsonValueKind.True,
            };
            // 缺失关键字段时从虚拟路径回填(mfplugin://hash/platform/id)
            if (string.IsNullOrEmpty(s.Id) || string.IsNullOrEmpty(s.PluginHash) || string.IsNullOrEmpty(s.Platform))
            {
                if (!string.IsNullOrEmpty(path) && TryParseVirtualPath(path, out var hash, out var platform, out var id))
                {
                    s.Id = string.IsNullOrEmpty(s.Id) ? id : s.Id;
                    s.PluginHash = string.IsNullOrEmpty(s.PluginHash) ? hash : s.PluginHash;
                    s.Platform = string.IsNullOrEmpty(s.Platform) ? platform : s.Platform;
                }
            }
            // 跨端兼容规范化: platform/id/isLx 回填 + 插件哈希按平台回退(与库内歌曲加载共用同一逻辑)
            _plugins.NormalizeOnlineSong(s);
            return (true, s, null);
        }

        private static bool TryParseVirtualPath(string path, out string hash, out string platform, out string id)
        {
            hash = platform = id = string.Empty;
            var trimmed = path.Trim();
            if (!trimmed.StartsWith(OnlineMusicRegistry.SchemePrefix, StringComparison.Ordinal)) return false;
            var rest = trimmed[OnlineMusicRegistry.SchemePrefix.Length..].Split('/');
            if (rest.Length != 3) return false;
            hash = rest[0];
            platform = rest[1];
            try { id = Uri.UnescapeDataString(rest[2]); }
            catch { id = rest[2]; }
            return hash.Length > 0 && platform.Length > 0 && id.Length > 0;
        }

        // ─── 插件同步 ─────────────────────────────────

        private async Task<(bool NoChange, int UploadedPlugins, List<string> Errors)> UploadPluginsIfChangedAsync()
        {
            var accountId = RequireAccountId();
            var items = _plugins.GetPluginSyncItems();
            var hash = PluginsHash(items);
            lock (_stateGate)
            {
                if (StateOf(accountId).LastPluginsHash == hash)
                    return (true, 0, []);
            }
            var errors = new List<string>();
            int uploaded = 0;
            // 空插件目录不上传, 避免新设备误清云端
            for (var index = 0; index < items.Count; index++)
            {
                var (entry, code) = items[index];
                try
                {
                    var payload = new Dictionary<string, object?>
                    {
                        ["id"] = entry.Hash,
                        ["name"] = entry.Platform,
                        ["format"] = entry.Format == "lx" ? "lx" : "musicfree",
                        ["version"] = entry.Version,
                        ["author"] = entry.Author,
                        ["description"] = "",
                        ["filePath"] = entry.FileName,
                        ["importedAt"] = 0,
                        ["enabled"] = entry.Enabled,
                        ["sources"] = Array.Empty<string>(),
                        ["sourceUrl"] = entry.SrcUrl,
                        ["script"] = EncodeScript(code),
                        ["scriptEncoded"] = true,
                    };
                    if (entry.Format == "lx") payload["isLx"] = true;
                    await _auth.RequestActionAsync("plugin_sync_upload_one", new
                    {
                        user_id = accountId,
                        plugin = payload,
                        is_first = index == 0,
                    }, 60000);
                    uploaded++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{entry.Platform} 上传失败：{ex.Message}");
                }
            }
            // 全部成功才记录哈希, 部分失败保留重试机会
            if (errors.Count == 0)
            {
                lock (_stateGate)
                {
                    StateOf(accountId).LastPluginsHash = hash;
                    SaveStateLocked();
                }
            }
            return (false, uploaded, errors);
        }

        private async Task<(int DownloadedPlugins, List<string> Errors)> DownloadPluginsAsync()
        {
            var accountId = RequireAccountId();
            var data = await _auth.RequestActionAsync("plugin_sync_download", new { user_id = accountId }, 60000);
            var errors = new List<string>();
            if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("plugins", out var pluginsEl) || pluginsEl.ValueKind != JsonValueKind.Array)
                return (0, errors);

            var installed = _plugins.GetInstalledItems().ToDictionary(i => i.Hash, i => i.Enabled);
            foreach (var plugin in pluginsEl.EnumerateArray())
            {
                if (plugin.ValueKind != JsonValueKind.Object) continue;
                var name = plugin.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : "未知插件";
                try
                {
                    var encoded = (plugin.TryGetProperty("scriptEncoded", out var se) && se.ValueKind == JsonValueKind.True)
                        || (plugin.TryGetProperty("script_encoded", out var se2) && se2.ValueKind == JsonValueKind.True);
                    var rawScript = plugin.TryGetProperty("script", out var sc) && sc.ValueKind == JsonValueKind.String ? sc.GetString() : null;
                    if (string.IsNullOrWhiteSpace(rawScript))
                    {
                        errors.Add($"{name}缺少脚本");
                        continue;
                    }
                    var script = encoded ? DecodeScript(rawScript) : rawScript;
                    if (script.TrimStart().StartsWith("<", StringComparison.OrdinalIgnoreCase) || script.Length > MaxPluginBytes)
                    {
                        errors.Add($"{name}脚本无效");
                        continue;
                    }
                    var cloudId = plugin.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString()?.Trim() : null;
                    var cloudEnabled = plugin.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.True;

                    if (string.IsNullOrEmpty(cloudId) || !installed.TryGetValue(cloudId, out var localEnabled))
                    {
                        // 未安装(桌面按内容哈希判定): 安装脚本
                        var (hash, error) = await _plugins.InstallFromCodeAsync(script);
                        if (hash is null)
                        {
                            errors.Add($"{name}恢复失败：{error}");
                            continue;
                        }
                        // 安装默认启用, 云端禁用时同步禁用状态
                        if (!cloudEnabled) await _plugins.SetEnabledAsync(hash, false);
                    }
                    else if (localEnabled != cloudEnabled)
                    {
                        // 已安装: 应用云端启用状态
                        await _plugins.SetEnabledAsync(cloudId, cloudEnabled);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{name}恢复失败：{ex.Message}");
                }
            }
            return (0, errors);
        }

        private static string EncodeScript(string script)
            => new string(Convert.ToBase64String(Encoding.UTF8.GetBytes(script)).Reverse().ToArray());

        private static string DecodeScript(string value)
            => Encoding.UTF8.GetString(Convert.FromBase64String(new string(value.Reverse().ToArray())));

        private static string PluginsHash(List<(PluginManifestEntry Entry, string Code)> items)
        {
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartArray();
                foreach (var (entry, code) in items)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", entry.Hash);
                    writer.WriteString("version", entry.Version);
                    writer.WriteBoolean("enabled", entry.Enabled);
                    writer.WriteString("code", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant());
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            return Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant();
        }

        // ─── 定时自动上传 ─────────────────────────────────

        /// <summary>登录后按账号设置启动定时上传; 手动模式不启动。</summary>
        public void StartAutoUpload()
        {
            var generation = ++_autoStartGeneration;
            _autoTimer?.Dispose();
            _autoTimer = null;
            _autoUploading = false;
            var accountId = _auth.AccountId;
            if (string.IsNullOrEmpty(accountId) || !IsEnabled(accountId)) return;
            var interval = GetFrequency(accountId) switch
            {
                CloudSyncFrequency.FiveMinutes => TimeSpan.FromMinutes(5),
                CloudSyncFrequency.FifteenMinutes => TimeSpan.FromMinutes(15),
                CloudSyncFrequency.ThirtyMinutes => TimeSpan.FromMinutes(30),
                CloudSyncFrequency.OneHour => TimeSpan.FromHours(1),
                CloudSyncFrequency.SixHours => TimeSpan.FromHours(6),
                CloudSyncFrequency.TwelveHours => TimeSpan.FromHours(12),
                CloudSyncFrequency.OneDay => TimeSpan.FromDays(1),
                _ => (TimeSpan?)null,
            };
            if (interval is null) return;
            _autoTimer = new System.Threading.Timer(async _ =>
            {
                if (_autoUploading || _auth.AccountId != accountId || generation != _autoStartGeneration) return;
                _autoUploading = true;
                try
                {
                    // 自动同步发送完整快照, 不依赖哈希跳过(自愈服务端不完整数据)
                    await UploadAsync();
                    await UploadPluginsIfChangedAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "自动云同步失败");
                }
                finally
                {
                    _autoUploading = false;
                }
            }, null, interval.Value, interval.Value);
        }

        public void StopAutoUpload()
        {
            _autoStartGeneration++;
            _autoTimer?.Dispose();
            _autoTimer = null;
            _autoUploading = false;
        }

        private string RequireAccountId()
        {
            var id = _auth.AccountId;
            if (string.IsNullOrEmpty(id)) throw new AuthException("请先登录账号");
            return id;
        }
    }
}
