using CommunityToolkit.WinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SQLite;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using WinUIMusicPlayer.Services.Plugins;
using System.Threading.Channels;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI;
using WinUIMusicPlayer.Helper;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Model.Stats;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.ViewModel;
using ZLinq;
using static WinUIMusicPlayer.Utils.ToolUtils;

namespace WinUIMusicPlayer.Services
{
    public class MusicDatabaseService
    {
        private SQLiteAsyncConnection _dbConnection;
        private string DbPath = Path.Combine(AppPaths.LocalFolder, "MusicDatabase.db");
        private string SettingsPath => GetSettingsFilePath();
        private string PlayStatePath => GetPlayStateFilePath();
        private string VersionRecordPath => GetVersionRecordFilePath();
        private readonly AddFolderService addFolderService = new();
        private SaveSettings _currentSettings;
        public SaveSettings CurrentSettings => _currentSettings;
        // 设置文件读写互斥：写不并发（避免 IOException 丢更新），读不撞写（避免读到半截 JSON）
        private readonly SemaphoreSlim _settingsIoGate = new(1, 1);
        // 播放状态文件同一套互斥（同步读写路径用 Wait() 阻塞进入，临界区仅一次小文件 IO）
        private readonly SemaphoreSlim _playStateIoGate = new(1, 1);
        // 桌面歌词窗口状态文件仅同步读写，用 lock 即可
        private readonly object _desktopLyricsStateFileLock = new();
        // 版本记录文件同一套互斥
        private readonly SemaphoreSlim _versionRecordIoGate = new(1, 1);
        private SavePlayState _currentPlayState;
        public SavePlayState CurrentPlayState => _currentPlayState;
        // 优化1: 信号量保持4并发，但 _toDelete/_toUpdate 改为方法局部变量，消除共享状态与线程安全隐患
        private readonly SemaphoreSlim _rescanfolderSemaphore = new(4, 4);
        private AppViewModel AppViewModel { get; set; }
        private ILogger<MusicDatabaseService> _logger;

        public MusicDatabaseService(ILogger<MusicDatabaseService> logger)
        {
            _logger = logger;
        }

