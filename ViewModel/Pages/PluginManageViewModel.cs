using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
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

        /// <summary>拖拽排序完成后, 把当前列表顺序持久化到插件清单(顺序即在线搜索 Tab 顺序)。</summary>
        public void CommitReorder()
        {
            var hashes = Plugins.Select(p => p.Hash).ToList();
            Task.Run(() =>
            {
                var (ok, error) = _pluginManager.ReorderPlugins(hashes);
                App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                {
                    if (!ok) ToastFlyout.ShowError(error ?? "操作失败");
                    else ToastFlyout.ShowSuccess(ToolUtils.GetString("PluginReorderSaved"));
                });
            });
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
                picker.FileTypeFilter.Add(".json");
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
                    // 部分插件源(如 jsDelivr/Gitee)拒绝无 UA 请求, 附浏览器 UA(对齐弦予 URL 安装)
                    http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                    var content = await http.GetStringAsync(urlOrCode.Trim());
                    // 批量 JSON 导入(对齐弦予 importMultiplePlugins): 内容是插件列表 [{name,url,version},...] 时逐个下载安装
                    if (TryParsePluginList(content, out var pluginList))
                    {
                        string? lastHash = null;
                        var okCount = 0;
                        foreach (var (name, url) in pluginList)
                        {
                            try
                            {
                                var script = await http.GetStringAsync(url);
                                if (string.IsNullOrWhiteSpace(script)) continue;
                                var (hash, error) = await _pluginManager.InstallFromCodeAsync(script);
                                if (hash is not null) { lastHash = hash; okCount++; _ = name; }
                            }
                            catch { /* 单个失败继续下一个 */ }
                        }
                        if (okCount > 0)
                        {
                            ToastFlyout.ShowSuccess($"批量导入完成: 成功 {okCount} 个, 失败 {pluginList.Count - okCount} 个");
                            return (lastHash, null);
                        }
                        return (null, "批量导入: 所有插件安装失败");
                    }
                    return await _pluginManager.InstallFromCodeAsync(content);
                });
            }
            else
            {
                await InstallCore(ToolUtils.GetString("PluginInstallReading"), () => _pluginManager.InstallFromCodeAsync(urlOrCode));
            }
        }

        /// <summary>识别插件列表 JSON(对齐弦予): 顶层数组或 {plugins:[...]} 且元素含 url 字段。</summary>
        private static bool TryParsePluginList(string content, out List<(string? Name, string Url)> pluginList)
        {
            pluginList = [];
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(content.Trim());
                var root = doc.RootElement;
                if (root.ValueKind == System.Text.Json.JsonValueKind.Object && root.TryGetProperty("plugins", out var pl) && pl.ValueKind == System.Text.Json.JsonValueKind.Array)
                    root = pl;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Array) return false;
                foreach (var item in root.EnumerateArray())
                {
                    if (item.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    if (!item.TryGetProperty("url", out var url) || url.ValueKind != System.Text.Json.JsonValueKind.String || !url.GetString()!.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                    string? name = item.TryGetProperty("name", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString() : null;
                    pluginList.Add((name, url.GetString()!));
                }
                return pluginList.Count > 0;
            }
            catch { return false; }
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

        /// <summary>一键卸载全部插件(MF+LX)。先弹确认框, 避免误触不可恢复。</summary>
        [RelayCommand]
        private async Task UninstallAllAsync()
        {
            if (IsBusy) return;
            var dialog = new ContentDialog
            {
                Title = ToolUtils.GetString("PluginUninstallAllTitle"),
                Content = ToolUtils.GetString("PluginUninstallAllConfirm"),
                PrimaryButtonText = ToolUtils.GetString("PluginUninstallAllTitle2"),
                CloseButtonText = ToolUtils.GetString("CancelButton"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = App.MainWindow!.Content.XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            IsBusy = true;
            try
            {
                var (count, error) = await _pluginManager.UninstallAllAsync();
                if (error is not null) ToastFlyout.ShowError(error);
                else if (count == 0) ToastFlyout.ShowInfo(ToolUtils.GetString("PluginManageEmpty"));
                else ToastFlyout.ShowSuccess($"{ToolUtils.GetString("PluginUninstallSuccess")} ({count})");
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
