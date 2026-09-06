using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using System;
using System.Threading.Tasks;
using WinUIMusicPlayer.Services.Plugins;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.ViewModel.Pages;

namespace WinUIMusicPlayer.View.SubView
{
    /// <summary>
    /// 新建歌单弹窗: 三个 Tab —— 新建歌单 / 从网络导入(插件 importMusicSheet 解析歌单链接) /
    /// 从本地导入(M3U/M3U8 匹配本地音乐库, MusicFree 备份 JSON 按插件/本地路径匹配)。
    /// </summary>
    public sealed partial class CreatePlayListDialog : ContentDialog
    {
        private readonly MyPlayListViewModel _viewModel;

        public CreatePlayListDialog(MyPlayListViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            Title = ToolUtils.GetString("MyPlayListNewTitle");
            CloseButtonText = ToolUtils.GetString("CloseButton");
            TabCreateHeader.Text = ToolUtils.GetString("CreatePlayListTabCreate");
            TabNetworkHeader.Text = ToolUtils.GetString("CreatePlayListTabNetwork");
            TabLocalHeader.Text = ToolUtils.GetString("CreatePlayListTabLocal");
            NameTextBox.PlaceholderText = ToolUtils.GetString("MyPlayListNamePlaceholder");
            CreateButtonText.Text = ToolUtils.GetString("CreatePlayListCreateButton");
            NetworkSourceLabel.Text = ToolUtils.GetString("CreatePlayListNetworkSource");
            NoPluginHint.Text = ToolUtils.GetString("CreatePlayListNoImportPlugin");
            UrlTextBox.PlaceholderText = ToolUtils.GetString("CreatePlayListNetworkUrlPlaceholder");
            ImportNetworkButtonText.Text = ToolUtils.GetString("CreatePlayListImportButton");
            LocalHintText.Text = ToolUtils.GetString("CreatePlayListLocalHint");
            SelectFileButtonText.Text = ToolUtils.GetString("CreatePlayListSelectFileButton");
            RefreshPluginList();
        }

        // ---------------- Tab 切换(Button Click, 直接绑定) ----------------

        private void TabCreate_Click(object sender, RoutedEventArgs e) => SelectTab(0);

        private void TabNetwork_Click(object sender, RoutedEventArgs e) => SelectTab(1);

        private void TabLocal_Click(object sender, RoutedEventArgs e) => SelectTab(2);

        private void SelectTab(int index)
        {
            CreatePanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            NetworkPanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            LocalPanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
            TabCreateIndicator.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            TabNetworkIndicator.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            TabLocalIndicator.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------------- Tab 1: 新建歌单 ----------------

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            var name = NameTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            _ = CreatePlayListCoreAsync(name);
        }

        private async Task CreatePlayListCoreAsync(string name)
        {
            CreateButton.IsEnabled = false;
            try
            {
                var pl = await _viewModel.CreatePlayListAsync(name);
                if (pl is not null) Hide();
            }
            finally
            {
                CreateButton.IsEnabled = true;
            }
        }

        // ---------------- Tab 2: 从网络导入 ----------------

        /// <summary>网络导入来源: 仅列出声明 importMusicSheet 的已启用插件。</summary>
        private void RefreshPluginList()
        {
            var plugins = _viewModel.GetImportablePlugins();
            PluginComboBox.ItemsSource = plugins;
            if (plugins.Count > 0)
            {
                PluginComboBox.SelectedIndex = 0;
                PluginComboBox.IsEnabled = true;
                NoPluginHint.Visibility = Visibility.Collapsed;
                ImportNetworkButton.IsEnabled = true;
            }
            else
            {
                PluginComboBox.IsEnabled = false;
                NoPluginHint.Visibility = Visibility.Visible;
                ImportNetworkButton.IsEnabled = false;
            }
        }

        private void ImportNetworkButton_Click(object sender, RoutedEventArgs e)
        {
            if (PluginComboBox.SelectedItem is not PluginRuntime runtime)
            {
                ToastFlyout.ShowWarning(ToolUtils.GetString("CreatePlayListNoImportPlugin"));
                return;
            }
            var url = UrlTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(url))
            {
                ToastFlyout.ShowWarning(ToolUtils.GetString("CreatePlayListUrlRequired"));
                return;
            }
            _ = RunImportAsync(ImportNetworkButton, () => _viewModel.ImportNetworkAsync(runtime.Hash, url));
        }

        // ---------------- Tab 3: 从本地导入 ----------------

        private void SelectFileButton_Click(object sender, RoutedEventArgs e)
        {
            _ = SelectAndImportLocalFileAsync();
        }

        /// <summary>选择 M3U/M3U8/MusicFree 备份文件并导入(M3U 匹配本地音乐库, 备份按插件/本地路径匹配)。</summary>
        private async Task SelectAndImportLocalFileAsync()
        {
            string? filePath;
            try
            {
                var picker = new FileOpenPicker(App.MainWindow.AppWindow.Id)
                {
                    SuggestedStartLocation = PickerLocationId.MusicLibrary,
                };
                picker.FileTypeFilter.Add(".m3u");
                picker.FileTypeFilter.Add(".m3u8");
                picker.FileTypeFilter.Add(".json");
                var file = await picker.PickSingleFileAsync();
                filePath = string.IsNullOrEmpty(file?.Path) ? null : file.Path;
            }
            catch (Exception ex)
            {
                ToastFlyout.ShowError(string.Format(ToolUtils.GetString("CreatePlayListImportFailedReason"), ex.Message));
                return;
            }
            if (filePath is null) return;
            await RunImportAsync(SelectFileButton, () => _viewModel.ImportLocalFileAsync(filePath));
        }

        /// <summary>导入执行样板: 导入中以彩色弹窗提示, 成功绿色并关闭弹窗, 失败红色保留弹窗。</summary>
        private async Task RunImportAsync(Button button, Func<Task<(bool Ok, string Message)>> import)
        {
            ToastFlyout.ShowInfo(ToolUtils.GetString("CreatePlayListImporting"));
            button.IsEnabled = false;
            try
            {
                var (ok, message) = await import();
                if (ok)
                {
                    Hide();
                    ToastFlyout.ShowSuccess(message);
                }
                else
                {
                    ToastFlyout.ShowError(message);
                }
            }
            catch (Exception ex)
            {
                ToastFlyout.ShowError(string.Format(ToolUtils.GetString("CreatePlayListImportFailedReason"), ex.Message));
            }
            finally
            {
                button.IsEnabled = true;
            }
        }
    }
}