        public async Task Initialize()
        {
            InitalizeDbPath();
            if (_dbConnection is null)
            {
                _dbConnection = new SQLiteAsyncConnection(DbPath);
                await _dbConnection.CreateTableAsync<Music>();
                await _dbConnection.CreateTableAsync<MusicLyrics>();
                await _dbConnection.CreateTableAsync<Folder>();
                await _dbConnection.CreateTableAsync<SaveEqualizer>();
                await _dbConnection.CreateTableAsync<PlayList>();
                await _dbConnection.CreateTableAsync<PlayListMusic>();
                await _dbConnection.CreateTableAsync<OnlinePlayListMusic>();
                try
                {
                    // PlayList.IsOnline 列迁移(老库补列, 在线歌单=1)
                    var playListColumns = await _dbConnection.QueryAsync<TableColumnInfo>("PRAGMA table_info(PlayList)");
                    if (playListColumns.All(c => c.Name != "IsOnline"))
                        await _dbConnection.ExecuteAsync("ALTER TABLE PlayList ADD COLUMN IsOnline INTEGER DEFAULT 0");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PlayList.IsOnline 列迁移失败: {Message}", ex.Message);
                }
                await _dbConnection.CreateTableAsync<LastPlayListState>();
                await _dbConnection.CreateTableAsync<SubFolder>();
                await _dbConnection.CreateTableAsync<UsbDeviceMusic>();
                try
                {
                    // 清理为在线歌曲(负数临时 Id)误存的歌词行: 该类 Id 跨会话重复分配, 会读到别的歌的歌词
                    await _dbConnection.ExecuteAsync("DELETE FROM MusicLyrics WHERE MusicId < 0");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "清理在线歌曲遗留歌词行失败: {Message}", ex.Message);
                }
                try
                {
                    await _dbConnection.CreateTableAsync<PlaybackHistory>();
                    await _dbConnection.ExecuteAsync(
                        "CREATE INDEX IF NOT EXISTS IX_PlaybackHistory_StartedAt ON PlaybackHistory(StartedAt)");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "初始化播放统计表失败，统计功能降级不可用: {Message}", ex.Message);
                }
            }
            AppViewModel = App.Services.GetRequiredService<AppViewModel>();
        }

        private void InitalizeDbPath()
        {
            try
            {
                string userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (userProfilePath is not null)
                {
                    string appFolderPath = Path.Combine(userProfilePath, "XYMusic", "DataBase");
                    string dbFilePath = Path.Combine(appFolderPath, "MusicDatabase.db");
                    string sourceDbPath = Path.Combine(AppPaths.LocalFolder, "MusicDatabase.db");
                    if (!Directory.Exists(appFolderPath))
                    {
                        Directory.CreateDirectory(appFolderPath);
                        CopyFile(sourceDbPath, dbFilePath);
                        DbPath = dbFilePath;
                    }
                    else
                    {
                        if (!File.Exists(dbFilePath))
                        {
                            CopyFile(sourceDbPath, dbFilePath);
                        }
                        DbPath = dbFilePath;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"InitalizeDbPath 初始化数据库路径失败: {ex.Message}");
                DbPath = System.IO.Path.Combine(AppPaths.LocalFolder, "MusicDatabase.db");
            }
        }

        private string GetSettingsFilePath()
        {
            try
            {
                string userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string appFolderPath = Path.Combine(userProfilePath, "XYMusic", "Settings");
                if (!Directory.Exists(appFolderPath))
                {
                    Directory.CreateDirectory(appFolderPath);
                }
                return Path.Combine(appFolderPath, "Settings.json");
            }
            catch
            {
                return Path.Combine(AppPaths.LocalFolder, "Settings.json");
            }
        }

        private string GetPlayStateFilePath()
        {
            try
            {
                string userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string appFolderPath = Path.Combine(userProfilePath, "XYMusic", "Settings");
                if (!Directory.Exists(appFolderPath))
                {
                    Directory.CreateDirectory(appFolderPath);
                }
                return Path.Combine(appFolderPath, "PlayState.json");
            }
            catch
            {
                return Path.Combine(AppPaths.LocalFolder, "PlayState.json");
            }
        }

        private string GetDesktopLyricsStateFilePath()
        {
            try
            {
                string userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string appFolderPath = Path.Combine(userProfilePath, "XYMusic", "Settings");
                if (!Directory.Exists(appFolderPath))
                {
                    Directory.CreateDirectory(appFolderPath);
                }
                return Path.Combine(appFolderPath, "DesktopLyricsState.json");
            }
            catch
            {
                return Path.Combine(AppPaths.LocalFolder, "DesktopLyricsState.json");
            }
        }

        public SaveDesktopLyricsState LoadDesktopLyricsState()
        {
            lock (_desktopLyricsStateFileLock)
            {
                string path = GetDesktopLyricsStateFilePath();
                if (!File.Exists(path))
                {
                    return new SaveDesktopLyricsState();
                }
                try
                {
                    return JsonSerializer.Deserialize(File.ReadAllText(path), DesktopLyricsStateJsonContext.Default.SaveDesktopLyricsState) ?? new SaveDesktopLyricsState();
                }
                catch (JsonException ex)
                {
                    // 损坏文件留底后按默认值继续，避免之后一次写入把事故固化成永久丢失
                    _logger.LogError(ex, $"DesktopLyricsState.json 解析失败，备份损坏文件后按默认值继续: {ex.Message}");
                    TryBackupCorruptFile(path);
                    return new SaveDesktopLyricsState();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"LoadDesktopLyricsState 读取桌面歌词窗口状态失败: {ex.Message}");
                    return new SaveDesktopLyricsState();
                }
            }
        }

        public void SaveDesktopLyricsState(SaveDesktopLyricsState state)
        {
            lock (_desktopLyricsStateFileLock)
            {
                try
                {
                    File.WriteAllText(GetDesktopLyricsStateFilePath(), JsonSerializer.Serialize(state, DesktopLyricsStateJsonContext.Default.SaveDesktopLyricsState));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"SaveDesktopLyricsState 写入桌面歌词窗口状态失败: {ex.Message}");
                }
            }
        }

        private string GetVersionRecordFilePath()
        {
            try
            {
                string userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string appFolderPath = Path.Combine(userProfilePath, "XYMusic", "Settings");
                if (!Directory.Exists(appFolderPath))
                {
                    Directory.CreateDirectory(appFolderPath);
                }
                return Path.Combine(appFolderPath, "VersionRecord.json");
            }
            catch
            {
                return Path.Combine(AppPaths.LocalFolder, "VersionRecord.json");
            }
        }

        private void CopyFile(string sourceFilePath, string targetFilePath)
        {
            if (File.Exists(sourceFilePath))
            {
                using FileStream sourceStream = File.Open(sourceFilePath, FileMode.Open);
                using FileStream destinationStream = File.Create(targetFilePath);
                sourceStream.CopyTo(destinationStream);
            }
        }

        public SQLiteAsyncConnection GetDbConnection()
        {
            return _dbConnection;
        }

        public async Task SavePlayList(List<Music> currentPlayingList)
        {
            await _dbConnection.DeleteAllAsync<LastPlayListState>();
            // 优化2: 去掉多余的 ToArray()，string.Join 直接接受 IEnumerable<int>
            // 注意: ZLinq.ValueEnumerable 是 struct，未实现 IEnumerable<T>，
            //       .AsEnumerable() 会分配 enumerator，所以保留 .ToArray()。
            var musicIds = string.Join(',', currentPlayingList.AsValueEnumerable().Select(m => m.Id).ToArray());
            var playListState = new LastPlayListState
            {
                PlayListMusicIds = musicIds
            };
            await _dbConnection.InsertAsync(playListState);
        }

        public async Task InsertSubFolders(List<SubFolder> subFolder)
        {
            await _dbConnection.InsertAllAsync(subFolder);
        }

        public async Task AddSubFolder(SubFolder subFolder)
        {
            await _dbConnection.InsertAsync(subFolder);
        }

        public async Task UpdateSubFolder(SubFolder subFolder)
        {
            await _dbConnection.UpdateAsync(subFolder);
        }

        public async Task DeleteSubFolder(SubFolder subFolder)
        {
            await _dbConnection.DeleteAsync(subFolder);
        }

        public async Task DeleteSubFolderByPath(string subFolderPath)
        {
            var musicToDelete = await _dbConnection.Table<Music>()
                                              .Where(m => m.Path.Contains(subFolderPath))
                                              .ToListAsync();
            foreach (var music in musicToDelete)
            {
                await _dbConnection.DeleteAsync(music);
            }
        }

        public async Task DeleteAllSubFolder()
        {
            await _dbConnection.DeleteAllAsync<SubFolder>();
        }

        public async Task<List<SubFolder>> GetSubFolders(int folderId)
        {
            return await _dbConnection.Table<SubFolder>().Where(f => f.FolderId == folderId).ToListAsync();
        }


        public async Task<List<Folder>> GetFolders()
        {
            return await _dbConnection.Table<Folder>().ToListAsync();
        }

        public async Task<List<Music>> LoadPlayList(IEnumerable<Music> AllMusicList)
        {
            var playListState = await _dbConnection.Table<LastPlayListState>().FirstOrDefaultAsync();
            if (playListState is null)
            {
                return [];
            }
            var musicIds = ParseCsvIntList(playListState.PlayListMusicIds);
            var musicList = new List<Music>(musicIds.Count);
            foreach (var musicId in musicIds)
            {
                var music = AllMusicList.FirstOrDefault(m => m.Id == musicId);
                if (music is not null)
                {
                    musicList.Add(music);
                }
            }
            return musicList;
        }

        private static List<int> ParseCsvIntList(string csv)
        {
            var result = new List<int>();
            if (string.IsNullOrEmpty(csv)) return result;
            ReadOnlySpan<char> span = csv;
            while (span.Length > 0)
            {
                int comma = span.IndexOf(',');
                ReadOnlySpan<char> segment = comma >= 0 ? span[..comma] : span;
                if (segment.Length > 0 && int.TryParse(segment, out int id))
                    result.Add(id);
                span = comma >= 0 ? span[(comma + 1)..] : [];
            }
            return result;
        }

        public async Task<List<Folder>> GetFoldersAsync()
        {
            try
            {
                return await _dbConnection.Table<Folder>().ToListAsync();
            }
            catch (SQLiteException)
            {
                return new List<Folder>();
            }
        }

        public async Task InitalPlayListAsync()
        {
            try
            {
                var list = await _dbConnection.Table<PlayList>().ToListAsync();
                // 本地歌单与在线歌单分集合维护: AllPlayList 仅本地(右键"添加到播放列表"菜单不受影响)
                await AppViewModel.AllPlayList.AddRangeAsync(list.Where(p => p.IsOnline == 0));
                await AppViewModel.OnlinePlayLists.AddRangeAsync(list.Where(p => p.IsOnline == 1));
                await RefreshOnlinePlayListCounts();
            }
            catch (Exception ex) { _logger.LogError(ex, $"InitalPlayListAsync 初始化播放列表失败: {ex.Message}"); }
        }

        private async Task RefreshOnlinePlayListCounts()
        {
            try
            {
                var counts = new Dictionary<int, int>();
                var entries = await _dbConnection.Table<OnlinePlayListMusic>().ToListAsync();
                foreach (var e in entries)
                    counts[e.PlayListId] = counts.GetValueOrDefault(e.PlayListId) + 1;
                foreach (var pl in AppViewModel.OnlinePlayLists)
                    pl.SongCount = counts.GetValueOrDefault(pl.Id, 0);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "统计在线歌单曲目数失败: {Message}", ex.Message); }
        }

        // ────────────────────────────────────────────────────────────
        //  在线歌单: 歌单 CRUD + 歌曲条目(在线存 OnlineSong JSON / 本地存 MusicId)
        // ────────────────────────────────────────────────────────────

        private static readonly System.Text.Json.JsonSerializerOptions OnlineSongJsonOpts = new() { PropertyNameCaseInsensitive = true };

        /// <summary>全部歌单(本地+在线), 云同步读取快照用。</summary>
        public Task<List<PlayList>> GetAllPlayListsAsync()
            => _dbConnection.Table<PlayList>().ToListAsync();

        /// <summary>本地歌单(PlayListMusic)条目, 云同步读取快照用。</summary>
        public Task<List<PlayListMusic>> GetPlayListMusicsAsync(int playListId)
            => _dbConnection.Table<PlayListMusic>()
                .Where(m => m.PlayListId == playListId).OrderBy(m => m.Order).ToListAsync();

        /// <summary>原始 Music 表数据(不做本地化替换), 云同步用。</summary>
        public Task<List<Music>> GetAllMusicsRawAsync()
            => _dbConnection.Table<Music>().ToListAsync();

        public async Task<int> CreateOnlinePlayListAsync(string name)
        {
            var pl = new PlayList { Name = name, SongCount = 0, IsOnline = 1 };
            await _dbConnection.InsertAsync(pl);
            return pl.Id;
        }

        /// <summary>添加在线歌曲到歌单(仅存插件链接 JSON, 不下载), 虚拟路径重复时跳过。返回是否实际添加。</summary>
        public async Task<bool> AddOnlineSongToPlayListAsync(int playListId, Services.Plugins.OnlineSong song)
        {
            var entries = await _dbConnection.Table<OnlinePlayListMusic>()
                .Where(e => e.PlayListId == playListId).ToListAsync();
            foreach (var e in entries)
            {
                if (e.MusicId > 0 || string.IsNullOrEmpty(e.SongJson)) continue;
                try
                {
                    var existing = System.Text.Json.JsonSerializer.Deserialize<Services.Plugins.OnlineSong>(e.SongJson, OnlineSongJsonOpts);
                    if (existing is null) continue;
                    if (existing.VirtualPath == song.VirtualPath) return false;
                    // 云端损坏条目(无 id)与本地已修复条目(有 id)同名同歌手 → 同一首, 跳过避免同步出重复
                    if (!string.IsNullOrEmpty(existing.Id) && string.IsNullOrEmpty(song.Id)
                        && Services.Plugins.OnlinePlaybackResolver.NormalizeText(existing.Title ?? "") == Services.Plugins.OnlinePlaybackResolver.NormalizeText(song.Title ?? ""))
                        return false;
                }
                catch { /* 损坏条目按不存在处理 */ }
            }
            int newOrder = (entries.Count == 0 ? 0 : entries.Max(e => e.Order)) + 1;
            await _dbConnection.InsertAsync(new OnlinePlayListMusic
            {
                PlayListId = playListId,
                Order = newOrder,
                MusicId = 0,
                SongJson = System.Text.Json.JsonSerializer.Serialize(song, OnlineSongJsonOpts),
            });
            return true;
        }

        /// <summary>把歌单库中损坏的在线条目(无歌曲 id)按 标题+歌手 匹配替换为修复后的完整歌曲数据, 返回修复条数。</summary>
        public async Task<int> RepairOnlineSongJsonAsync(string title, string artist, Services.Plugins.OnlineSong repaired)
        {
            var normTitle = Services.Plugins.OnlinePlaybackResolver.NormalizeText(title ?? string.Empty);
            if (normTitle.Length == 0) return 0;
            var wantArtists = Services.Plugins.OnlinePlaybackResolver.SplitArtists(artist ?? string.Empty);
            int count = 0;
            var entries = await _dbConnection.Table<OnlinePlayListMusic>().ToListAsync();
            foreach (var e in entries)
            {
                if (e.MusicId > 0 || string.IsNullOrEmpty(e.SongJson)) continue;
                try
                {
                    var existing = System.Text.Json.JsonSerializer.Deserialize<Services.Plugins.OnlineSong>(e.SongJson, OnlineSongJsonOpts);
                    // 仅修复无 id 的损坏条目, 且标题归一化一致 + 歌手有交集
                    if (existing is null || !string.IsNullOrEmpty(existing.Id)) continue;
                    if (Services.Plugins.OnlinePlaybackResolver.NormalizeText(existing.Title ?? "") != normTitle) continue;
                    if (wantArtists.Count > 0)
                    {
                        var got = Services.Plugins.OnlinePlaybackResolver.SplitArtists(existing.Artist ?? "");
                        if (got.Count > 0 && !got.Overlaps(wantArtists)) continue;
                    }
                    e.SongJson = System.Text.Json.JsonSerializer.Serialize(repaired, OnlineSongJsonOpts);
                    await _dbConnection.UpdateAsync(e);
                    count++;
                }
                catch { /* 损坏条目跳过 */ }
            }
            return count;
        }

        /// <summary>删除在线歌单中损坏的在线条目(跨端同步遗留: 歌曲 id 或 RawJson 为空, 播放必失败), 返回删除数。</summary>
        public async Task<int> RemoveBrokenOnlineSongsAsync(int playListId)
        {
            var entries = await _dbConnection.Table<OnlinePlayListMusic>()
                .Where(e => e.PlayListId == playListId).ToListAsync();
            var broken = new List<int>();
            foreach (var e in entries)
            {
                if (e.MusicId > 0 || string.IsNullOrEmpty(e.SongJson)) continue;
                try
                {
                    var existing = System.Text.Json.JsonSerializer.Deserialize<Services.Plugins.OnlineSong>(e.SongJson, OnlineSongJsonOpts);
                    // 歌曲 id 或原始数据缺失 → 无法解析音源(手机端旧版同步数据), 清除后由本次合并重建
                    if (existing is null || string.IsNullOrEmpty(existing.Id) || string.IsNullOrEmpty(existing.RawJson))
                        broken.Add(e.Id);
                }
                catch { broken.Add(e.Id); }
            }
            if (broken.Count > 0)
                await _dbConnection.ExecuteAsync($"DELETE FROM OnlinePlayListMusic WHERE Id IN ({string.Join(',', broken)})");
            return broken.Count;
        }

        /// <summary>添加本地歌曲到在线歌单(MusicId 引用), 已存在时跳过。</summary>
        public async Task<bool> AddLocalMusicToOnlinePlayListAsync(int playListId, int musicId)
        {
            var entries = await _dbConnection.Table<OnlinePlayListMusic>()
                .Where(e => e.PlayListId == playListId).ToListAsync();
            if (entries.Any(e => e.MusicId == musicId)) return false;
            int newOrder = (entries.Count == 0 ? 0 : entries.Max(e => e.Order)) + 1;
            await _dbConnection.InsertAsync(new OnlinePlayListMusic
            {
                PlayListId = playListId,
                Order = newOrder,
                MusicId = musicId,
                SongJson = string.Empty,
            });
            return true;
        }

        public async Task<List<OnlinePlayListMusic>> GetOnlinePlayListMusicsAsync(int playListId)
            => await _dbConnection.Table<OnlinePlayListMusic>()
                .Where(e => e.PlayListId == playListId).OrderBy(e => e.Order).ToListAsync();

        public async Task RemoveOnlinePlayListMusicAsync(int entryId)
            => await _dbConnection.ExecuteAsync("DELETE FROM OnlinePlayListMusic WHERE Id = ?", entryId);

        public async Task DeleteOnlinePlayListAsync(PlayList playList)
        {
            await _dbConnection.ExecuteAsync("DELETE FROM OnlinePlayListMusic WHERE PlayListId = ?", playList.Id);
            await _dbConnection.DeleteAsync(playList);
        }

        public async Task UpdateMusicInfo(Music music)
        {
            await _dbConnection.UpdateAsync(music);
        }

        // 定义接收 pragma_table_info 结果的类
        private class TableColumnInfo
        {
            public string Name { get; set; }
        }

        public async Task<(string? lyrics, string? transLrc, string? krc, string? tKrc)> GetLyricsAsync(int musicId)
        {
            var lyrics = await _dbConnection.FindAsync<MusicLyrics>(musicId);
            return (lyrics?.Lyrics, lyrics?.TranslatedLyrics, lyrics?.Krc, lyrics?.TKrc);
        }

        public async Task SaveLyricsAsync(int musicId, string? lyrics, string? transLrc, string? krc, string? tKrc)
        {
            await _dbConnection.InsertOrReplaceAsync(new MusicLyrics
            {
                MusicId = musicId,
                Lyrics = lyrics ?? "",
                TranslatedLyrics = transLrc ?? "",
                Krc = krc ?? "",
                TKrc = tKrc ?? ""
            });
        }

        /// <summary>删除指定歌曲的歌词库条目(取消关联歌词时调用, 之后自动联网搜索可重新探测)。</summary>
        public async Task ClearLyricsAsync(int musicId)
        {
            await _dbConnection.DeleteAsync<MusicLyrics>(musicId);
        }

        private async Task SaveEmbeddedLyricsAsync(IEnumerable<(Music Music, string Lyrics)> results)
        {
            foreach (var (music, lyrics) in results)
            {
                if (string.IsNullOrWhiteSpace(lyrics)) continue;
                var existing = await _dbConnection.FindAsync<MusicLyrics>(music.Id);
                if (existing is not null &&
                    !(string.IsNullOrWhiteSpace(existing.Lyrics) && string.IsNullOrWhiteSpace(existing.TranslatedLyrics) &&
                      string.IsNullOrWhiteSpace(existing.Krc) && string.IsNullOrWhiteSpace(existing.TKrc)))
                {
                    continue;
                }
                await SaveLyricsAsync(music.Id, lyrics, null, null, null);
            }
        }

        public IEnumerable<PlayListMusicItem> GetMusicByPlayListIdFromMem(int playListId, string search = null)
        {
            var plmSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(AppData.AllPlayListMusics);
            int plmCount = plmSpan.Length;
            bool hasSearch = !string.IsNullOrEmpty(search);

            var pool = System.Buffers.ArrayPool<PlayListMusicItem>.Shared;
            var buf = pool.Rent(plmCount);
            int written = 0;
            try
            {
                for (int i = 0; i < plmCount; i++)
                {
                    ref readonly var plm = ref plmSpan[i];
                    if (plm.PlayListId != playListId) continue;
                    if (!AppViewModel.TryFindById(plm.MusicId, out var m) || m is null) continue;
                    if (hasSearch)
                    {
                        bool match = (m.Title is not null && m.Title.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                                     (m.Album is not null && m.Album.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                                     (m.Author is not null && m.Author.Contains(search, StringComparison.OrdinalIgnoreCase));
                        if (!match) continue;
                    }
                    buf[written++] = new PlayListMusicItem { Music = m, PlayListOrder = plm.Order };
                }

                var slice = buf.AsSpan(0, written);
                slice.Sort(_plmOrderDesc);
                return slice.ToArray();
            }
            finally
            {
                pool.Return(buf, clearArray: false);
            }
        }

        private static readonly System.Collections.Generic.IComparer<PlayListMusicItem> _plmOrderDesc =
            System.Collections.Generic.Comparer<PlayListMusicItem>.Create((a, b) => b.PlayListOrder.CompareTo(a.PlayListOrder));

        public async Task UpdatePlayListMusicOrderBatch(int playListId, IEnumerable<PlayListMusicItem> musicList)
        {
            try
            {
                var items = musicList.AsValueEnumerable().ToArray();
                if (items.Length == 0) return;

                await _dbConnection.RunInTransactionAsync(conn =>
                {
                    for (int i = 0; i < items.Length; i++)
                    {
                        var m = items[i];
                        conn.Execute(
                            "UPDATE PlayListMusic SET [Order]=? WHERE PlayListId=? AND MusicId=?",
                            m.PlayListOrder, playListId, m.Music.Id);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"UpdatePlayListMusicOrderBatch 批量更新播放列表音乐排序时出错: {ex.Message}");
            }
        }

        public IEnumerable<Music> FindMusicListByAlbum(string album)
        {
            return AppViewModel.SongsSource.AsValueEnumerable()
                   .Where(m => m.Album is not null && m.Album.ToLower().Equals(album.ToLower())).OrderBy(m => m.TrackNumber).ToImmutableList();
        }

        public async Task AddMusicListToFavour(IEnumerable<Music> musics)
        {
            var maxOrder = await GetMaxOrder();
            foreach (var music in musics)
            {
                var existingMusic = await _dbConnection.Table<Music>().Where(m => m.Id == music.Id && m.IsFavorite == true).FirstOrDefaultAsync();
                if (existingMusic is not null)
                {
                    continue;
                }
                music.IsFavorite = true;
                music.Order = maxOrder + 1;
                await _dbConnection.UpdateAsync(music);
            }
        }

        public async Task AddMusicListToPlayList(IEnumerable<Music> musics, int playListId)
        {
            PlayListMusic lastplayListMusic = await _dbConnection.Table<PlayListMusic>()
                                          .Where(m => m.PlayListId == playListId)
                                          .OrderByDescending(m => m.Order)
                                          .FirstOrDefaultAsync();
            var maxOrder = lastplayListMusic?.Order ?? 0;

            // 优化3: 批量收集后一次 InsertAllAsync，减少多次 await 往返
            var toInsert = new List<PlayListMusic>();
            foreach (var music in musics)
            {
                var existingRecord = await _dbConnection.Table<PlayListMusic>()
                   .Where(plm => plm.PlayListId == playListId && plm.MusicId == music.Id)
                   .FirstOrDefaultAsync();
                if (existingRecord is not null)
                {
                    continue;
                }
                maxOrder++;
                toInsert.Add(new PlayListMusic
                {
                    PlayListId = playListId,
                    MusicId = music.Id,
                    Order = maxOrder
                });
            }
            if (toInsert.Count > 0)
            {
                await _dbConnection.InsertAllAsync(toInsert);
            }
            AppData.AllPlayListMusics = await _dbConnection.Table<PlayListMusic>().ToListAsync();
            RefreshPlayListSongCount(playListId);
        }

        public async Task AddMusicToPlayList(int playListId, int musicId)
        {
            var existingRecord = await _dbConnection.Table<PlayListMusic>()
               .Where(plm => plm.PlayListId == playListId && plm.MusicId == musicId)
               .FirstOrDefaultAsync();
            if (existingRecord is null)
            {
                PlayListMusic lastplayListMusic = await _dbConnection.Table<PlayListMusic>()
                                          .Where(m => m.PlayListId == playListId)
                                          .OrderByDescending(m => m.Order)
                                          .FirstOrDefaultAsync();
                // 优化4: 简化 maxOrder 计算逻辑，去掉多余分支
                int newOrder = (lastplayListMusic?.Order ?? 0) + 1;
                var playListMusic = new PlayListMusic
                {
                    PlayListId = playListId,
                    MusicId = musicId,
                    Order = newOrder
                };
                await _dbConnection.InsertAsync(playListMusic);
            }
            AppData.AllPlayListMusics = await _dbConnection.Table<PlayListMusic>().ToListAsync();
        }

        private async Task<int> GetMaxOrder()
        {
            Music lastFavouriteMusic = await _dbConnection.Table<Music>()
                                          .Where(m => m.IsFavorite)
                                          .OrderByDescending(m => m.Order)
                                          .FirstOrDefaultAsync();
            // 优化5: 用 null 合并简化，减少分支
            return lastFavouriteMusic?.Order ?? 1;
        }

        public async Task DeleteAllMusicFromPlayList(int playListId, IEnumerable<int> musicIds)
        {
            // 优化6: 用 !musicIds.Any() 代替 AsValueEnumerable().Count() == 0，避免全枚举
            if (musicIds is null || !musicIds.Any())
            {
                return;
            }

            var musicIdsString = string.Join(",", musicIds);
            var sql = $"DELETE FROM PlayListMusic WHERE PlayListId = ? AND MusicId IN ({musicIdsString})";
            await _dbConnection.ExecuteAsync(sql, playListId);
            RefreshPlayListSongCount(playListId);
        }

        public async Task RemoveMusicFromPlayList(int playListId, int musicId)
        {
            var playListMusic = await _dbConnection.Table<PlayListMusic>()
                .Where(plm => plm.PlayListId == playListId && plm.MusicId == musicId)
                .FirstOrDefaultAsync();

            if (playListMusic is not null)
            {
                await _dbConnection.DeleteAsync(playListMusic);
            }
            RefreshPlayListSongCount(playListId);
        }

        public async Task<int> InsertPlayList(PlayList playList)
        {
            await _dbConnection.InsertAsync(playList);
            return playList.Id;
        }

        public async Task UpdatePlayList(PlayList playList)
        {
            await _dbConnection.UpdateAsync(playList);
        }

        public async Task<PlayList> GetPlayListByName(string playListName)
        {
            return await _dbConnection.Table<PlayList>()
                .Where(plm => plm.Name == playListName)
                .FirstOrDefaultAsync();
        }

        public async Task RemovePlayList(PlayList playList)
        {
            var playListMusics = await _dbConnection.Table<PlayListMusic>()
               .Where(plm => plm.PlayListId == playList.Id)
               .ToListAsync();
            foreach (var playListMusic in playListMusics)
            {
                await _dbConnection.DeleteAsync(playListMusic);
            }
            await _dbConnection.DeleteAsync(playList);
        }

        public async Task UpdateAllAsync(IEnumerable<Music> musicList)
        {
            await _dbConnection.UpdateAllAsync(musicList);
        }

        public async Task<SaveSettings> GetSettings()
        {
            string path = SettingsPath;
            if (!File.Exists(path))
            {
                return new SaveSettings();
            }
            await _settingsIoGate.WaitAsync();
            try
            {
                string json = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SaveSettings)
                       ?? new SaveSettings();
            }
            catch (JsonException ex)
            {
                // 文件损坏：把坏文件改名留底后按默认值继续，避免之后一次全量保存把事故固化成永久丢失
                _logger.LogError(ex, $"Settings.json 解析失败，备份损坏文件后按默认值继续: {ex.Message}");
                TryBackupCorruptFile(path);
                return new SaveSettings();
            }
            catch (Exception ex)
            {
                // 文件被占用等瞬时 IO 错误：按默认值继续，但不删不动原文件
                _logger.LogError(ex, ex.Message, ex.StackTrace);
                return new SaveSettings();
            }
            finally
            {
                _settingsIoGate.Release();
            }
        }

        private void TryBackupCorruptFile(string path)
        {
            try
            {
                string backupPath = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
                File.Move(path, backupPath);
                _logger.LogInformation($"损坏的 JSON 文件已备份为: {backupPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"备份损坏的 JSON 文件失败: {ex.Message}");
            }
        }

        public async Task InsertSettings(SaveSettings settings)
        {
            await WriteSettingsToJson(settings);
        }

        public async Task UpdateSettings(SaveSettings settings)
        {
            await WriteSettingsToJson(settings);
        }

        private async Task WriteSettingsToJson(SaveSettings settings)
        {
            await _settingsIoGate.WaitAsync();
            try
            {
                string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.SaveSettings);
                await File.WriteAllTextAsync(SettingsPath, json);
                _currentSettings = settings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"WriteSettingsToJson 写入设置文件时出错: {ex.Message}");
            }
            finally
            {
                _settingsIoGate.Release();
            }
        }

        public async Task<SavePlayState> GetPlayState()
        {
            string path = PlayStatePath;
            if (!File.Exists(path))
            {
                return null;
            }
            await _playStateIoGate.WaitAsync();
            try
            {
                string json = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize(json, PlayStateJsonContext.Default.SavePlayState);
            }
            catch (JsonException ex)
            {
                // 损坏文件留底后按无状态继续，避免之后一次写入把事故固化成永久丢失
                _logger.LogError(ex, $"PlayState.json 解析失败，备份损坏文件后按空状态继续: {ex.Message}");
                TryBackupCorruptFile(path);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message, ex.StackTrace);
                return null;
            }
            finally
            {
                _playStateIoGate.Release();
            }
        }

        private async Task WritePlayStateToJson(SavePlayState state)
        {
            await _playStateIoGate.WaitAsync();
            try
            {
                string json = JsonSerializer.Serialize(state, PlayStateJsonContext.Default.SavePlayState);
                await File.WriteAllTextAsync(PlayStatePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"WritePlayStateToJson 写入播放状态文件时出错: {ex.Message}");
            }
            finally
            {
                _playStateIoGate.Release();
            }
        }

        public async Task<SaveEqualizer> GetEqualizer()
        {
            return await _dbConnection.Table<SaveEqualizer>().FirstOrDefaultAsync();
        }

        public async Task InsertEqualizer(SaveEqualizer equalizer)
        {
            await _dbConnection.InsertAsync(equalizer);
        }

        public async Task UpdateEqualizer(SaveEqualizer equalizer)
        {
            await _dbConnection.UpdateAsync(equalizer);
        }

        public async Task UpdateEqualizerSettings(string equalizerStr, bool isEnabled)
        {
            await _dbConnection.ExecuteAsync(
                "UPDATE SaveEqualizer SET EqualizerStr = ?, IsEqualizerEnabled = ? WHERE Id = 1",
                equalizerStr,
                isEnabled
            );
        }

        public async Task GetPlayListMusic()
        {
            AppData.AllPlayListMusics.Clear();
            AppData.AllPlayListMusics = await _dbConnection.Table<PlayListMusic>().ToListAsync();
            RefreshAllPlayListSongCounts();
        }

        private void RefreshAllPlayListSongCounts()
        {
            var appVm = AppViewModel;
            var counts = new Dictionary<int, int>();
            for (int i = 0; i < AppData.AllPlayListMusics.Count; i++)
            {
                var plm = AppData.AllPlayListMusics[i];
                if (!appVm.TryFindById(plm.MusicId, out var m) || m is null) continue;
                if (!counts.ContainsKey(plm.PlayListId)) counts[plm.PlayListId] = 0;
                counts[plm.PlayListId]++;
            }
            for (int i = 0; i < appVm.AllPlayList.Count; i++)
            {
                var pl = appVm.AllPlayList[i];
                pl.SongCount = counts.GetValueOrDefault(pl.Id, 0);
            }
        }

        private void RefreshPlayListSongCount(int playListId)
        {
            var appVm = AppViewModel;
            int count = 0;
            for (int i = 0; i < AppData.AllPlayListMusics.Count; i++)
            {
                var plm = AppData.AllPlayListMusics[i];
                if (plm.PlayListId != playListId) continue;
                if (!appVm.TryFindById(plm.MusicId, out var m) || m is null) continue;
                count++;
            }
            for (int i = 0; i < appVm.AllPlayList.Count; i++)
            {
                if (appVm.AllPlayList[i].Id == playListId)
                {
                    appVm.AllPlayList[i].SongCount = count;
                    return;
                }
            }
        }

        public async Task LoadMusicList()
        {
            AppViewModel.SongsSource.Clear();
            AppViewModel.SongsSource.AddRange(await GetMusicListAsync());
            await InitalPlayListAsync();
            await GetPlayListMusic();
            var newList = new BulkObservableCollection<Music>(await LoadPlayList(AppViewModel.SongsSource));
            // 本方法在后台线程执行, 直接赋值会触发 NotifyIconControl 等 x:Bind 在非 UI 线程
            // 更新 XAML 对象(RPC_E_WRONG_THREAD 0x8001010E), 需调度到 UI 线程
            await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
            {
                AppViewModel.SequentialPlayingList = newList;
                AppViewModel.NotifySongsSourceChanged();
            });
        }

        public async Task<IReadOnlyCollection<Music>> GetMusicListAsync()
        {
            string localizedUnknownAlbum = ToolUtils.GetString("UnknownAlbum");
            string localizedUnknownArtist = ToolUtils.GetString("UnknownArtist");
            var musicList = await _dbConnection
                .Table<Music>()
                .OrderBy(m => m.Title)
                .ToListAsync();

            // 优化7: AppData.UnknownAlbums / UnknownArtists 建议在 AppData 中改为 HashSet<string>
            // 以将 Contains 从 O(n) 降为 O(1)，此处调用方式不变，修改点在 AppData 定义处
            foreach (var music in musicList)
            {
                if (AppData.UnknownAlbums.Contains(music.Album) && music.Album != localizedUnknownAlbum)
                {
                    music.Album = localizedUnknownAlbum;
                }

                if (AppData.UnknownArtists.Contains(music.Author) && music.Author != localizedUnknownArtist)
                {
                    music.Author = localizedUnknownArtist;
                }
            }

            return musicList;
        }

        public ObservableCollection<Music> GetFavoriteMusicFromMem(string search = null)
        {
            return new(AppViewModel.SongsSource.Where(m => m.IsFavorite == true).OrderByDescending(m => m.Order));
        }

        public IEnumerable<Music> GetArtistMusicFromMem(string artist, string search = null)
        {
            var query = AppViewModel.SongsSource.AsValueEnumerable();
            if (artist is not null)
            {
                if (!string.IsNullOrEmpty(search))
                {
                    return query.Where(m => ArtistHelper.IsMusicByArtist(m, artist))
                        .Where(m =>
                        m.Title is not null && m.Title.ToLower().Contains(search.ToLower()) ||
                        m.Album is not null && m.Album.ToLower().Contains(search.ToLower())
                    ).OrderBy(m => m.Album).ToImmutableList();
                }
                else
                {
                    return query.Where(m => ArtistHelper.IsMusicByArtist(m, artist))
                         .OrderBy(m => m.Album).ToImmutableList();
                }
            }
            return query.OrderBy(m => m.Album).ToImmutableList();
        }

        public IEnumerable<Music> GetFolderMusicFromMem(string folder, string search = null)
        {
            var query = AppViewModel.SongsSource.AsValueEnumerable();
            if (folder is not null)
            {
                if (!string.IsNullOrEmpty(search))
                {
                    return query.Where(m =>
                        m.Title is not null && m.Title.ToLower().Contains(search.ToLower()) ||
                        m.Album is not null && m.Album.ToLower().Contains(search.ToLower()) ||
                        m.Author is not null && m.Author.ToLower().Contains(search.ToLower())
                    ).Where(m => m.LastLevelFolderPath is not null && m.LastLevelFolderPath.ToLower().Equals(folder.ToLower()))
                    .OrderBy(m => m.LastLevelFolderPath).ToImmutableList();
                }
                else
                {
                    return query.Where(m => m.LastLevelFolderPath is not null && m.LastLevelFolderPath.ToLower().Equals(folder.ToLower()))
                         .OrderBy(m => m.LastLevelFolderPath).ToImmutableList();
                }
            }
            return query.OrderBy(m => m.LastLevelFolderPath).ToImmutableList();
        }

        public async Task GetPlayStateAsync()
        {
            bool isFirstTime = !File.Exists(PlayStatePath);
            var playState = _currentPlayState ?? await GetPlayState();
            playState ??= new SavePlayState();
            await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
            {
                if (playState.LastPlayedMusicId is null && AppViewModel.SongsSource.Count > 0)
                {
                    playState.LastPlayedMusicId = AppViewModel.SongsSource[0].Id;
                }
                AppViewModel.CurrentPlayMode = playState.PlayMode;
                AppViewModel.CurrentPlayingMusic = LoadCurrentPlayingMusic(playState.LastPlayedMusicId);
                AppViewModel.Volume = playState.Volume;
                AppViewModel.TempVolume = playState.Volume;
                AppViewModel.SelectedSortOption = AppViewModel.SortOptions.AsValueEnumerable().FirstOrDefault(item => item.Tag == playState.SortOrder)
                    ?? AppViewModel.SortOptions.AsValueEnumerable().FirstOrDefault() ?? new SortOption("DefaultOrder", "SortOrderDefault");
                if (isFirstTime)
                {
                    _ = WritePlayStateToJson(playState);
                }
            });
        }

        public void LoadWindowState()
        {
            string path = PlayStatePath;
            if (!File.Exists(path))
            {
                _currentPlayState = new SavePlayState();
                return;
            }
            _playStateIoGate.Wait();
            try
            {
                string json = File.ReadAllText(path);
                _currentPlayState = JsonSerializer.Deserialize(json, PlayStateJsonContext.Default.SavePlayState)
                    ?? new SavePlayState();
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, $"PlayState.json 解析失败，备份损坏文件后按空状态继续: {ex.Message}");
                TryBackupCorruptFile(path);
                _currentPlayState = new SavePlayState();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"LoadWindowState 读取播放状态文件时出错: {ex.Message}");
                _currentPlayState = new SavePlayState();
            }
            finally
            {
                _playStateIoGate.Release();
            }
        }

        public async Task GetEqualizerSettingsAsync()
        {
            var equalizerSettings = await GetEqualizer();
            if (equalizerSettings is null)
            {
                equalizerSettings = new SaveEqualizer();
                await InsertEqualizer(equalizerSettings);
            }
            if (equalizerSettings is not null)
            {
                AppSettings.IsEqualizerEnabled = equalizerSettings.IsEqualizerEnabled;
                AppSettings.EqualizerStr = equalizerSettings.EqualizerStr;
                AppSettings.Equalizer = ToolUtils.ConvertToDictionary(equalizerSettings.EqualizerStr);
                AppSettings.EqualizerPreset = equalizerSettings.EqualizerPreset;
            }
        }

        public async Task GetSettingsAsync()
        {
            var settings = await GetSettings();
            if (settings is null)
            {
                settings = new SaveSettings
                {
                    MusicCoverCache = Path.Combine(AppPaths.LocalFolder, "MusicCoverCache")
                };
                await InsertSettings(settings);
            }
            _currentSettings = settings;
            if (settings is not null)
            {
                AppSettings.OutputMode = settings.OutputMode;
                AppSettings.DeviceName = settings.DeviceFriendlyName;
                AppSettings.BassOutputDeviceId = settings.BassOutputDeviceId;
                AppSettings.BassASIODeviceId = settings.BassASIODeviceId;
                AppViewModel.DefaultEntryComboBoxTag = settings.DefaultEntry;
                AppViewModel.DefaultPlayListComboBoxTag = settings.DefaultPlayList;
                AppViewModel.Latency = settings.Latency;
                AppViewModel.BackdropType = settings.AppStyle;
                AppViewModel.ThemeType = settings.AppTheme;
                AppViewModel.IsRunningBackend = settings.IsRunningBackend;
                AppSettings.IsDesktopLyricsEnabled = settings.IsDesktopLyricsEnabled;
                AppSettings.IsDesktopLyricsLocked = settings.IsDesktopLyricsLocked;
                AppSettings.IsDesktopLyricsKaraokeEnabled = settings.IsDesktopLyricsKaraokeEnabled;
                AppSettings.DesktopLyricsFontSize = settings.DesktopLyricsFontSize;
                AppSettings.DesktopLyricsFontFamily = settings.DesktopLyricsFontFamily;
                AppSettings.DesktopLyricsColorRgb = settings.DesktopLyricsColorRgb;
                AppSettings.IsDesktopLyricsCustomColorEnabled = settings.IsDesktopLyricsCustomColorEnabled;
                AppSettings.IsDesktopLyricsTranslationEnabled = settings.IsDesktopLyricsTranslationEnabled;
                AppSettings.DesktopLyricsFontWeight = settings.DesktopLyricsFontWeight;
                AppSettings.LyricsFontWeight = settings.LyricsFontWeight;
                AppViewModel.IsAutoLyricsEnabled = settings.IsAutoLyricsEnabled;
                AppViewModel.IsAutoCoverEnabled = settings.IsAutoCoverEnabled;
                AppViewModel.DsdGain = settings.DsdGain;
                AppViewModel.DsdPcmFreq = settings.DsdPcmFreq;
                AppViewModel.CoverSize = settings.CoverSize;
                AppViewModel.IsFluidBackgroundEnabled = settings.IsFluidBackgroundEnabled;
                AppViewModel.BackgroundShader = (AnimatedWin2dControls.BackgroundShaderMode)settings.BackgroundShader;
                AppViewModel.IsFogEffectEnabled = settings.IsFogEffectEnabled;
                AppViewModel.IsSnowEffectEnabled = settings.IsSnowEffectEnabled;
                AppViewModel.IsRaindropEffectEnabled = settings.IsRaindropEffectEnabled;
                AppViewModel.IsFolderWatchEnabled = settings.IsFolderWatchEnabled;
                AppViewModel.IsCustomAppSize = settings.IsCustomAppSize;
                AppViewModel.AppWidth = settings.AppWidth;
                AppViewModel.AppHeight = settings.AppHeight;
                AppViewModel.FontFamilyList = new ObservableCollection<FontInfo>(ToolUtils.GetSystemFontsInternal());
                AppViewModel.FontFamily = AppViewModel.FontFamilyList.AsValueEnumerable().FirstOrDefault(f => f.Name == ToolUtils.GetCleanFontName(new FontFamily(settings.GlobalFont).Source))
                    // 找不到已保存字体（被卸载/名称不匹配）时必须兜底：FontFamily 为 null 会让
                    // SaveCurrentSettings 取 GlobalFont 时抛 NRE，导致整个会话所有保存静默失败
                    ?? AppViewModel.FontFamilyList.AsValueEnumerable().FirstOrDefault();
                AppViewModel.DesktopLyricsFontSize = settings.DesktopLyricsFontSize;
                AppViewModel.DesktopLyricsFontFamily = AppViewModel.FontFamilyList.AsValueEnumerable().FirstOrDefault(f => f.Name == ToolUtils.GetCleanFontName(new FontFamily(settings.DesktopLyricsFontFamily).Source))
                    ?? AppViewModel.FontFamilyList.AsValueEnumerable().FirstOrDefault();
                AppViewModel.DesktopLyricsColor = Color.FromArgb(0xFF,
                    (byte)((settings.DesktopLyricsColorRgb >> 16) & 0xFF),
                    (byte)((settings.DesktopLyricsColorRgb >> 8) & 0xFF),
                    (byte)(settings.DesktopLyricsColorRgb & 0xFF));
                AppViewModel.IsDesktopLyricsCustomColorEnabled = settings.IsDesktopLyricsCustomColorEnabled;
                AppViewModel.IsDesktopLyricsKaraokeEnabled = settings.IsDesktopLyricsKaraokeEnabled;
                AppViewModel.IsDesktopLyricsTranslationEnabled = settings.IsDesktopLyricsTranslationEnabled;
                AppViewModel.IsDesktopLyricsGlowEnabled = settings.IsDesktopLyricsGlowEnabled;
                AppViewModel.IsDesktopLyricsCharFloatEnabled = settings.IsDesktopLyricsCharFloatEnabled;
                AppViewModel.IsDesktopLyricsCharScaleEnabled = settings.IsDesktopLyricsCharScaleEnabled;
                AppViewModel.DesktopLyricsLongSyllableThreshold = settings.DesktopLyricsLongSyllableThreshold;
                AppViewModel.DesktopLyricsGlowAmount = settings.DesktopLyricsGlowAmount;
                AppViewModel.DesktopLyricsCharFloatAmount = settings.DesktopLyricsCharFloatAmount;
                AppViewModel.DesktopLyricsCharScaleAmount = settings.DesktopLyricsCharScaleAmount;
                AppViewModel.DesktopLyricsShadowAmount = settings.DesktopLyricsShadowAmount;
                AppViewModel.DesktopLyricsFontWeight = settings.DesktopLyricsFontWeight;
                AppViewModel.LyricsFontWeight = settings.LyricsFontWeight;
                AppViewModel.CustomOpacity = settings.CustomAcrylicOpacity;
                AppViewModel.CustomColor = Color.FromArgb(
                    (byte)((settings.CustomColorArgb >> 24) & 0xFF),
                    (byte)((settings.CustomColorArgb >> 16) & 0xFF),
                    (byte)((settings.CustomColorArgb >> 8) & 0xFF),
                    (byte)(settings.CustomColorArgb & 0xFF));
                AppViewModel.IsCustomLyricsColorEnabled = settings.IsCustomLyricsColorEnabled;
                AppViewModel.LyricsCustomColor = Color.FromArgb(0xFF,
                    (byte)((settings.LyricsCustomColorRgb >> 16) & 0xFF),
                    (byte)((settings.LyricsCustomColorRgb >> 8) & 0xFF),
                    (byte)(settings.LyricsCustomColorRgb & 0xFF));
                AppViewModel.IsUpdateBackDrop = settings.IsUpdateBackDrop;
                // 自定义图片背景: 仅在持久文件仍存在时恢复(防止误删目录后启动异常)
                AppSettings.CustomBackgroundPath = settings.CustomBackgroundPath;
                AppViewModel.CustomBackgroundFileName = !string.IsNullOrEmpty(settings.CustomBackgroundPath) && File.Exists(settings.CustomBackgroundPath)
                    ? Path.GetFileName(settings.CustomBackgroundPath) : string.Empty;
                AppSettings.CustomBackgroundBlur = Math.Clamp(settings.CustomBackgroundBlur, 0, 50);
                AppViewModel.CustomBackgroundBlur = AppSettings.CustomBackgroundBlur;
                AppSettings.DownloadPath = settings.DownloadPath ?? string.Empty;
                AppSettings.DownloadQuality = LxSources.NormalizeQuality(settings.DownloadQuality) is { Length: > 0 } q ? q : "320k";
                AppSettings.DownloadSaveLrc = settings.DownloadSaveLrc;
                AppSettings.DownloadSaveCover = settings.DownloadSaveCover;
                AppViewModel.LyricsAlignment = settings.LyricsAlignment;
                AppViewModel.LyricsMargin = new Thickness(settings.LyricsMargin, 0, settings.LyricsMargin, 0);
                AppViewModel.GlobalFontSize = settings.GlobalFontSize;
                AppViewModel.IsGlobalFontSizeEnabled = settings.IsGlobalFontSizeEnabled;
                AppViewModel.MusicCoverCache = string.IsNullOrEmpty(settings.MusicCoverCache) ? Path.Combine(AppPaths.LocalFolder, "MusicCoverCache") : settings.MusicCoverCache;
                AppViewModel.IsDopEnabled = settings.IsDopEnabled;
                AppViewModel.IsFadeEnabled = settings.IsFadeEnabled;
                AppViewModel.LyricsBlurAmount = settings.LyricsBlurAmount;
                AppViewModel.UseImageDominantTheme = settings.UseImageDominantTheme;
                AppViewModel.EnableLightWave = settings.EnableLightWave;
                AppViewModel.PaletteAlgorithm = (AnimatedWin2dControls.Impressionist.PaletteAlgorithm)settings.PaletteAlgorithm;
                AppViewModel.IsWin2dCoverImageControlEnable = settings.IsWin2dCoverImageControlEnable;
                AppViewModel.IsWin2dAnimatedText = settings.IsWin2dAnimatedText;
                AppViewModel.Win2dTextEffectType = AppViewModel.TextEffectItems.AsValueEnumerable().FirstOrDefault(t => t.Value == settings.Win2dTextEffectType) ?? AppViewModel.TextEffectItems[0];
                AppViewModel.CharFloatAmount = settings.CharFloatAmount;
                AppViewModel.CharScaleAmount = settings.CharScaleAmount;
                AppViewModel.GlowAmount = settings.GlowAmount;
                AppViewModel.LongSyllableThreshold = settings.LongSyllableThreshold;
                AppViewModel.PlayingLineTopOffsetPercent = settings.PlayingLineTopOffsetPercent;
                AppViewModel.TranslatedOpacityPercent = settings.TranslatedOpacityPercent;
                AppViewModel.UnplayedOpacityPercent = settings.UnplayedOpacityPercent;
                AppViewModel.TargetFrameRate = settings.TargetFrameRate;
                AppViewModel.EnableAdvancedLyricsEffect = settings.EnableAdvancedLyricsEffect;
                AppViewModel.ScrollEasingType = settings.ScrollEasingType;
                AppViewModel.ScrollEasingMode = settings.ScrollEasingMode;
                AppViewModel.PlayOrPauseShortcut = settings.PlayOrPauseShortcut;
                AppViewModel.NextSongShortcut = settings.NextSongShortcut;
                AppViewModel.PreviousSongShortcut = settings.PreviousSongShortcut;
                AppViewModel.VolumeUpShortcut = settings.VolumeUpShortcut;
                AppViewModel.VolumeDownShortcut = settings.VolumeDownShortcut;
                AppViewModel.TogglePlayingDetailShortcut = settings.TogglePlayingDetailShortcut;
                AppViewModel.BackShortcut = settings.BackShortcut;
                AppViewModel.ShowWindowShortcut = settings.ShowWindowShortcut;
                AppViewModel.ToggleFullScreenShortcut = settings.ToggleFullScreenShortcut;
                AppSettings.EnableGlobalHotKey = settings.EnableGlobalHotKey;
                AppViewModel.EnableGlobalHotKey = settings.EnableGlobalHotKey;
                AppSettings.IsTrimOnHideEnabled = settings.IsTrimOnHideEnabled;
                AppViewModel.IsTrimOnHideEnabled = settings.IsTrimOnHideEnabled;
                AppSettings.IsTrimAfterPlaybackEnabled = settings.IsTrimAfterPlaybackEnabled;
                AppViewModel.IsTrimAfterPlaybackEnabled = settings.IsTrimAfterPlaybackEnabled;
                AppViewModel.ArtistSplitSymbols = settings.ArtistSplitSymbols;
                AppViewModel.PlayingDetailAlignment = settings.PlayingDetailAlignment;
                AppViewModel.UsePlayingDetailAlignmentInPortrait = settings.UsePlayingDetailAlignmentInPortrait;
                AppViewModel.IsMusicInfoVisible = settings.IsMusicInfoVisible;
                LoadSettingsToAppViewModel();
            }
        }

        private void LoadSettingsToAppViewModel()
        {
            if (AppViewModel.BackdropType != "CustomAcrylicStyle")
            {
                AppViewModel.IsColorPickerVisible = false;
            }
            else
            {
                AppViewModel.IsColorPickerVisible = true;
            }
            AppViewModel.Version = GetAppVersion();
            _ = AppViewModel.GetWasapiDeviceAsync();
        }

        /// <summary>
        /// 获取应用版本号。非 MSIX 打包(无包标识)进程访问 Package.Current 会抛 0x80073D54,回退到程序集版本
        /// </summary>
        private static string GetAppVersion()
        {
            try
            {
                var v = Windows.ApplicationModel.Package.Current.Id.Version;
                return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            }
            catch (InvalidOperationException)
            {
                var v = typeof(MusicDatabaseService).Assembly.GetName().Version;
                return $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }

        public async Task SaveSettingAsync()
        {
            try
            {
                // 以最后一次已知的磁盘状态为基底合并写盘：未被 SaveCurrentSettings 覆盖的字段
                // 不会被默认值冲掉；快照构建的异常也不再静默吞掉（此前 fire-and-forget 会丢掉整次保存）。
                SaveSettings merged = _currentSettings ?? await GetSettings();
                SaveCurrentSettings(merged);
                await WriteSettingsToJson(merged);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SaveSettingAsync 保存设置失败: {ex.Message}");
            }
        }

        public async Task SaveEqualizerSettingAsync()
        {
            SaveEqualizer equalizerSettings = await GetEqualizer();
            SaveEqualizer newEqualizer = SaveEqualizeSettings(new SaveEqualizer(), equalizerSettings.EqualizerStr);
            if (equalizerSettings is null)
            {
                await InsertEqualizer(newEqualizer);
            }
            else
            {
                newEqualizer.Id = equalizerSettings.Id;
                await UpdateEqualizer(newEqualizer);
            }
        }

        private SaveEqualizer SaveEqualizeSettings(SaveEqualizer newEqualizer, string equalizerStr = null)
        {
            newEqualizer.EqualizerStr = equalizerStr ?? AppSettings.EqualizerStr;
            newEqualizer.IsEqualizerEnabled = AppSettings.IsEqualizerEnabled;
            newEqualizer.EqualizerPreset = AppSettings.EqualizerPreset;
            return newEqualizer;
        }

        private SaveSettings SaveCurrentSettings(SaveSettings newSettings)
        {
            newSettings.OutputMode = AppSettings.OutputMode;
            newSettings.DeviceFriendlyName = AppSettings.DeviceName;
            newSettings.BassOutputDeviceId = AppSettings.BassOutputDeviceId;
            newSettings.BassASIODeviceId = AppSettings.BassASIODeviceId;
            newSettings.Latency = AppViewModel.Latency;
            newSettings.DefaultEntry = AppViewModel.DefaultEntryComboBoxTag;
            newSettings.DefaultPlayList = AppViewModel.DefaultPlayListComboBoxTag;
            newSettings.AppStyle = AppViewModel.BackdropType;
            newSettings.AppTheme = AppViewModel.ThemeType;
            newSettings.IsRunningBackend = AppViewModel.IsRunningBackend;
            newSettings.IsAutoLyricsEnabled = AppViewModel.IsAutoLyricsEnabled;
            newSettings.IsAutoCoverEnabled = AppViewModel.IsAutoCoverEnabled;
            newSettings.DsdGain = AppViewModel.DsdGain;
            newSettings.CoverSize = AppViewModel.CoverSize;
            newSettings.Win2dTextEffectType = AppViewModel.Win2dTextEffectType.Value;
            newSettings.IsFluidBackgroundEnabled = AppViewModel.IsFluidBackgroundEnabled;
            newSettings.BackgroundShader = (int)AppViewModel.BackgroundShader;
            newSettings.IsFogEffectEnabled = AppViewModel.IsFogEffectEnabled;
            newSettings.IsSnowEffectEnabled = AppViewModel.IsSnowEffectEnabled;
            newSettings.IsRaindropEffectEnabled = AppViewModel.IsRaindropEffectEnabled;
            newSettings.IsFolderWatchEnabled = AppViewModel.IsFolderWatchEnabled;
            newSettings.IsCustomAppSize = AppViewModel.IsCustomAppSize;
            newSettings.AppHeight = AppViewModel.AppHeight;
            newSettings.AppWidth = AppViewModel.AppWidth;
            newSettings.GlobalFont = AppViewModel.FontFamily.FontFamily.Source;
            newSettings.CustomAcrylicOpacity = AppViewModel.CustomOpacity;
            newSettings.CustomColorArgb = (uint)((AppViewModel.CustomColor.A << 24) | (AppViewModel.CustomColor.R << 16) | (AppViewModel.CustomColor.G << 8) | AppViewModel.CustomColor.B);
            newSettings.IsCustomLyricsColorEnabled = AppViewModel.IsCustomLyricsColorEnabled;
            newSettings.LyricsCustomColorRgb = (uint)((AppViewModel.LyricsCustomColor.R << 16) | (AppViewModel.LyricsCustomColor.G << 8) | AppViewModel.LyricsCustomColor.B);
            newSettings.IsUpdateBackDrop = AppViewModel.IsUpdateBackDrop;
            newSettings.CustomBackgroundPath = AppSettings.CustomBackgroundPath;
            newSettings.CustomBackgroundBlur = AppSettings.CustomBackgroundBlur;
            newSettings.DownloadPath = AppSettings.DownloadPath;
            newSettings.DownloadQuality = AppSettings.DownloadQuality;
            newSettings.DownloadSaveLrc = AppSettings.DownloadSaveLrc;
            newSettings.DownloadSaveCover = AppSettings.DownloadSaveCover;
            newSettings.LyricsAlignment = AppViewModel.LyricsAlignment;
            newSettings.LyricsMargin = (int)AppViewModel.LyricsMargin.Left;
            newSettings.GlobalFontSize = AppViewModel.GlobalFontSize;
            newSettings.IsGlobalFontSizeEnabled = AppViewModel.IsGlobalFontSizeEnabled;
            newSettings.MusicCoverCache = AppViewModel.MusicCoverCache;
            newSettings.IsDopEnabled = AppViewModel.IsDopEnabled;
            newSettings.DsdPcmFreq = AppViewModel.DsdPcmFreq;
            newSettings.IsFadeEnabled = AppViewModel.IsFadeEnabled;
            newSettings.LyricsBlurAmount = AppViewModel.LyricsBlurAmount;
            newSettings.UseImageDominantTheme = AppViewModel.UseImageDominantTheme;
            newSettings.EnableLightWave = AppViewModel.EnableLightWave;
            newSettings.PaletteAlgorithm = (int)AppViewModel.PaletteAlgorithm;
            newSettings.IsWin2dAnimatedText = AppViewModel.IsWin2dAnimatedText;
            newSettings.IsWin2dCoverImageControlEnable = AppViewModel.IsWin2dCoverImageControlEnable;
            newSettings.CharFloatAmount = AppViewModel.CharFloatAmount;
            newSettings.CharScaleAmount = AppViewModel.CharScaleAmount;
            newSettings.GlowAmount = AppViewModel.GlowAmount;
            newSettings.LongSyllableThreshold = AppViewModel.LongSyllableThreshold;
            newSettings.PlayingLineTopOffsetPercent = AppViewModel.PlayingLineTopOffsetPercent;
            newSettings.TranslatedOpacityPercent = AppViewModel.TranslatedOpacityPercent;
            newSettings.UnplayedOpacityPercent = AppViewModel.UnplayedOpacityPercent;
            newSettings.TargetFrameRate = AppViewModel.TargetFrameRate;
            newSettings.EnableAdvancedLyricsEffect = AppViewModel.EnableAdvancedLyricsEffect;
            newSettings.ScrollEasingType = AppViewModel.ScrollEasingType;
            newSettings.ScrollEasingMode = AppViewModel.ScrollEasingMode;
            newSettings.PlayOrPauseShortcut = AppViewModel.PlayOrPauseShortcut;
            newSettings.NextSongShortcut = AppViewModel.NextSongShortcut;
            newSettings.PreviousSongShortcut = AppViewModel.PreviousSongShortcut;
            newSettings.VolumeUpShortcut = AppViewModel.VolumeUpShortcut;
            newSettings.VolumeDownShortcut = AppViewModel.VolumeDownShortcut;
            newSettings.TogglePlayingDetailShortcut = AppViewModel.TogglePlayingDetailShortcut;
            newSettings.BackShortcut = AppViewModel.BackShortcut;
            newSettings.ShowWindowShortcut = AppViewModel.ShowWindowShortcut;
            newSettings.ToggleFullScreenShortcut = AppViewModel.ToggleFullScreenShortcut;
            newSettings.EnableGlobalHotKey = AppViewModel.EnableGlobalHotKey;
            newSettings.IsTrimOnHideEnabled = AppViewModel.IsTrimOnHideEnabled;
            newSettings.IsTrimAfterPlaybackEnabled = AppViewModel.IsTrimAfterPlaybackEnabled;
            newSettings.ArtistSplitSymbols = AppViewModel.ArtistSplitSymbols;
            newSettings.PlayingDetailAlignment = AppViewModel.PlayingDetailAlignment;
            newSettings.UsePlayingDetailAlignmentInPortrait = AppViewModel.UsePlayingDetailAlignmentInPortrait;
            newSettings.IsDesktopLyricsEnabled = AppSettings.IsDesktopLyricsEnabled;
            newSettings.IsDesktopLyricsLocked = AppSettings.IsDesktopLyricsLocked;
            newSettings.IsDesktopLyricsKaraokeEnabled = AppSettings.IsDesktopLyricsKaraokeEnabled;
            newSettings.DesktopLyricsFontSize = AppSettings.DesktopLyricsFontSize;
            newSettings.DesktopLyricsFontFamily = AppSettings.DesktopLyricsFontFamily;
            newSettings.DesktopLyricsColorRgb = AppSettings.DesktopLyricsColorRgb;
            newSettings.IsDesktopLyricsCustomColorEnabled = AppSettings.IsDesktopLyricsCustomColorEnabled;
            newSettings.IsDesktopLyricsTranslationEnabled = AppSettings.IsDesktopLyricsTranslationEnabled;
            newSettings.IsDesktopLyricsGlowEnabled = AppSettings.IsDesktopLyricsGlowEnabled;
            newSettings.IsDesktopLyricsCharFloatEnabled = AppSettings.IsDesktopLyricsCharFloatEnabled;
            newSettings.IsDesktopLyricsCharScaleEnabled = AppSettings.IsDesktopLyricsCharScaleEnabled;
            newSettings.DesktopLyricsLongSyllableThreshold = AppSettings.DesktopLyricsLongSyllableThreshold;
            newSettings.DesktopLyricsGlowAmount = AppSettings.DesktopLyricsGlowAmount;
            newSettings.DesktopLyricsCharFloatAmount = AppSettings.DesktopLyricsCharFloatAmount;
            newSettings.DesktopLyricsCharScaleAmount = AppSettings.DesktopLyricsCharScaleAmount;
            newSettings.DesktopLyricsShadowAmount = AppSettings.DesktopLyricsShadowAmount;
            newSettings.DesktopLyricsFontWeight = AppSettings.DesktopLyricsFontWeight;
            newSettings.LyricsFontWeight = AppSettings.LyricsFontWeight;
            newSettings.IsMusicInfoVisible = AppViewModel.IsMusicInfoVisible;
            return newSettings;
        }

        public async Task RemoveMusic(int musicId)
        {
            try
            {
                await _dbConnection.DeleteAsync<Music>(musicId);
                AppViewModel.SongsSource.Clear();
                AppViewModel.SongsSource.AddRange(await _dbConnection.Table<Music>().ToListAsync());
                AppViewModel.NotifySongsSourceChanged();
                var usbMusicGroups = AppData.MusicOnUsbDevice.AsValueEnumerable()
                    .GroupBy(u => u.Title)
                    .ToDictionary(g => g.Key, g => g.AsValueEnumerable().ToList());
                foreach (var music in AppViewModel.SongsSource)
                {
                    music.IsExistOnDevice = 0;
                    if (usbMusicGroups.TryGetValue(music.Title, out var matchingItems))
                    {
                        music.IsExistOnDevice = 1;
                        foreach (var usbMusic in matchingItems)
                        {
                            if (music.Author == usbMusic.Author &&
                                music.Album == usbMusic.Album &&
                                music.Extension == usbMusic.Extension)
                            {
                                music.IsExistOnDevice = 2;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"RemoveMusic 删除音乐时出错: {e.Message}");
            }
        }

        public async Task AddToFavourite(Music music)
        {
            await _dbConnection.UpdateAsync(music);
        }

        public Music? LoadCurrentPlayingMusic(int? lastPlayedMusicId)
        {
            return AppViewModel.SongsSource.FirstOrDefault(m => m.Id == lastPlayedMusicId);
        }

        public async Task SavePlayStateAsync(SavePlayState playState, IEnumerable<Music> currentPlayingList)
        {
            try
            {
                await _dbConnection.DeleteAllAsync<LastPlayListState>().ConfigureAwait(false);
                var musicIds = string.Join(',', currentPlayingList.AsValueEnumerable().Select(m => m.Id).ToArray());
                await _dbConnection.InsertAsync(new LastPlayListState { PlayListMusicIds = musicIds }).ConfigureAwait(false);

                await WritePlayStateToJson(playState);
                _currentPlayState = playState;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SavePlayStateAsync 异步保存播放状态时出错: {ex.Message}");
            }
        }

        public void WritePlayStateJsonSync()
        {
            if (_currentPlayState == null) return;
            _playStateIoGate.Wait();
            try
            {
                string json = JsonSerializer.Serialize(_currentPlayState, PlayStateJsonContext.Default.SavePlayState);
                File.WriteAllText(PlayStatePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"WritePlayStateJsonSync 写入播放状态 JSON 时出错: {ex.Message}");
            }
            finally
            {
                _playStateIoGate.Release();
            }
        }

        public async Task<List<Music>> GetMusicListByFolder(StorageFolder folder)
        {
            var musicFiles = new List<(Music Music, string Lyrics)>();
            await addFolderService.GetMusicFilesRecursive(folder, musicFiles);
            return musicFiles.AsValueEnumerable().Select(r => r.Music).ToList();
        }

        public async Task ScanFolderAsync(StorageFolder folder, int folderId)
        {
            var musicFiles = new List<(Music Music, string Lyrics)>();
            List<SubFolder> subFolders = AutoRescanService.RecordInitialFolderTimes(folder.Path, folderId);
            await InsertSubFolders(subFolders);
            await addFolderService.GetMusicFilesRecursive(folder, musicFiles);
            var existingMusicPaths = await _dbConnection.Table<Music>()
                .ToListAsync()
                .ContinueWith(t => t.Result.Select(m => m.Path).ToHashSet(StringComparer.OrdinalIgnoreCase));

            // 优化9: 用 HashSet 做路径查重，O(1) 代替 O(n)
            var newMusicFiles = musicFiles.AsValueEnumerable()
                .Where(r => !existingMusicPaths.Contains(r.Music.Path))
                .ToList();

            if (newMusicFiles.Count != 0)
            {
                await _dbConnection.InsertAllAsync(newMusicFiles.AsValueEnumerable().Select(r => r.Music).ToList());
                await SaveEmbeddedLyricsAsync(newMusicFiles);
            }
        }

        public async Task RemoveFolder(int folderId)
        {
            var folderToRemove = await _dbConnection.Table<Folder>().Where(f => f.Id == folderId).FirstOrDefaultAsync();
            if (folderToRemove is not null)
            {
                var musicFilesToRemove = await _dbConnection.Table<Music>()
                    .Where(m => m.FolderPath.StartsWith(folderToRemove.Path))
                    .ToListAsync();

                foreach (var musicFile in musicFilesToRemove)
                {
                    await _dbConnection.DeleteAsync(musicFile);
                    await _dbConnection.DeleteAsync<MusicLyrics>(musicFile.Id);
                }

                var subfoldersToRemove = await _dbConnection.Table<SubFolder>()
                    .Where(sf => sf.Path.StartsWith(folderToRemove.Path))
                    .ToListAsync();
                foreach (var subfolder in subfoldersToRemove)
                {
                    await _dbConnection.DeleteAsync(subfolder);
                }

                await _dbConnection.DeleteAsync(folderToRemove);
            }
        }

        public async Task<Folder> GetFolder(int folderId)
        {
            return await _dbConnection.Table<Folder>().Where(f => f.Id == folderId).FirstOrDefaultAsync();
        }

        public async Task CheckFolderBeforeAdd(StorageFolder folder)
        {
            var existingFolders = await _dbConnection.Table<Folder>().ToListAsync();

            bool folderAlreadyExists = existingFolders.AsValueEnumerable().Any(f =>
                folder.Path.StartsWith(f.Path) || f.Path.StartsWith(folder.Path));

            if (!folderAlreadyExists)
            {
                var foldersToRemove = existingFolders.AsValueEnumerable()
                    .Where(f => folder.Path.StartsWith(f.Path))
                    .ToList();

                foreach (var folderToRemove in foldersToRemove)
                {
                    var musicFilesToRemove = await _dbConnection.Table<Music>()
                        .Where(m => m.FolderPath.StartsWith(folderToRemove.Path))
                        .ToListAsync();

                    foreach (var musicFile in musicFilesToRemove)
                    {
                        await _dbConnection.DeleteAsync(musicFile);
                        await _dbConnection.DeleteAsync<MusicLyrics>(musicFile.Id);
                    }

                    await _dbConnection.DeleteAsync(folderToRemove);
                }

                var newFolder = new Folder
                {
                    Name = folder.Name,
                    Path = folder.Path,
                    Type = "本地"
                };
                await _dbConnection.InsertAsync(newFolder);
                await ScanFolderAsync(folder, newFolder.Id);
            }
        }

        public async Task<List<StorageFile>> GetAllFilesInFolderAndSubfolders(StorageFolder folder)
        {
            var allFiles = new List<StorageFile>();

            try
            {
                var currentFiles = await folder.GetFilesAsync();
                allFiles.AddRange(currentFiles);
                var subFolders = await folder.GetFoldersAsync();
                foreach (var subFolder in subFolders)
                {
                    var subFolderFiles = await GetAllFilesInFolderAndSubfolders(subFolder);
                    allFiles.AddRange(subFolderFiles);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GetAllFilesInFolderAndSubfolders 获取文件时出错: {ex.Message}");
            }

            return allFiles;
        }

        private async Task<(Music Music, string Lyrics)> UpdateMusic(Music music)
        {
            StorageFile storageFile = await StorageFile.GetFileFromPathAsync(music.Path);
            var (newMusic, lyrics) = await ToolUtils.GetMusicInfo(storageFile);
            if (newMusic is null) return (music, "");
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                music.Title = newMusic.Title;
                music.Author = newMusic.Author;
                music.Duration = newMusic.Duration;
                music.Album = newMusic.Album;
                music.FolderPath = newMusic.FolderPath;
                music.LastLevelFolderPath = newMusic.LastLevelFolderPath;
                music.BitDepth = newMusic.BitDepth;
                music.BitRate = newMusic.BitRate;
                music.SampleRate = newMusic.SampleRate;
                music.Channel = newMusic.Channel;
                music.TrackNumber = newMusic.TrackNumber;
                music.DiskNumber = newMusic.DiskNumber;
                music.Year = newMusic.Year;
                music.UpdateTime = newMusic.UpdateTime;
                music.CreateTime = newMusic.CreateTime;
            });
            return (music, lyrics ?? "");
        }

        public async Task RescanFolder(int folderId)
        {
            var folderToRescan = await _dbConnection.Table<Folder>().Where(f => f.Id == folderId).FirstOrDefaultAsync();
            if (folderToRescan is not null)
            {
                try
                {
                    await RescanFolderByPath(folderToRescan.Path);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"RescanFolder 重新扫描文件夹时出错: {ex.Message}");
                }
            }
        }

        public async Task RescanFolderByPath(string folderPath, bool isUpdate = true, bool isSingleFolder = false)
        {
            var musicPaths = await Task.Run(() =>
                isSingleFolder ? EnumerateMusicFilesInDirectory(folderPath) : EnumerateAllMusicFiles(folderPath));

            var filePaths = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in musicPaths)
                filePaths.TryAdd(path, true);

            List<Music> musicFilesInFolder;
            if (isSingleFolder)
            {
                musicFilesInFolder = await _dbConnection.Table<Music>()
                    .Where(m => m.FolderPath == folderPath)
                    .ToListAsync();
            }
            else
            {
                musicFilesInFolder = await _dbConnection.Table<Music>()
                   .Where(m => m.FolderPath.Contains(folderPath))
                   .ToListAsync();
            }

            // 优化13: 并行检查改预分配数组
            var checkTasks = new Task[musicFilesInFolder.Count];
            // 优化14: toDelete/toUpdate 在并行中使用 ConcurrentBag（局部），安全且无共享状态
            var toDeleteBag = new ConcurrentBag<Music>();
            var toUpdateBag = new ConcurrentBag<Music>();
            for (int i = 0; i < musicFilesInFolder.Count; i++)
            {
                var music = musicFilesInFolder[i];
                checkTasks[i] = CheckMusicExistsAsync(music, filePaths, toDeleteBag, toUpdateBag);
            }
            await Task.WhenAll(checkTasks);

            // 优化15: 批量删除，预分配数组
            var deleteList = toDeleteBag.ToList();
            var deleteTasks = new Task[deleteList.Count];
            for (int i = 0; i < deleteList.Count; i++)
            {
                var music = deleteList[i];
                deleteTasks[i] = DeleteMusicAsync(music);
            }
            await Task.WhenAll(deleteTasks);

            // 优化16: 批量更新，预分配数组
            var updateList = toUpdateBag.ToList();
            var updateTasks = new Task<(Music Music, string Lyrics)>[updateList.Count];
            for (int i = 0; i < updateList.Count; i++)
            {
                var music = updateList[i];
                updateTasks[i] = UpdateMusicWithSemaphoreAsync(music);
            }
            var results = await Task.WhenAll(updateTasks);
            var validResults = results.AsValueEnumerable().Where(r => r.Music is not null).ToList();
            if (validResults.Count != 0)
            {
                await _dbConnection.UpdateAllAsync(validResults.AsValueEnumerable().Select(r => r.Music).ToList());
                await SaveEmbeddedLyricsAsync(validResults);
            }

            // 优化17: 新增文件批量处理，预分配数组
            var filePathKeys = filePaths.Keys.ToList();
            var addTasks = new Task<(Music? Music, string Lyrics)>[filePathKeys.Count];
            for (int i = 0; i < filePathKeys.Count; i++)
            {
                var path = filePathKeys[i];
                addTasks[i] = AddNewMusicAsync(path);
            }
            var addResults = await Task.WhenAll(addTasks);
            var validMusic = addResults.AsValueEnumerable().Where(r => r.Music is not null).ToList();
            if (validMusic.Count != 0)
            {
                await _dbConnection.InsertAllAsync(validMusic.AsValueEnumerable().Select(r => r.Music!).ToList());
                await SaveEmbeddedLyricsAsync(validMusic);
            }

            if (isUpdate)
            {
                await App.Services.GetRequiredService<AppViewModel>().RefreshSongsSourceAsync();
            }
        }

        // 优化18: 提取具名私有方法，编译器可生成 struct 状态机（相比 async lambda 减少堆分配）
        private async Task AddFilePathAsync(StorageFile file, ConcurrentDictionary<string, bool> filePaths)
        {
            await _rescanfolderSemaphore.WaitAsync();
            try
            {
                if (ToolUtils.IsMusicFile(file.FileType))
                {
                    filePaths.TryAdd(file.Path, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"AddFilePathAsync 添加文件路径时出错: {ex.Message}");
            }
            finally
            {
                _rescanfolderSemaphore.Release();
            }
        }

        private async Task CheckMusicExistsAsync(Music music, ConcurrentDictionary<string, bool> filePaths,
            ConcurrentBag<Music> toDelete, ConcurrentBag<Music> toUpdate)
        {
            await _rescanfolderSemaphore.WaitAsync();
            try
            {
                if (!filePaths.ContainsKey(music.Path))
                {
                    toDelete.Add(music);
                }
                else
                {
                    toUpdate.Add(music);
                    filePaths.TryRemove(music.Path, out _);
                }
            }
            finally
            {
                _rescanfolderSemaphore.Release();
            }
        }

        private async Task DeleteMusicAsync(Music music)
        {
            await _rescanfolderSemaphore.WaitAsync();
            try
            {
                await _dbConnection.DeleteAsync(music);
                await _dbConnection.DeleteAsync<MusicLyrics>(music.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"DeleteMusicAsync 删除音乐文件时出错: {ex.Message}");
            }
            finally
            {
                _rescanfolderSemaphore.Release();
            }
        }

        private async Task<(Music Music, string Lyrics)> UpdateMusicWithSemaphoreAsync(Music music)
        {
            await _rescanfolderSemaphore.WaitAsync();
            try
            {
                return await UpdateMusic(music);
            }
            finally
            {
                _rescanfolderSemaphore.Release();
            }
        }

        private async Task<(Music? Music, string Lyrics)> AddNewMusicAsync(string path)
        {
            await _rescanfolderSemaphore.WaitAsync();
            try
            {
                var existingMusic = await _dbConnection.Table<Music>().Where(m => m.Path == path).FirstOrDefaultAsync();
                if (existingMusic is not null)
                {
                    return (null, "");
                }
                StorageFile storageFile = await StorageFile.GetFileFromPathAsync(path);
                var (music, lyrics) = await ToolUtils.GetMusicInfo(storageFile);
                return (music, lyrics ?? "");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"AddNewMusicAsync 添加新音乐文件时出错: {ex.Message}");
                return (null, "");
            }
            finally
            {
                _rescanfolderSemaphore.Release();
            }
        }

        public async Task<int> RescanFolderWithOutUpdateAll(string folderPath, bool isSingleFolder = false)
        {
            var toDelete = new ConcurrentBag<Music>();

            var musicPaths = await Task.Run(() =>
                isSingleFolder ? EnumerateMusicFilesInDirectory(folderPath) : EnumerateAllMusicFiles(folderPath));

            var filePaths = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in musicPaths)
                filePaths.TryAdd(path, true);

            List<Music> musicFilesInFolder;
            if (isSingleFolder)
            {
                musicFilesInFolder = await _dbConnection.Table<Music>()
                    .Where(m => m.FolderPath == folderPath)
                    .ToListAsync();
            }
            else
            {
                musicFilesInFolder = await _dbConnection.Table<Music>()
                   .Where(m => m.FolderPath.Contains(folderPath))
                   .ToListAsync();
            }

            var checkTasks = new Task[musicFilesInFolder.Count];
            for (int i = 0; i < musicFilesInFolder.Count; i++)
            {
                var music = musicFilesInFolder[i];
                checkTasks[i] = CheckMusicExistsForRescanAsync(music, filePaths, toDelete);
            }
            await Task.WhenAll(checkTasks);

            var deleteList = toDelete.ToList();
            var deleteTasks = new Task[deleteList.Count];
            for (int i = 0; i < deleteList.Count; i++)
            {
                var music = deleteList[i];
                deleteTasks[i] = DeleteMusicAsync(music);
            }
            await Task.WhenAll(deleteTasks);

            var filePathKeys = filePaths.Keys.ToList();
            var addTasks = new Task<(Music? Music, string Lyrics)>[filePathKeys.Count];
            for (int i = 0; i < filePathKeys.Count; i++)
            {
                var path = filePathKeys[i];
                addTasks[i] = AddNewMusicAsync(path);
            }
            var addResults = await Task.WhenAll(addTasks);
            var validMusic = addResults.AsValueEnumerable().Where(r => r.Music is not null).ToList();
            if (validMusic.Count != 0)
            {
                await _dbConnection.InsertAllAsync(validMusic.AsValueEnumerable().Select(r => r.Music!).ToList());
                await SaveEmbeddedLyricsAsync(validMusic);
            }

            return validMusic.Count;
        }

        private async Task CheckMusicExistsForRescanAsync(Music music, ConcurrentDictionary<string, bool> filePaths, ConcurrentBag<Music> toDelete)
        {
            await _rescanfolderSemaphore.WaitAsync();
            try
            {
                if (!filePaths.ContainsKey(music.Path))
                {
                    toDelete.Add(music);
                }
                else
                {
                    filePaths.TryRemove(music.Path, out _);
                }
            }
            finally
            {
                _rescanfolderSemaphore.Release();
            }
        }

        public async Task AddMusicList(IEnumerable<Music> _toAdd)
        {
            var toAddList = _toAdd is ICollection<Music> c ? new List<Music>(c) : _toAdd.ToList();
            if (toAddList.Count == 0) return;

            var validMusic = new List<(Music Music, string Lyrics)>();
            var channel = Channel.CreateUnbounded<Music>();
            int workerCount = Math.Min(4, toAddList.Count);
            var workers = new Task[workerCount];

            for (int i = 0; i < workerCount; i++)
                workers[i] = WorkerLoop(channel.Reader);

            foreach (var m in toAddList)
                channel.Writer.TryWrite(m);
            channel.Writer.Complete();

            await Task.WhenAll(workers);

            if (validMusic.Count != 0)
                await _dbConnection.InsertAllAsync(validMusic.AsValueEnumerable().Select(r => r.Music).ToList());
            await SaveEmbeddedLyricsAsync(validMusic);

            async Task WorkerLoop(ChannelReader<Music> reader)
            {
                while (await reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (reader.TryRead(out var m))
                    {
                        var (music, lyrics) = await AddMusicFromPathAsync(m.Path);
                        if (music is not null)
                            lock (validMusic) validMusic.Add((music, lyrics));
                    }
                }
            }
        }

        private async Task<(Music? Music, string Lyrics)> AddMusicFromPathAsync(string path)
        {
            await _rescanfolderSemaphore.WaitAsync();
            try
            {
                StorageFile storageFile = await StorageFile.GetFileFromPathAsync(path);
                var (music, lyrics) = await ToolUtils.GetMusicInfo(storageFile);
                return (music, lyrics ?? "");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"AddMusicFromPathAsync 添加新音乐文件时出错: {ex.Message}");
                return (null, "");
            }
            finally
            {
                _rescanfolderSemaphore.Release();
            }
        }

        public async Task UpdateMusicList(IEnumerable<Music> _toUpdate)
        {
            var toUpdateList = _toUpdate is ICollection<Music> c ? new List<Music>(c) : _toUpdate.ToList();
            if (toUpdateList.Count == 0) return;

            var validResults = new List<(Music Music, string Lyrics)>();
            var channel = Channel.CreateUnbounded<Music>();
            int workerCount = Math.Min(4, toUpdateList.Count);
            var workers = new Task[workerCount];

            for (int i = 0; i < workerCount; i++)
                workers[i] = WorkerLoop(channel.Reader);

            foreach (var music in toUpdateList)
                channel.Writer.TryWrite(music);
            channel.Writer.Complete();

            await Task.WhenAll(workers);

            if (validResults.Count != 0)
                await _dbConnection.UpdateAllAsync(validResults.AsValueEnumerable().Select(r => r.Music).ToList());
            await SaveEmbeddedLyricsAsync(validResults);

            async Task WorkerLoop(ChannelReader<Music> reader)
            {
                while (await reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (reader.TryRead(out var music))
                    {
                        var result = await UpdateMusicWithSemaphoreAsync(music);
                        lock (validResults) validResults.Add(result);
                    }
                }
            }
        }

        public async Task DeletedMusicList(IEnumerable<Music> toDelete)
        {
            var toDeleteList = toDelete is ICollection<Music> c ? new List<Music>(c) : toDelete.ToList();
            if (toDeleteList.Count == 0) return;

            var channel = Channel.CreateUnbounded<Music>();
            int workerCount = Math.Min(4, toDeleteList.Count);
            var workers = new Task[workerCount];

            for (int i = 0; i < workerCount; i++)
                workers[i] = WorkerLoop(channel.Reader);

            foreach (var music in toDeleteList)
                channel.Writer.TryWrite(music);
            channel.Writer.Complete();

            await Task.WhenAll(workers);

            async Task WorkerLoop(ChannelReader<Music> reader)
            {
                while (await reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (reader.TryRead(out var music))
                    {
                        await DeleteMusicAsync(music);
                    }
                }
            }
        }

        public async Task<List<UsbDeviceMusic>> GetUsbDeviceMusics(string uniqueDeviceId)
        {
            return await _dbConnection.Table<UsbDeviceMusic>().Where(m => m.UniqueDeviceId == uniqueDeviceId).ToListAsync();
        }

        public async Task<List<UsbDeviceMusic>> RescanUsbDeviceFolderByPath(List<UsbDeviceMusic> usbDeviceMusics, string uniqueDeviceId, string folderPath, bool isSingleFolder = false)
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(folderPath);
            List<StorageFile> files;
            List<UsbDeviceMusic> musicFilesInFolder;

            if (isSingleFolder)
            {
                var currentFiles = await folder.GetFilesAsync();
                files = [.. currentFiles];
                musicFilesInFolder = usbDeviceMusics.AsValueEnumerable().Where(m => Path.GetDirectoryName(m.Path) == folderPath).ToList();
            }
            else
            {
                files = await GetAllFilesInFolderAndSubfolders(folder);
                musicFilesInFolder = usbDeviceMusics.AsValueEnumerable()
                   .Where(m => m.Path.Contains(folderPath)).ToList();
            }

            // 优化24: 用 HashSet 而非逐步 Add，构造时一次性去重
            var filePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                try
                {
                    if (ToolUtils.IsMusicFile(file.FileType))
                    {
                        filePaths.Add(file.Path);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"RescanUsbDeviceFolderByPath 添加文件路径时出错: {ex.Message}");
                }
            }

            var toDelete = new List<UsbDeviceMusic>();
            foreach (var newMusic in musicFilesInFolder)
            {
                if (!filePaths.Contains(newMusic.Path))
                {
                    toDelete.Add(newMusic);
                }
                else
                {
                    filePaths.Remove(newMusic.Path);
                }
            }

            foreach (var music in toDelete)
            {
                await _dbConnection.DeleteAsync(music);
                musicFilesInFolder.Remove(music);
            }

            // 优化25: 并行获取文件信息后批量 InsertAllAsync，减少 N 次数据库往返为 1 次
            var usbDeviceMusicIndexByPath = new HashSet<string>(
                usbDeviceMusics.Select(m => m.Path), StringComparer.OrdinalIgnoreCase);

            var newPathList = filePaths
                .Where(p => !usbDeviceMusicIndexByPath.Contains(p))
                .ToList();

            var fetchTasks = newPathList.Select(async path =>
            {
                StorageFile storageFile = await StorageFile.GetFileFromPathAsync(path);
                return addFolderService.GetUsbDeviceMusicInfo(storageFile, folder.Path, uniqueDeviceId);
            });
            var fetchResults = await Task.WhenAll(fetchTasks);
            var usbDeviceMusicsInsertList = fetchResults.Where(r => r is not null).ToList();

            if (usbDeviceMusicsInsertList.Count > 0)
            {
                await _dbConnection.InsertAllAsync(usbDeviceMusicsInsertList);
            }

            return usbDeviceMusicsInsertList;
        }

        public async Task ScanUsbDeviceAsync(string drivePath, string uniqueDeviceId)
        {
            try
            {
                var usbDeviceMusics = await GetUsbDeviceMusics(uniqueDeviceId) ?? [];
                var diskPaths = await Task.Run(() =>
                {
                    var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var path in EnumerateAllMusicFiles(drivePath))
                        paths.Add(path);
                    return paths;
                });

                var toRemove = usbDeviceMusics.Where(m => !diskPaths.Contains(m.Path)).ToList();
                foreach (var music in toRemove)
                    await _dbConnection.DeleteAsync(music);

                var existingPaths = new HashSet<string>(
                    usbDeviceMusics.Select(m => m.Path), StringComparer.OrdinalIgnoreCase);
                var newPaths = diskPaths.Where(p => !existingPaths.Contains(p)).ToList();

                if (newPaths.Count > 0)
                {
                    var fetchTasks = newPaths.Select(path =>
                        Task.Run(() => addFolderService.GetUsbDeviceMusicInfoByPath(path, drivePath, uniqueDeviceId)));
                    var fetchResults = await Task.WhenAll(fetchTasks);
                    var newMusic = fetchResults.Where(r => r is not null).ToList();
                    if (newMusic.Count > 0)
                        await _dbConnection.InsertAllAsync(newMusic);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ScanUsbDeviceAsync 扫描USB设备失败: {ex.Message}");
            }
        }

        private static readonly string[] _musicPatterns =
            [".mp3", ".wav", ".flac", ".wma", ".aac", ".ogg", ".oga", ".aiff", ".aif", ".m4a", ".dsf", ".dff", ".ape", ".opus", ".wv"];

        private static List<string> EnumerateAllMusicFiles(string rootPath)
        {
            var paths = new List<string>();
            try
            {
                foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
                {
                    if (HasMusicExtension(file))
                        paths.Add(file);
                }
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException)
            {
            }
            return paths;
        }

        private static List<string> EnumerateMusicFilesInDirectory(string folderPath)
        {
            var paths = new List<string>();
            try
            {
                foreach (var file in Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly))
                {
                    if (HasMusicExtension(file))
                        paths.Add(file);
                }
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException)
            {
            }
            return paths;
        }

        public async Task<string?> GetRecordedVersionAsync()
        {
            string path = VersionRecordPath;
            if (!File.Exists(path))
                return null;
            await _versionRecordIoGate.WaitAsync();
            try
            {
                string json = await File.ReadAllTextAsync(path);
                var record = JsonSerializer.Deserialize(json, VersionJsonContext.Default.VersionRecord);
                return record?.Version;
            }
            catch (JsonException ex)
            {
                // 损坏文件留底后按未记录处理：最坏情况只是更新日志弹窗多弹一次
                _logger.LogError(ex, $"VersionRecord.json 解析失败，备份损坏文件后按未记录继续: {ex.Message}");
                TryBackupCorruptFile(path);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GetRecordedVersionAsync 读取版本记录时出错: {ex.Message}");
                return null;
            }
            finally
            {
                _versionRecordIoGate.Release();
            }
        }

        public async Task SaveCurrentVersionAsync(string version)
        {
            await _versionRecordIoGate.WaitAsync();
            try
            {
                var record = new VersionRecord { Version = version };
                string json = JsonSerializer.Serialize(record, VersionJsonContext.Default.VersionRecord);
                await File.WriteAllTextAsync(VersionRecordPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SaveCurrentVersionAsync 保存版本记录时出错: {ex.Message}");
            }
            finally
            {
                _versionRecordIoGate.Release();
            }
        }

        private static bool HasMusicExtension(string filePath)
        {
            ReadOnlySpan<char> span = filePath;
            int dot = span.LastIndexOf('.');
            if (dot < 0) return false;
            ReadOnlySpan<char> ext = span[dot..];
            foreach (var pattern in _musicPatterns)
            {
                if (MemoryExtensions.Equals(ext, pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}