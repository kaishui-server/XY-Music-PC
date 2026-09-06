using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinUIMusicPlayer.ViewModel;
using ZLinq;

namespace WinUIMusicPlayer.View
{
    /// <summary>
    /// 首页:应用启动后的默认入口页面。
    /// 无文件夹时展示欢迎引导(支持点击/拖拽添加文件夹),已有文件夹时展示就绪状态。
    /// </summary>
    public sealed partial class HomePage : Page
    {
        public AddFolderViewModel FolderViewModel { get; }

        public HomePage()
        {
            InitializeComponent();
            FolderViewModel = App.Services.GetRequiredService<AddFolderViewModel>();
            DataContext = this;
            FolderViewModel.FoldersLoaded += OnFoldersLoaded;
            NavigationCacheMode = NavigationCacheMode.Disabled;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            UpdateState();
        }

        private void OnFoldersLoaded()
        {
            if (DispatcherQueue is not null)
            {
                DispatcherQueue.TryEnqueue(UpdateState);
            }
            else
            {
                UpdateState();
            }
        }

        private void UpdateState()
        {
            bool hasFolders = FolderViewModel.FolderList.Count > 0;
            WelcomeGrid.Visibility = hasFolders ? Visibility.Collapsed : Visibility.Visible;
            ReadyGrid.Visibility = hasFolders ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void AddFolderButton_Click(object sender, RoutedEventArgs e)
        {
            ShowLoading();
            await FolderViewModel.AddFolderButton_Click();
            HideLoading();
        }

        private void GoMusicBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow?.Content is Frame shellFrame && shellFrame.Content is MainPage mainPage)
            {
                mainPage.NavigateToMusicBrowsePage();
            }
        }

        private void ShowLoading()
        {
            LoadingGrid.Visibility = Visibility.Visible;
            WelcomeGrid.Visibility = Visibility.Collapsed;
            ReadyGrid.Visibility = Visibility.Collapsed;
            DropOverlay.Visibility = Visibility.Collapsed;
        }

        private void HideLoading()
        {
            LoadingGrid.Visibility = Visibility.Collapsed;
            UpdateState();
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
