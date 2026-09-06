using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System;
using WinUIMusicPlayer.Helper;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.ViewModel.Pages;
using WinUIMusicPlayer.View.SubView;

namespace WinUIMusicPlayer.View
{
    /// <summary>我的歌单页: 在线歌单列表(仅存插件链接, 无需下载歌曲), 可创建/重命名/删除, 点击进入详情。</summary>
    public sealed partial class MyPlayListPage : Page
    {
        public MyPlayListViewModel ViewModel { get; }

        public MyPlayListPage()
        {
            ViewModel = App.Services.GetRequiredService<MyPlayListViewModel>();
            InitializeComponent();
            DataContext = this;
            NavigationCacheMode = NavigationCacheMode.Disabled;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            // 从详情返回或首次进入均刷新列表(歌曲数/封面可能变化)
            CollapseDetail();
            _ = ViewModel.RefreshAsync();
        }

        private void CreatePlayList_Click(object sender, RoutedEventArgs e)
        {
            // 新建歌单弹窗: 新建歌单 / 从网络导入 / 从本地导入(三个 Tab)
            var dialog = new SubView.CreatePlayListDialog(ViewModel);
            _ = dialog.ShowThemedAsync(this.XamlRoot);
        }

        private void PlayListGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is MyPlayListItem item)
            {
                OpenDetail(item.PlayList);
            }
        }

        /// <summary>在承载 Frame 中钻入在线歌单详情页, 返回时恢复列表。</summary>
        private void OpenDetail(PlayList playList)
        {
            if (DetailFrame.Content is MyPlayListDetailPage oldPage)
                oldPage.CloseRequested -= DetailPage_CloseRequested;
            DetailFrame.Navigate(typeof(MyPlayListDetailPage), playList, new DrillInNavigationTransitionInfo());
            if (DetailFrame.Content is MyPlayListDetailPage page)
                page.CloseRequested += DetailPage_CloseRequested;
            ListRoot.Visibility = Visibility.Collapsed;
            DetailFrame.Visibility = Visibility.Visible;
        }

        private void CollapseDetail()
        {
            if (DetailFrame.Content is MyPlayListDetailPage page)
                page.CloseRequested -= DetailPage_CloseRequested;
            DetailFrame.Content = null;
            DetailFrame.BackStack.Clear();
            DetailFrame.Visibility = Visibility.Collapsed;
            ListRoot.Visibility = Visibility.Visible;
        }

        private void DetailPage_CloseRequested()
        {
            CollapseDetail();
            // 歌单曲目数可能在详情中变化(移除歌曲), 返回时刷新
            _ = ViewModel.RefreshAsync();
        }

        private async void RemovePlayListButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item || item.Tag is not PlayList playList) return;
            if (await DialogHelper.ShowConfirmAsync(this.XamlRoot, "AreUSureDeletePlayList"))
            {
                await ViewModel.RemovePlayListAsync(playList);
            }
        }

        private async void RemoveAllPlayLists_Click(object sender, RoutedEventArgs e)
        {
            var count = ViewModel.PlayLists.Count;
            if (count == 0) return;
            if (await DialogHelper.ShowConfirmAsync(this.XamlRoot, "MyPlayListDeleteAllTitle",
                ToolUtils.GetString("MyPlayListDeleteAllConfirm").Replace("{0}", count.ToString())))
            {
                var removed = await ViewModel.RemoveAllPlayListsAsync();
                if (removed > 0)
                    ToastFlyout.ShowSuccess(ToolUtils.GetString("MyPlayListDeleteAllDone").Replace("{0}", removed.ToString()));
            }
        }

        private void EditPlayListNameButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item || item.Tag is not PlayList playList) return;
            _ = ViewModel.RenamePlayListAsync(playList, () =>
                DialogHelper.ShowInputAsync(this.XamlRoot, "ModifyPlaylist", playList.Name));
        }

        private void OnCoverPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Grid grid) return;
            if (grid.FindName("MoreBtn") is Button moreBtn) moreBtn.Visibility = Visibility.Visible;
        }

        private void OnCoverPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Grid grid) return;
            if (grid.FindName("MoreBtn") is Button moreBtn) moreBtn.Visibility = Visibility.Collapsed;
        }
    }
}
