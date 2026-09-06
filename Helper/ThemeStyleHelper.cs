using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using Windows.UI;
using WinUIEx;
using WinUIMusicPlayer.Model;

namespace WinUIMusicPlayer.Helper
{
    public class ThemeStyleHelper
    {
        public event EventHandler ThemeChanged;
        public event EventHandler StyleChanged;
        public event EventHandler CustomStyleChanged;

        private Window _window;
        private AppWindow _appWindow;
        private CustomAcrylicSystemBackdrop _acrylicSystemBackdrop;
        private CustomMicaSystemBackdrop _micaSystemBackdrop;
        private TransparentTintBackdrop _transparentTintBackdrop;

        public ThemeStyleHelper(Window window, AppWindow appWindow)
        {
            _window = window;
            _appWindow = appWindow;
            _acrylicSystemBackdrop = new CustomAcrylicSystemBackdrop(window)
            {
                IsInputActive = AppSettings.IsUpdateBackDrop
            };
            _micaSystemBackdrop = new CustomMicaSystemBackdrop(window)
            {
                IsInputActive = AppSettings.IsUpdateBackDrop
            };
            _transparentTintBackdrop = new TransparentTintBackdrop(Colors.Transparent);

        }

        private static Color GetUiColor()
        {
            var isDarkTheme = AppSettings.AppTheme switch
            {
                "Dark" => true,
                "Light" => false,
                "Default" => Application.Current.RequestedTheme == ApplicationTheme.Dark,
                _ => true
            };
            return isDarkTheme
                ? Color.FromArgb(255, 32, 32, 32)
                : Color.FromArgb(255, 255, 255, 255);
        }

        public void SetAppStyle()
        {
            try
            {

                var backdrop = _window.SystemBackdrop as CustomAcrylicSystemBackdrop;
                switch (AppSettings.AppStyle)
                {
                    case "Acrylic":
                        if (backdrop is not null)
                        {
                            backdrop.UpdateProperties(0.5f, 0.8f, GetUiColor());
                        }
                        else
                        {
                            _acrylicSystemBackdrop.TintOpacity = 0.5f;
                            _acrylicSystemBackdrop.LuminosityOpacity = 0.8f;
                            _acrylicSystemBackdrop.TintColor = GetUiColor();
                            _window.SystemBackdrop = _acrylicSystemBackdrop;
                        }
                        break;
                    case "TransparentAcrylic":
                        if (backdrop is not null)
                        {
                            backdrop.UpdateProperties(0, 0.4f, GetUiColor());
                        }
                        else
                        {
                            _acrylicSystemBackdrop.TintOpacity = 0;
                            _acrylicSystemBackdrop.LuminosityOpacity = 0.4f;
                            _acrylicSystemBackdrop.TintColor = GetUiColor();
                            _window.SystemBackdrop = _acrylicSystemBackdrop;
                        }
                        break;
                    case "Mica":
                        if (_window.SystemBackdrop is not CustomMicaSystemBackdrop)
                        {
                            _window.SystemBackdrop = _micaSystemBackdrop;
                        }
                        break;
                    case "TransparentTint":
                        if (_window.SystemBackdrop is not TransparentTintBackdrop)
                        {
                            _window.SystemBackdrop = _transparentTintBackdrop;
                        }
                        break;
                    case "CustomAcrylicStyle":
                        if (backdrop is not null)
                        {
                            backdrop.UpdateProperties(1.0,
                                            AppSettings.CustomAcrylicOpacity,
                                            Color.FromArgb((byte)(AppSettings.CustomColorArgb >> 24),
                                                (byte)(AppSettings.CustomColorArgb >> 16),
                                                (byte)(AppSettings.CustomColorArgb >> 8),
                                                (byte)AppSettings.CustomColorArgb));
                        }
                        else
                        {
                            _acrylicSystemBackdrop.TintOpacity = 1.0;
                            _acrylicSystemBackdrop.LuminosityOpacity = AppSettings.CustomAcrylicOpacity;
                            _acrylicSystemBackdrop.TintColor = Color.FromArgb((byte)(AppSettings.CustomColorArgb >> 24),
                                                        (byte)(AppSettings.CustomColorArgb >> 16),
                                                        (byte)(AppSettings.CustomColorArgb >> 8),
                                                        (byte)AppSettings.CustomColorArgb);
                            _window.SystemBackdrop = _acrylicSystemBackdrop;
                        }
                        break;
                    default:
                        _acrylicSystemBackdrop.TintOpacity = 0.5f;
                        _acrylicSystemBackdrop.LuminosityOpacity = 0.8f;
                        _acrylicSystemBackdrop.TintColor = GetUiColor();
                        _window.SystemBackdrop = _acrylicSystemBackdrop;
                        break;
                }
                StyleChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetAppStyle error: {ex.Message}");
            }
        }

