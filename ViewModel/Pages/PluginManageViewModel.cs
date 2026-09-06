using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinUIMusicPlayer.Services.Plugins;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.View.SubView;

namespace WinUIMusicPlayer.ViewModel.Pages
{
    /// <summary>插件管理页 ViewModel。</summary>
    public partial class PluginManageViewModel : ObservableObject
    {
        private readonly PluginManagerService _pluginManager;

        public ObservableCollection<InstalledPluginItem> Plugins { get; } = new();

        [ObservableProperty]
        private bool _isBusy;

        public PluginManageViewModel()
        {
            _pluginManager = App.Services.GetRequiredService<PluginManagerService>();
            _pluginManager.PluginsChanged += () =>
            {
                App.MainWindow?.DispatcherQueue.TryEnqueue(RefreshList);
            };
        }

        public void RefreshList()
        {
            Plugins.Clear();
            foreach (var item in _pluginManager.GetInstalledItems())
                Plugins.Add(item);
            if (Plugins.Count == 0)
                ToastFlyout.ShowInfo(ToolUtils.GetString("PluginManageEmpty"));
        }

        /// <summary>供页面 code-behind 显式控制忙态(如检查更新流程)。</summary>
        public void SetBusy(bool busy)
        {
            IsBusy = busy;
        }

        [RelayCommand]
        private async Task InstallFromFileAsync()
        {
            try
            {
                var picker = new FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(
                    picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
                picker.FileTypeFilter.Add(".js");
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                var file = await picker.PickSingleFileAsync();
                if (file is null) return;
                await InstallCore(ToolUtils.GetString("PluginInstallReading"), () => _pluginManager.InstallFromFileAsync(file.Path));
            }
            catch (Exception ex)
            {
                ToastFlyout.ShowError($"{ToolUtils.GetString("PluginInstallFailed")}: {ex.Message}");
            }
        }

        public async Task InstallFromUrlOrTextAsync(string urlOrCode)
        {
            if (string.IsNullOrWhiteSpace(urlOrCode)) return;
            if (urlOrCode.TrimStart().StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                await InstallCore(ToolUtils.GetString("PluginInstallDownloading"), async () =>
                {
                    using var http = new System.Net.Http.HttpClient();
                    http.Timeout = TimeSpan.FromSeconds(60);
                    var code = await http.GetStringAsync(urlOrCode.Trim());
                    return await _pluginManager.InstallFromCodeAsync(code);
                });
            }
            else
            {
                await InstallCore(ToolUtils.GetString("PluginInstallReading"), () => _pluginManager.InstallFromCodeAsync(urlOrCode));
            }
        }

        private async Task InstallCore(string busyText, Func<Task<(string? Hash, string? Error)>> installer)
        {
            IsBusy = true;
            ToastFlyout.ShowInfo(busyText);
            try
            {
                var (hash, error) = await installer();
                if (error is not null)
                    ToastFlyout.ShowError($"{ToolUtils.GetString("PluginInstallFailed")}: {error}");
                else
                    ToastFlyout.ShowSuccess(ToolUtils.GetString("PluginInstallSuccess"));
            }
            catch (Exception ex)
            {
                ToastFlyout.ShowError($"{ToolUtils.GetString("PluginInstallFailed")}: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                App.MainWindow?.DispatcherQueue.TryEnqueue(RefreshList);
            }
        }

        [RelayCommand]
        private async Task ToggleEnabledAsync(InstalledPluginItem item)
        {
            if (IsBusy || item is null) return;
            IsBusy = true;
            try
            {
                var (ok, error) = await _pluginManager.SetEnabledAsync(item.Hash, item.Enabled);
                if (!ok) ToastFlyout.ShowError(error ?? "操作失败");
            }
            finally
            {
                IsBusy = false;
                RefreshList();
            }
        }

        [RelayCommand]
        private async Task UninstallAsync(InstalledPluginItem item)
        {
            if (IsBusy || item is null) return;
            IsBusy = true;
            try
            {
                var (ok, error) = await _pluginManager.UninstallAsync(item.Hash);
                if (!ok) ToastFlyout.ShowError(error ?? "操作失败");
                else ToastFlyout.ShowSuccess(ToolUtils.GetString("PluginUninstallSuccess"));
            }
            finally
            {
                IsBusy = false;
                RefreshList();
            }
        }

        /// <summary>读取插件当前用户变量(用于编辑弹窗初始化)。</summary>
        public Dictionary<string, string> GetUserVariables(InstalledPluginItem item)
            => _pluginManager.GetUserVariables(item.Platform);

        /// <summary>保存用户变量并热重载插件运行时。</summary>
        [RelayCommand]
        private async Task SaveUserVariablesAsync((InstalledPluginItem Item, Dictionary<string, string> Values) args)
        {
            if (IsBusy || args.Item is null) return;
            IsBusy = true;
            try
            {
                var (ok, error) = await _pluginManager.SetUserVariablesAsync(args.Item.Hash, args.Values);
                if (!ok) ToastFlyout.ShowError(error ?? "操作失败");
                else ToastFlyout.ShowSuccess(ToolUtils.GetString("PluginUserVariablesSaved"));
            }
            finally
            {
                IsBusy = false;
                RefreshList();
            }
        }

        /// <summary>检查更新: 返回 (状态, 新版本号, 新代码, 错误)。</summary>
        public Task<(string State, string NewVersion, string? Code, string? Error)> CheckUpdateAsync(InstalledPluginItem item)
            => _pluginManager.CheckUpdateAsync(item.Hash);

        /// <summary>用新代码替换插件(更新)。</summary>
        [RelayCommand]
        private async Task ReplacePluginAsync((InstalledPluginItem Item, string Code) args)
        {
            if (IsBusy || args.Item is null) return;
            IsBusy = true;
            ToastFlyout.ShowInfo(ToolUtils.GetString("PluginInstallDownloading"));
            try
            {
                var (ok, error) = await _pluginManager.ReplacePluginAsync(args.Item.Hash, args.Code);
                if (!ok) ToastFlyout.ShowError($"{ToolUtils.GetString("PluginInstallFailed")}: {error}");
                else ToastFlyout.ShowSuccess(ToolUtils.GetString("PluginUpdateSuccess"));
            }
            finally
            {
                IsBusy = false;
                RefreshList();
            }
        }
    }
}
