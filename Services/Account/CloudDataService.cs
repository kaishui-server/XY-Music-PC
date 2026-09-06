using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WinUIMusicPlayer.Services.Account
{
    /// <summary>
    /// 云数据查看/管理服务: 调用服务端 cloud_data_* 接口,
    /// 只读展示云端保存的歌单/收藏/插件/账号信息, 并支持删除(仅影响云端)。
    /// 与移动端「查看云数据」功能对齐, 服务端身份一律取自 Bearer token。
    /// </summary>
    public class CloudDataService
    {
        private readonly AuthService _auth;
        private readonly ILogger<CloudDataService> _logger;

        public CloudDataService(AuthService auth, ILogger<CloudDataService> logger)
        {
            _auth = auth;
            _logger = logger;
        }

        private string AccountIdOrThrow()
        {
            var id = _auth.AccountId;
            if (string.IsNullOrEmpty(id))
                throw new AuthException("请先登录账号");
            return id;
        }

        /// <summary>拉取云数据概览(统计 + 歌单/收藏/插件摘要 + 云端账号)。</summary>
        public async Task<CloudDataOverview> FetchOverviewAsync()
        {
            var data = await _auth.RequestActionAsync("cloud_data_overview",
                new { user_id = AccountIdOrThrow() }, timeoutMs: 30000);
            return CloudDataOverview.FromJson(data);
        }

        /// <summary>拉取单个云端歌单的完整歌曲列表; 歌单不存在返回 null。</summary>
        public async Task<CloudPlaylistDetail?> FetchPlaylistDetailAsync(string playlistId)
        {
            var data = await _auth.RequestActionAsync("cloud_data_playlist_detail",
                new { user_id = AccountIdOrThrow(), playlist_id = playlistId }, timeoutMs: 30000);
            return CloudPlaylistDetail.FromJson(data);
        }

        /// <summary>清空云端数据(歌单/收藏/插件/设置快照全部删除, 不可恢复)。
        /// 返回 true 表示有数据被清除; 云端本来就没有数据返回 false。</summary>
        public async Task<bool> ClearCloudDataAsync()
        {
            var data = await _auth.RequestActionAsync("cloud_data_clear",
                new { user_id = AccountIdOrThrow() }, timeoutMs: 30000);
            return data.TryGetProperty("cleared", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.True;
        }

        private static int DeletedOf(System.Text.Json.JsonElement data) =>
            data.TryGetProperty("deleted", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.Number
                ? d.GetInt32()
                : 0;

        /// <summary>批量删除云端歌单(按歌单 id), 返回实际删除数量。</summary>
        public async Task<int> DeletePlaylistsAsync(IReadOnlyList<string> playlistIds)
        {
            if (playlistIds.Count == 0) return 0;
            var data = await _auth.RequestActionAsync("cloud_data_delete_playlists",
                new { user_id = AccountIdOrThrow(), playlist_ids = playlistIds }, timeoutMs: 30000);
            return DeletedOf(data);
        }

        /// <summary>批量删除云端歌单内的歌曲(按歌单 id + 歌曲唯一 path)。</summary>
        public async Task<int> DeletePlaylistSongsAsync(string playlistId, IReadOnlyList<string> songPaths)
        {
            if (songPaths.Count == 0) return 0;
            var data = await _auth.RequestActionAsync("cloud_data_delete_playlist_songs",
                new { user_id = AccountIdOrThrow(), playlist_id = playlistId, song_paths = songPaths }, timeoutMs: 30000);
            return DeletedOf(data);
        }

        /// <summary>批量删除云端收藏歌曲(按歌曲唯一 path)。</summary>
        public async Task<int> DeleteFavoritesAsync(IReadOnlyList<string> songPaths)
        {
            if (songPaths.Count == 0) return 0;
            var data = await _auth.RequestActionAsync("cloud_data_delete_favorites",
                new { user_id = AccountIdOrThrow(), song_paths = songPaths }, timeoutMs: 30000);
            return DeletedOf(data);
        }

        /// <summary>批量删除云端插件(按插件 id)。</summary>
        public async Task<int> DeletePluginsAsync(IReadOnlyList<string> pluginIds)
        {
            if (pluginIds.Count == 0) return 0;
            var data = await _auth.RequestActionAsync("cloud_data_delete_plugins",
                new { user_id = AccountIdOrThrow(), plugin_ids = pluginIds }, timeoutMs: 30000);
            return DeletedOf(data);
        }
    }
}
