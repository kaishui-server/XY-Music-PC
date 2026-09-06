using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WinUIMusicPlayer.Services.Plugins;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.ViewModel.Pages;
using WinUIMusicPlayer.View.SubView;

namespace WinUIMusicPlayer.View
{
    /// <summary>插件管理页面: 安装/启用/卸载 MusicFree 格式插件。</summary>
    public sealed partial class PluginManagePage : Page
    {
        public PluginManageViewModel ViewModel { get; }

        public PluginManagePage()
        {
            ViewModel = App.Services.GetRequiredService<PluginManageViewModel>();
            InitializeComponent();
            DataContext = this;
            NavigationCacheMode = NavigationCacheMode.Disabled;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ViewModel.RefreshList();
        }

        private void PluginManagePage_Loaded(object sender, RoutedEventArgs e)
        {
            ViewModel.RefreshList();
        }

        /// <summary>拖拽排序完成: 把当前列表顺序写回插件清单(同时决定在线搜索 Tab 顺序)。</summary>
        private void PluginList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            if (args.Items.Count == 0) return;
            ViewModel.CommitReorder();
        }

        private void PluginToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle && toggle.DataContext is InstalledPluginItem item)
            {
                // OneWay 绑定初始化(IsOn←Enabled)触发 Toggled 时两者相等, 跳过, 仅响应用户操作
                if (toggle.IsOn == item.Enabled) return;
                item.Enabled = toggle.IsOn;
                _ = ViewModel.ToggleEnabledCommand.ExecuteAsync(item);
            }
        }

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: InstalledPluginItem item })
            {
                _ = ShowUninstallConfirmAsync(item);
            }
        }

        private async Task ShowUninstallConfirmAsync(InstalledPluginItem item)
        {
            var dialog = new ContentDialog
            {
                Title = ToolUtils.GetString("PluginUninstallTitle"),
                Content = string.Format(ToolUtils.GetString("PluginUninstallConfirm"), item.Platform),
                PrimaryButtonText = ToolUtils.GetString("PluginUninstallTitle"),
                CloseButtonText = ToolUtils.GetString("TextCancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _ = ViewModel.UninstallCommand.ExecuteAsync(item);
            }
        }

        private async void InstallFromUrl_Click(object sender, RoutedEventArgs e)
        {
            var textBox = new TextBox
            {
                PlaceholderText = "https://.../plugin.js",
                Height = 36,
            };
            var dialog = new ContentDialog
            {
                Title = ToolUtils.GetString("PluginInstallFromUrlTitle"),
                Content = textBox,
                PrimaryButtonText = ToolUtils.GetString("PluginInstall"),
                CloseButtonText = ToolUtils.GetString("TextCancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.InstallFromUrlOrTextAsync(textBox.Text);
            }
        }

        private void PluginInfo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: InstalledPluginItem item })
            {
                _ = ShowPluginInfoAsync(item);
            }
        }

        private async Task ShowPluginInfoAsync(InstalledPluginItem item)
        {
            var panel = new StackPanel { Spacing = 8, MinWidth = 420 };
            void AddRow(string label, string value)
            {
                var labelBlock = new TextBlock
                {
                    Text = label,
                    Foreground = App.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
                    VerticalAlignment = VerticalAlignment.Top,
                };
                var valueBlock = new TextBlock
                {
                    Text = string.IsNullOrEmpty(value) ? "-" : value,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                };
                Grid.SetColumn(valueBlock, 1);
                var row = new Grid
                {
                    ColumnDefinitions =
                    {
                        new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = new GridLength(110) },
                        new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    },
                };
                row.Children.Add(labelBlock);
                row.Children.Add(valueBlock);
                panel.Children.Add(row);
            }

            AddRow(ToolUtils.GetString("PluginInfoPlatform"), item.Platform);
            AddRow(ToolUtils.GetString("PluginInfoVersion"), item.Version);
            AddRow(ToolUtils.GetString("PluginInfoAuthor"), item.Author);
            AddRow(ToolUtils.GetString("PluginInfoSource"), item.SrcUrl);
            AddRow(ToolUtils.GetString("PluginInfoFile"), item.FileName);
            AddRow(ToolUtils.GetString("PluginInfoMethods"), item.MethodsText);
            AddRow(ToolUtils.GetString("PluginInfoSearchTypes"), item.SearchTypesText);
            AddRow(ToolUtils.GetString("PluginInfoUserVariables"), item.HasUserVariables
                ? string.Join(", ", item.UserVariables!.Select(v => string.IsNullOrEmpty(v.Name) ? v.Key : v.Name))
                : "-");

            var dialog = new ContentDialog
            {
                Title = ToolUtils.GetString("PluginInfoTitle"),
                Content = new ScrollViewer { Content = panel, MaxHeight = 480 },
                CloseButtonText = ToolUtils.GetString("TextClose"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            await dialog.ShowAsync();
        }

        private void UserVariables_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: InstalledPluginItem item } && item.HasUserVariables)
            {
                _ = ShowUserVariablesAsync(item);
            }
        }

        private async Task ShowUserVariablesAsync(InstalledPluginItem item)
        {
            var initValues = ViewModel.GetUserVariables(item);
            var inputs = new Dictionary<string, TextBox>();
            var panel = new StackPanel { Spacing = 10, MinWidth = 420 };
            foreach (var variable in item.UserVariables!)
            {
                var label = string.IsNullOrEmpty(variable.Name) ? variable.Key : variable.Name;
                var input = new TextBox
                {
                    Header = label,
                    PlaceholderText = variable.Hint,
                    Text = initValues.TryGetValue(variable.Key, out var v) ? v : string.Empty,
                };
                inputs[variable.Key] = input;
                panel.Children.Add(input);
            }

            var dialog = new ContentDialog
            {
                Title = $"{item.Platform} - {ToolUtils.GetString("PluginUserVariablesTitle")}",
                Content = new ScrollViewer { Content = panel, MaxHeight = 480 },
                PrimaryButtonText = ToolUtils.GetString("TextSave"),
                CloseButtonText = ToolUtils.GetString("TextCancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var values = inputs.ToDictionary(kv => kv.Key, kv => kv.Value.Text);
                await ViewModel.SaveUserVariablesCommand.ExecuteAsync((item, values));
            }
        }

        private void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: InstalledPluginItem item })
            {
                _ = CheckUpdateAsync(item);
            }
        }

        private async Task CheckUpdateAsync(InstalledPluginItem item)
        {
            ViewModel.SetBusy(true);
            ToastFlyout.ShowInfo(ToolUtils.GetString("PluginCheckUpdateChecking"));
            var (state, newVersion, code, error) = await ViewModel.CheckUpdateAsync(item);
            ViewModel.SetBusy(false);
            switch (state)
            {
                case "latest":
                    ToastFlyout.ShowSuccess(ToolUtils.GetString("PluginCheckUpdateLatest"));
                    return;
                case "update" when code is not null:
                    break;
                default:
                    ToastFlyout.ShowError($"{ToolUtils.GetString("PluginCheckUpdateFailed")}: {error}");
                    return;
            }

            var message = item.Version == newVersion
                ? string.Format(ToolUtils.GetString("PluginUpdateAvailableSameVersion"), item.Version)
                : string.Format(ToolUtils.GetString("PluginUpdateAvailable"), item.Version, newVersion);
            var confirm = new ContentDialog
            {
                Title = ToolUtils.GetString("PluginCheckUpdate"),
                Content = message,
                PrimaryButtonText = ToolUtils.GetString("PluginUpdateNow"),
                CloseButtonText = ToolUtils.GetString("TextCancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.ReplacePluginCommand.ExecuteAsync((item, code!));
            }
        }
    }
}
