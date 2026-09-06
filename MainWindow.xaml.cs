using CommunityToolkit.WinUI;
using H.NotifyIcon;
using H.NotifyIcon.EfficiencyMode;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Timers;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.WindowManagement;
using WinUIEx;
using WinUIMusicPlayer.Helper;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Services;
using WinUIMusicPlayer.Taskbar;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.View;
using WinUIMusicPlayer.ViewModel;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUIMusicPlayer
{
    public sealed partial class MainWindow : WindowEx, IDisposable
    {
        public event EventHandler themeChanged;
        public event EventHandler styleChanged;
        public event EventHandler customStyleChanged;
        public event EventHandler<bool> backdropInputState;

        private ThemeStyleHelper themeStyleHelper;
        private UISettings uiSettings;
        private IntPtr defaultWndProc;
        private WindowHelper.WndProcDelegate newWndProcDelegate;
        private TaskbarHelper _taskbarHelper;
        private ILogger<MainWindow> _logger;
        private readonly object _trimLock = new();

        public MainWindow()
        {
            InitializeComponent();
            AppData.HWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            SetWindow();
            AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
            this.SetTitleBarBackgroundColors(Colors.Transparent);
            if (AppWindow.Presenter is OverlappedPresenter overlapped)
            {
                overlapped.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
            }
            _logger = App.GetLogger<MainWindow>();
            this.Activated += MainWindow_Activated;
            themeStyleHelper = new ThemeStyleHelper(this, AppWindow);
            themeStyleHelper.ThemeChanged += (s, e) => themeChanged?.Invoke(this, EventArgs.Empty);
            themeStyleHelper.StyleChanged += (s, e) => styleChanged?.Invoke(this, EventArgs.Empty);
            themeStyleHelper.CustomStyleChanged += (s, e) => customStyleChanged?.Invoke(this, EventArgs.Empty);
            InitializeApp();
            this.AppWindow.Closing += AppWindow_Closing;
            this.AppWindow.Changed += AppWindow_Changed;
            //重复启动显示窗口
            newWndProcDelegate = new WindowHelper.WndProcDelegate(NewWindowProc);
            defaultWndProc = WindowHelper.GetWindowLongPtr(AppData.HWnd, WindowHelper.GWLP_WNDPROC);
            WindowHelper.SetWindowLongPtr(AppData.HWnd, WindowHelper.GWLP_WNDPROC, System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(newWndProcDelegate));
            SaveMainWindowHandle(AppData.HWnd);
            uiSettings = new UISettings();
            uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            InitializeTaskbarHelper();
        }

        // 仅在 OverlappedPresenterState.Restored(普通窗口)时刷新此基准；
        // Maximized / Minimized / FullScreen 等瞬时状态不会覆盖本字段——
        // 退出保存时一律回退到此处，作为下次启动的还原依据。
        // 启动路径 SetWindow() 在还原(MoveAndResizeExact)/CenterOnScreen 之后会立即抓一次，
        // 保证首次退出也能拿到有效基准。
        // 写入前还要通过尺寸合理性校验，防止状态切换瞬间捕获到全屏/最大化尺寸。
        private (int X, int Y, int Width, int Height) _lastRestoredBounds;
        private bool _hasRestoredBounds;
        public (int X, int Y, int Width, int Height) TrackedBounds => _lastRestoredBounds;
        public bool HasTrackedBounds => _hasRestoredBounds;
        public bool IsCurrentlyMaximized => WindowSizeHelper.IsAppWindowMaximized(AppWindow);

        // 窗口尺寸合理范围: 200..10000。多屏 + 高 DPI 也不可能超过此范围。
        // 任何超出该范围的捕获值都被视为污染(如全屏/最大化尺寸), 拒绝写入。
        private const int MinReasonableSize = 200;
        private const int MaxReasonableSize = 10000;

        private void CaptureRestoredBounds()
        {
            if (AppWindow == null) return;
            if (AppWindow.Presenter is not OverlappedPresenter op) return;
            // 仅信任普通窗口的位置/尺寸；其他状态由 _lastRestoredBounds 兜底。
            if (op.State != OverlappedPresenterState.Restored) return;

            var pos = AppWindow.Position;
            var size = AppWindow.Size;

            // 纵深防御: 即使 DidPresenterChange 守卫因未来 API 变更失效,
            // 也不写入超大/超小尺寸。校验失败保留旧值, 不覆盖 _lastRestoredBounds。
            if (size.Width < MinReasonableSize || size.Width > MaxReasonableSize) return;
            if (size.Height < MinReasonableSize || size.Height > MaxReasonableSize) return;

            _lastRestoredBounds = (pos.X, pos.Y, size.Width, size.Height);
            _hasRestoredBounds = true;
        }

        private void AppWindow_Changed(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
        {
            if (AppWindow == null) return;

            // 状态切换瞬间 DidPositionChange/DidSizeChange 会先于 DidPresenterChange 到达,
            // 此时 op.State 仍是旧值(Restored), 但 AppWindow.Position/Size 已经是新状态
            // (Maximized/FullScreen/Minimized)的尺寸. 一旦捕获就会污染 _lastRestoredBounds.
            // 故状态切换期间绝对不捕获 — 根因防御.
            if (args.DidPresenterChange) return;

            if (args.DidPositionChange || args.DidSizeChange)
            {
                // 注意: 窗口进入 FullScreen 时 Presenter 类型会从 OverlappedPresenter 切换到
                // FullScreenPresenter (二者是 AppWindowPresenter 的兄弟类, 不是父子),
                // 此时 `is OverlappedPresenter` 永远为 false 是预期行为 — FullScreen 下
                // _lastRestoredBounds 不应被刷新 (配合尺寸合理性校验 200..10000 双重防御)。
                if (AppWindow.Presenter is OverlappedPresenter op
                    && op.State == OverlappedPresenterState.Restored)
                {
                    CaptureRestoredBounds();
                }
            }
        }

        private void SetWindow()
        {
            this.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico"));
            Title = ToolUtils.GetString("AppMainTitle");

            var ps = App.Services.GetRequiredService<MusicDatabaseService>().CurrentPlayState;
            if (ps != null && ps.HasWindowBounds
                && WindowSizeHelper.IsBoundsOnScreen(ps.WindowX, ps.WindowY, ps.WindowWidth, ps.WindowHeight))
            {
                // 跨 DPI 显示器还原需先 Move 后 Resize,见 WindowSizeHelper.MoveAndResizeExact
                WindowSizeHelper.MoveAndResizeExact(AppWindow, ps.WindowX, ps.WindowY, ps.WindowWidth, ps.WindowHeight);
            }
            else
            {
                this.CenterOnScreen();
            }

            CaptureRestoredBounds();

            if (ps != null && ps.IsMaximized
                && AppWindow.Presenter is OverlappedPresenter overlapped)
            {
                overlapped.Maximize();
            }
        }

        private void SaveMainWindowHandle(IntPtr handle)
        {
            try
            {
                //使用注册表
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\XYMusic\XYMusic"))
                {
                    key.SetValue("MainWindowHandle", handle.ToInt64());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"保存窗口句柄失败: {ex.Message}");
            }
        }
        private void UiSettings_ColorValuesChanged(UISettings sender, object args)
        {
            _ = DispatcherQueue.EnqueueAsync(() =>
            {
                SetAppStyle();
                if (AppSettings.AppTheme == "Default")
                {
                    App.Services.GetRequiredService<MusicBrowseViewModel>().ThemeChangedUpdateCover();
                }
            });
        }
        //显示窗口
        private IntPtr NewWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (msg == SingleInstanceHelper.WM_SHOWME)
                {
                    //Debug.WriteLine("收到显示窗口消息");
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (this is null)
                        {
                            return;
                        }

                        if (!this.Visible)
                        {
                            this.Show();
                            InitializeTaskbarHelper();
                        }
                    });
                    return IntPtr.Zero;
                }
                if (msg == 0x0312) // WM_HOTKEY
                {
                    int id = (int)wParam;
                    Helper.GlobalHotKeyHook.TryInvokeAction(id);
                    return IntPtr.Zero;
                }
                // 调用默认窗口过程处理其他消息
                return WindowHelper.CallWindowProc(defaultWndProc, hWnd, msg, wParam, lParam);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理窗口消息时发生错误");
                return IntPtr.Zero;
            }
        }


        private async void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, AppWindowClosingEventArgs args)
        {
            AppWindow.Changed -= AppWindow_Changed;
            if (AppSettings.IsRunningBackend)
            {
                args.Cancel = true;
                this.Hide();
                if (AppSettings.IsTrimOnHideEnabled)
                    _ = WorkingSetCompressor.TrimSelfAsync();
            }
            else
            {
                await App.Current_Exit();
            }
        }

        public void InitializeApp()
        {
            try
            {
                themeStyleHelper.SetAppStyle();
                themeStyleHelper.SetAppTheme();
                UpdateCustomBackground();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "应用主题初始化失败，可能是因为系统主题设置不受支持。");
            }
        }

        /// <summary>仅切换本次运行的界面主题(不写用户设置): 自定义背景图对比度检测自动切换用。</summary>
        public void ApplyRuntimeTheme(Microsoft.UI.Xaml.ElementTheme theme)
        {
            themeStyleHelper?.ApplyRuntimeTheme(theme);
        }

        /// <summary>背景应用版本号: 拖动滑杆时丢弃过期的异步应用结果。</summary>
        private int _backgroundVersion;

        /// <summary>应用/清除自定义图片背景(设置页选择图片/调节模糊度/启动恢复时调用)。</summary>
        public void UpdateCustomBackground()
        {
            var version = ++_backgroundVersion;
            _ = ApplyCustomBackgroundAsync(version);
        }

        private async Task ApplyCustomBackgroundAsync(int version)
        {
            try
            {
                var path = AppSettings.CustomBackgroundPath;
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    var blur = (int)Math.Round(Math.Clamp(AppSettings.CustomBackgroundBlur, 0, 50));
                    // 模糊度>0 时生成/复用模糊缓存(后台线程, 防卡 UI)
                    if (blur > 0)
                    {
                        var blurred = await Task.Run(() => GetOrCreateBlurredBackground(path, blur));
                        if (version != _backgroundVersion) return; // 期间设置已变更, 丢弃
                        if (blurred is null)
                        {
                            _logger.LogWarning("生成模糊背景失败, 回退原图");
                        }
                        else
                        {
                            path = blurred;
                        }
                    }
                    CustomBackgroundImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path));
                    CustomBackgroundImage.Visibility = Visibility.Visible;
                }
                else
                {
                    CustomBackgroundImage.Source = null;
                    CustomBackgroundImage.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "应用自定义背景失败");
                CustomBackgroundImage.Source = null;
                CustomBackgroundImage.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>生成模糊背景缓存(按模糊度命名, 已存在直接复用)。返回缓存文件路径, 失败返回 null。</summary>
        private static async Task<string?> GetOrCreateBlurredBackground(string sourcePath, int blur)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(sourcePath)!;
                var cache = System.IO.Path.Combine(dir, $"custom_background_blur{blur}.png");
                if (System.IO.File.Exists(cache)) return cache;

                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(sourcePath);
                using var fileStream = await file.OpenReadAsync();
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(fileStream);
                // 超大图缩到最长边 4096 再模糊, 控制内存与耗时
                var transform = new Windows.Graphics.Imaging.BitmapTransform();
                uint srcW = decoder.OrientedPixelWidth, srcH = decoder.OrientedPixelHeight;
                if (Math.Max(srcW, srcH) > 4096)
                {
                    var scale = 4096.0 / Math.Max(srcW, srcH);
                    transform.ScaledWidth = (uint)(srcW * scale);
                    transform.ScaledHeight = (uint)(srcH * scale);
                }
                var width = (int)(transform.ScaledWidth != 0 ? transform.ScaledWidth : srcW);
                var height = (int)(transform.ScaledHeight != 0 ? transform.ScaledHeight : srcH);
                var provider = await decoder.GetPixelDataAsync(
                    Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                    Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                    transform,
                    Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation,
                    Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
                var pixels = provider.DetachPixelData();
                if (pixels is null || width <= 0 || height <= 0 || pixels.Length < width * height * 4) return null;

                BoxBlurBgra(pixels, width, height, blur);

                // 先写临时文件再改名, 避免中途失败留下损坏缓存
                var tmp = cache + ".tmp";
                var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(dir);
                var outFile = await folder.CreateFileAsync(
                    System.IO.Path.GetFileName(tmp), Windows.Storage.CreationCollisionOption.ReplaceExisting);
                using (var outStream = await outFile.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
                {
                    var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
                        Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, outStream);
                    encoder.SetPixelData(
                        Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                        Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                        (uint)width, (uint)height, 96, 96, pixels);
                    await encoder.FlushAsync();
                }
                if (System.IO.File.Exists(cache)) System.IO.File.Delete(cache);
                System.IO.File.Move(tmp, cache);
                return cache;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>BGRA 像素盒式模糊: 水平+垂直滑窗, 3 轮迭代逼近高斯模糊。</summary>
        private static void BoxBlurBgra(byte[] pixels, int width, int height, int radius)
        {
            if (radius <= 0 || width <= 1 || height <= 1) return;
            var temp = new byte[pixels.Length];
            for (var iter = 0; iter < 3; iter++)
            {
                HorizontalBoxBlur(pixels, temp, width, height, radius);
                VerticalBoxBlur(temp, pixels, width, height, radius);
            }
        }

        private static void HorizontalBoxBlur(byte[] src, byte[] dst, int w, int h, int r)
        {
            var window = 2 * r + 1;
            for (var y = 0; y < h; y++)
            {
                var row = y * w * 4;
                int b = 0, g = 0, rr = 0, a = 0;
                for (var i = -r; i <= r; i++)
                {
                    var p = row + Math.Clamp(i, 0, w - 1) * 4;
                    b += src[p]; g += src[p + 1]; rr += src[p + 2]; a += src[p + 3];
                }
                for (var x = 0; x < w; x++)
                {
                    var d = row + x * 4;
                    dst[d] = (byte)(b / window);
                    dst[d + 1] = (byte)(g / window);
                    dst[d + 2] = (byte)(rr / window);
                    dst[d + 3] = (byte)(a / window);
                    var addP = row + Math.Min(x + r + 1, w - 1) * 4;
                    var remP = row + Math.Max(x - r, 0) * 4;
                    b += src[addP] - src[remP];
                    g += src[addP + 1] - src[remP + 1];
                    rr += src[addP + 2] - src[remP + 2];
                    a += src[addP + 3] - src[remP + 3];
                }
            }
        }

        private static void VerticalBoxBlur(byte[] src, byte[] dst, int w, int h, int r)
        {
            var window = 2 * r + 1;
            var stride = w * 4;
            for (var x = 0; x < w; x++)
            {
                var col = x * 4;
                int b = 0, g = 0, rr = 0, a = 0;
                for (var i = -r; i <= r; i++)
                {
                    var p = col + Math.Clamp(i, 0, h - 1) * stride;
                    b += src[p]; g += src[p + 1]; rr += src[p + 2]; a += src[p + 3];
                }
                for (var y = 0; y < h; y++)
                {
                    var d = col + y * stride;
                    dst[d] = (byte)(b / window);
                    dst[d + 1] = (byte)(g / window);
                    dst[d + 2] = (byte)(rr / window);
                    dst[d + 3] = (byte)(a / window);
                    var addP = col + Math.Min(y + r + 1, h - 1) * stride;
                    var remP = col + Math.Max(y - r, 0) * stride;
                    b += src[addP] - src[remP];
                    g += src[addP + 1] - src[remP + 1];
                    rr += src[addP + 2] - src[remP + 2];
                    a += src[addP + 3] - src[remP + 3];
                }
            }
        }

        public void ShowMainPage()
        {
            ShellFrame.Content = App.Services.GetRequiredService<MainPage>();
            LoadingGrid.Visibility = Visibility.Collapsed;
        }

        public void UpdateTaskbarIcon()
        {
            _taskbarHelper?.UpdateTaskbarButtonIcon();
        }

        public void SetAppStyle()
        {
            themeStyleHelper.SetAppStyle();
        }

        public void SetCustomAppStyle()
        {
            themeStyleHelper.ChangeCustomAcrylicStyle();
        }

        public void SetAppTheme()
        {
            themeStyleHelper.SetAppTheme();
        }

        public void UpdateBackdropActiveState(bool isActive)
        {
            themeStyleHelper.UpdateBackdropActiveState(isActive);
            backdropInputState?.Invoke(this, isActive);
        }



        public void InitializeTaskbarHelper()
        {
            try
            {
                if (_taskbarHelper is null)
                {
                    _taskbarHelper = new TaskbarHelper(AppData.HWnd, App.Services.GetRequiredService<MusicBrowseViewModel>());
                    _taskbarHelper.ErrorOccurred += (_, e) =>
                    {
                        _logger.LogError(e.Exception, "任务栏助手发生错误");
                    };
                    _taskbarHelper.InitializeThumbButtons();
                }
                else
                {
                    _taskbarHelper.RecoverTaskbarHelper();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化任务栏助手失败");
            }
        }
        public void ToggleShowHide()
        {
            if (this.Visible)
            {
                this.Hide();
                if (AppSettings.IsTrimOnHideEnabled)
                    _ = WorkingSetCompressor.TrimSelfAsync();
            }
            else
            {
                this.Show();
                InitializeTaskbarHelper();
                this.Activate();
                WindowHelper.SetForegroundWindow(AppData.HWnd);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool dispose)
        {
            if (dispose)
            {
                AppNotifyIconControl?.Dispose();
                _taskbarHelper?.Dispose();
            }
        }
    }
}
