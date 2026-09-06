using DevWinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Linq;
using System.Threading.Tasks;
using WinUIMusicPlayer.Behaviors;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Services.NavigationService;
using WinUIMusicPlayer.ViewModel;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUIMusicPlayer.View
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SongListPage : Page, INavigatable
    {
        public SongListViewModel ViewModel { get; }
        private readonly ScrollerHelper _scrollHelper;
        private readonly Music?[] _selectedBuffer = new Music?[256];
        private Music? _pendingScroll;

        public SongListPage()
        {
            InitializeComponent();
            ViewModel = App.Services.GetRequiredService<SongListViewModel>(); ;
            ViewModel.SetCurrentPage(this);
            DataContext = this;
            this.NavigationCacheMode = NavigationCacheMode.Enabled;
            _scrollHelper = new ScrollerHelper(DispatcherQueue);
            _scrollHelper.Tick += OnScrollTick;
        }

        public void ReceiveNavigationParameter(object parameter)
        {
            ViewModel.ReceiveNavigation();
        }

        /// <summary>当前本地搜索关键词(null 表示未过滤, 显示完整列表)。</summary>
        private string? _searchKeyword;

        /// <summary>应用/清除本地搜索过滤: 按歌名/歌手/专辑模糊匹配, 过滤生效时列表上方显示提示条。</summary>
        public void SetSearchKeyword(string? keyword)
        {
            _searchKeyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
            ApplySearchFilter();
        }

        private void ApplySearchFilter()
        {
            if (_searchKeyword is null)
            {
                MusicListView.ItemsSource = ViewModel.AppViewModel.ListSongs;
                FilterChipBorder.Visibility = Visibility.Collapsed;
                return;
            }
            var keyword = _searchKeyword;
            MusicListView.ItemsSource = ViewModel.AppViewModel.ListSongs
                .Where(m =>
                    (m.Title?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (m.Author?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (m.Album?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
            FilterChipText.Text = $"{Utils.ToolUtils.GetString("LocalSearchChipPrefix")}: {keyword}";
            FilterChipBorder.Visibility = Visibility.Visible;
        }

        private void ClearFilterButton_Click(object sender, RoutedEventArgs e)
        {
            SetSearchKeyword(null);
        }

        /// <summary>播放全部: 播放当前展示的列表(过滤生效时播放过滤结果)。</summary>
        private void PlayAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (MusicListView.ItemsSource is System.Collections.IEnumerable enumerable)
                ViewModel.PlayAllFromList(enumerable.OfType<Music>());
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ViewModel.ReceiveNavigation();
        }

        public void OnScrollToMusic(Music selectedMusic)
        {
            _pendingScroll = selectedMusic;
            _scrollHelper.Trigger();
        }

        private void OnScrollTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
        {
            if (_pendingScroll is { } m)
            {
                MusicListView.ScrollIntoView(m);
                _pendingScroll = null;
            }
        }

        public void UpdateMusicListView()
        {
            ViewModel.UpdateMusicListView();
        }

        private void MusicListView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            ViewModel.MusicListView_DoubleTapped();
        }

        private void MusicListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var frameworkElement = e.OriginalSource as FrameworkElement;
            ViewModel.SelectedMusics.Clear();
            if (frameworkElement?.DataContext is not Music clickedItem)
            {
                e.Handled = true;
                return;
            }

            var selectedItems = MusicListView.SelectedItems;
            int n = selectedItems.Count;
            var buffer = _selectedBuffer.AsSpan();
            int written = 0;
            int clickedId = clickedItem.Id;
            bool isCurrentItemSelected = false;
            for (int i = 0; i < n && written < buffer.Length; i++)
            {
                if (selectedItems[i] is Music m)
                {
                    buffer[written++] = m;
                    if (m.Id == clickedId) isCurrentItemSelected = true;
                }
            }

            if (!isCurrentItemSelected)
            {
                selectedItems.Clear();
                ViewModel.SelectedMusic = clickedItem;
                ViewModel.SelectedMusics.Add(clickedItem);
            }
            else
            {
                for (int i = 0; i < written; i++)
                {
                    if (buffer[i] is { } sel) ViewModel.SelectedMusics.Add(sel);
                }
            }
            e.Handled = true;
        }

        private void AuthorTextBlock_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is TextBlock textBlock)
            {
                string artist = textBlock.Text;
                ViewModel.AuthorTextBlock_Tapped(artist);
            }
        }

        private void AlbumTextBlock_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is TextBlock textBlock)
            {
                string albumName = textBlock.Text;
                ViewModel.AlbumTextBlock_Tapped(albumName);
            }
        }

        private void AutoScrollHover_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is AutoScrollView autoScrollView)
            {
                autoScrollView.IsPlaying = true;
            }
        }

        private void AutoScrollHover_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (sender is AutoScrollView autoScrollView)
            {
                autoScrollView.IsPlaying = false;
            }
        }

        private void AutoScrollHover_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is AutoScrollView autoScrollView)
            {
                autoScrollView.IsPlaying = false;
            }
        }
    }
}
