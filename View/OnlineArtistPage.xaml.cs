using Microsoft.Extensions.DependencyInjection;
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using WinUIMusicPlayer.Helper;
using WinUIMusicPlayer.Services.Plugins;
using WinUIMusicPlayer.ViewModel.Pages;

namespace WinUIMusicPlayer.View
{
    /// <summary>在线歌手作品页: 从在线搜索的歌手目录进入, 布局仿歌单详情(头像 + 播放全部 + 歌曲列表)。</summary>
    public sealed partial class OnlineArtistPage : Page
    {
        public OnlineArtistViewModel ViewModel { get; }

        /// <summary>请求关闭(由宿主在线搜索页处理: 清空承载 Frame 并隐藏)。</summary>
        public event Action? CloseRequested;

        public OnlineArtistPage()
        {
            ViewModel = App.Services.GetRequiredService<OnlineArtistViewModel>();
            InitializeComponent();
            DataContext = this;
            NavigationCacheMode = NavigationCacheMode.Disabled;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is OnlineCatalogItem item)
                ViewModel.Initialize(item);
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

        private void ResultList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is OnlineSongItem item)
            {
                _ = ViewModel.PlayCommand.ExecuteAsync(item);
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.Tag as OnlineSongItem
                ?? (sender as FrameworkElement)?.DataContext as OnlineSongItem;
            if (item is not null)
            {
                _ = ViewModel.PlayCommand.ExecuteAsync(item);
            }
        }

        /// <summary>歌曲条目 ⋯ 菜单「添加到歌单」: 对该条目歌曲弹添加弹窗(非当前播放歌曲)。</summary>
        private void AddToPlayListMenu_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is OnlineSongItem item)
            {
                var appViewModel = App.Services.GetRequiredService<ViewModel.AppViewModel>();
                var dialog = new SubView.AddToMyPlayListDialog(appViewModel, onlineSong: item.Song);
                _ = dialog.ShowThemedAsync(XamlRoot);
            }
        }
    }
}
