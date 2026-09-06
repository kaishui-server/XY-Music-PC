using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
using WinUIMusicPlayer.Helper;
using WinUIMusicPlayer.Services.Plugins;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.ViewModel.Pages;

namespace WinUIMusicPlayer.View
{
    /// <summary>在线搜索页面: 通过 MusicFree 插件搜索在线音乐并播放。</summary>
    public sealed partial class OnlineSearchPage : Page
    {
        public OnlineSearchViewModel ViewModel { get; }

        public string PlaceholderText => ToolUtils.GetString("OnlineSearchPlaceholderText");

        public OnlineSearchPage()
        {
            ViewModel = App.Services.GetRequiredService<OnlineSearchViewModel>();
            InitializeComponent();
            DataContext = this;
            NavigationCacheMode = NavigationCacheMode.Disabled;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ViewModel.RefreshPluginTabs();
            // 首页搜索框入口: 带关键词导航进来时填充并自动搜索
            if (e.Parameter is string keyword && !string.IsNullOrWhiteSpace(keyword))
            {
                ViewModel.Keyword = keyword;
                _ = ViewModel.SearchCommand.ExecuteAsync(null);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            // 切走页面时销毁内容, 不记忆搜索状态(歌手作品页一并关闭)
            ArtistPage_CloseRequested();
            ViewModel.ResetState();
        }

        private void OnlineSearchPage_Loaded(object sender, RoutedEventArgs e)
        {
            ViewModel.RefreshPluginTabs();
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            // x:Bind TwoWay 默认 LostFocus 才写回, 点击按钮前手动同步, 避免搜到旧关键词
            ViewModel.Keyword = KeywordBox.Text;
            _ = ViewModel.SearchCommand.ExecuteAsync(null);
        }

        private void KeywordBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                // x:Bind TwoWay 默认 LostFocus 才写回, 回车时焦点仍在框内, 手动同步
                ViewModel.Keyword = KeywordBox.Text;
                _ = ViewModel.SearchCommand.ExecuteAsync(null);
                e.Handled = true;
            }
        }

        private void PluginTab_Click(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is PluginTab tab)
            {
                ViewModel.SelectPlugin(tab.Hash);
            }
        }

        private void SearchTypeTab_Click(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SearchTypeTab tab)
            {
                ViewModel.SelectSearchType(tab.Type);
            }
        }

        private void CatalogList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is CatalogEntryItem entry)
            {
                // 歌手: 打开独立的作品页(仿歌单详情); 专辑/歌单: 页内详情列表
                if (entry.Item.CatalogType == "artist")
                {
                    OpenArtistDetail(entry.Item);
                }
                else
                {
                    _ = ViewModel.OpenDetailCommand.ExecuteAsync(entry);
                }
            }
        }

        /// <summary>在承载 Frame 中钻入歌手作品页, 返回时恢复目录列表(搜索状态完整保留)。</summary>
        private void OpenArtistDetail(OnlineCatalogItem item)
        {
            // 卸载旧实例避免事件悬挂
            if (ArtistFrame.Content is OnlineArtistPage oldPage)
                oldPage.CloseRequested -= ArtistPage_CloseRequested;
            ArtistFrame.Navigate(typeof(OnlineArtistPage), item, new DrillInNavigationTransitionInfo());
            if (ArtistFrame.Content is OnlineArtistPage page)
                page.CloseRequested += ArtistPage_CloseRequested;
            // 隐藏搜索页自身内容, 避免歌手页透明背景与上一层重叠
            ContentRoot.Visibility = Visibility.Collapsed;
            ArtistFrame.Visibility = Visibility.Visible;
        }

        private void ArtistPage_CloseRequested()
        {
            if (ArtistFrame.Content is OnlineArtistPage page)
                page.CloseRequested -= ArtistPage_CloseRequested;
            ArtistFrame.Content = null;
            ArtistFrame.BackStack.Clear();
            ArtistFrame.Visibility = Visibility.Collapsed;
            ContentRoot.Visibility = Visibility.Visible;
        }

        private void HistoryChip_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is string keyword)
            {
                ViewModel.ApplyHistory(keyword);
            }
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