        public void ChangeCustomAcrylicStyle()
        {
            if (AppSettings.AppStyle == "CustomAcrylicStyle")
            {
                if (_window.SystemBackdrop is CustomAcrylicSystemBackdrop backdrop)
                {
                    backdrop.UpdateProperties(1.0,
                        AppSettings.CustomAcrylicOpacity,
                        Color.FromArgb((byte)(AppSettings.CustomColorArgb >> 24),
                            (byte)(AppSettings.CustomColorArgb >> 16),
                            (byte)(AppSettings.CustomColorArgb >> 8),
                            (byte)AppSettings.CustomColorArgb));
                    CustomStyleChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public void UpdateBackdropActiveState(bool IsActive)
        {
            _acrylicSystemBackdrop.IsInputActive = IsActive;
            _micaSystemBackdrop.IsInputActive = IsActive;
        }

        public void SetAppTheme()
        {
            try
            {
                // 持久化入口: 按用户设置的 AppTheme 应用主题
                ApplyThemeCore(AppSettings.AppTheme switch
                {
                    "Dark" => ElementTheme.Dark,
                    "Light" => ElementTheme.Light,
                    _ => ElementTheme.Default,
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetAppTheme error: {ex.Message}");
            }
        }

        /// <summary>仅应用运行时主题(不读写 AppSettings.AppTheme):
        /// 供自定义背景图对比度检测自动切换使用——切换只对本次运行生效,
        /// 用户保存的主题设置不被覆盖, 手动改主题/重启后仍以用户设置优先。</summary>
        public void ApplyRuntimeTheme(ElementTheme theme)
        {
            try
            {
                ApplyThemeCore(theme);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ApplyRuntimeTheme error: {ex.Message}");
            }
        }

        private void ApplyThemeCore(ElementTheme theme)
        {
            try
            {
                AppWindowTitleBar titleBar = _appWindow.TitleBar;
                if (_window.Content is FrameworkElement rootElement)
                {
                    switch (theme)
                    {
                        case ElementTheme.Dark:
                            rootElement.RequestedTheme = ElementTheme.Dark;
                            AppSettings.ElementTheme = ElementTheme.Dark;
                            titleBar.ButtonForegroundColor = Colors.White;
                            titleBar.ButtonHoverForegroundColor = Colors.White;
                            titleBar.ButtonPressedForegroundColor = Colors.White;
                            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 50, 50, 50);
                            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 80, 80, 80);
                            break;
                        case ElementTheme.Light:
                            rootElement.RequestedTheme = ElementTheme.Light;
                            AppSettings.ElementTheme = ElementTheme.Light;
                            titleBar.ButtonForegroundColor = Colors.Black;
                            titleBar.ButtonHoverForegroundColor = Colors.Black;
                            titleBar.ButtonPressedForegroundColor = Colors.Black;
                            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 220, 220, 220);
                            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 190, 190, 190);
                            break;
                        default:
                            titleBar.ButtonForegroundColor = null;
                            titleBar.ButtonHoverForegroundColor = null;
                            titleBar.ButtonPressedForegroundColor = null;
                            titleBar.ButtonHoverBackgroundColor = null;
                            titleBar.ButtonPressedBackgroundColor = null;
                            rootElement.RequestedTheme = ElementTheme.Default;
                            AppSettings.ElementTheme = ElementTheme.Default;
                            break;
                    }
                    ThemeChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ApplyThemeCore error: {ex.Message}");
            }
        }
    }
}
