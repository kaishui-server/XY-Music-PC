using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using WinUIMusicPlayer.Helper;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Services;
using WinUIMusicPlayer.Services.Plugins;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.ViewModel;

namespace WinUIMusicPlayer.View.SubView
{
    /// <summary>
    /// 播放详情页"添加到歌单"弹窗: 点击已有在线歌单直接收入当前歌曲,
    /// 或输入名称新建歌单并自动收入。在线歌曲仅存插件链接, 无需下载。
    /// </summary>
    public sealed partial class AddToMyPlayListDialog : ContentDialog
    {
        private readonly AppViewModel _appViewModel;
        private readonly MusicDatabaseService _db;
        /// <summary>指定要收入的在线歌曲(为空时回退当前播放歌曲)。</summary>
        private readonly OnlineSong? _targetOnlineSong;
        /// <summary>指定要收入的本地歌曲(为空时回退当前播放歌曲)。</summary>
        private readonly Music? _targetLocalMusic;

        public AddToMyPlayListDialog(AppViewModel appViewModel, OnlineSong? onlineSong = null, Music? localMusic = null)
        {
            InitializeComponent();
            _appViewModel = appViewModel;
            _db = App.Services.GetRequiredService<MusicDatabaseService>();
            _targetOnlineSong = onlineSong;
            _targetLocalMusic = localMusic;
            Title = ToolUtils.GetString("AddToMyPlayList");
            ExistingHeader.Text = ToolUtils.GetString("MyPlayListDialogTitle");
            EmptyHint.Text = ToolUtils.GetString("MyPlayListDialogEmptyHint");
            NameTextBox.PlaceholderText = ToolUtils.GetString("MyPlayListNamePlaceholder");
            CreateAndAddButton.Content = ToolUtils.GetString("MyPlayListCreateAndAdd");
            CloseButtonText = ToolUtils.GetString("CloseButton");
            RefreshList();
        }

        private void RefreshList()
        {
            PlayListListView.ItemsSource = _appViewModel.OnlinePlayLists;
            PlayListListView.Visibility = _appViewModel.OnlinePlayLists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyHint.Visibility = _appViewModel.OnlinePlayLists.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void PlayListListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is PlayList playList)
            {
                _ = AddCurrentSongToAsync(playList);
            }
        }

        private void CreateAndAddButton_Click(object sender, RoutedEventArgs e)
        {
            var name = NameTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                ToastFlyout.ShowWarning(ToolUtils.GetString("MyPlayListNamePlaceholder"));
                return;
            }
            _ = CreateAndAddAsync(name);
        }

        private async Task CreateAndAddAsync(string name)
        {
            CreateAndAddButton.IsEnabled = false;
            try
            {
                var pl = new PlayList { Name = name, IsOnline = 1 };
                pl.Id = await _db.CreateOnlinePlayListAsync(name);
                _appViewModel.OnlinePlayLists.Add(pl);
                await AddCurrentSongToAsync(pl);
            }
            finally
            {
                CreateAndAddButton.IsEnabled = true;
            }
        }

        /// <summary>把目标歌曲收入歌单: 优先列表条目指定歌曲, 否则当前播放歌曲。在线存插件链接 JSON, 本地存 MusicId。</summary>
        private async Task AddCurrentSongToAsync(PlayList playList)
        {
            try
            {
                bool added;
                if (_targetOnlineSong is not null)
                {
                    // 列表条目指定在线歌曲: 直接存插件链接
                    added = await _db.AddOnlineSongToPlayListAsync(playList.Id, _targetOnlineSong);
                }
                else if (_targetLocalMusic is not null && _targetLocalMusic.Id > 0)
                {
                    // 列表条目指定本地歌曲: 存 MusicId 引用
                    added = await _db.AddLocalMusicToOnlinePlayListAsync(playList.Id, _targetLocalMusic.Id);
                }
                else
                {
                    var music = _appViewModel.CurrentPlayingMusic;
                    if (music is null)
                    {
                        ToastFlyout.ShowWarning(ToolUtils.GetString("MyPlayListNoCurrentSong"));
                        return;
                    }
                    // 在线歌曲: 经虚拟路径取会话注册表中的 OnlineSong(仅存链接)
                    var onlinePath = music.OnlineVirtualPath;
                    if (string.IsNullOrEmpty(onlinePath) && OnlineMusicRegistry.IsOnlinePath(music.Path))
                        onlinePath = music.Path;
                    if (!string.IsNullOrEmpty(onlinePath) && OnlineMusicRegistry.TryGet(onlinePath, out var song))
                    {
                        added = await _db.AddOnlineSongToPlayListAsync(playList.Id, song);
                    }
                    else if (music.Id > 0 && !OnlineMusicRegistry.IsOnlinePath(music.Path))
                    {
                        // 本地歌曲: 存 MusicId 引用
                        added = await _db.AddLocalMusicToOnlinePlayListAsync(playList.Id, music.Id);
                    }
                    else
                    {
                        ToastFlyout.ShowError(ToolUtils.GetString("MyPlayListAddFailedOnline"));
                        return;
                    }
                }
                if (added)
                {
                    playList.SongCount++;
                    Hide();
                }
                else
                {
                    // 重复添加: 关闭弹窗, 以黄色浮动卡片提示
                    Hide();
                    ToastFlyout.ShowWarning(ToolUtils.GetString("MyPlayListAlreadyAdded"));
                }
            }
            catch (Exception ex)
            {
                ToastFlyout.ShowError(ex.Message);
            }
        }
    }
}
