using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WinUIMusicPlayer.Helper;
using WinUIMusicPlayer.Services.Account;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.ViewModel.Pages;

namespace WinUIMusicPlayer.View.SubView
{
    /// <summary>
    /// 「查看云数据」视图: 展示云端保存的歌单/收藏/插件/账号信息,
    /// 支持单个/批量删除与一键清空(仅影响云端数据)。由账号页托管。
    /// </summary>
    public sealed partial class CloudDataView : UserControl
    {
        /// <summary>概览页点返回时触发(宿主页收回本视图)。</summary>
        public event EventHandler? CloseRequested;

        public CloudDataViewModel ViewModel { get; }

        public CloudDataView()
        {
            ViewModel = App.Services.GetRequiredService<CloudDataViewModel>();
            InitializeComponent();
            InitBatchBars();
            InitializeTexts();
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        /// <summary>宿主页打开本视图: 回到概览并加载。</summary>
        public void Open()
        {
            ExitBatch(_playlistsBatch);
            ExitBatch(_songsBatch);
            ExitBatch(_favoritesBatch);
            ExitBatch(_pluginsBatch);
            ViewModel.GoBack();
            _ = ViewModel.LoadAsync();
        }

        private void InitializeTexts()
        {
            RetryButtonText.Text = ToolUtils.GetString("CloudDataRetry");
            SectionListTitle.Text = ToolUtils.GetString("CloudDataSectionTitle");
            StatPlaylistsLabel.Text = ToolUtils.GetString("CloudDataStatPlaylists");
            StatSongsLabel.Text = ToolUtils.GetString("CloudDataStatSongs");
            StatFavoritesLabel.Text = ToolUtils.GetString("CloudDataStatFavorites");
            StatPluginsLabel.Text = ToolUtils.GetString("CloudDataStatPlugins");
            EntryPlaylistsTitle.Text = ToolUtils.GetString("CloudDataStatPlaylists");
            EntryFavoritesTitle.Text = ToolUtils.GetString("CloudDataStatFavorites");
            EntryPluginsTitle.Text = ToolUtils.GetString("CloudDataStatPlugins");
            EntryUserTitle.Text = ToolUtils.GetString("CloudDataEntryUserTitle");
            ClearButtonText.Text = ToolUtils.GetString("CloudDataClearButton");
            PlaylistsEmptyText.Text = ToolUtils.GetString("CloudDataEmptyPlaylists");
            SongsEmptyText.Text = ToolUtils.GetString("CloudDataEmptyPlaylistSongs");
            FavoritesEmptyText.Text = ToolUtils.GetString("CloudDataEmptyFavorites");
            PluginsEmptyText.Text = ToolUtils.GetString("CloudDataEmptyPlugins");
            UserEmailLabel.Text = ToolUtils.GetString("CloudDataEmailLabel");
            UserClientLabel.Text = ToolUtils.GetString("CloudDataClientTypeLabel");
            UserCreatedLabel.Text = ToolUtils.GetString("CloudDataCreatedAtLabel");
            UploadPlaylistsLabel.Text = ToolUtils.GetString("CloudDataUploadPlaylistsLabel");
            UploadPluginsLabel.Text = ToolUtils.GetString("CloudDataUploadPluginsLabel");
            UploadSettingsLabel.Text = ToolUtils.GetString("CloudDataUploadSettingsLabel");
            UserNotFoundText.Text = ToolUtils.GetString("CloudDataUserNotFound");
            UpdateBatchLabels();
        }

        // ─── 状态联动 ─────────────────────────────

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(CloudDataViewModel.IsLoading):
                case nameof(CloudDataViewModel.Error):
                case nameof(CloudDataViewModel.Overview):
                case nameof(CloudDataViewModel.PlaylistDetail):
                case nameof(CloudDataViewModel.Section):
                case nameof(CloudDataViewModel.IsBusy):
                    UpdateUi();
                    break;
            }
        }

