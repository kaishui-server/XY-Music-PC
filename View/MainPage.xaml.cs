using DevWinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics;
using WinUIEx;
using WinUIMusicPlayer.DesktopLyrics;
using WinUIMusicPlayer.Helper;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Services;
using WinUIMusicPlayer.Services.NavigationService;
using WinUIMusicPlayer.Services.Plugins;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.View.SubView;
using WinUIMusicPlayer.ViewModel;
using WinUIMusicPlayer.ViewModel.Pages;
using ZLinq;
using static WinUIMusicPlayer.Utils.ToolUtils;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUIMusicPlayer.View
{
    public enum TitleBarArea { None, Top }

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainViewModel ViewModel { get; }
        public EqualizerDialog EqualizerDialog { get; set; }
        public SettingsDialog SettingsDialog { get; set; }
        public AddPlayListDialog AddPlayListDialog { get; set; }
        private readonly INavigationService _playingNavigation;
        private bool _isPageTransitioning = false;

        public bool IsPlayingDetailVisible => PlayingFrame.Visibility == Visibility.Visible;
        //private ToolTip _progressToolTip = new();
        private readonly Services.Account.AuthService _authService;

        public MainPage(MainViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = this;
            _authService = App.Services.GetRequiredService<Services.Account.AuthService>();
            _authService.LoginStateChanged += OnAuthLoginStateChanged;
            Loaded += (_, _) => _ = UpdateAccountAvatarAsync();
            var navigationServiceFactory = App.Services.GetRequiredService<INavigationServiceFactory>();
            _playingNavigation = navigationServiceFactory.CreateNavigationService(PlayingFrame);
            _playingNavigation.RegisterPage<PlayingDetailPage>();
            ViewModel.MusicBrowseVM.SetMainPage(this);
            // 播放音质菜单: 主底栏与播放详情页底栏共用同一套构建逻辑(持久化默认音质 + 在线歌曲换质重播)
            PlayQualityButton.Flyout = Helper.PlayQualityMenuHelper.BuildMenu();
            Loaded += MainPage_Loaded;
            Unloaded += MainPage_Unloaded;
        }

        private void DesktopLyricsButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.DesktopLyrics.IsEnabled = !ViewModel.DesktopLyrics.IsEnabled;
        }

        /// <summary>底栏下载按钮: 当前歌曲为在线歌曲时弹出下载对话框(音质/目录/独立歌词封面), 确认后下载并内嵌元数据。</summary>
        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            DownloadFlowHelper.Start(XamlRoot, ViewModel.AppViewModel.CurrentPlayingMusic);
        }

        private void PlayBarAddToPlayListButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SubView.AddToMyPlayListDialog(ViewModel.AppViewModel);
            _ = dialog.ShowThemedAsync(XamlRoot);
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            NavigateToDefaultPage();
            InitiaizeEqualizerDialog();
            SetSettingsDialog();
            AddPlayListDialog ??= new AddPlayListDialog(ViewModel.AppViewModel);
            NavigationViewControl.Visibility = Visibility.Visible;
            if (App.MainWindow is { } mainWindow)
            {
                mainWindow.AppWindow.Changed += MainPage_AppWindow_Changed;
                AppTitleBar.SizeChanged += AppTitleBar_SizeChanged;
                SetTitleBarArea(TitleBarArea.Top);
            }
            ViewModel.AppViewModel.UpdateMaximizeState();
            Loaded -= MainPage_Loaded;
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (App.MainWindow is { } mw)
                    mw.AppWindow.Changed -= MainPage_AppWindow_Changed;
            }
            catch
            {
            }
            Unloaded -= MainPage_Unloaded;
        }

        private void MainPage_AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {            
            if (args.DidPresenterChange)
            {
                ViewModel.AppViewModel.SyncFullScreenStateFromWindow();
            }
            if (args.DidPositionChange || args.DidSizeChange)
            {
                ViewModel.AppViewModel.UpdateMaximizeState();
                SetTitleBarArea(TitleBarArea.Top);
            }
        }

        private void AppTitleBar_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (!ViewModel.AppViewModel.IsPlayingDetailVisible) return;
            ViewModel.AppViewModel.IsPointerOverTitleBar = true;
        }

        private void AppTitleBar_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!ViewModel.AppViewModel.IsPlayingDetailVisible) return;
            ViewModel.AppViewModel.IsPointerOverTitleBar = false;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            App.MainWindow?.Minimize();
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            var window = App.MainWindow;
            if (window?.AppWindow.Presenter is OverlappedPresenter overlapped)
            {
                if (overlapped.State == OverlappedPresenterState.Maximized)
                    window.Restore();
                else
                    window.Maximize();
            }
            ViewModel.AppViewModel.UpdateMaximizeState();
        }

        private void FullscreenTitleBarButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.AppViewModel.ToggleFullScreen();
        }

        private void AppTitleBar_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SetTitleBarArea(TitleBarArea.Top);
        }

        private static readonly RectInt32[] _dragRectNone = [new(0, 0, 0, 0)];
        private static readonly RectInt32[] _dragRectTop = [new()];

        private void SetTitleBarArea(TitleBarArea titleBarArea)
        {
            if (App.MainWindow?.AppWindow?.TitleBar is null) return;

            var scale = AppTitleBar.XamlRoot?.RasterizationScale ?? 1.0;

            switch (titleBarArea)
            {
                case TitleBarArea.None:
                    App.MainWindow.AppWindow.TitleBar.SetDragRectangles(_dragRectNone);
                    break;

                case TitleBarArea.Top:
                    _dragRectTop[0] = new RectInt32(
                        0, 0,
                        (int)(AppTitleBar.ActualWidth * scale),
                        (int)(AppTitleBar.ActualHeight * scale)
                    );
                    App.MainWindow.AppWindow.TitleBar.SetDragRectangles(_dragRectTop);
                    break;
            }
        }

        private void CloseTitleBarButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is null) return;
            if (AppSettings.IsRunningBackend)
            {
                App.MainWindow.Hide();
                if (AppSettings.IsTrimOnHideEnabled)
                    _ = WorkingSetCompressor.TrimSelfAsync();
            }
            else
            {
                _ = App.Current_Exit();
            }
        }

        private void NavigateTo(Type pageType, object? parameter = null, NavigationTransitionInfo? navigationTransitionInfo = null)
        {
            MainFrame.Navigate(pageType, parameter, navigationTransitionInfo);
            MainFrame.BackStack.Clear();
        }

        private void InitiaizeEqualizerDialog()
        {
            if (EqualizerDialog is null)
            {
                EqualizerDialog = new EqualizerDialog();
                EqualizerDialog.EqualizerGainChanged += (s, frequency) =>
                {
                    // Fire-and-forget full state sync: idempotent, latest state wins,
                    // so rapid slider drags can never lose the final gain values.
                    ViewModel.PlayerCommandService.EqUpdate();
                };
            }
        }

        private void SetSettingsDialog()
        {
            SettingsDialog ??= new SettingsDialog(ViewModel.AppViewModel);
        }

        private void NavigateToDefaultPage()
        {

            foreach (var item in NavigationViewControl.MenuItems)
            {
                if (item is NavigationViewItem navigationViewItem && navigationViewItem.Tag?.ToString() == ViewModel.AppViewModel.DefaultEntryComboBoxTag)
                {
                    NavigationViewControl.SelectedItem = navigationViewItem;
                    break;
                }
            }
            switch (ViewModel.AppViewModel.DefaultEntryComboBoxTag)
            {
                case "Home":
                    NavigateTo(typeof(HomePage), null, new EntranceNavigationTransitionInfo());
                    break;
                case "AddFolder":
                    NavigateTo(typeof(AddFolderPage), null, new EntranceNavigationTransitionInfo());
                    break;
                case "MusicBrowse":
                    NavigateTo(typeof(MusicBrowsePage), null, new EntranceNavigationTransitionInfo());
                    break;
                case "Stats":
                    NavigateTo(typeof(StatsPage), null, new EntranceNavigationTransitionInfo());
                    break;
                default:
                    // 播放列表页已从侧边栏隐藏, 历史配置值回退到首页
                    NavigateTo(typeof(HomePage), null, new EntranceNavigationTransitionInfo());
                    break;
            }
        }

        public void NavigateToSettingsPage()
        {
            if (PlayingFrame.Visibility is Visibility.Visible)
            {
                NavigationViewControl.Visibility = Visibility.Visible;
                _playingNavigation.Dismiss(300);
                ViewModel.AppViewModel.IsPlayingDetailVisible = false;
                ViewModel.AppViewModel.IsPointerOverTitleBar = true;
            }
            if (MainFrame.Content is not SettingsPage)
            {
                NavigationViewControl.SelectedItem = NavigationViewControl.SettingsItem;
                NavigateTo(typeof(SettingsPage), null, new EntranceNavigationTransitionInfo());
            }
        }

        // ─── 顶栏账号入口 ─────────────────────────────

        private void OnAuthLoginStateChanged()
        {
            DispatcherQueue.TryEnqueue(() => _ = UpdateAccountAvatarAsync());
        }

        private async Task UpdateAccountAvatarAsync()
        {
            var user = _authService.CurrentUser;
            var avatar = user?.Avatar;
            if (AccountAvatarToolTipText is not null)
            {
                AccountAvatarToolTipText.Text = _authService.IsLoggedIn
                    ? user!.Nickname
                    : GetString("AccountTitle");
            }
            if (string.IsNullOrWhiteSpace(avatar) || AccountAvatarEllipse is null)
            {
                if (AccountAvatarEllipse is not null) AccountAvatarEllipse.Visibility = Visibility.Collapsed;
                if (AccountAvatarPlaceholderIcon is not null) AccountAvatarPlaceholderIcon.Visibility = Visibility.Visible;
                return;
            }
            var source = await AvatarToImageAsync(avatar);
            if (source is null)
            {
                AccountAvatarEllipse.Visibility = Visibility.Collapsed;
                AccountAvatarPlaceholderIcon.Visibility = Visibility.Visible;
                return;
            }
            AccountAvatarBrush.ImageSource = source;
            AccountAvatarEllipse.Visibility = Visibility.Visible;
            AccountAvatarPlaceholderIcon.Visibility = Visibility.Collapsed;
        }

        /// <summary>头像 data URI(base64)或 URL → ImageSource。</summary>
        private static async Task<ImageSource?> AvatarToImageAsync(string avatar)
        {
            try
            {
                if (avatar.StartsWith("data:image/", StringComparison.Ordinal))
                {
                    var comma = avatar.IndexOf(',');
                    if (comma <= 0 || comma >= avatar.Length - 1) return null;
                    var bytes = Convert.FromBase64String(avatar[(comma + 1)..]);
                    using var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                    await ms.WriteAsync(bytes.AsBuffer());
                    ms.Seek(0);
                    var bmp = new BitmapImage();
                    await bmp.SetSourceAsync(ms);
                    return bmp;
                }
                if (Uri.TryCreate(avatar, UriKind.Absolute, out var uri))
                    return new BitmapImage { UriSource = uri };
            }
            catch { }
            return null;
        }

        private void AccountAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlayingFrame.Visibility is Visibility.Visible)
            {
                NavigationViewControl.Visibility = Visibility.Visible;
                _playingNavigation.Dismiss(300);
                ViewModel.AppViewModel.IsPlayingDetailVisible = false;
                ViewModel.AppViewModel.IsPointerOverTitleBar = true;
            }
            if (MainFrame.Content is not AccountPage)
            {
                // 账号页不在侧边栏菜单中, 取消侧边栏选中态
                NavigationViewControl.SelectedItem = null;
                NavigateTo(typeof(AccountPage), null, new EntranceNavigationTransitionInfo());
            }
        }

        private void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            Type? targetType = null;

            if (args.IsSettingsInvoked)
            {
                targetType = typeof(SettingsPage);
            }
            else
            {
                targetType = args.InvokedItemContainer.Tag.ToString() switch
                {
                    "Home" => typeof(HomePage),
                    "AddFolder" => typeof(AddFolderPage),
                    "MusicBrowse" => typeof(MusicBrowsePage),
                    "PlayLists" => typeof(PlayListPage),
                    "MyPlayLists" => typeof(MyPlayListPage),
                    "OnlineSearch" => typeof(OnlineSearchPage),
                    "PluginManage" => typeof(PluginManagePage),
                    "Stats" => typeof(StatsPage),
                    _ => null
                };
            }

            if (targetType is not null && MainFrame.Content?.GetType() != targetType)
            {
                NavigateTo(targetType, null, new EntranceNavigationTransitionInfo());
            }
        }

        public void NavigateToMusicBrowsePage(string? keyword = null)
        {
            // 按 Tag 定位侧边栏选中项(不依赖菜单顺序, 顺序调整后不会错位)
            NavigationViewControl.SelectedItem = NavigationViewControl.MenuItems.OfType<NavigationViewItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == "MusicBrowse");
            if (MainFrame.Content is not MusicBrowsePage)
            {
                NavigateTo(typeof(MusicBrowsePage), keyword, new EntranceNavigationTransitionInfo());
            }
            else if (MainFrame.Content is MusicBrowsePage browsePage)
            {
                // 页面已在当前: 直接应用/清除本地搜索关键词
                browsePage.ApplySearchKeyword(keyword);
            }
        }

        /// <summary>跳转到在线搜索页(首页搜索框入口): 选中侧边栏对应项并带入关键词自动搜索。</summary>
        public void NavigateToOnlineSearchPage(string keyword)
        {
            NavigationViewControl.SelectedItem = NavigationViewControl.MenuItems.OfType<NavigationViewItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == "OnlineSearch");
            if (MainFrame.Content is not OnlineSearchPage)
            {
                NavigateTo(typeof(OnlineSearchPage), keyword, new EntranceNavigationTransitionInfo());
            }
            else
            {
                // 页面已在当前(缓存被禁用场景外的二次进入), 直接触发搜索
                if (MainFrame.Content is OnlineSearchPage page)
                {
                    page.ViewModel.Keyword = keyword;
                    _ = page.ViewModel.SearchCommand.ExecuteAsync(null);
                }
            }
        }

        public void NavigateToPlayingDetailPage()
        {
            if (_isPageTransitioning) return;
            _isPageTransitioning = true;

            if (PlayingFrame.Visibility is Visibility.Collapsed)
            {
                var pendingCount = 1;
                void OnOneCompleted()
                {
                    if (Interlocked.Decrement(ref pendingCount) == 0)
                        _isPageTransitioning = false;
                }
                _playingNavigation.Show(typeof(PlayingDetailPage), 300, onCompleted: OnOneCompleted);
                NavigationViewControl.Visibility = Visibility.Collapsed;
                ViewModel.AppViewModel.IsPlayingDetailVisible = true;
                ViewModel.AppViewModel.IsPointerOverTitleBar = false;
            }
            else
            {
                _isPageTransitioning = false;
            }
        }

        public void NavigatebackToMusicBrowsePage()
        {
            if (_isPageTransitioning) return;
            _isPageTransitioning = true;

            if (PlayingFrame.Visibility is Visibility.Visible)
            {
                var pendingCount = 1;
                void OnOneCompleted()
                {
                    if (Interlocked.Decrement(ref pendingCount) == 0)
                        _isPageTransitioning = false;
                }
                _playingNavigation.Dismiss(300, onCompleted: OnOneCompleted);
                NavigationViewControl.Visibility = Visibility.Visible;
                ViewModel.AppViewModel.IsPlayingDetailVisible = false;
                ViewModel.AppViewModel.IsPointerOverTitleBar = true;
            }
            else
            {
                _isPageTransitioning = false;
            }
        }

        public void HandleBackNavigation()
        {
            if (MainFrame.Content is PlayListPage playListPage && playListPage.ViewModel.IsInDetailMode)
            {
                playListPage.CollapseDetail();
                return;
            }
            App.Services.GetRequiredService<MusicBrowseViewModel>().BackButton();
        }

        private void NavigationViewControl_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            HandleBackNavigation();
        }

        private void ProgressSlider_Loaded(object sender, RoutedEventArgs e)
        {
            var thumb = FindVisualChild<Thumb>(ProgressSlider);
            if (thumb is not null)
            {
                thumb.DragStarted += Thumb_DragStarted;
                thumb.DragCompleted += Thumb_DragCompleted;
                //thumb.DragDelta += (s, e) =>
                //{
                //    _progressToolTip?.Content = ViewModel.AppViewModel.ProgressSliderThumbTipText;
                //};
                //ToolTipService.SetToolTip(thumb, _progressToolTip);
                //_progressToolTip.Opened += (s, e) =>
                //    _progressToolTip.Content = ViewModel.AppViewModel.ProgressSliderThumbTipText;
            }
        }

        private void ProgressSlider_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            ViewModel.AppViewModel.IsMouseOverProgressBar = true;
        }

        private void ProgressSlider_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            ViewModel.AppViewModel.IsMouseOverProgressBar = false;
        }

        private void VolumeSlider_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            ViewModel.AppViewModel.IsMouseOverVolumeSlider = true;
        }

        private void VolumeButton_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var delta = e.GetCurrentPoint(VolumeButton).Properties.MouseWheelDelta;
            if (delta > 0)
            {
                ViewModel.AppViewModel.AdjustVolume(5);
            }
            else if (delta < 0)
            {
                ViewModel.AppViewModel.AdjustVolume(-5);
            }
            e.Handled = true;
        }

        private void VolumeSlider_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            ViewModel.AppViewModel.IsMouseOverVolumeSlider = false;
        }

        private void VolumeSlider_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (ViewModel.AppViewModel.IsMouseOverVolumeSlider)
            {
                var delta = e.GetCurrentPoint(VolumeSlider).Properties.MouseWheelDelta;
                if (delta > 0)
                {
                    ViewModel.AppViewModel.AdjustVolume(1);
                }
                else if (delta < 0)
                {
                    ViewModel.AppViewModel.AdjustVolume(-1);
                }
                e.Handled = true;
            }
        }

        private void Thumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            ViewModel.AppViewModel.IsUserDraggingProgressSlider = true;
        }

        private void Thumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            ViewModel.AppViewModel.IsUserDraggingProgressSlider = false;
            _ = Task.Run(() =>
            {
                var (_, totalMs) = ViewModel.AppViewModel.GetTimeProgressCache();
                long newPosMs = Math.Max(0, Math.Min((long)(ViewModel.AppViewModel.ProgressSlider * 1000), totalMs));
                ViewModel.AppViewModel.IsManualSelect = true;
                ViewModel.PlayerCommandService.ChangeWaveChannelTime(newPosMs);
                ViewModel.AppViewModel.SetTimeProgressCache(newPosMs, totalMs);
                ViewModel.AppViewModel.IsManualSelect = false;
            });
        }

        private void CurrentPlayListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            var selectedMusic = CurrentPlayListView.SelectedItem as Music;
            if (selectedMusic is not null)
            {
                _ = ViewModel.MusicBrowseVM.PlayMusic(music: selectedMusic, IsChangeList: false);
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

        /// <summary>播放列表面板是否展开(防重复动画)。</summary>
        private bool _isPlayListPanelOpen;

        private void CurrentPlayListButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlayListPanelOpen)
            {
                ClosePlayListPanel();
            }
            else
            {
                OpenPlayListPanel();
            }
        }

        private void OpenPlayListPanel()
        {
            _isPlayListPanelOpen = true;
            CurrentPlayListOverlay.Visibility = Visibility.Visible;
            CurrentPlayListPanel.Visibility = Visibility.Visible;
            // 先归位到右侧边缘再滑入, 保证每次都有滑入动画
            ((TranslateTransform)CurrentPlayListPanel.RenderTransform).X = CurrentPlayListPanel.Width;
            UpdateCurrentPlayList();
            AnimatePlayListPanel(0);
        }

        private void ClosePlayListPanel()
        {
            if (!_isPlayListPanelOpen) return;
            _isPlayListPanelOpen = false;
            AnimatePlayListPanel(CurrentPlayListPanel.Width, () =>
            {
                // 动画完成前面板被重新打开时不收起
                if (!_isPlayListPanelOpen)
                {
                    CurrentPlayListPanel.Visibility = Visibility.Collapsed;
                    CurrentPlayListOverlay.Visibility = Visibility.Collapsed;
                }
            });
        }

        /// <summary>面板横向滑动动画(220ms EaseOut)。</summary>
        private void AnimatePlayListPanel(double to, Action? onCompleted = null)
        {
            var translate = (TranslateTransform)CurrentPlayListPanel.RenderTransform;
            var anim = new DoubleAnimation
            {
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(anim, translate);
            Storyboard.SetTargetProperty(anim, "X");
            var storyboard = new Storyboard();
            storyboard.Children.Add(anim);
            if (onCompleted is not null)
            {
                storyboard.Completed += (_, _) => onCompleted();
            }
            storyboard.Begin();
        }

        /// <summary>点击面板外区域(遮罩层)关闭。</summary>
        private void CurrentPlayListOverlay_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ClosePlayListPanel();
        }

        /// <summary>点击面板内部: 阻止冒泡到遮罩层导致误关闭。</summary>
        private void CurrentPlayListPanel_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void CurrentPlayListPanelCloseButton_Click(object sender, RoutedEventArgs e)
        {
            ClosePlayListPanel();
        }

        public void UpdateCurrentPlayList()
        {
            if (ViewModel.AppViewModel.CurrentPlayingList is not null)
            {
                if (ViewModel.AppViewModel.CurrentPlayingMusic is not null)
                {
                    var selectedMusic = ViewModel.AppViewModel.CurrentPlayingList.AsValueEnumerable().FirstOrDefault(music =>
                    music.Id == ViewModel.AppViewModel.CurrentPlayingMusic.Id);

                    if (selectedMusic is not null)
                    {
                        _ = Task.Delay(100).ContinueWith(_ =>
                        {
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                CurrentPlayListView.SelectedItem = selectedMusic;
                                CurrentPlayListView.ScrollIntoView(selectedMusic);
                            });
                        });
                    }
                }
            }
        }

        private void CancelPlayingDetailButton_Click(object sender, RoutedEventArgs e)
        {
            NavigatebackToMusicBrowsePage();
        }
    }
}
