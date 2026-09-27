using AnimatedWin2dControls.Controls.AnimatedLyricsLineControl;
using AnimatedWin2dControls.Controls.AnimatedTextBlock.Effects;
using AnimatedWin2dControls.Controls.AnimatedTextBlock.Enums;
using DevWinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Windowing;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using WinUIMusicPlayer.Helper;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Services;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.ViewModel;
using WinUIMusicPlayer.ViewModel.Pages;
using WinUIMusicPlayer.View.SubView;
using ZLinq;
using CommunityToolkit.WinUI;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUIMusicPlayer.View
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class PlayingDetailPage : Page, IDisposable
    {
        public PlayingDetailViewModel ViewModel { get; }
        private ILogger<PlayingDetailPage> _logger;
        private float _dpiScale = 1.0f;
        private bool _isPortraitLayout;
        private bool _isPortraitWanted;
        private const double PortraitEnterRatio = 1.15;
        private const double PortraitExitRatio = 1.10;
        private const double PortraitTopVerticalMargin = 20;
        private const double textScale = 1.6;  
        private const double lyricsScale = 1.6;
        public PlayingDetailPage(PlayingDetailViewModel viewModel)
        {
            AnimatedWin2dControls.Controls.AlbumImgControl.AlbumArtControl.CoverCacheBasePath = AppSettings.MusicCoverCache;
            this.InitializeComponent();
            ViewModel = viewModel;
            DataContext = this;
            // 播放音质菜单: 与主底栏共用同一套构建逻辑(持久化默认音质 + 在线歌曲换质重播)
            PlayQualityButtonPlayingDetail.Flyout = Helper.PlayQualityMenuHelper.BuildMenu();
            Loaded += PlayingDetailPage_Loaded;
            // 页面 DI 单例: 静态歌词总线订阅一次, 空歌词(如 B 站无词歌曲)时显示"暂无歌词"空态
            AnimatedWin2dControls.Messages.UILyricsBus.Changed += OnUILyricsChangedForEmpty;
            UpdateEmptyLyricsPanel(viewModel.AppViewModel.UILyrics);
            _logger = App.GetLogger<PlayingDetailPage>();
        }

        /// <summary>歌词数据变化: null/空列表显示"暂无歌词"空态, 有词隐藏。</summary>
        private void OnUILyricsChangedForEmpty(System.Collections.Generic.IList<AnimatedWin2dControls.Controls.AnimatedLyricsLineControl.LyricLine>? value)
            => UpdateEmptyLyricsPanel(value);

        private void UpdateEmptyLyricsPanel(System.Collections.Generic.IList<AnimatedWin2dControls.Controls.AnimatedLyricsLineControl.LyricLine>? value)
            => EmptyLyricsPanel.Visibility = (value is null || value.Count == 0)
                ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>空态"关联歌词"按钮: 复用底栏关联歌词对话框流程。</summary>
        private void EmptyLinkLyrics_Click(object sender, RoutedEventArgs e)
            => LinkLyricsButtonPlayingDetail_Click(sender, e);

        private bool _isLoaded;
        private bool _disposed;
        public bool IsLoaded => _isLoaded;

        private void PlayingDetailPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            if (ViewModel.AppViewModel.IsWin2dAnimatedText)
            {
                var effectType = ViewModel.AppViewModel.Win2dTextEffectType.Value;
                // 1. 定义一个简单的局部函数或直接在表达式中实例化
                AnimatedWin2dControls.Controls.AnimatedTextBlock.ITextEffect CreateEffect(AnimatedTextEffect type) => type switch
                {
                    AnimatedTextEffect.TextBlurEffect => new AnimatedWin2dControls.Controls.AnimatedTextBlock.Effects.TextBlurEffect(),
                    AnimatedTextEffect.TextElasticEffect => new AnimatedWin2dControls.Controls.AnimatedTextBlock.Effects.TextElasticEffect(),
                    AnimatedTextEffect.TextFadeEffect => new TextFadeEffect(),
                    AnimatedTextEffect.TextMotionBlurEffect => new AnimatedWin2dControls.Controls.AnimatedTextBlock.Effects.TextMotionBlurEffect(),
                    AnimatedTextEffect.TextPivotEffect => new AnimatedWin2dControls.Controls.AnimatedTextBlock.Effects.TextPivotEffect(),
                    AnimatedTextEffect.TextWipeEffect => new TextWipeEffect(),
                    AnimatedTextEffect.TextZoomEffect => new AnimatedWin2dControls.Controls.AnimatedTextBlock.Effects.TextZoomEffect(),
                    _ => new AnimatedWin2dControls.Controls.AnimatedTextBlock.Effects.TextDefaultEffect()
                };
                var effect = CreateEffect(effectType);
                AnimatedPlayingDetailTitleTextBlock?.TextEffect = effect;
                AnimatedPlayingDetailAlbumArtistTextBlock?.TextEffect = effect;
            }
            App.MainWindow.SizeChanged += MainWindow_SizeChanged;
            App.MainWindow.AppWindow.Changed += AppWindow_Changed;
            ViewModel.AppViewModel.PropertyChanged += AppViewModel_PropertyChanged;
            _ = ChangeControlsFontSize();
            if (ViewModel.AppViewModel.LyricPagePalette is { } palette)
                NowPlaying?.SetPalette(palette);
            if (ViewModel.AppViewModel.LyricPageArtwork is { } artwork)
                NowPlaying?.SetArtwork(artwork);
            UpdateLyricsRegion();
            // 背景自适应字色初评(当前曲已播放时页面后加载的场景)
            ScheduleAdaptiveTextUpdate();
            Loaded -= PlayingDetailPage_Loaded;
        }

        private void AppViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppViewModel.LyricPagePalette))
            {
                if (ViewModel.AppViewModel.LyricPagePalette is { } palette)
                {
                    NowPlaying?.SetPalette(palette);
                }
                ScheduleAdaptiveTextUpdate();
            }
            else if (e.PropertyName == nameof(AppViewModel.LyricPageArtwork))
            {
                NowPlaying?.SetArtwork(ViewModel.AppViewModel.LyricPageArtwork);
            }
            else if (e.PropertyName == nameof(AppViewModel.LyricsMargin))
            {
                UpdateLyricsRegion();
            }
            else if (e.PropertyName == nameof(AppViewModel.IsDarkMode)
                || e.PropertyName == nameof(AppViewModel.UseImageDominantTheme)
                || e.PropertyName == nameof(AppViewModel.IsFluidBackgroundEnabled))
            {
                ScheduleAdaptiveTextUpdate();
            }
            else if (e.PropertyName == nameof(AppViewModel.IsWin2dAnimatedText))
            {
                // 动画文字控件因 x:Load 重建, 丢弃已应用状态以便对新建实例重设前景色
                _adaptiveWhiteText = null;
                ScheduleAdaptiveTextUpdate();
            }
        }

        // ── 背景自适应文字色 ─────────────────────────────────────────────────

        /// <summary>黑白字 WCAG 对比度交叉点: 背景亮度低于该值时白字对比更高, 反之黑字更高
        /// (白字 1.05/(L+0.05) 与黑字 (L+0.05)/0.05 在 L≈0.179 处相等)。</summary>
        private const float AdaptiveTextCrossoverLuminance = 0.179f;

        /// <summary>当前自适应覆盖: null=未覆盖(跟主题), true=白字, false=黑字。防重复应用。</summary>
        private bool? _adaptiveWhiteText;

        /// <summary>上次评估的背景亮度: 漂移超阈值才记日志, 防逐曲重复刷屏。</summary>
        private float? _lastAdaptiveBgLum;

        /// <summary>按当前歌曲背景亮度自适应文字颜色(逐曲重算, 仅本页临时生效):
        /// 背景过暗→白字, 背景过亮→黑字(对比度择优, 不依赖主题默认字色);
        /// 非流体背景时页面底色跟主题永远可读, 还原跟随主题。
        /// 覆盖范围: 主内容区与底部控制条两棵子树, 播放列表面板(亚克力底)不受影响。</summary>
        private void ScheduleAdaptiveTextUpdate()
        {
            // 延迟一拍: 等 x:Bind 把 IsDarkMode 推到背景画布(DP→RefreshColors 重算目标色)后再读亮度
            DispatcherQueue.TryEnqueue(UpdateAdaptiveTextColor);
        }

        private void UpdateAdaptiveTextColor()
        {
            try
            {
                // 流体背景未启用: 页面底色跟主题, 文字永远可读 → 还原
                if (!ViewModel.AppViewModel.IsFluidBackgroundEnabled || NowPlaying is null)
                {
                    _lastAdaptiveBgLum = null;
                    ApplyAdaptiveTextOverride(null);
                    return;
                }
                float bgLum = NowPlaying.GetBackgroundLuminance();
                // 对比度择优: 背景亮度低于交叉点选白字, 反之选黑字。
                // 不沿用"主题默认色≥4.5 对比即保持": 亮色主题下调色板被亮度缩放拉到理论亮区,
                // 实际渲染(LightWave 压暗/流体空间不均)偏暗时黑字仍会被误判可读
                bool whiteText = bgLum < AdaptiveTextCrossoverLuminance;
                if (_lastAdaptiveBgLum is not { } prev || MathF.Abs(prev - bgLum) > 0.02f)
                {
                    _lastAdaptiveBgLum = bgLum;
                    _logger.LogInformation("背景自适应字色评估: 背景亮度={bgLum:F3} → 择优{choice}",
                        bgLum, whiteText ? "白字" : "黑字");
                }
                ApplyAdaptiveTextOverride(whiteText);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "背景自适应字色计算失败");
            }
        }

        private void ApplyAdaptiveTextOverride(bool? whiteText)
        {
            if (whiteText == _adaptiveWhiteText) return;
            _adaptiveWhiteText = whiteText;
            _logger.LogInformation("背景自适应字色: {action} (背景亮度≈{bgLum:F3})",
                whiteText is null ? "跟随主题" : whiteText.Value ? "应用白字" : "应用黑字",
                NowPlaying?.GetBackgroundLuminance() ?? -1f);

            // 高级歌词由 Win2D 绘制, 不吃子树主题翻转, 走协调器文字色覆盖口
            NowPlaying?.SetLyricsTextColorOverride(
                whiteText is null ? null : whiteText.Value ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black);

            if (whiteText is null)
            {
                // 还原: 子树主题回跟应用, 动画文字清本地覆盖回主题色
                PlayingDetail.RequestedTheme = ElementTheme.Default;
                ControlsStack.RequestedTheme = ElementTheme.Default;
                AnimatedPlayingDetailTitleTextBlock?.ClearValue(Control.ForegroundProperty);
                AnimatedPlayingDetailAlbumArtistTextBlock?.ClearValue(Control.ForegroundProperty);
                return;
            }

            // 临时翻转: 子树整体换主题(标题/歌手/歌词/控制条图标等所有 ThemeResource 文字一致反色);
            // Win2D 动画文字直接读应用级资源不吃子树主题, 单独设本地前景色
            var theme = whiteText.Value ? ElementTheme.Dark : ElementTheme.Light;
            PlayingDetail.RequestedTheme = theme;
            ControlsStack.RequestedTheme = theme;
            var foreground = new SolidColorBrush(whiteText.Value ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black);
            if (AnimatedPlayingDetailTitleTextBlock is not null)
                AnimatedPlayingDetailTitleTextBlock.Foreground = foreground;
            if (AnimatedPlayingDetailAlbumArtistTextBlock is not null)
                AnimatedPlayingDetailAlbumArtistTextBlock.Foreground = foreground;
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidVisibilityChange)
            {
                bool visible = sender.IsVisible;
                NowPlaying?.SetWindowPaused(!visible);
                LyricsView?.SetWindowPaused(!visible);
            }
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            _ = ChangeControlsFontSize();
        }

        private async Task ChangeControlsFontSize()
        {
            var windowSize = App.MainWindow.AppWindow.Size;
            _dpiScale = WindowSizeHelper.GetScaleFactor(AppData.HWnd);

            bool wantPortrait = _isPortraitWanted;
            if (windowSize.Width > 0)
            {
                double ratio = (double)windowSize.Height / windowSize.Width;
                wantPortrait = _isPortraitWanted
                    ? ratio > PortraitExitRatio
                    : ratio > PortraitEnterRatio;
            }

            bool portrait = wantPortrait;
            double driver = windowSize.Width / _dpiScale;
            var (lyrics, title, artist,info, topHeight) = driver switch
            {
                <= 1024 => (36, 30, 24, 10,  260),
                < 1280 => (42, 36, 26, 11,  280),
                < 1600 => (52, 44, 30, 13, 300),
                < 1920 => (64, 56, 34,  14, 320),
                < 2560 => (80, 64, 38, 15, 340),
                < 2880 => (96, 72, 40, 16, 360),
                _ => (120, 86, 44, 18, 380)
            };

            if (portrait)
            { 
                lyrics = (int)(lyrics * lyricsScale);
                title = (int)(title * textScale);
                artist = (int)(artist * textScale);
                info = (int)(info * textScale);
            }

            // 4. 应用变更
            await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
            {
                ViewModel.TitleFontSize = title;
                ViewModel.ArtistAlbumFontSize = artist;
                ViewModel.InfoFontSize = info;
                ViewModel.AppViewModel.LyricsFontSize = lyrics;
                AnimatedPlayingDetailTitleTextBlock?.FontSize = title;
                AnimatedPlayingDetailAlbumArtistTextBlock?.FontSize = artist;

                if (windowSize.Width > 0 && wantPortrait != _isPortraitWanted)
                {
                    _isPortraitWanted = wantPortrait;
                    ApplyAspectRatioLayout(wantPortrait, topHeight);
                    UpdateLyricsRegion();
                }
            });
        }

        private void ApplyAspectRatioLayout(bool portrait, double topHeight = 300)
        {
            if (_isPortraitLayout == portrait) return;
            _isPortraitLayout = portrait;
            ViewModel.AppViewModel.IsPortraitLayout = portrait;

            if (portrait)
            {
                // Z-order: LyricsRegionHost 最底层，LeftControlPanel 浮在上面（不用 Remove/Insert 避免 Unloaded 触发 PrepareForShutdown）
                Canvas.SetZIndex(LyricsRegionHost, 0);
                Canvas.SetZIndex(LeftControlPanel, 2);

                PlayingDetail.RowDefinitions.Clear();
                PlayingDetail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                PlayingDetail.ColumnDefinitions.Clear();
                PlayingDetail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // LyricsRegionHost 占满全页
                Grid.SetRow(LyricsRegionHost, 0);
                Grid.SetColumn(LyricsRegionHost, 0);
                Grid.SetRowSpan(LyricsRegionHost, 1);
                Grid.SetColumnSpan(LyricsRegionHost, 1);
                LyricsRegionHost.Margin = new Thickness(0,60,0,0);

                // LeftControlPanel 固定在顶部，浮在 lyrics 之上
                LeftControlPanel.Height = topHeight;
                LeftControlPanel.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetRow(LeftControlPanel, 0);
                Grid.SetColumn(LeftControlPanel, 0);
                Grid.SetRowSpan(LeftControlPanel, 1);
                Grid.SetColumnSpan(LeftControlPanel, 1);

                LeftControlPanel.ColumnDefinitions.Clear();
                LeftControlPanel.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                LeftControlPanel.Margin = new Thickness(10, PortraitTopVerticalMargin, 10, 0);
                LeftControlPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(topHeight, GridUnitType.Pixel) });
                LeftControlPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                LeftControlPanel.RowDefinitions.Clear();
                LeftControlPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                LeftControlPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                LeftControlPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                CoverContainer.VerticalAlignment = VerticalAlignment.Center;
                CoverContainer.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetRow(CoverContainer, 0);
                Grid.SetRowSpan(CoverContainer, 3);
                Grid.SetColumn(CoverContainer, 0);
                Grid.SetColumnSpan(CoverContainer, 1);

                AnimatedTextBlock.Margin = new Thickness(16, 0, 16, 0);
                Grid.SetRow(AnimatedTextBlock, 1);
                Grid.SetColumn(AnimatedTextBlock, 1);
                Grid.SetRowSpan(AnimatedTextBlock, 1);
                Grid.SetColumnSpan(AnimatedTextBlock, 1);
            }
            else
            {
                PlayingDetail.RowDefinitions.Clear();
                PlayingDetail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                PlayingDetail.ColumnDefinitions.Clear();
                PlayingDetail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                PlayingDetail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // 还原 Z-order
                LyricsRegionHost.ClearValue(Canvas.ZIndexProperty);
                LeftControlPanel.ClearValue(Canvas.ZIndexProperty);

                LeftControlPanel.ClearValue(FrameworkElement.HeightProperty);
                LeftControlPanel.ClearValue(FrameworkElement.VerticalAlignmentProperty);
                LeftControlPanel.ClearValue(FrameworkElement.MarginProperty);
                LeftControlPanel.Background = null;

                Grid.SetRow(LeftControlPanel, 0);
                Grid.SetColumn(LeftControlPanel, 0);
                Grid.SetRowSpan(LeftControlPanel, 1);
                Grid.SetColumnSpan(LeftControlPanel, 1);

                LeftControlPanel.ColumnDefinitions.Clear();               
                LeftControlPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                LeftControlPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) });
                LeftControlPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                LeftControlPanel.RowDefinitions.Clear();
                LeftControlPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                LeftControlPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8, GridUnitType.Star) });
                LeftControlPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                LeftControlPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50, GridUnitType.Pixel) });
                LeftControlPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                CoverContainer.ClearValue(FrameworkElement.MarginProperty);
                CoverContainer.ClearValue(FrameworkElement.VerticalAlignmentProperty);
                CoverContainer.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
                Grid.SetRow(CoverContainer, 1);
                Grid.SetRowSpan(CoverContainer, 1);
                Grid.SetColumn(CoverContainer, 1);
                Grid.SetColumnSpan(CoverContainer, 1);
                AnimatedTextBlock.ClearValue(FrameworkElement.VerticalAlignmentProperty);
                AnimatedTextBlock.ClearValue(FrameworkElement.MarginProperty);
                AnimatedTextBlock.Margin = new Thickness(20, 0, 20, 0);
                Grid.SetRow(AnimatedTextBlock, 2);
                Grid.SetColumn(AnimatedTextBlock, 1);
                Grid.SetRowSpan(AnimatedTextBlock, 1);
                Grid.SetColumnSpan(AnimatedTextBlock, 1);

                Grid.SetRow(LyricsRegionHost, 0);
                Grid.SetColumn(LyricsRegionHost, 1);
                Grid.SetRowSpan(LyricsRegionHost, 1);
                Grid.SetColumnSpan(LyricsRegionHost, 1);
                LyricsRegionHost.Margin = new Thickness(0, 40, 0, 40);
            }

            PlayingDetail.UpdateLayout();
            LeftControlPanel.UpdateLayout();
        }

        private void ControlsStack_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            ViewModel.AppViewModel.ControlsStackOpacity = 1.0f;
        }

        private void ControlsStack_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            ViewModel.AppViewModel.ControlsStackOpacity = 0.0f;
        }

        private void ProgressSlider_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            ViewModel.AppViewModel.IsMouseOverProgressBar = true;
        }

        private void ProgressSlider_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            ViewModel.AppViewModel.IsMouseOverProgressBar = false;
        }       

        private void ProgressSliderPlayingDetail_Loaded(object sender, RoutedEventArgs e)
        {
            var thumb = ToolUtils.FindVisualChild<Thumb>(ProgressSliderPlayingDetail);
            if (thumb is not null)
            {
                thumb.DragStarted += Thumb_DragStarted;
                thumb.DragCompleted += Thumb_DragCompleted;
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
                App.Services.GetRequiredService<BassPlayerCommandService>().ChangeWaveChannelTime(newPosMs);
                ViewModel.AppViewModel.SetTimeProgressCache(newPosMs, totalMs);
                ViewModel.AppViewModel.IsManualSelect = false;
            });
        }

        private void EqualizerButton_Click(object sender, RoutedEventArgs e)
        {
            _ = App.Services.GetRequiredService<MainPage>().EqualizerDialog.ShowThemedAsync(this.XamlRoot);
        }

        /// <summary>添加到歌单: 弹出在线歌单选择弹窗, 可选已有歌单或新建(自动收入当前歌曲)。</summary>
        private void TitleAddToPlayListButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SubView.AddToMyPlayListDialog(ViewModel.AppViewModel);
            _ = dialog.ShowThemedAsync(this.XamlRoot);
        }

        /// <summary>播放列表面板是否展开(防重复动画)。</summary>
        private bool _isPlayListPanelOpenPlayingDetail;

        private void CurrentPlayListButtonPlayingDetail_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlayListPanelOpenPlayingDetail)
            {
                ClosePlayListPanelPlayingDetail();
            }
            else
            {
                OpenPlayListPanelPlayingDetail();
            }
        }

        private void OpenPlayListPanelPlayingDetail()
        {
            _isPlayListPanelOpenPlayingDetail = true;
            CurrentPlayListOverlayPlayingDetail.Visibility = Visibility.Visible;
            CurrentPlayListPanelPlayingDetail.Visibility = Visibility.Visible;
            // 先归位到右侧边缘再滑入, 保证每次都有滑入动画
            ((TranslateTransform)CurrentPlayListPanelPlayingDetail.RenderTransform).X = CurrentPlayListPanelPlayingDetail.Width;
            UpdateCurrentPlayList();
            AnimatePlayListPanelPlayingDetail(0);
        }

        private void ClosePlayListPanelPlayingDetail()
        {
            if (!_isPlayListPanelOpenPlayingDetail) return;
            _isPlayListPanelOpenPlayingDetail = false;
            AnimatePlayListPanelPlayingDetail(CurrentPlayListPanelPlayingDetail.Width, () =>
            {
                // 动画完成前面板被重新打开时不收起
                if (!_isPlayListPanelOpenPlayingDetail)
                {
                    CurrentPlayListPanelPlayingDetail.Visibility = Visibility.Collapsed;
                    CurrentPlayListOverlayPlayingDetail.Visibility = Visibility.Collapsed;
                }
            });
        }

        /// <summary>面板横向滑动动画(220ms EaseOut)。</summary>
        private void AnimatePlayListPanelPlayingDetail(double to, Action? onCompleted = null)
        {
            var translate = (TranslateTransform)CurrentPlayListPanelPlayingDetail.RenderTransform;
            var anim = new DoubleAnimation
            {
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                EasingFunction = new CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
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
        private void CurrentPlayListOverlayPlayingDetail_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ClosePlayListPanelPlayingDetail();
        }

        /// <summary>点击面板内部: 阻止冒泡到遮罩层导致误关闭。</summary>
        private void CurrentPlayListPanelPlayingDetail_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void CurrentPlayListPanelPlayingDetailCloseButton_Click(object sender, RoutedEventArgs e)
        {
            ClosePlayListPanelPlayingDetail();
        }

        /// <summary>底栏下载按钮: 与主页共用下载流程。</summary>
        private void DownloadButtonPlayingDetail_Click(object sender, RoutedEventArgs e)
        {
            DownloadFlowHelper.Start(XamlRoot, ViewModel.AppViewModel.CurrentPlayingMusic);
        }

        /// <summary>关联歌词: 打开搜索对话框(插件搜索歌词候选); 本地歌曲写歌词库, 在线歌曲会话级关联。</summary>
        private async void LinkLyricsButtonPlayingDetail_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var music = ViewModel.AppViewModel.CurrentPlayingMusic;
                if (music is null) return;

                var dialog = new LyricsLinkDialog(music)
                {
                    XamlRoot = XamlRoot,
                    RequestedTheme = AppSettings.ElementTheme,
                };
                _ = await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关联歌词流程异常");
            }
        }

        /// <summary>歌词区空态"关联歌词"按钮: 与底栏关联歌词按钮共用同一对话框流程。</summary>
        private void LyricsView_LinkLyricsRequested(object? sender, EventArgs e)
            => LinkLyricsButtonPlayingDetail_Click(sender, new RoutedEventArgs());
        private void UpdateCurrentPlayList()
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
                                _isProgrammaticPlayListSelectionPlayingDetail = true;
                                try
                                {
                                    CurrentPlayListViewPlayingDetail.SelectedItem = selectedMusic;
                                    CurrentPlayListViewPlayingDetail.ScrollIntoView(selectedMusic);
                                }
                                finally
                                {
                                    _isProgrammaticPlayListSelectionPlayingDetail = false;
                                }
                            });
                        });
                    }
                }
            }
        }
        /// <summary>
        /// 程序性高亮当前播放曲时抑制点播: 面板打开/切歌都会经 UpdateCurrentPlayList
        /// 设置 SelectedItem, 不抑制的话每次都会触发重播当前曲。
        /// </summary>
        private bool _isProgrammaticPlayListSelectionPlayingDetail;

        private void CurrentPlayListViewPlayingDetail_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isProgrammaticPlayListSelectionPlayingDetail) return;
            // 单击队列歌曲立即跳播(原来仅双击才播, 单击只选中不播放)
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is Music clicked)
            {
                _ = App.Services.GetRequiredService<MusicBrowseViewModel>().PlayMusic(music: clicked, IsChangeList: false);
            }
        }

        private void VolumeSlider_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            ViewModel.AppViewModel.IsMouseOverVolumeSlider = true;
        }

        private void VolumeButton_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var delta = e.GetCurrentPoint(VolumePlayingDetailBtn).Properties.MouseWheelDelta;
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

        private void AutoScrollHover_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (ViewModel.AppViewModel.IsWin2dAnimatedText) return;
            if (sender is AutoScrollView autoScrollView)
            {
                autoScrollView.IsPlaying = true;
            }
        }

        private void AutoScrollHover_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (ViewModel.AppViewModel.IsWin2dAnimatedText) return;
            if (sender is AutoScrollView autoScrollView)
            {
                autoScrollView.IsPlaying = false;
            }
        }

        private void AutoScrollHover_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (ViewModel.AppViewModel.IsWin2dAnimatedText) return;
            if (sender is AutoScrollView autoScrollView)
            {
                autoScrollView.IsPlaying = false;
            }
        }

        private void VolumeSlider_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (ViewModel.AppViewModel.IsMouseOverVolumeSlider)
            {
                var delta = e.GetCurrentPoint(VolumeSliderPlayingDetail).Properties.MouseWheelDelta;
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void PauseCanvasRendering()
        {
            NowPlaying?.PauseRendering();
            LyricsView?.PauseRendering();
        }

        public void ResumeCanvasRendering()
        {
            NowPlaying?.ResumeRendering();
            LyricsView?.ResumeRendering();
        }

        private void Dispose(bool dispose)
        {
            if (dispose)
            {
                App.MainWindow?.SizeChanged -= MainWindow_SizeChanged;
                if (App.MainWindow?.AppWindow is { } appWindow)
                    appWindow.Changed -= AppWindow_Changed;
                if (ViewModel?.AppViewModel is { } appvm)
                    appvm.PropertyChanged -= AppViewModel_PropertyChanged;
                AnimatedWin2dControls.Messages.UILyricsBus.Changed -= OnUILyricsChangedForEmpty;
                LyricsView?.LyricInteracted -= LyricsView_LyricInteracted;
                LyricsView?.ExceptionInteracted -= LyricsView_ExceptionInteracted;
                LyricsView?.ShutdownLyricsCanvas();
                AlbumArtControl?.Dispose();
                NowPlaying?.ExceptionOccurred -= BackGround_ExceptionOccurred;
                NowPlaying?.Dispose();
            }
        }

        private void BackGround_ThemeResolved(object sender, bool isDark)
        {
            App.Services.GetRequiredService<AppViewModel>().IsDarkMode = isDark;
            if (isDark)
            {
                AppSettings.AppTheme = "Dark";
                AppSettings.ElementTheme = ElementTheme.Dark;

            }
            else
            {
                AppSettings.AppTheme = "Light";
                AppSettings.ElementTheme = ElementTheme.Light;
            }
            App.MainWindow?.SetAppTheme();
        }

        private void LyricsView_LyricInteracted(object? sender, TimeSpan e)
        {
            Task.Run(() =>
            {
                AnimatedWin2dControls.Controls.AnimatedLyricsLineControl.LyricLine? lyricLine = ViewModel.AppViewModel.UILyrics.AsValueEnumerable().FirstOrDefault(line => line.StartMs >= e.TotalMilliseconds);
                ViewModel.AppViewModel.IsManualSelect = true;
                if (lyricLine is not null)
                {
                    int index = ViewModel.AppViewModel.UILyrics.IndexOf(lyricLine);
                    ViewModel.UpdateLyricsToUI(index);
                }
                App.Services.GetRequiredService<BassPlayerCommandService>().ChangeWaveChannelTime((long)e.TotalMilliseconds);
                ViewModel.AppViewModel.SetTimeProgressCacheCurMs((long)e.TotalMilliseconds);
                ViewModel.AppViewModel.IsManualSelect = false;
            });
        }

        private void LyricsView_ExceptionInteracted(object? sender, Exception e)
        {
            _logger.LogError(e, "歌词渲染错误");
        }

        private void BackGround_ExceptionOccurred(object? sender, Exception e)
        {
            _logger.LogError(e, "背景渲染错误");
        }

        private void NowPlaying_LyricLineClicked(object? sender, TimeSpan e)
        {
            LyricsView_LyricInteracted(sender, e);
        }

        private void LyricsRegionHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateLyricsRegion();
        }

        private void UpdateLyricsRegion()
        {
            if (NowPlaying is null || LyricsRegionHost is null) return;
            if (LyricsRegionHost.ActualWidth <= 0 || LyricsRegionHost.ActualHeight <= 0) return;
            try
            {
                var transform = LyricsRegionHost.TransformToVisual(NowPlaying);
                var origin = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                var margin = ViewModel.AppViewModel.LyricsMargin;
                NowPlaying.LyricsRegion = new Windows.Foundation.Rect(
                    origin.X + margin.Left,
                    origin.Y + 32,
                    LyricsRegionHost.ActualWidth - margin.Left - margin.Right,
                    LyricsRegionHost.ActualHeight - 32);
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "更新歌词区域时发生错误:{StackTrace}",ex.StackTrace);
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            _ = App.Services.GetRequiredService<MainPage>().SettingsDialog.ShowThemedAsync(this.XamlRoot);
        }
    }
}