        /// <summary>根据 VM 状态整体刷新可见区域与内容。</summary>
        private void UpdateUi()
        {
            var vm = ViewModel;
            LoadingPanel.Visibility = vm.IsLoading ? Visibility.Visible : Visibility.Collapsed;
            var hasError = !vm.IsLoading && !string.IsNullOrEmpty(vm.Error);
            ErrorPanel.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
            if (hasError) ErrorText.Text = vm.Error;
            ContentRoot.Visibility = vm.IsLoading || hasError ? Visibility.Collapsed : Visibility.Visible;

            OverviewSection.Visibility = vm.Section == CloudDataSection.Overview ? Visibility.Visible : Visibility.Collapsed;
            PlaylistsSection.Visibility = vm.Section == CloudDataSection.Playlists ? Visibility.Visible : Visibility.Collapsed;
            PlaylistDetailSection.Visibility = vm.Section == CloudDataSection.PlaylistDetail ? Visibility.Visible : Visibility.Collapsed;
            FavoritesSection.Visibility = vm.Section == CloudDataSection.Favorites ? Visibility.Visible : Visibility.Collapsed;
            PluginsSection.Visibility = vm.Section == CloudDataSection.Plugins ? Visibility.Visible : Visibility.Collapsed;
            UserSection.Visibility = vm.Section == CloudDataSection.User ? Visibility.Visible : Visibility.Collapsed;

            // 标题
            SectionTitleText.Text = vm.Section switch
            {
                CloudDataSection.Playlists => ToolUtils.GetString("CloudDataPlaylistsTitle"),
                CloudDataSection.PlaylistDetail => vm.PlaylistDetail?.Name is { Length: > 0 } name
                    ? name
                    : ToolUtils.GetString("CloudDataPlaylistDetailTitle"),
                CloudDataSection.Favorites => ToolUtils.GetString("CloudDataFavoritesTitle"),
                CloudDataSection.Plugins => ToolUtils.GetString("CloudDataPluginsTitle"),
                CloudDataSection.User => ToolUtils.GetString("CloudDataUserTitle"),
                _ => ToolUtils.GetString("CloudDataTitle"),
            };

            // 列表绑定
            PlaylistsListView.ItemsSource = vm.Overview?.Playlists;
            FavoritesListView.ItemsSource = vm.Overview?.Favorites;
            PluginsListView.ItemsSource = vm.Overview?.Plugins;
            SongsListView.ItemsSource = vm.PlaylistDetail?.Songs;

            var stats = vm.Overview?.Stats;
            StatPlaylistsText.Text = stats?.PlaylistCount.ToString() ?? "0";
            StatSongsText.Text = stats?.SongTotal.ToString() ?? "0";
            StatFavoritesText.Text = stats?.FavoriteCount.ToString() ?? "0";
            StatPluginsText.Text = stats?.PluginCount.ToString() ?? "0";

            // 摘要行
            var uploadedAt = vm.Overview?.PlaylistsUploadedAt;
            LastUploadText.Text = vm.Overview is null
                ? string.Empty
                : vm.Overview.HasData
                    ? string.IsNullOrEmpty(uploadedAt)
                        ? string.Format(ToolUtils.GetString("CloudDataLastUploadFormat"), ToolUtils.GetString("CloudDataNeverUpload"))
                        : string.Format(ToolUtils.GetString("CloudDataLastUploadFormat"), uploadedAt)
                    : ToolUtils.GetString("CloudDataNoData");

            // 概览入口副标题
            EntryPlaylistsSub.Text = stats is null
                ? string.Empty
                : string.Format(ToolUtils.GetString("CloudDataEntryPlaylistsSubFormat"), stats.PlaylistCount, stats.SongTotal);
            EntryFavoritesSub.Text = stats is null
                ? string.Empty
                : string.Format(ToolUtils.GetString("CloudDataEntryFavoritesSubFormat"), stats.FavoriteCount);
            EntryPluginsSub.Text = stats is null
                ? string.Empty
                : string.Format(ToolUtils.GetString("CloudDataEntryPluginsSubFormat"), stats.PluginCount);
            EntryUserSub.Text = vm.Overview?.User?.Nickname is { Length: > 0 } nickname
                ? nickname
                : ToolUtils.GetString("CloudDataEntryUserDefault");

            ClearButton.Visibility = vm.Overview?.HasData == true ? Visibility.Visible : Visibility.Collapsed;

            // 空态
            PlaylistsEmptyText.Visibility =
                vm.Section == CloudDataSection.Playlists && (vm.Overview?.Playlists.Count ?? 0) == 0
                    ? Visibility.Visible : Visibility.Collapsed;
            SongsEmptyText.Visibility =
                vm.Section == CloudDataSection.PlaylistDetail && vm.PlaylistDetail is not null && vm.PlaylistDetail.Songs.Count == 0
                    ? Visibility.Visible : Visibility.Collapsed;
            FavoritesEmptyText.Visibility =
                vm.Section == CloudDataSection.Favorites && (vm.Overview?.Favorites.Count ?? 0) == 0
                    ? Visibility.Visible : Visibility.Collapsed;
            PluginsEmptyText.Visibility =
                vm.Section == CloudDataSection.Plugins && (vm.Overview?.Plugins.Count ?? 0) == 0
                    ? Visibility.Visible : Visibility.Collapsed;

            // 歌单详情元信息
            PlaylistDetailMetaText.Text = vm.PlaylistDetail is null
                ? string.Empty
                : BindUtils.CloudPlaylistSubtitle(vm.PlaylistDetail.Songs.Count, vm.PlaylistDetail.CreatedAt);

            UpdateUserSection();
        }

