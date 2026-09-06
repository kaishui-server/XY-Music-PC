using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using WinUIMusicPlayer.Helper;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.ViewModel.Pages;

namespace WinUIMusicPlayer.View
{
    /// <summary>
    /// 在线歌单详情页: 歌单封面/标题 + 曲目列表(在线歌曲仅存插件链接, 点击播放时才解析下载)。
    /// 承载于 MyPlayListPage 的内嵌 Frame, 返回时请求宿主页恢复列表。
    /// </summary>
    public sealed partial class MyPlayListDetailPage : Page
    {
        public MyPlayListDetailViewModel ViewModel { get; }

        /// <summary>详情页返回请求(宿主页订阅后恢复列表)。</summary>
        public event Action? CloseRequested;

        public MyPlayListDetailPage()
        {
            ViewModel = App.Services.GetRequiredService<MyPlayListDetailViewModel>();
            InitializeComponent();
            DataContext = this;
            NavigationCacheMode = NavigationCacheMode.Disabled;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is PlayList playList)
                ViewModel.Initialize(playList);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ViewModel.Reset();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke();
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.Tag as MyPlayListSongItem
                ?? (sender as FrameworkElement)?.DataContext as MyPlayListSongItem;
            if (item is not null)
                _ = ViewModel.PlayCommand.ExecuteAsync(item);
        }

        /// <summary>歌曲条目 ⋯ 菜单「添加到歌单」: 对该条目歌曲弹添加弹窗(在线/本地均可)。</summary>
        private void AddToPlayListMenu_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is MyPlayListSongItem item)
            {
                var appViewModel = App.Services.GetRequiredService<ViewModel.AppViewModel>();
                var dialog = new SubView.AddToMyPlayListDialog(appViewModel, onlineSong: item.Song, localMusic: item.LocalMusic);
                _ = dialog.ShowThemedAsync(XamlRoot);
            }
        }

        private void SongList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is MyPlayListSongItem item)
                _ = ViewModel.PlayCommand.ExecuteAsync(item);
        }

        private void RemoveSongButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is MyPlayListSongItem song)
                _ = ViewModel.RemoveSongCommand.ExecuteAsync(song);
        }
    }
}
