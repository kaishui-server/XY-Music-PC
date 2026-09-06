using System;
using System.Linq;
using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace WinUIMusicPlayer.View.SubView;

/// <summary>提示级别: 决定卡片半透明背景与文字颜色(白/绿/黄/红)。</summary>
public enum ToastSeverity
{
    /// <summary>普通提示(白色半透明)。</summary>
    Info,

    /// <summary>成功(绿色半透明)。</summary>
    Success,

    /// <summary>警告(黄色半透明)。</summary>
    Warning,

    /// <summary>失败/错误(红色半透明)。</summary>
    Error,
}

/// <summary>
/// 通用顶部提示小卡片: 顶部居中, 挂载到 MainWindow 根 Grid(叠放不推挤布局, 不阻挡其他区域点击)。
/// 背景用 Composition BackdropBrush 实时模糊背后内容, 与下载进度弹窗同款视觉;
/// 按级别叠加白/绿/黄/红半透明 tint。1.8 秒后自动向上滑出消失。
/// </summary>
public sealed partial class ToastFlyout : UserControl
{
    private SpriteVisual? _blurSprite;
    private DispatcherQueueTimer? _autoCloseTimer;
    private Vector2 _lastClipSize;

    /// <summary>显示一条普通提示(白色)。</summary>
    public static void Show(string message) => ShowCore(message, ToastSeverity.Info);

    /// <summary>显示一条普通提示(白色)。</summary>
    public static void ShowInfo(string message) => ShowCore(message, ToastSeverity.Info);

    /// <summary>显示一条成功提示(绿色)。</summary>
    public static void ShowSuccess(string message) => ShowCore(message, ToastSeverity.Success);

    /// <summary>显示一条警告提示(黄色)。</summary>
    public static void ShowWarning(string message) => ShowCore(message, ToastSeverity.Warning);

    /// <summary>显示一条失败/错误提示(红色)。</summary>
    public static void ShowError(string message) => ShowCore(message, ToastSeverity.Error);

    /// <summary>线程安全入口: UI 线程直接显示, 否则经 DispatcherQueue 派发。</summary>
    private static void ShowCore(string message, ToastSeverity severity)
    {
        var dq = App.MainWindow?.DispatcherQueue;
        if (dq is null) return;
        if (dq.HasThreadAccess) ShowOnUi(message, severity);
        else dq.TryEnqueue(() => ShowOnUi(message, severity));
    }

    /// <summary>显示一条提示(同一时间仅保留最新一条)。</summary>
    private static void ShowOnUi(string message, ToastSeverity severity)
    {
        if (App.MainWindow?.RootGrid is not { } root) return;
        foreach (var existing in root.Children.OfType<ToastFlyout>().ToList())
        {
            existing.Close();
        }
        var toast = new ToastFlyout(message, severity);
        root.Children.Add(toast);
        toast.Visibility = Visibility.Visible;
    }

    public ToastFlyout(string message, ToastSeverity severity = ToastSeverity.Info)
    {
        InitializeComponent();
        messageText.Text = message;
        ApplySeverity(severity);
        Loaded += (_, _) => SetupBackdropBlur();
        // Collapsed 状态下 Loaded, 布局就绪后靠 LayoutUpdated 持续同步模糊层圆角裁剪
        LayoutUpdated += (_, _) => UpdateBlurSpriteClip();
        StartAutoDismiss();
    }

    /// <summary>按级别着色: 半透明 tint + 同色系边框 + 深色正文, 明暗主题下均可读。</summary>
    private void ApplySeverity(ToastSeverity severity)
    {
        var (bg, border, text) = severity switch
        {
            ToastSeverity.Success => (
                Windows.UI.Color.FromArgb(0xB3, 0x66, 0xBB, 0x6A),
                Windows.UI.Color.FromArgb(0x8C, 0x43, 0xA0, 0x47),
                Windows.UI.Color.FromArgb(0xE5, 0x0B, 0x29, 0x10)),
            ToastSeverity.Warning => (
                Windows.UI.Color.FromArgb(0xB3, 0xFF, 0xD5, 0x4F),
                Windows.UI.Color.FromArgb(0x8C, 0xFF, 0xA0, 0x00),
                Windows.UI.Color.FromArgb(0xE5, 0x4E, 0x3B, 0x00)),
            ToastSeverity.Error => (
                Windows.UI.Color.FromArgb(0xB3, 0xEF, 0x53, 0x50),
                Windows.UI.Color.FromArgb(0x8C, 0xD3, 0x2F, 0x2F),
                Windows.UI.Color.FromArgb(0xE5, 0x4E, 0x0B, 0x0B)),
            _ => (
                Windows.UI.Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF),
                Windows.UI.Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF),
                Windows.UI.Color.FromArgb(0xE5, 0x20, 0x20, 0x20)),
        };
        tintBorder.Background = new SolidColorBrush(bg);
        cardBorder.BorderBrush = new SolidColorBrush(border);
        messageText.Foreground = new SolidColorBrush(text);
    }

    /// <summary>注入背景模糊 visual(Composition BackdropBrush + 高斯模糊)。</summary>
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

    /// <summary>彻底关闭并从根 Grid 移除卡片。</summary>
    public void Close()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _autoCloseTimer?.Stop();
            if (Parent is Panel panel) panel.Children.Remove(this);
        });
    }

    /// <summary>1.8 秒后自动向上滑出消失。</summary>
    private void StartAutoDismiss()
    {
        _autoCloseTimer?.Stop();
        _autoCloseTimer = DispatcherQueue.CreateTimer();
        _autoCloseTimer.Interval = TimeSpan.FromMilliseconds(1800);
        _autoCloseTimer.IsRepeating = false;
        _autoCloseTimer.Tick += (_, _) => DismissWithSlideUp();
        _autoCloseTimer.Start();
    }

    /// <summary>向上滑动 + 淡出动画, 结束后从根 Grid 移除卡片。</summary>
    private void DismissWithSlideUp()
    {
        if (Parent is null) return; // 已被移除
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
}