        private void UpdateUserSection()
        {
            var user = ViewModel.Overview?.User;
            if (user is null)
            {
                UserInitialText.Text = "?";
                UserNicknameText.Text = string.Empty;
                UserIdText.Text = string.Empty;
                UserEmailText.Text = string.Empty;
                UserClientText.Text = string.Empty;
                UserCreatedText.Text = string.Empty;
                UploadPlaylistsText.Text = string.Empty;
                UploadPluginsText.Text = string.Empty;
                UploadSettingsText.Text = string.Empty;
                UserNotFoundText.Visibility = ViewModel.Section == CloudDataSection.User ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            UserNotFoundText.Visibility = Visibility.Collapsed;
            UserInitialText.Text = string.IsNullOrEmpty(user.Nickname) ? "?" : user.Nickname[..1];
            UserNicknameText.Text = user.Nickname;
            UserIdText.Text = string.IsNullOrEmpty(user.XymusicId)
                ? string.Empty
                : ToolUtils.GetString("CloudDataUserIdLabel") + ": " + user.XymusicId;
            UserEmailText.Text = user.Email;
            UserClientText.Text = user.ClientType;
            UserCreatedText.Text = user.CreatedAt;
            UploadPlaylistsText.Text = ViewModel.Overview?.PlaylistsUploadedAt ?? string.Empty;
            UploadPluginsText.Text = ViewModel.Overview?.PluginsUploadedAt ?? string.Empty;
            UploadSettingsText.Text = ViewModel.Overview?.SettingsUploadedAt ?? string.Empty;
        }

        // ─── 顶栏 ─────────────────────────────────

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.GoBack())
                CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = ViewModel.LoadAsync();

        private void RetryButton_Click(object sender, RoutedEventArgs e) => _ = ViewModel.LoadAsync();

        // ─── 概览入口 ─────────────────────────────

        private void PlaylistsEntry_Click(object sender, RoutedEventArgs e) => _ = ViewModel.OpenSectionAsync(CloudDataSection.Playlists);

        private void FavoritesEntry_Click(object sender, RoutedEventArgs e) => _ = ViewModel.OpenSectionAsync(CloudDataSection.Favorites);

        private void PluginsEntry_Click(object sender, RoutedEventArgs e) => _ = ViewModel.OpenSectionAsync(CloudDataSection.Plugins);

        private void UserEntry_Click(object sender, RoutedEventArgs e) => _ = ViewModel.OpenSectionAsync(CloudDataSection.User);

