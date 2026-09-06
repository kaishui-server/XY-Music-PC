using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using WinUIMusicPlayer.Services.Account;
using WinUIMusicPlayer.Utils;

namespace WinUIMusicPlayer.ViewModel.Pages
{
    /// <summary>云数据视图内部导航区域。</summary>
    public enum CloudDataSection
    {
        Overview,
        Playlists,
        PlaylistDetail,
        Favorites,
        Plugins,
        User,
    }

    /// <summary>
    /// 「查看云数据」ViewModel: 云端歌单/收藏/插件/账号信息的只读展示与删除管理。
    /// 数据来自 CloudDataService(cloud_data_* 接口), 删除仅影响云端数据。
    /// </summary>
    public partial class CloudDataViewModel : ObservableObject
    {
        private readonly Services.Account.CloudDataService _service;

        /// <summary>当前展示区域; Overview 之外均为云端数据详情页。</summary>
        [ObservableProperty]
        private CloudDataSection _section = CloudDataSection.Overview;

        [ObservableProperty]
        private bool _isLoading;

        /// <summary>最近一次加载失败的错误信息(空串表示无错误)。</summary>
        [ObservableProperty]
        private string _error = string.Empty;

        [ObservableProperty]
        private CloudDataOverview? _overview;

        /// <summary>当前打开的云端歌单详情(仅 PlaylistDetail 区域)。</summary>
        [ObservableProperty]
        private CloudPlaylistDetail? _playlistDetail;

        /// <summary>删除/清空操作进行中(期间禁用批量操作按钮)。</summary>
        [ObservableProperty]
        private bool _isBusy;

        public CloudDataViewModel()
        {
            _service = App.Services.GetRequiredService<Services.Account.CloudDataService>();
        }

        /// <summary>按当前区域加载对应数据(Overview 数据或歌单详情)。</summary>
        public async Task LoadAsync()
        {
            if (IsLoading) return;
            IsLoading = true;
            Error = string.Empty;
            try
            {
                if (Section == CloudDataSection.PlaylistDetail)
                {
                    var id = PlaylistDetail?.Id;
                    if (string.IsNullOrEmpty(id))
                    {
                        Error = ToolUtils.GetString("CloudDataPlaylistNotFound");
                        PlaylistDetail = null;
                    }
                    else
                    {
                        var detail = await _service.FetchPlaylistDetailAsync(id);
                        if (detail is null)
                        {
                            Error = ToolUtils.GetString("CloudDataPlaylistNotFound");
                            PlaylistDetail = null;
                        }
                        else
                        {
                            PlaylistDetail = detail;
                        }
                    }
                }
                else
                {
                    Overview = await _service.FetchOverviewAsync();
                }
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>打开某个云端数据区域并加载。</summary>
        public async Task OpenSectionAsync(CloudDataSection section)
        {
            if (Section == section) return;
            Section = section;
            PlaylistDetail = null;
            Error = string.Empty;
            await LoadAsync();
        }

        /// <summary>打开云端歌单详情。</summary>
        public async Task OpenPlaylistAsync(string playlistId)
        {
            if (IsLoading || string.IsNullOrEmpty(playlistId)) return;
            Section = CloudDataSection.PlaylistDetail;
            PlaylistDetail = null; // 先清空, 加载完成后填充
            Error = string.Empty;
            IsLoading = true;
            try
            {
                var detail = await _service.FetchPlaylistDetailAsync(playlistId);
                if (detail is null)
                {
                    Error = ToolUtils.GetString("CloudDataPlaylistNotFound");
                }
                else
                {
                    PlaylistDetail = detail;
                }
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>详情页返回概览(Overview 的返回由宿主页处理)。</summary>
        public bool GoBack()
        {
            if (Section == CloudDataSection.Overview) return false;
            Section = CloudDataSection.Overview;
            PlaylistDetail = null;
            Error = string.Empty;
            return true;
        }

        // ─── 删除操作(仅影响云端, 由视图确认后调用) ─────────

        public async Task<int> DeletePlaylistsAsync(IReadOnlyList<string> playlistIds)
        {
            IsBusy = true;
            try
            {
                var deleted = await _service.DeletePlaylistsAsync(playlistIds);
                await ReloadAfterChangeAsync();
                return deleted;
            }
            finally { IsBusy = false; }
        }

        public async Task<int> DeletePlaylistSongsAsync(string playlistId, IReadOnlyList<string> songPaths)
        {
            IsBusy = true;
            try
            {
                var deleted = await _service.DeletePlaylistSongsAsync(playlistId, songPaths);
                await LoadAsync();
                return deleted;
            }
            finally { IsBusy = false; }
        }

        public async Task<int> DeleteFavoritesAsync(IReadOnlyList<string> songPaths)
        {
            IsBusy = true;
            try
            {
                var deleted = await _service.DeleteFavoritesAsync(songPaths);
                await ReloadAfterChangeAsync();
                return deleted;
            }
            finally { IsBusy = false; }
        }

        public async Task<int> DeletePluginsAsync(IReadOnlyList<string> pluginIds)
        {
            IsBusy = true;
            try
            {
                var deleted = await _service.DeletePluginsAsync(pluginIds);
                await ReloadAfterChangeAsync();
                return deleted;
            }
            finally { IsBusy = false; }
        }

        /// <summary>清空云端数据; 返回 true=有数据被清除, false=云端本来就没有数据。</summary>
        public async Task<bool> ClearCloudDataAsync()
        {
            IsBusy = true;
            try
            {
                var cleared = await _service.ClearCloudDataAsync();
                await ReloadAfterChangeAsync();
                return cleared;
            }
            finally { IsBusy = false; }
        }

        /// <summary>删除导致概览变化后整体重载(歌单/收藏/插件页共用同一份 Overview)。</summary>
        private async Task ReloadAfterChangeAsync()
        {
            Error = string.Empty;
            IsLoading = true;
            try
            {
                Overview = await _service.FetchOverviewAsync();
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
