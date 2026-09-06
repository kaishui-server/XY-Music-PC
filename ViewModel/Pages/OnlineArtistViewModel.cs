using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Services.Plugins;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.ViewModel;
using WinUIMusicPlayer.View.SubView;

namespace WinUIMusicPlayer.ViewModel.Pages
{
    /// <summary>
    /// 在线歌手作品页 ViewModel: 从在线搜索的歌手目录进入, 分页拉取该歌手的歌曲(弦予电脑版同款)。
    /// 布局参照歌单详情(PlaylistDetailControl): 顶部头像 + 标题 + 播放全部, 下方歌曲列表。
    /// </summary>
    public partial class OnlineArtistViewModel : ObservableObject
    {
        private readonly PluginManagerService _pluginManager;
        private readonly MusicBrowseViewModel _musicBrowseVM;
        private readonly AppViewModel _appViewModel;

        public ObservableCollection<OnlineSongItem> Results { get; } = new();

        [ObservableProperty]
        private string _title = string.Empty;

        /// <summary>副标题: 插件/音源名。</summary>
        [ObservableProperty]
        private string _subtitle = string.Empty;

        /// <summary>歌手头像哈希(异步下载填充)。</summary>
        [ObservableProperty]
        private string _artworkHash = string.Empty;

        [ObservableProperty]
        private bool _isSearching;

        [ObservableProperty]
        private bool _canLoadMore;

        private OnlineCatalogItem? _item;
        private int _page;
        private bool _isEnd;
        private CancellationTokenSource? _loadCts;

        public OnlineArtistViewModel()
        {
            _pluginManager = App.Services.GetRequiredService<PluginManagerService>();
            _musicBrowseVM = App.Services.GetRequiredService<MusicBrowseViewModel>();
            _appViewModel = App.Services.GetRequiredService<AppViewModel>();
        }

        /// <summary>进入页面时初始化: 设置歌手信息并加载第一页作品。</summary>
        public void Initialize(OnlineCatalogItem item)
        {
            _item = item;
            Title = item.Title;
            Subtitle = item.PluginName;
            ArtworkHash = string.Empty;
            Results.Clear();
            _page = 1;
            _isEnd = false;
            CanLoadMore = false;
            _ = LoadAsync(reset: true);
            _ = FetchAvatarAsync(item);
        }

        /// <summary>离开页面时取消进行中的加载。</summary>
        public void Reset()
        {
            _loadCts?.Cancel();
            _item = null;
            Results.Clear();
            _page = 1;
            _isEnd = false;
            IsSearching = false;
            CanLoadMore = false;
        }

        [RelayCommand]
        private async Task LoadMoreAsync()
        {
            await LoadAsync(reset: false);
        }

        private async Task LoadAsync(bool reset)
        {
            if (_item is null || IsSearching) return;
            if (!reset && _isEnd) return;

            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            IsSearching = true;
            try
            {
                var result = await _pluginManager.GetCatalogDetailAsync(_item, _page);
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
                        _ = OnlineSearchViewModel.FetchSongArtworkAsync(_appViewModel, songItem);
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

        /// <summary>播放全部: 解析第一首并整队入列(后续歌曲切到时再解析)。</summary>
        [RelayCommand]
        private Task PlayAllAsync()
        {
            return PlayAsync(Results.Count > 0 ? Results[0] : null);
        }

        /// <summary>播放在线歌曲: 转为 Music(虚拟路径) → 整队加入播放队列 → PlayMusic 先上底栏再解析音源。
        /// 与在线搜索页一致: 点击歌曲即把整个作品列表加入播放队列。</summary>
        [RelayCommand]
        private async Task PlayAsync(OnlineSongItem? item)
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
                // 点击歌曲即把整个作品列表加入播放队列(后续歌曲切到时再解析下载)
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
                _ = OnlineSearchViewModel.FetchSongArtworkAsync(_appViewModel, item, music);
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

        /// <summary>下载歌手头像填充 ArtworkHash。</summary>
        private async Task FetchAvatarAsync(OnlineCatalogItem item)
        {
            try
            {
                var imageHash = await OnlineSearchViewModel.DownloadArtworkAsync(item.Artwork);
                if (imageHash is null) return;
                App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                {
                    try { ArtworkHash = imageHash; }
                    catch { /* UI 回调异常不得外泄导致闪退 */ }
                });
            }
            catch { /* 头像下载失败不影响列表 */ }
        }
    }
}