        private void PlaylistsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is CloudPlaylistSummary playlist)
                _ = ViewModel.OpenPlaylistAsync(playlist.Id);
        }

        // ─── 批量管理 ─────────────────────────────

        private sealed class BatchBar
        {
            public required ListView List;
            public required Button ModeButton;
            public required TextBlock ModeText;
            public required Button SelectAllButton;
            public required TextBlock SelectAllText;
            public required Button DeleteButton;
            public required TextBlock DeleteText;
            /// <summary>从选中项收集待删除的唯一标识(过滤不可删项)。</summary>
            public required Func<IList<object>, IReadOnlyList<string>> CollectIds;
            public bool Active;
        }

        private BatchBar? _playlistsBatch;
        private BatchBar? _songsBatch;
        private BatchBar? _favoritesBatch;
        private BatchBar? _pluginsBatch;

        private BatchBar CreateBatchBar(
            ListView list,
            Button modeButton,
            TextBlock modeText,
            Button selectAllButton,
            TextBlock selectAllText,
            Button deleteButton,
            TextBlock deleteText,
            Func<IList<object>, IReadOnlyList<string>> collectIds)
        {
            return new BatchBar
            {
                List = list,
                ModeButton = modeButton,
                ModeText = modeText,
                SelectAllButton = selectAllButton,
                SelectAllText = selectAllText,
                DeleteButton = deleteButton,
                DeleteText = deleteText,
                CollectIds = collectIds,
            };
        }

        private void InitBatchBars()
        {
            _playlistsBatch = CreateBatchBar(PlaylistsListView, PlaylistsBatchModeButton, PlaylistsBatchModeText,
                PlaylistsSelectAllButton, PlaylistsSelectAllText, PlaylistsDeleteButton, PlaylistsDeleteText,
                items => items.OfType<CloudPlaylistSummary>().Select(p => p.Id).ToList());
            _songsBatch = CreateBatchBar(SongsListView, SongsBatchModeButton, SongsBatchModeText,
                SongsSelectAllButton, SongsSelectAllText, SongsDeleteButton, SongsDeleteText,
                items => items.OfType<CloudSongItem>().Select(s => s.Path).Where(p => !string.IsNullOrEmpty(p)).ToList());
            _favoritesBatch = CreateBatchBar(FavoritesListView, FavoritesBatchModeButton, FavoritesBatchModeText,
                FavoritesSelectAllButton, FavoritesSelectAllText, FavoritesDeleteButton, FavoritesDeleteText,
                items => items.OfType<CloudSongItem>().Select(s => s.Path).Where(p => !string.IsNullOrEmpty(p)).ToList());
            _pluginsBatch = CreateBatchBar(PluginsListView, PluginsBatchModeButton, PluginsBatchModeText,
                PluginsSelectAllButton, PluginsSelectAllText, PluginsDeleteButton, PluginsDeleteText,
                items => items.OfType<CloudPluginSummary>().Select(p => p.Id).ToList());
        }

        private void UpdateBatchLabels()
        {
            foreach (var bar in new[] { _playlistsBatch, _songsBatch, _favoritesBatch, _pluginsBatch })
            {
                if (bar is null) continue;
                bar.ModeText.Text = ToolUtils.GetString(bar.Active ? "CloudDataBatchExit" : "CloudDataBatchManage");
                bar.SelectAllText.Text = ToolUtils.GetString("CloudDataSelectAll");
            }
        }

        private void ToggleBatch(BatchBar bar)
        {
            if (bar.Active) ExitBatch(bar);
            else
            {
                bar.Active = true;
                bar.List.SelectionMode = ListViewSelectionMode.Multiple;
                bar.List.IsItemClickEnabled = false;
                bar.SelectAllButton.Visibility = Visibility.Visible;
                bar.DeleteButton.Visibility = Visibility.Visible;
                UpdateBatchCount(bar);
            }
            UpdateBatchLabels();
        }

        private void ExitBatch(BatchBar? bar)
        {
            if (bar is null) return;
            bar.Active = false;
            bar.List.SelectionMode = ListViewSelectionMode.None;
            bar.List.IsItemClickEnabled = ReferenceEquals(bar.List, PlaylistsListView);
            bar.List.SelectedItems.Clear();
            bar.SelectAllButton.Visibility = Visibility.Collapsed;
            bar.DeleteButton.Visibility = Visibility.Collapsed;
            bar.DeleteButton.IsEnabled = false;
            UpdateBatchLabels();
        }

        private void SelectAllBatch(BatchBar bar)
        {
            if (bar.List.SelectedItems.Count >= bar.List.Items.Count)
                bar.List.SelectedItems.Clear();
            else
                bar.List.SelectAll();
        }

        private void UpdateBatchCount(BatchBar bar)
        {
            var count = bar.CollectIds(bar.List.SelectedItems).Count;
            bar.DeleteText.Text = string.Format(ToolUtils.GetString("CloudDataDeleteSelectedFormat"), count);
            bar.DeleteButton.IsEnabled = count > 0;
        }

        // 批量按钮事件
        private void PlaylistsBatchMode_Click(object sender, RoutedEventArgs e) { if (_playlistsBatch is not null) ToggleBatch(_playlistsBatch); }
        private void PlaylistsSelectAll_Click(object sender, RoutedEventArgs e) { if (_playlistsBatch is not null) SelectAllBatch(_playlistsBatch); }
        private void PlaylistsListView_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_playlistsBatch is not null && _playlistsBatch.Active) UpdateBatchCount(_playlistsBatch); }

        private void SongsBatchMode_Click(object sender, RoutedEventArgs e) { if (_songsBatch is not null) ToggleBatch(_songsBatch); }
        private void SongsSelectAll_Click(object sender, RoutedEventArgs e) { if (_songsBatch is not null) SelectAllBatch(_songsBatch); }
        private void SongsListView_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_songsBatch is not null && _songsBatch.Active) UpdateBatchCount(_songsBatch); }

        private void FavoritesBatchMode_Click(object sender, RoutedEventArgs e) { if (_favoritesBatch is not null) ToggleBatch(_favoritesBatch); }
        private void FavoritesSelectAll_Click(object sender, RoutedEventArgs e) { if (_favoritesBatch is not null) SelectAllBatch(_favoritesBatch); }
        private void FavoritesListView_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_favoritesBatch is not null && _favoritesBatch.Active) UpdateBatchCount(_favoritesBatch); }

        private void PluginsBatchMode_Click(object sender, RoutedEventArgs e) { if (_pluginsBatch is not null) ToggleBatch(_pluginsBatch); }
        private void PluginsSelectAll_Click(object sender, RoutedEventArgs e) { if (_pluginsBatch is not null) SelectAllBatch(_pluginsBatch); }
        private void PluginsListView_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_pluginsBatch is not null && _pluginsBatch.Active) UpdateBatchCount(_pluginsBatch); }

        // ─── 确认弹窗与删除 ───────────────────────

        /// <summary>删除确认弹窗(红色主按钮 + 默认焦点在取消防误触), 附「仅影响云端」提示。</summary>
        private async Task<bool> ConfirmDeleteAsync(string title, string message, string confirmText)
        {
            var panel = new StackPanel { Spacing = 10, MinWidth = 340 };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock
            {
                Text = ToolUtils.GetString("CloudDataDeleteNote"),
                FontSize = 12,
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
            });
            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = confirmText,
                CloseButtonText = ToolUtils.GetString("TextCancel"),
                DefaultButton = ContentDialogButton.Close,
            };
            return await dialog.ShowThemedAsync(XamlRoot) == ContentDialogResult.Primary;
        }

        // 单个删除
        private async void PlaylistDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: CloudPlaylistSummary playlist })
            {
                var message = string.Format(ToolUtils.GetString("CloudDataDeletePlaylistOneFormat"), playlist.Name, playlist.SongCount);
                if (ViewModel.IsBusy) return;
                if (!await ConfirmDeleteAsync(ToolUtils.GetString("CloudDataDeletePlaylistTitle"), message, ToolUtils.GetString("CloudDataDelete"))) return;
                try
                {
                    var deleted = await ViewModel.DeletePlaylistsAsync(new[] { playlist.Id });
                    ToastFlyout.ShowSuccess(string.Format(ToolUtils.GetString("CloudDataDeletedPlaylistsFormat"), deleted));
                }
                catch (Exception ex)
                {
                    ToastFlyout.ShowError(ToolUtils.GetString("CloudDataDeleteFailedPrefix") + ex.Message);
                }
            }
        }

        private async void SongDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: CloudSongItem song }) return;
            if (ViewModel.IsBusy || !song.CanDelete) return;
            var isFavorite = ViewModel.Section == CloudDataSection.Favorites;
            var message = isFavorite
                ? string.Format(ToolUtils.GetString("CloudDataDeleteSongOneFavoriteFormat"), song.Title)
                : string.Format(ToolUtils.GetString("CloudDataDeleteSongOnePlaylistFormat"), ViewModel.PlaylistDetail?.Name ?? string.Empty, song.Title);
            if (!await ConfirmDeleteAsync(ToolUtils.GetString("CloudDataDeleteSongTitle"), message, ToolUtils.GetString("CloudDataDelete"))) return;
            try
            {
                var deleted = isFavorite
                    ? await ViewModel.DeleteFavoritesAsync(new[] { song.Path })
                    : await ViewModel.DeletePlaylistSongsAsync(ViewModel.PlaylistDetail?.Id ?? string.Empty, new[] { song.Path });
                ToastFlyout.ShowSuccess(string.Format(ToolUtils.GetString("CloudDataDeletedSongsFormat"), deleted));
            }
            catch (Exception ex)
            {
                ToastFlyout.ShowError(ToolUtils.GetString("CloudDataDeleteFailedPrefix") + ex.Message);
            }
        }

        private async void PluginDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: CloudPluginSummary plugin })
            {
                var message = string.Format(ToolUtils.GetString("CloudDataDeletePluginOneFormat"), plugin.Name);
                if (ViewModel.IsBusy) return;
                if (!await ConfirmDeleteAsync(ToolUtils.GetString("CloudDataDeletePluginTitle"), message, ToolUtils.GetString("CloudDataDelete"))) return;
                try
                {
                    var deleted = await ViewModel.DeletePluginsAsync(new[] { plugin.Id });
                    ToastFlyout.ShowSuccess(string.Format(ToolUtils.GetString("CloudDataDeletedPluginsFormat"), deleted));
                }
                catch (Exception ex)
                {
                    ToastFlyout.ShowError(ToolUtils.GetString("CloudDataDeleteFailedPrefix") + ex.Message);
                }
            }
        }

        // 批量删除
        private async void PlaylistsDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_playlistsBatch is not { Active: true }) return;
            var ids = _playlistsBatch.CollectIds(_playlistsBatch.List.SelectedItems).ToList();
            if (ids.Count == 0 || ViewModel.IsBusy) return;
            var message = string.Format(ToolUtils.GetString("CloudDataDeletePlaylistSelectedFormat"), ids.Count);
            if (!await ConfirmDeleteAsync(ToolUtils.GetString("CloudDataDeletePlaylistTitle"), message, ToolUtils.GetString("CloudDataDelete"))) return;
            try
            {
                var deleted = await ViewModel.DeletePlaylistsAsync(ids);
                ToastFlyout.ShowSuccess(string.Format(ToolUtils.GetString("CloudDataDeletedPlaylistsFormat"), deleted));
                ExitBatch(_playlistsBatch);
            }
            catch (Exception ex)
            {
                ToastFlyout.ShowError(ToolUtils.GetString("CloudDataDeleteFailedPrefix") + ex.Message);
            }
        }

        private async void SongsDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_songsBatch is not { Active: true }) return;
            var paths = _songsBatch.CollectIds(_songsBatch.List.SelectedItems).ToList();
            if (paths.Count == 0 || ViewModel.IsBusy) return;
            var message = string.Format(ToolUtils.GetString("CloudDataDeleteSongSelectedFormat"), paths.Count);
            if (!await ConfirmDeleteAsync(ToolUtils.GetString("CloudDataDeleteSongTitle"), message, ToolUtils.GetString("CloudDataDelete"))) return;
            try
            {
                var deleted = await ViewModel.DeletePlaylistSongsAsync(ViewModel.PlaylistDetail?.Id ?? string.Empty, paths);
                ToastFlyout.ShowSuccess(string.Format(ToolUtils.GetString("CloudDataDeletedSongsFormat"), deleted));
                ExitBatch(_songsBatch);
            }
            catch (Exception ex)
            {
                ToastFlyout.ShowError(ToolUtils.GetString("CloudDataDeleteFailedPrefix") + ex.Message);
            }
        }

        private async void FavoritesDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_favoritesBatch is not { Active: true }) return;
            var paths = _favoritesBatch.CollectIds(_favoritesBatch.List.SelectedItems).ToList();
            if (paths.Count == 0 || ViewModel.IsBusy) return;
            var message = string.Format(ToolUtils.GetString("CloudDataDeleteSongSelectedFavoriteFormat"), paths.Count);
            if (!await ConfirmDeleteAsync(ToolUtils.GetString("CloudDataDeleteSongTitle"), message, ToolUtils.GetString("CloudDataDelete"))) return;
            try
            {
                var deleted = await ViewModel.DeleteFavoritesAsync(paths);
                ToastFlyout.ShowSuccess(string.Format(ToolUtils.GetString("CloudDataDeletedSongsFormat"), deleted));
                ExitBatch(_favoritesBatch);
            }
            catch (Exception ex)
            {
                ToastFlyout.ShowError(ToolUtils.GetString("CloudDataDeleteFailedPrefix") + ex.Message);
            }
        }

        private async void PluginsDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_pluginsBatch is not { Active: true }) return;
            var ids = _pluginsBatch.CollectIds(_pluginsBatch.List.SelectedItems).ToList();
            if (ids.Count == 0 || ViewModel.IsBusy) return;
            var message = string.Format(ToolUtils.GetString("CloudDataDeletePluginSelectedFormat"), ids.Count);
            if (!await ConfirmDeleteAsync(ToolUtils.GetString("CloudDataDeletePluginTitle"), message, ToolUtils.GetString("CloudDataDelete"))) return;
            try
            {
                var deleted = await ViewModel.DeletePluginsAsync(ids);
                ToastFlyout.ShowSuccess(string.Format(ToolUtils.GetString("CloudDataDeletedPluginsFormat"), deleted));
                ExitBatch(_pluginsBatch);
            }
            catch (Exception ex)
            {
                ToastFlyout.ShowError(ToolUtils.GetString("CloudDataDeleteFailedPrefix") + ex.Message);
            }
        }

        // ─── 清空云端数据 ─────────────────────────

        private async void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsBusy) return;
            var stats = ViewModel.Overview?.Stats;
            if (stats is null) return;
            var message = string.Format(
                ToolUtils.GetString("CloudDataClearConfirmFormat"),
                stats.PlaylistCount, stats.SongTotal, stats.FavoriteCount, stats.PluginCount);
            if (!await ConfirmDeleteAsync(
                    ToolUtils.GetString("CloudDataClearConfirmTitle"),
                    message,
                    ToolUtils.GetString("CloudDataClearConfirmButton")))
                return;
            try
            {
                var cleared = await ViewModel.ClearCloudDataAsync();
                ToastFlyout.ShowSuccess(ToolUtils.GetString(cleared ? "CloudDataCleared" : "CloudDataClearNothing"));
            }
            catch (Exception ex)
            {
                ToastFlyout.ShowError(ToolUtils.GetString("CloudDataDeleteFailedPrefix") + ex.Message);
            }
        }
    }
}
