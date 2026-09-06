using System;
using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WinUIMusicPlayer.Utils;

namespace WinUIMusicPlayer.View.SubView;

/// <summary>
/// 下载进度悬浮小弹窗: 顶部居中, 挂载到 MainWindow 根 Grid(Grid 叠放不推挤布局, 不阻挡其他区域点击)。
/// 背景用 Composition BackdropBrush 实时模糊背后内容(壁纸/页面), 颜色恒定不随窗口焦点变化。
/// 下载中可点"隐藏"收起(后台继续下载), 完成时重新弹出结果提示, 1.5 秒后未手动关闭则自动向上滑出消失。
/// </summary>
public sealed partial class DownloadProgressFlyout : UserControl
{
    private SpriteVisual? _blurSprite;
    private DispatcherQueueTimer? _autoCloseTimer;
    private Vector2 _lastClipSize;

    public DownloadProgressFlyout(string title)
    {
        InitializeComponent();
        titleText.Text = title;
        hideButton.Content = ToolUtils.GetString("DownloadHide");
        closeButton.Content = ToolUtils.GetString("DialogClose");
        Loaded += (_, _) => SetupBackdropBlur();
        // 弹窗在 Collapsed 状态下 Loaded, 布局就绪后靠 LayoutUpdated 持续同步模糊层圆角裁剪
        LayoutUpdated += (_, _) => UpdateBlurSpriteClip();
    }

    /// <summary>
    /// 注入背景模糊 visual(Composition BackdropBrush + 高斯模糊): 实时模糊卡片背后的壁纸/页面内容。
    /// 子 visual 恒渲染在宿主元素内容之上, 故挂到独立的 blurHost(Grid 中位于 tint/cardRoot 之前),
    /// 使模糊层整体渲染在卡片内容之下; 尺寸用 RelativeSizeAdjustment 自动跟随宿主,
    /// 不依赖 SizeChanged 事件时序(与 LyricsMaskView 的 redirectVisual 同一模式)。
    /// </summary>
    private void SetupBackdropBlur()
    {
        if (_blurSprite is not null) return;
        try
        {
            var elementVisual = ElementCompositionPreview.GetElementVisual(blurHost);
            var compositor = elementVisual.Compositor;

            var blurEffect = new GaussianBlurEffect
            {
                BlurAmount = 40.0f,
                BorderMode = EffectBorderMode.Soft,
                Source = new CompositionEffectSourceParameter("backdrop"),
            };
            var effectBrush = compositor.CreateEffectFactory(blurEffect).CreateBrush();
            effectBrush.SetSourceParameter("backdrop", compositor.CreateBackdropBrush());

            _blurSprite = compositor.CreateSpriteVisual();
            _blurSprite.Brush = effectBrush;
            _blurSprite.RelativeSizeAdjustment = Vector2.One;
            ElementCompositionPreview.SetElementChildVisual(blurHost, _blurSprite);
            UpdateBlurSpriteClip();
        }
        catch
        {
            // 模糊不可用时仅剩白色 tint, 卡片仍然可读
        }
    }

    /// <summary>同步模糊层圆角裁剪(尺寸已由 RelativeSizeAdjustment 自动跟随宿主)。</summary>
    private void UpdateBlurSpriteClip()
    {
        if (_blurSprite is null) return;
        try
        {
            var w = (float)blurHost.ActualWidth;
            var h = (float)blurHost.ActualHeight;
            if (w <= 0 || h <= 0) return;
            if (_lastClipSize.X == w && _lastClipSize.Y == h) return;
            _lastClipSize = new Vector2(w, h);
            var compositor = _blurSprite.Compositor;
            var geometry = compositor.CreateRoundedRectangleGeometry();
            geometry.CornerRadius = new Vector2(10, 10);
            geometry.Size = new Vector2(w, h);
            _blurSprite.Clip = compositor.CreateGeometricClip(geometry);
        }
        catch
        {
            // 忽略裁剪失败
        }
    }

    /// <summary>显示弹窗(挂到根 Grid 时调用)。</summary>
    public void Show()
    {
        Visibility = Visibility.Visible;
        _ = DispatcherQueue.TryEnqueue(() => UpdateBlurSpriteClip());
    }

    /// <summary>彻底关闭并从根 Grid 移除弹窗。</summary>
    public void Close()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _autoCloseTimer?.Stop();
            if (Parent is Panel panel) panel.Children.Remove(this);
        });
    }

    public void UpdateStatus(string status)
    {
        DispatcherQueue.TryEnqueue(() => statusText.Text = status);
    }

    public void UpdateProgress(double percent)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            progressBar.Value = percent;
            percentText.Text = $"{(int)percent}%";
        });
    }

    /// <summary>下载完成: 切换为结果态并重新弹出提示(即使此前被用户隐藏), 1.5 秒后自动向上滑出消失。</summary>
    public void Complete(bool ok, string message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            progressRing.IsActive = false;
            ProgressSection.Visibility = Visibility.Collapsed;
            hideButton.Visibility = Visibility.Collapsed;
            titleText.Text = ToolUtils.GetString(ok ? "DownloadDone" : "DownloadFailedShort");
            resultIcon.Glyph = ok ? "\uE73E" : "\uE783";
            // 白色磨砂底上用深色调图标(白底上可读)
            resultIcon.Foreground = new SolidColorBrush(ok
                ? Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x0F, 0x7B, 0x0F)
                : Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xC4, 0x2B, 0x1C));
            resultText.Text = message;
            ResultSection.Visibility = Visibility.Visible;
            // 完成时再次弹出提示
            Visibility = Visibility.Visible;
            StartAutoDismiss();
        });
    }

    /// <summary>完成态 1.5 秒后自动向上滑出消失(用户手动关闭则定时器停止)。</summary>
    private void StartAutoDismiss()
    {
        _autoCloseTimer?.Stop();
        _autoCloseTimer = DispatcherQueue.CreateTimer();
        _autoCloseTimer.Interval = TimeSpan.FromMilliseconds(1500);
        _autoCloseTimer.IsRepeating = false;
        _autoCloseTimer.Tick += (_, _) => DismissWithSlideUp();
        _autoCloseTimer.Start();
    }

    /// <summary>向上滑动 + 淡出动画, 结束后从根 Grid 移除弹窗。</summary>
    private void DismissWithSlideUp()
    {
        if (Parent is null) return; // 已被用户手动关闭
        try
        {
            if (Content is not FrameworkElement root)
            {
                Close();
                return;
            }

            var translate = new TranslateTransform();
            root.RenderTransform = translate;
            // 顶部 margin 52 + 卡片高度 + 少量余量, 确保完全滑出视野
            var slideOut = root.ActualHeight + 52 + 16;

            var storyboard = new Storyboard();
            var slide = new DoubleAnimation
            {
                To = -slideOut,
                Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(slide, root);
            Storyboard.SetTargetProperty(slide, "(UIElement.RenderTransform).(TranslateTransform.Y)");
            storyboard.Children.Add(slide);

            var fade = new DoubleAnimation
            {
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            };
            Storyboard.SetTarget(fade, this);
            Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard.Children.Add(fade);

            storyboard.Completed += (_, _) => Close();
            storyboard.Begin();
        }
        catch
        {
            Close();
        }
    }

    /// <summary>隐藏按钮: 仅收起弹窗, 下载在后台继续。</summary>
    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
    }

    /// <summary>关闭按钮(结果态): 彻底移除弹窗。</summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
