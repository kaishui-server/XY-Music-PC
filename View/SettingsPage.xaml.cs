using DevWinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.System;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Services;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.ViewModel;
using WinUIMusicPlayer.View.SubView;
using Microsoft.Windows.Storage.Pickers;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUIMusicPlayer.View
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        private ContentDialog? _thirdPartyDialog;
        private bool _isImportingBackup;
        public SettingsViewModel ViewModel { get; }
        public SettingsPage()
        {
            InitializeComponent();
            ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
            DataContext = this;
            NavigationCacheMode = NavigationCacheMode.Disabled;
            Unloaded += OnUnloaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LoadOutputDevices();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Unloaded -= OnUnloaded;
            if (AutoScrollViewControl is not null)
            {
                AutoScrollViewControl.PointerEntered -= AutoScrollHover_PointerEntered;
                AutoScrollViewControl.PointerExited -= AutoScrollHover_PointerExited;
                AutoScrollViewControl.PointerCanceled -= AutoScrollHover_PointerCanceled;
            }
            _thirdPartyDialog = null;
        }

        private void LoadOutputDevices()
        {
            ViewModel.AppViewModel.IsRealDevceChange = false;
            _ = ViewModel.AppViewModel.GetWasapiDeviceAsync();
        }

        private void ThirdParty_Click(object sender, RoutedEventArgs e)
        {
            if (_thirdPartyDialog is null)
            {
                _thirdPartyDialog = new ContentDialog
                {
                    Title = ToolUtils.GetString("ThirdPartyComponentsText"),
                    Content = new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = new TextBlock
                        {
                            TextWrapping = TextWrapping.Wrap,
                            Text = "Description of Third-Party Component Dependencies\r\nThe Software uses the following third-party component packages, and the copyright information and usage of them are explained as follows:\r\n\r\n## Audio Processing Related\r\n- Un4seen.Bass (2.4.16): A high-performance cross-platform audio library that provides core capabilities such as audio playback, recording, streaming, and real-time effects processing. Copyright belongs to un4seen developments and is subject to the Bass License.\r\n- ManagedBass (3.1.0): A .NET wrapper for the Un4seen.Bass library, enabling .NET applications to access and utilize the full functionality of the Bass library through managed code, simplifying integration in .NET development environments. Copyright belongs to the ManagedBass developers and is subject to the MIT License.\r\n\r\n## MVVM Framework and Tools\r\n- CommunityToolkit.Mvvm (8.4.0): The MVVM implementation of the Microsoft Community Toolkit, providing essential MVVM development functions such as property notification and command pattern. Copyright belongs to Microsoft and is subject to the MIT License.\r\n\r\n## UI and System Integration\r\n- H.NotifyIcon.WinUI (2.3.0): A system tray icon component under the WinUI platform, supporting custom tray menus and interactions. Copyright belongs to Hans-Peter Grahsl and is subject to the MIT License.\r\n- Microsoft.Graphics.Win2D (1.3.2): A high-performance 2D graphics rendering library used for graphics drawing in WinUI applications. Copyright belongs to Microsoft and is subject to the MIT License.\r\n- Microsoft.WindowsAppSDK (1.7.250401001): A set of basic functions provided by the Windows App SDK, supporting WinUI 3 application development. Copyright belongs to Microsoft and is subject to the MIT License.\r\n\r\n## Basic Framework and Services\r\n- Microsoft.Extensions.Hosting (9.0.6): .NET general host framework for building extensible applications. Copyright belongs to Microsoft and is subject to the MIT License.\r\n- Microsoft.Extensions.Hosting.Abstractions (9.0.6): Abstract interface definition of the .NET host framework. Copyright belongs to Microsoft and is subject to the MIT License.\r\n- Microsoft.Windows.Compatibility (7.0.3): Provides compatibility packaging of Windows platform-specific APIs. Copyright belongs to Microsoft and is subject to the MIT License.\r\n- Microsoft.Windows.SDK.BuildTools (10.0.26100.1742): Windows SDK build tools, providing basic components required for Windows platform development. Copyright belongs to Microsoft and is subject to the MIT License.\r\n\r\n## Data Storage and Processing\r\n- sqlite-net-pcl (1.9.172): .NET packaging of SQLite database, providing lightweight local data storage functions. Copyright belongs to Frank A. Krueger and is subject to the MIT License.\r\n- System.Data.SqlClient (4.9.0): Microsoft SQL Server database client, providing data interaction functions with SQL Server. Copyright belongs to Microsoft and is subject to the MIT License.\r\n\r\n## Logging and Diagnostics\r\n- Serilog (4.3.0): A powerful logging framework that supports structured logging. Copyright belongs to the Serilog team and is subject to the Apache-2.0 License.\r\n- Serilog.Extensions.Logging (9.0.2): Integration package of Serilog and Microsoft.Extensions.Logging. Copyright belongs to the Serilog team and is subject to the Apache-2.0 License.\r\n- Serilog.Sinks.File (7.0.0): File log output plugin for Serilog. Copyright belongs to the Serilog team and is subject to the Apache-2.0 License.\r\n\r\n## Other Functions\r\n- Microsoft.PinYinConverter (1.0.0): Chinese character pinyin conversion library, used for converting Chinese characters to pinyin. Copyright belongs to Microsoft and is subject to the MIT License.\r\n- System.Formats.Asn1 (8.0.1): ASN.1 format data encoding and decoding library. Copyright belongs to Microsoft and is subject to the MIT License.\r\n- System.Security.Cryptography.Pkcs (7.0.3): PKCS standard encryption format processing library. Copyright belongs to Microsoft and is subject to the MIT License.\r\n\r\n## License Description\r\nAll the above third-party components are subject to their respective open-source licenses, and you can view the complete license text in their official repositories. The Software respects the copyright of all third-party components and uses these components in strict accordance with the requirements of relevant licenses."
                        }
                    },
                    CloseButtonText = ToolUtils.GetString("CloseButton"),
                    XamlRoot = this.XamlRoot
                };
                _thirdPartyDialog.RequestedTheme = AppSettings.ElementTheme;
            }
            _thirdPartyDialog?.ShowAsync();
        }

        private void SpectrumVisualization_Click(object sender, RoutedEventArgs e)
        {
            string storeUri = "spectrumvisualization:";
            LauncherOptions options = new LauncherOptions
            {
                FallbackUri = new Uri("ms-windows-store://pdp/?ProductId=9PL2DSHJ79W")
            };
            _ = Launcher.LaunchUriAsync(new Uri(storeUri), options);
        }

        private async void ImportMobileBackup_Click(object sender, RoutedEventArgs e)
        {
            if (_isImportingBackup) return;
            var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(App.MainWindow.AppWindow.Id);
            picker.FileTypeFilter.Add(".json");
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            string backupJson;
            try
            {
                backupJson = await File.ReadAllTextAsync(file.Path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                await ShowMobileBackupResultAsync(ToolUtils.GetString("MobileBackupInvalidFile"), ex.Message);
                return;
            }

            _isImportingBackup = true;
            var progressDialog = new ContentDialog
            {
                Title = ToolUtils.GetString("MobileBackupImporting"),
                Content = new Microsoft.UI.Xaml.Controls.ProgressRing { IsActive = true },
                XamlRoot = this.XamlRoot
            };
            progressDialog.RequestedTheme = AppSettings.ElementTheme;
            _ = progressDialog.ShowAsync();
            MobileBackupImportResult result;
            try
            {
                result = await new MobileBackupImportService().ImportAsync(backupJson);
            }
            catch (Exception ex)
            {
                result = new MobileBackupImportResult { Ok = false, Error = ex.Message };
            }
            finally
            {
                _isImportingBackup = false;
                progressDialog.Hide();
            }

            if (result.Ok)
            {
                // 收藏/歌单导入后刷新本地收藏集合与各视图
                ViewModel.AppViewModel.RefreshAllViews();
            }
            await ShowMobileBackupResultAsync(
                result.Ok ? ToolUtils.GetString("MobileBackupImportDone") : ToolUtils.GetString("MobileBackupInvalidFile"),
                result.Ok ? BuildMobileBackupSummary(result) : result.Error);
        }

        private static string BuildMobileBackupSummary(MobileBackupImportResult r)
        {
            var sb = new StringBuilder();
            sb.Append(ToolUtils.GetString("MobileBackupLblPlaylists")).Append(": ")
              .Append(r.CreatedPlaylists + r.ReusedPlaylists).AppendLine();
            sb.Append(ToolUtils.GetString("MobileBackupLblSongs")).Append(": ")
              .Append(r.ImportedOnlineSongs + r.ImportedLocalSongs)
              .Append(" (").Append(ToolUtils.GetString("MobileBackupLblOnline")).Append(' ').Append(r.ImportedOnlineSongs)
              .Append(", ").Append(ToolUtils.GetString("MobileBackupLblLocal")).Append(' ').Append(r.ImportedLocalSongs)
              .Append(", ").Append(ToolUtils.GetString("MobileBackupLblSkipped")).Append(' ').Append(r.SkippedSongs)
              .Append(')').AppendLine();
            sb.Append(ToolUtils.GetString("MobileBackupLblFavorites")).Append(": ")
              .Append(r.LocalFavorites + r.OnlineFavorites)
              .Append(" (").Append(ToolUtils.GetString("MobileBackupLblLocal")).Append(' ').Append(r.LocalFavorites)
              .Append(", ").Append(ToolUtils.GetString("MobileBackupLblOnline")).Append(' ').Append(r.OnlineFavorites)
              .Append(')').AppendLine();
            sb.Append(ToolUtils.GetString("MobileBackupLblPlugins")).Append(": ")
              .Append(r.InstalledPlugins)
              .Append(", ").Append(ToolUtils.GetString("MobileBackupLblFailed")).Append(' ').Append(r.FailedPlugins)
              .AppendLine();
            sb.Append(ToolUtils.GetString("MobileBackupLblUserVars")).Append(": ")
              .Append(ToolUtils.GetString("MobileBackupLblApplied")).Append(' ').Append(r.AppliedUserVarPlugins);
            foreach (var err in r.PluginErrors)
                sb.AppendLine().Append(err);
            return sb.ToString();
        }

        private async Task ShowMobileBackupResultAsync(string title, string? message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new ScrollViewer
                {
                    MaxHeight = 400,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 13,
                        Text = message ?? string.Empty
                    }
                },
                CloseButtonText = ToolUtils.GetString("CloseButton"),
                XamlRoot = this.XamlRoot
            };
            dialog.RequestedTheme = AppSettings.ElementTheme;
            await dialog.ShowAsync();
        }

        private bool _isExportingBackup;

        private async void ExportMobileBackup_Click(object sender, RoutedEventArgs e)
        {
            if (_isExportingBackup) return;
            var picker = new FileSavePicker(App.MainWindow.AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeChoices.Add("JSON", new[] { ".json" });
            var now = DateTime.Now;
            picker.SuggestedFileName = $"xy_music_backup_{now:yyyyMMdd}_{now:HHmmss}";
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            _isExportingBackup = true;
            var progressDialog = new ContentDialog
            {
                Title = ToolUtils.GetString("MobileBackupExporting"),
                Content = new Microsoft.UI.Xaml.Controls.ProgressRing { IsActive = true },
                XamlRoot = this.XamlRoot
            };
            progressDialog.RequestedTheme = AppSettings.ElementTheme;
            _ = progressDialog.ShowAsync();
            MobileBackupExportResult result;
            try
            {
                result = await new MobileBackupExportService().ExportAsync(file.Path);
            }
            catch (Exception ex)
            {
                result = new MobileBackupExportResult { Ok = false, Error = ex.Message };
            }
            finally
            {
                _isExportingBackup = false;
                progressDialog.Hide();
            }
            await ShowMobileBackupResultAsync(
                result.Ok ? ToolUtils.GetString("MobileBackupExportDone") : ToolUtils.GetString("MobileBackupExportFailed"),
                result.Ok ? BuildMobileBackupExportSummary(result) : result.Error);
        }

        private static string BuildMobileBackupExportSummary(MobileBackupExportResult r)
        {
            var sb = new StringBuilder();
            sb.Append(ToolUtils.GetString("MobileBackupLblPlaylists")).Append(": ")
              .Append(r.Playlists).AppendLine();
            sb.Append(ToolUtils.GetString("MobileBackupLblSongs")).Append(": ")
              .Append(r.OnlineSongs + r.LocalSongs)
              .Append(" (").Append(ToolUtils.GetString("MobileBackupLblOnline")).Append(' ').Append(r.OnlineSongs)
              .Append(", ").Append(ToolUtils.GetString("MobileBackupLblLocal")).Append(' ').Append(r.LocalSongs)
              .Append(", ").Append(ToolUtils.GetString("MobileBackupLblSkipped")).Append(' ').Append(r.SkippedSongs)
              .Append(')').AppendLine();
            sb.Append(ToolUtils.GetString("MobileBackupLblFavorites")).Append(": ")
              .Append(r.OnlineFavorites + r.LocalFavorites)
              .Append(" (").Append(ToolUtils.GetString("MobileBackupLblOnline")).Append(' ').Append(r.OnlineFavorites)
              .Append(", ").Append(ToolUtils.GetString("MobileBackupLblLocal")).Append(' ').Append(r.LocalFavorites)
              .Append(')').AppendLine();
            sb.Append(ToolUtils.GetString("MobileBackupLblPlugins")).Append(": ")
              .Append(r.Plugins).AppendLine();
            sb.Append(ToolUtils.GetString("MobileBackupLblUserVars")).Append(": ")
              .Append(ToolUtils.GetString("MobileBackupLblApplied")).Append(' ').Append(r.UserVarPlugins);
            return sb.ToString();
        }

        private async void ExportCrashLogs_Click(object sender, RoutedEventArgs e) =>
            await ExportLogsAsync(LogExportKind.Crash);

        private async void ExportErrorLogs_Click(object sender, RoutedEventArgs e) =>
            await ExportLogsAsync(LogExportKind.Error);

        private async void ExportAllLogs_Click(object sender, RoutedEventArgs e) =>
            await ExportLogsAsync(LogExportKind.All);

        private async Task ExportLogsAsync(LogExportKind kind)
        {
            var kindName = kind switch
            {
                LogExportKind.Crash => ToolUtils.GetString("LogExportCrashTitle"),
                LogExportKind.Error => ToolUtils.GetString("LogExportErrorTitle"),
                _ => ToolUtils.GetString("LogExportAllTitle")
            };
            var picker = new FileSavePicker(App.MainWindow.AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeChoices.Add("Log", new[] { ".log" });
            picker.SuggestedFileName = $"XYMusic-{kind.ToString().ToLowerInvariant()}-log-{DateTime.Now:yyyyMMdd-HHmmss}";
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            var (ok, count, error) = LogManagementService.Export(kind, file.Path);
            if (!ok)
            {
                ToastFlyout.ShowError(error ?? ToolUtils.GetString("Error"));
            }
            else if (count == 0)
            {
                ToastFlyout.ShowInfo(ToolUtils.GetString("LogExportEmpty"));
            }
            else
            {
                ToastFlyout.ShowSuccess(string.Format(
                    ToolUtils.GetString("LogExportDoneFormat"), count, kindName));
            }
        }

        private async void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = ToolUtils.GetString("LogClearConfirmTitle"),
                Content = new TextBlock
                {
                    Text = ToolUtils.GetString("LogClearConfirmContent"),
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = ToolUtils.GetString("LogClearButton"),
                CloseButtonText = ToolUtils.GetString("TextCancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            dialog.RequestedTheme = AppSettings.ElementTheme;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            var deleted = LogManagementService.ClearAllLogs();
            ToastFlyout.ShowSuccess(string.Format(
                ToolUtils.GetString("LogClearDoneFormat"), deleted));
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
