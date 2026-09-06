using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Services;
using WinUIMusicPlayer.Services.Plugins;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.ViewModel;
using WinUIMusicPlayer.View.SubView;

namespace WinUIMusicPlayer.ViewModel.Pages
{
    /// <summary>在线歌单详情页歌曲项: 在线歌曲(Song)或本地歌曲(LocalMusic)二选一。</summary>
    public partial class MyPlayListSongItem : ObservableObject
    {
        public required OnlinePlayListMusic Entry { get; set; }
        /// <summary>本地歌曲(MusicId 引用解析所得)。</summary>
        public Music? LocalMusic { get; set; }
        /// <summary>在线歌曲(反序列化所得, 含插件链接)。</summary>
        public OnlineSong? Song { get; set; }
        [ObservableProperty]
        private string _artworkHash = string.Empty;

        public bool IsOnline => Song is not null;
        public string Title => LocalMusic?.Title ?? Song?.Title ?? string.Empty;
        public string Artist => LocalMusic?.Author ?? Song?.Artist ?? string.Empty;
        public string Album => LocalMusic?.Album ?? Song?.Album ?? string.Empty;
        /// <summary>来源平台(在线歌曲显示, 本地为空)。</summary>
        public string Platform => LocalMusic is null ? Song?.Platform ?? string.Empty : string.Empty;
        public string DurationText
        {
            get
            {
                var ts = LocalMusic?.Duration ?? TimeSpan.FromSeconds(Song?.DurationSec ?? 0);
                return BindUtils.TimeSpanToTextConverter(ts);
            }
        }
    }

    /// <summary>
    /// 在线歌单详情页 ViewModel: 加载歌单曲目(在线=插件链接, 本地=MusicId 引用),
    /// 播放时在线歌曲再经插件解析下载(懒解析), 布局仿歌单详情(封面 + 播放全部 + 歌曲列表)。
    /// </summary>
    public partial class MyPlayListDetailViewModel : ObservableObject
    {
        private readonly AppViewModel _appViewModel;
        private readonly MusicDatabaseService _db;
        private readonly MusicBrowseViewModel _musicBrowseVM;

        public ObservableCollection<MyPlayListSongItem> Songs { get; } = new();

        [ObservableProperty]
        private string _title = string.Empty;

        /// <summary>副标题: 曲目数。</summary>
        [ObservableProperty]
        private string _subtitle = string.Empty;

        /// <summary>歌单封面哈希(第一首歌封面, 异步填充)。</summary>
        [ObservableProperty]
        private string _artworkHash = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

        private PlayList? _playList;

        public MyPlayListDetailViewModel()
        {
            _appViewModel = App.Services.GetRequiredService<AppViewModel>();
            _db = App.Services.GetRequiredService<MusicDatabaseService>();
            _musicBrowseVM = App.Services.GetRequiredService<MusicBrowseViewModel>();
        }

        public void Initialize(PlayList playList)
        {
            _playList = playList;
            Title = playList.Name;
            ArtworkHash = string.Empty;
            Songs.Clear();
            UpdateSubtitle();
            _ = LoadAsync();
        }

        public void Reset()
        {
            _playList = null;
            Songs.Clear();
        }

