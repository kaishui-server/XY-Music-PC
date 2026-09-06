using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using WinUIMusicPlayer.ViewModel;
using ZLinq;

namespace WinUIMusicPlayer.View
{
    /// <summary>
    /// 首页:应用启动后的默认入口页面。
    /// 品牌大字 + 副标题 + 搜索框(可选在网络/在本地搜索), 支持拖拽文件夹添加音乐库。
    /// </summary>
    public sealed partial class HomePage : Page
    {
        public AddFolderViewModel FolderViewModel { get; }

        /// <summary>搜索框占位文案: 跟随下拉选择的搜索范围(在网络搜索/在本地搜索)。</summary>
        public string SearchPlaceholderText => (SearchScopeBox?.SelectedIndex ?? 0) == 1
            ? Utils.ToolUtils.GetString("HomeSearchPlaceholderLocal")
            : Utils.ToolUtils.GetString("HomeSearchPlaceholderOnline");

        public HomePage()
        {
            InitializeComponent();
            FolderViewModel = App.Services.GetRequiredService<AddFolderViewModel>();
            DataContext = this;
            // 下拉项用代码填充本地化字符串(字符串项直接渲染, 避免 ComboBoxItem 显示空白)
            SearchScopeBox.Items.Add(Utils.ToolUtils.GetString("HomeSearchScopeOnline"));
            SearchScopeBox.Items.Add(Utils.ToolUtils.GetString("HomeSearchScopeLocal"));
            SearchScopeBox.SelectedIndex = 0;
            SearchButtonText.Text = Utils.ToolUtils.GetString("HomeSearchButton");
            FolderViewModel.FoldersLoaded += OnFoldersLoaded;
            NavigationCacheMode = NavigationCacheMode.Disabled;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Bindings.Update();
        }

        private void OnFoldersLoaded()
        {
            // 文件夹列表异步加载完成后无需更新 UI(首页已不展示文件夹状态), 保留订阅以兼容基类事件
        }

        private void SearchScopeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Bindings.Update(); // 刷新占位文案
        }

        private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                ExecuteSearch();
                e.Handled = true;
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteSearch();
        }

        /// <summary>执行搜索: 在网络搜索 → 跳转在线搜索页并自动搜索; 在本地搜索 → 跳转音乐库歌曲列表并按关键词过滤。</summary>
        private void ExecuteSearch()
        {
            var keyword = SearchBox.Text?.Trim() ?? string.Empty;
            if (keyword.Length == 0) return;
            // MainPage 是 DI 单例(HomePage 位于其 MainFrame 内, 必然已构造), 直接解析调用
            var mainPage = App.Services.GetRequiredService<MainPage>();
            if (SearchScopeBox.SelectedIndex == 1)
            {
                // 本地搜索: 跳转音乐库歌曲列表并带入关键词过滤
                mainPage.NavigateToMusicBrowsePage(keyword);
            }
            else
            {
                // 网络搜索: 跳转在线搜索页并带入关键词自动搜索
                mainPage.NavigateToOnlineSearchPage(keyword);
            }
        }

        private void ShowLoading()
        {
            LoadingGrid.Visibility = Visibility.Visible;
            DropOverlay.Visibility = Visibility.Collapsed;
        }

        private void HideLoading()
        {
            LoadingGrid.Visibility = Visibility.Collapsed;
        }

        private void Grid_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Link;
                DropOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                e.AcceptedOperation = DataPackageOperation.None;
                DropOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void Grid_DragLeave(object sender, DragEventArgs e)
        {
            var position = e.GetPosition(this);
            if (position.X < 0 || position.Y < 0 ||
                position.X > ActualWidth || position.Y > ActualHeight)
            {
                DropOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void Grid_Drop(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                try
                {
                    var items = await e.DataView.GetStorageItemsAsync();
                    // 筛选出文件夹
                    var folders = items.AsValueEnumerable().Where(item => item.IsOfType(Windows.Storage.StorageItemTypes.Folder));

                    if (folders.Any())
                    {
                        ShowLoading();
                        await FolderViewModel.Grid_Drop(folders.ToList());
                        HideLoading();
                    }
                }
                catch (Exception)
                {
                    HideLoading();
                }
            }
        }
    }
}
