using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Services.Plugins;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.ViewModel;
using WinUIMusicPlayer.ViewModel.Pages;
using WinUIMusicPlayer.View.SubView;

namespace WinUIMusicPlayer.ViewModel.Pages
{
    /// <summary>在线搜索页插件 Tab 项。</summary>
    public partial class PluginTab : ObservableObject
    {
        public required string Hash { get; init; }
        public required string Name { get; init; }
        /// <summary>是否为当前选中 Tab(下划线指示器)。</summary>
        [ObservableProperty]
        private bool _isSelected;
    }

    /// <summary>一级搜索类型 Tab 项(歌曲/歌手/歌单/专辑)。</summary>
    public partial class SearchTypeTab : ObservableObject
    {
        public required string Type { get; init; }
        public required string Name { get; init; }
        /// <summary>是否为当前选中 Tab(下划线指示器)。</summary>
        [ObservableProperty]
        private bool _isSelected;
    }

    /// <summary>在线搜索页结果项。</summary>
    public partial class OnlineSongItem : ObservableObject
    {
        public required OnlineSong Song { get; init; }
        public string Title => Song.Title;
        public string Artist => Song.Artist;
        public string Album => Song.Album;
        public string Platform => Song.Platform;
        public string DurationText
        {
            get
            {
                var t = TimeSpan.FromSeconds(Song.DurationSec);
                return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}" : $"{t.Minutes}:{t.Seconds:D2}";
            }
        }
        [ObservableProperty]
        private string _artworkHash = string.Empty;
        [ObservableProperty]
        private bool _isResolving;
    }

    /// <summary>目录条目显示项(歌手/专辑/歌单), ArtworkHash 异步填充。</summary>
    public partial class CatalogEntryItem : ObservableObject
    {
        public required OnlineCatalogItem Item { get; init; }
        public string Title => Item.Title;
        public string Subtitle => Item.Subtitle;
        /// <summary>按目录类型选择占位图标字形。</summary>
        public string TypeGlyph => Item.CatalogType switch
        {
            "artist" => "\uE77B", // 人物
            "album" => "\uE8D6",  // 音乐信息
            _ => "\uE8FD",        // 列表(歌单)
        };
        [ObservableProperty]
        private string _artworkHash = string.Empty;
    }

    /// <summary>在线搜索页 ViewModel: 按插件 Tab 搜索 + 加载更多 + 播放。</summary>
    public partial class OnlineSearchViewModel : ObservableObject
    {
        private readonly PluginManagerService _pluginManager;
        private readonly MusicBrowseViewModel _musicBrowseVM;
        private readonly AppViewModel _appViewModel;

        public ObservableCollection<OnlineSongItem> Results { get; } = new();

        /// <summary>目录搜索结果(歌手/专辑/歌单)。</summary>
        public ObservableCollection<CatalogEntryItem> CatalogResults { get; } = new();

        /// <summary>一级搜索类型 Tab(歌曲/歌手/歌单/专辑)。</summary>
        public ObservableCollection<SearchTypeTab> SearchTypes { get; } = new();

        /// <summary>可搜索的插件 Tab。</summary>
        public ObservableCollection<PluginTab> AvailablePlugins { get; } = new();

        /// <summary>搜索历史关键词(最新在前), 持久化到本地 JSON。</summary>
        public ObservableCollection<string> SearchHistory { get; } = new();

        private string _selectedPluginHash = string.Empty;

        [ObservableProperty]
        private string _keyword = string.Empty;

        [ObservableProperty]
        private bool _isSearching;

        [ObservableProperty]
        private bool _canLoadMore;

        [ObservableProperty]
        private bool _noPlugins;

        /// <summary>空白态(无结果且未在搜索)且存在历史时, 显示历史标签区。</summary>
        [ObservableProperty]
        private bool _showHistory;

        /// <summary>目录列表显示中(搜索类型非歌曲且不在详情模式)。</summary>
        [ObservableProperty]
        private bool _isCatalogVisible;

        /// <summary>目录详情模式: 正在查看歌手/专辑/歌单内的歌曲列表。</summary>
        [ObservableProperty]
        private bool _isDetailMode;

        [ObservableProperty]
        private string _detailTitle = string.Empty;

        [ObservableProperty]
        private string _detailSubtitle = string.Empty;

        private string _selectedSearchType = "music";
        private OnlineCatalogItem? _detailItem;
        private int _page;
        private bool _isEnd;
        private int _catalogPage;
        private bool _catalogIsEnd;
        private CancellationTokenSource? _searchCts;

        private const int MaxHistoryEntries = 20;
        private static string SearchHistoryFile => System.IO.Path.Combine(AppPaths.LocalFolder, "OnlineSearchHistory.json");

        public OnlineSearchViewModel()
        {
            _pluginManager = App.Services.GetRequiredService<PluginManagerService>();
            _musicBrowseVM = App.Services.GetRequiredService<MusicBrowseViewModel>();
            _appViewModel = App.Services.GetRequiredService<AppViewModel>();
            _pluginManager.PluginsChanged += () =>
                App.MainWindow?.DispatcherQueue.TryEnqueue(RefreshPluginTabs);
            // 一级搜索类型 Tab(歌曲/歌手/歌单/专辑), 顺序参照 BakaMusic
            SearchTypes.Add(new SearchTypeTab { Type = "music", Name = ToolUtils.GetString("OnlineSearchTypeMusic") });
            SearchTypes.Add(new SearchTypeTab { Type = "artist", Name = ToolUtils.GetString("OnlineSearchTypeArtist") });
            SearchTypes.Add(new SearchTypeTab { Type = "sheet", Name = ToolUtils.GetString("OnlineSearchTypeSheet") });
            SearchTypes.Add(new SearchTypeTab { Type = "album", Name = ToolUtils.GetString("OnlineSearchTypeAlbum") });
            UpdateSearchTypeSelection();
            LoadHistory();
            // 历史在构造时已就绪, 必须主动重算一次: ShowHistory 的更新触发器
            // (IsSearching/NoPlugins 变化) 在首次进入时可能都不发生, 否则空白态不显示历史
            UpdateShowHistory();
        }

        partial void OnIsSearchingChanged(bool value) => UpdateShowHistory();

        partial void OnNoPluginsChanged(bool value) => UpdateShowHistory();

        private void UpdateShowHistory()
        {
            ShowHistory = SearchHistory.Count > 0 && Results.Count == 0 && CatalogResults.Count == 0
                && !IsSearching && !NoPlugins && !IsDetailMode;
        }

        private void UpdateSearchTypeSelection()
        {
            foreach (var t in SearchTypes)
                t.IsSelected = t.Type == _selectedSearchType;
        }

        private void UpdateCatalogVisibility()
        {
            IsCatalogVisible = _selectedSearchType != "music" && !IsDetailMode;
        }

        public string SelectedSearchType => _selectedSearchType;

        /// <summary>切换一级搜索类型: 退出详情并清空旧结果, 按新类型过滤插件 Tab 后重新搜索。</summary>
        public void SelectSearchType(string type)
        {
            if (_selectedSearchType == type) return;
            _selectedSearchType = type;
            UpdateSearchTypeSelection();
            ExitDetail();
            Results.Clear();
            CatalogResults.Clear();
            _isEnd = false;
            _catalogIsEnd = false;
            CanLoadMore = false;
            UpdateCatalogVisibility();
            var oldHash = _selectedPluginHash;
            RefreshPluginTabs();
            // 插件 Tab 未因类型切换变化时, RefreshPluginTabs 不会自动重搜, 这里补一次
            if (Keyword.Length > 0 && _selectedPluginHash == oldHash)
                _ = SearchAsync(reset: true);
            UpdateShowHistory();
        }

        private void LoadHistory()
        {
            try
            {
                if (System.IO.File.Exists(SearchHistoryFile))
                {
                    var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(System.IO.File.ReadAllText(SearchHistoryFile));
                    if (list is not null)
                        foreach (var keyword in list) SearchHistory.Add(keyword);
                }
            }
            catch { /* 历史文件损坏时忽略, 不影响搜索 */ }
        }

        private void SaveHistory()
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SearchHistoryFile)!);
                System.IO.File.WriteAllText(SearchHistoryFile, System.Text.Json.JsonSerializer.Serialize(SearchHistory.ToList()));
            }
            catch { /* 历史写入失败不影响搜索 */ }
        }

        /// <summary>记录一次搜索关键词: 去重后插到最前, 超上限裁掉最旧的。</summary>
        private void AddHistoryEntry(string keyword)
        {
            var k = keyword.Trim();
            if (string.IsNullOrEmpty(k)) return;
            var existing = SearchHistory.FirstOrDefault(h => string.Equals(h, k, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) SearchHistory.Remove(existing);
            SearchHistory.Insert(0, k);
            while (SearchHistory.Count > MaxHistoryEntries) SearchHistory.RemoveAt(SearchHistory.Count - 1);
            SaveHistory();
            UpdateShowHistory();
        }

        /// <summary>点击历史标签: 填充关键词并立即搜索。</summary>
        public void ApplyHistory(string keyword)
        {
            Keyword = keyword;
            _ = SearchAsync(reset: true);
        }

        [RelayCommand]
        private void ClearHistory()
        {
            SearchHistory.Clear();
            SaveHistory();
            UpdateShowHistory();
        }

        /// <summary>离开页面时重置状态: 清空关键词/结果/分页并取消进行中的搜索, 再次进入不记忆旧内容。</summary>
        public void ResetState()
        {
            _searchCts?.Cancel();
            Results.Clear();
            CatalogResults.Clear();
            AvailablePlugins.Clear();
            _selectedPluginHash = string.Empty;
            _selectedSearchType = "music";
            UpdateSearchTypeSelection();
            ExitDetail();
            Keyword = string.Empty;
            IsSearching = false;
            CanLoadMore = false;
            NoPlugins = false;
            IsCatalogVisible = false;
            _page = 1;
            _isEnd = false;
            _catalogPage = 1;
            _catalogIsEnd = false;
            UpdateShowHistory();
        }

        /// <summary>页面激活时刷新插件 Tab(按当前搜索类型过滤)。Tab 从左到右的顺序 = 插件管理页排序。</summary>
        public void RefreshPluginTabs()
        {
            var plugins = new List<PluginTab>();
            var usedLxSources = new HashSet<string>();
            // MF 插件一个插件一个 Tab; LX 插件按其覆盖音源生成多个 Tab(同一插件的音源间保持标准顺序, 重复音源先到先得)
            foreach (var (mf, lx) in _pluginManager.GetActiveRuntimesInOrder())
            {
                if (mf is not null)
                {
                    if (mf.SupportsMethod("search") && PluginSupportsSearchType(mf, _selectedSearchType))
                        plugins.Add(new PluginTab { Hash = mf.Hash, Name = mf.Metadata.Platform });
                }
                else if (lx is not null)
                {
                    // LX 音源四种搜索类型均支持
                    foreach (var source in LxSources.StandardOrder)
                    {
                        if (usedLxSources.Contains(source) || !lx.SupportsSource(source)) continue;
                        usedLxSources.Add(source);
                        plugins.Add(new PluginTab { Hash = $"lx:{source}", Name = LxSources.DisplayName(source) });
                    }
                }
            }
            AvailablePlugins.Clear();
            foreach (var p in plugins) AvailablePlugins.Add(p);
            NoPlugins = AvailablePlugins.Count == 0;
            if (NoPlugins)
            {
                Results.Clear();
                CatalogResults.Clear();
                CanLoadMore = false;
                ToastFlyout.ShowWarning(ToolUtils.GetString("OnlineSearchNoPlugins"));
                return;
            }
            if (!plugins.Any(p => p.Hash == _selectedPluginHash))
            {
                _selectedPluginHash = plugins[0].Hash;
                if (Keyword.Length > 0) _ = SearchAsync(reset: true);
            }
            else if (string.IsNullOrEmpty(_selectedPluginHash))
            {
                _selectedPluginHash = plugins[0].Hash;
            }
            foreach (var t in AvailablePlugins)
                t.IsSelected = t.Hash == _selectedPluginHash;
        }

        /// <summary>MusicFree 插件是否支持指定搜索类型: 未声明 supportedSearchType 的插件默认仅支持歌曲。</summary>
        private static bool PluginSupportsSearchType(PluginRuntime plugin, string type)
        {
            if (type == "music") return true;
            return plugin.Metadata.SupportedSearchType.Contains(type);
        }

        public string SelectedPluginHash => _selectedPluginHash;

        public void SelectPlugin(string hash)
        {
            if (_selectedPluginHash == hash) return;
            _selectedPluginHash = hash;
            foreach (var t in AvailablePlugins)
                t.IsSelected = t.Hash == hash;
            if (Keyword.Length > 0)
            {
                // 切换插件时退出目录详情, 以新插件重新搜索当前类型
                if (IsDetailMode)
                {
                    ExitDetail();
                    UpdateCatalogVisibility();
                }
                _ = SearchAsync(reset: true);
            }
        }

        [RelayCommand]
        private async Task SearchAsync()
        {
            await SearchAsync(reset: true);
        }

        [RelayCommand]
        private async Task LoadMoreAsync()
        {
            if (IsDetailMode)
            {
                await LoadDetailAsync(reset: false);
                return;
            }
            await SearchAsync(reset: false);
        }

        /// <summary>打开目录详情(歌手/专辑/歌单的歌曲列表)。</summary>
        [RelayCommand]
        private async Task OpenDetailAsync(CatalogEntryItem? entry)
        {
            if (entry is null || IsSearching) return;
            _detailItem = entry.Item;
            IsDetailMode = true;
            IsCatalogVisible = false;
            DetailTitle = entry.Item.Title;
            DetailSubtitle = string.IsNullOrEmpty(entry.Item.Subtitle)
                ? entry.Item.PluginName
                : $"{entry.Item.Subtitle} · {entry.Item.PluginName}";
            Results.Clear();
            _page = 1;
            _isEnd = false;
            CanLoadMore = false;
            await LoadDetailAsync(reset: true);
        }

        /// <summary>从目录详情返回目录列表(保留目录结果与分页状态)。</summary>
        [RelayCommand]
        private void BackFromDetail()
        {
            ExitDetail();
            UpdateCatalogVisibility();
            CanLoadMore = !_catalogIsEnd && CatalogResults.Count > 0;
            UpdateShowHistory();
        }

        private void ExitDetail()
        {
            _detailItem = null;
            IsDetailMode = false;
            Results.Clear();
            _page = 1;
            _isEnd = false;
            DetailTitle = string.Empty;
            DetailSubtitle = string.Empty;
        }

        private async Task LoadDetailAsync(bool reset)
        {
            if (_detailItem is null || IsSearching) return;
            if (!reset && _isEnd) return;

            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            IsSearching = true;
            try
            {
                var result = await _pluginManager.GetCatalogDetailAsync(_detailItem, _page);
                token.ThrowIfCancellationRequested();
                if (result.Error is not null)
                {
                    ToastFlyout.ShowError($"{ToolUtils.GetString("OnlineSearchSearchFailed")}: {result.Error}");
                    if (reset) _isEnd = true;
                }
                else
                {
                    foreach (var song in result.Songs)
                    {
                        OnlineMusicRegistry.Register(song);
                        var songItem = new OnlineSongItem { Song = song };
                        Results.Add(songItem);
                        _ = FetchArtworkAsync(songItem);
                    }
                    _isEnd = result.IsEnd;
                    CanLoadMore = !_isEnd && Results.Count > 0;
                    if (Results.Count == 0)
                        ToastFlyout.ShowInfo(ToolUtils.GetString("OnlineSearchNoResults"));
                    if (!_isEnd) _page++;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ToastFlyout.ShowError(ex.Message);
            }
            finally
            {
                IsSearching = false;
            }
        }

        private async Task SearchAsync(bool reset)
        {
            if (IsSearching || NoPlugins || string.IsNullOrWhiteSpace(Keyword)) return;
            var isCatalog = _selectedSearchType != "music";
            // 从详情模式发起搜索(改关键词/换插件)时先退回目录列表
            if (reset && IsDetailMode)
            {
                ExitDetail();
                UpdateCatalogVisibility();
            }
            if (reset)
            {
                if (isCatalog)
                {
                    _catalogPage = 1;
                    _catalogIsEnd = false;
                    CatalogResults.Clear();
                }
                else
                {
                    _page = 1;
                    _isEnd = false;
                    Results.Clear();
                }
                CanLoadMore = false;
                AddHistoryEntry(Keyword);
            }
            else if (isCatalog ? _catalogIsEnd : _isEnd) return;

            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            IsSearching = true;
            try
            {
                if (isCatalog)
                {
                    var keyword = Keyword.Trim();
                    var result = _selectedPluginHash.StartsWith("lx:", StringComparison.Ordinal)
                        ? await _pluginManager.SearchLxCatalogAsync(_selectedPluginHash[3..], keyword, _selectedSearchType, _catalogPage)
                        : await _pluginManager.SearchCatalogAsync(_selectedPluginHash, keyword, _catalogPage, _selectedSearchType);
                    token.ThrowIfCancellationRequested();
                    if (result.Error is not null)
                    {
                        ToastFlyout.ShowError($"{ToolUtils.GetString("OnlineSearchSearchFailed")}: {result.Error}");
                        if (reset) _catalogIsEnd = true;
                    }
                    else
                    {
                        foreach (var entry in result.Items)
                        {
                            var item = new CatalogEntryItem { Item = entry };
                            CatalogResults.Add(item);
                            _ = FetchCatalogArtworkAsync(item);
                        }
                        _catalogIsEnd = result.IsEnd;
                        CanLoadMore = !_catalogIsEnd && CatalogResults.Count > 0;
                        if (CatalogResults.Count == 0)
                            ToastFlyout.ShowInfo(ToolUtils.GetString("OnlineSearchNoCatalogResults"));
                        if (!_catalogIsEnd) _catalogPage++;
                        UpdateShowHistory();
                    }
                }
                else
                {
                    var result = _selectedPluginHash.StartsWith("lx:", StringComparison.Ordinal)
                        ? await _pluginManager.SearchLxAsync(_selectedPluginHash[3..], Keyword.Trim(), _page)
                        : await _pluginManager.SearchAsync(_selectedPluginHash, Keyword.Trim(), _page);
                    token.ThrowIfCancellationRequested();
                    if (result.Error is not null)
                    {
                        ToastFlyout.ShowError($"{ToolUtils.GetString("OnlineSearchSearchFailed")}: {result.Error}");
                        if (reset) _isEnd = true;
                    }
                    else
                    {
                        foreach (var song in result.Songs)
                        {
                            OnlineMusicRegistry.Register(song);
                            var songItem = new OnlineSongItem { Song = song };
                            Results.Add(songItem);
                            // 异步拉取列表封面, 不阻塞搜索结果展示
                            _ = FetchArtworkAsync(songItem);
                        }
                        _isEnd = result.IsEnd;
                        CanLoadMore = !_isEnd && Results.Count > 0;
                        if (Results.Count == 0)
                            ToastFlyout.ShowInfo(ToolUtils.GetString("OnlineSearchNoResults"));
                        if (!_isEnd) _page++;
                        UpdateShowHistory();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ToastFlyout.ShowError(ex.Message);
            }
            finally
            {
                IsSearching = false;
            }
        }

        /// <summary>播放在线歌曲: 转为 Music(虚拟路径) → 整队加入播放队列 → PlayMusic 先上底栏再解析音源。
        /// 播放子进程(BassPlayerSharp)仅支持本地文件, 音源解析与缓存下载由 PlayMusic 内部完成。</summary>
        [RelayCommand]
        private async Task PlayAsync(OnlineSongItem item)
        {
            if (item is null || item.IsResolving) return;
            item.IsResolving = true;
            try
            {
                var song = item.Song;
                var music = new Music
                {
                    Id = OnlineMusicRegistry.NextMusicId(),
                    Path = song.VirtualPath,
                    Title = song.Title,
                    Author = song.Artist,
                    Album = song.Album,
                    Duration = TimeSpan.FromSeconds(song.DurationSec),
                    Extension = "Online",
                    // 列表封面已下载时直接复用, 避免播放条先显示内嵌/联网兜底封面再跳变
                    ImageHash = item.ArtworkHash,
                    OnlineVirtualPath = song.VirtualPath,
                };
                // 点击歌曲即把整个搜索结果列表加入播放队列(后续歌曲切到时再解析下载)
                _appViewModel.SequentialPlayingList.Clear();
                foreach (var entry in Results)
                {
                    var queueMusic = ReferenceEquals(entry, item) ? music : new Music
                    {
                        Id = OnlineMusicRegistry.NextMusicId(),
                        Path = entry.Song.VirtualPath,
                        Title = entry.Song.Title,
                        Author = entry.Song.Artist,
                        Album = entry.Song.Album,
                        Duration = TimeSpan.FromSeconds(entry.Song.DurationSec),
                        Extension = "Online",
                        ImageHash = entry.ArtworkHash,
                        OnlineVirtualPath = entry.Song.VirtualPath,
                    };
                    _appViewModel.SequentialPlayingList.Add(queueMusic);
                }
                _appViewModel.CurrentPlayingList = _appViewModel.SequentialPlayingList;
                // PlayMusic 先把歌曲上到底栏播放栏, 再解析音源并下载缓存(含试听检测与跨插件回退)
                await _musicBrowseVM.PlayMusic(music, IsChangeList: true);
                _ = FetchArtworkAsync(item, music);
            }
            catch (Exception ex)
            {
                ToastFlyout.ShowError($"{ToolUtils.GetString("OnlineSearchPlayFailed")}: {ex.Message}");
            }
            finally
            {
                item.IsResolving = false;
            }
        }

        /// <summary>下载在线封面到本地缓存并返回封面哈希, 失败返回 null。</summary>
        internal static async Task<string?> DownloadArtworkAsync(string? artwork)
        {
            if (string.IsNullOrEmpty(artwork)) return null;
            using var http = new System.Net.Http.HttpClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, artwork);
            OnlinePlaybackResolver.ApplyMediaHeaders(req, artwork, null);
            using var resp = await http.SendAsync(req);
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0) return null;
            Span<byte> hashSpan = stackalloc byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(hashSpan, System.IO.Hashing.XxHash64.HashToUInt64(bytes));
            var imageHash = Convert.ToHexString(hashSpan);
            var cachePath = ToolUtils.GetRawCachePath(imageHash);
            if (!System.IO.File.Exists(cachePath))
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cachePath)!);
                await System.IO.File.WriteAllBytesAsync(cachePath, bytes);
            }
            return imageHash;
        }

        /// <summary>下载目录条目(歌手/专辑/歌单)封面, 填充 ArtworkHash。</summary>
        private async Task FetchCatalogArtworkAsync(CatalogEntryItem item)
        {
            try
            {
                var imageHash = await DownloadArtworkAsync(item.Item.Artwork);
                if (imageHash is null) return;
                App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                {
                    try { item.ArtworkHash = imageHash; }
                    catch { /* UI 回调异常不得外泄导致闪退 */ }
                });
            }
            catch { /* 封面下载失败不影响列表 */ }
        }

        /// <summary>下载在线歌曲封面并回填(列表项 + 播放条/背景), 供在线搜索页与歌手作品页共用。</summary>
        internal static async Task FetchSongArtworkAsync(AppViewModel appViewModel, OnlineSongItem item, Music? music = null)
        {
            try
            {
                var imageHash = await DownloadArtworkAsync(item.Song.Artwork);
                if (imageHash is null) return;
                App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        // 列表封面场景 music 为 null, 必须判空否则 UI 回调抛 NRE 导致闪退
                        if (music is not null) music.ImageHash = imageHash;
                        item.ArtworkHash = imageHash;
                        // 该歌仍是当前播放时同步刷新播放条封面(UpdatePlayBar 可能已用兜底封面先行渲染);
                        // 已切到其他歌则丢弃本次结果, 避免旧封面覆盖新歌封面
                        if (music is not null && ReferenceEquals(appViewModel.CurrentPlayingMusic, music))
                        {
                            appViewModel.LyricPageBackgroundHash = imageHash;
                        }
                    }
                    catch { /* UI 回调异常不得外泄导致闪退 */ }
                });
            }
            catch { /* 封面下载失败不影响播放 */ }
        }

        /// <summary>下载在线封面到本地缓存, 填充列表项 ArtworkHash; 传入 music 时同步关联其 ImageHash。</summary>
        private Task FetchArtworkAsync(OnlineSongItem item, Music? music = null)
            => FetchSongArtworkAsync(_appViewModel, item, music);
    }
}