        private async Task LoadAsync()
        {
            if (_playList is null) return;
            IsBusy = true;
            try
            {
                var entries = await _db.GetOnlinePlayListMusicsAsync(_playList.Id);
                foreach (var e in entries)
                {
                    Music? local = null;
                    if (e.MusicId > 0 && _appViewModel.TryFindById(e.MusicId, out var m)) local = m;
                    OnlineSong? song = local is null && !string.IsNullOrEmpty(e.SongJson)
                        ? MyPlayListViewModel.DeserializeSong(e.SongJson) : null;
                    if (local is null && song is null) continue; // 本地歌曲已删除且无在线数据 → 跳过
                    var item = new MyPlayListSongItem { Entry = e, LocalMusic = local, Song = song };
                    Songs.Add(item);
                    if (song is not null)
                    {
                        // 封面异步下载, 失败不影响列表
                        _ = Task.Run(async () =>
                        {
                            var hash = await OnlineSearchViewModel.DownloadArtworkAsync(song.Artwork);
                            if (hash is null) return;
                            App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                            {
                                try { item.ArtworkHash = hash; }
                                catch { /* UI 回调异常不得外泄导致闪退 */ }
                            });
                        });
                    }
                    else if (local is not null)
                    {
                        item.ArtworkHash = local.ImageHash ?? string.Empty;
                    }
                }
                UpdateSubtitle();
                // 歌单封面 = 第一首歌封面
                if (Songs.Count > 0)
                {
                    var first = Songs[0];
                    if (first.IsOnline && first.Song is not null)
                    {
                        var hash = await OnlineSearchViewModel.DownloadArtworkAsync(first.Song.Artwork);
                        if (hash is not null) ArtworkHash = hash;
                    }
                    else if (first.LocalMusic is not null)
                    {
                        ArtworkHash = first.LocalMusic.ImageHash ?? string.Empty;
                    }
                }
                if (Songs.Count == 0)
                    ToastFlyout.ShowInfo(ToolUtils.GetString("OnlineSearchNoResults"));
            }
            catch (Exception ex)
            {
                ToastFlyout.ShowError(ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void UpdateSubtitle()
            => Subtitle = $"{Songs.Count} {ToolUtils.GetString("MyPlayListSongsSuffix")}";

        /// <summary>播放全部: 整个歌单加入播放队列, 在线歌曲保持虚拟路径懒解析。</summary>
        [RelayCommand]
        private Task PlayAllAsync()
            => PlayAsync(Songs.Count > 0 ? Songs[0] : null);

        /// <summary>播放指定歌曲: 整个歌单入列, 在线歌曲切到时再经插件解析下载。</summary>
        [RelayCommand]
        private async Task PlayAsync(MyPlayListSongItem? item)
        {
            if (item is null) return;
            try
            {
                // 构建播放队列: 在线歌曲注册到会话注册表并生成虚拟路径 Music, 本地歌曲直接用库内实例
                Music? target = null;
                _appViewModel.SequentialPlayingList.Clear();
                foreach (var s in Songs)
                {
                    if (ReferenceEquals(s, item) && s.IsOnline && s.Song is not null)
                    {
                        OnlineMusicRegistry.Register(s.Song);
                        target = new Music
                        {
                            Id = OnlineMusicRegistry.NextMusicId(),
                            Path = s.Song.VirtualPath,
                            Title = s.Song.Title,
                            Author = s.Song.Artist,
                            Album = s.Song.Album,
                            Duration = TimeSpan.FromSeconds(s.Song.DurationSec),
                            Extension = "Online",
                            ImageHash = s.ArtworkHash,
                            OnlineVirtualPath = s.Song.VirtualPath,
                        };
                        _appViewModel.SequentialPlayingList.Add(target);
                    }
                    else if (s.IsOnline && s.Song is not null)
                    {
                        OnlineMusicRegistry.Register(s.Song);
                        _appViewModel.SequentialPlayingList.Add(new Music
                        {
                            Id = OnlineMusicRegistry.NextMusicId(),
                            Path = s.Song.VirtualPath,
                            Title = s.Song.Title,
                            Author = s.Song.Artist,
                            Album = s.Song.Album,
                            Duration = TimeSpan.FromSeconds(s.Song.DurationSec),
                            Extension = "Online",
                            ImageHash = s.ArtworkHash,
                            OnlineVirtualPath = s.Song.VirtualPath,
                        });
                    }
                    else if (s.LocalMusic is not null)
                    {
                        if (ReferenceEquals(s, item)) target = s.LocalMusic;
                        _appViewModel.SequentialPlayingList.Add(s.LocalMusic);
                    }
                }
                if (target is null) return;
                _appViewModel.CurrentPlayingList = _appViewModel.SequentialPlayingList;
                // PlayMusic 内部对虚拟路径在线歌曲先解析音源并下载缓存
                await _musicBrowseVM.PlayMusic(target, IsChangeList: true);
            }
            catch (Exception ex)
            {
                ToastFlyout.ShowError(ex.Message);
            }
        }

        /// <summary>从歌单移除歌曲(仅删条目, 在线歌曲本就不落盘)。</summary>
        [RelayCommand]
        private async Task RemoveSongAsync(MyPlayListSongItem? item)
        {
            if (item is null || _playList is null) return;
            await _db.RemoveOnlinePlayListMusicAsync(item.Entry.Id);
            Songs.Remove(item);
            _playList.SongCount = Songs.Count;
            UpdateSubtitle();
        }
    }
}
