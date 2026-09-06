using System;
using System.Collections.Generic;
using System.Text.Json;

namespace WinUIMusicPlayer.Services.Account
{
    /// <summary>云数据概览: 服务端 cloud_data_overview 返回的轻量展示模型。</summary>
    public sealed class CloudDataOverview
    {
        public bool HasData { get; init; }
        public string PlaylistsUploadedAt { get; init; } = string.Empty;
        public string PluginsUploadedAt { get; init; } = string.Empty;
        public string SettingsUploadedAt { get; init; } = string.Empty;
        public CloudDataStats Stats { get; init; } = new();
        public IReadOnlyList<CloudPlaylistSummary> Playlists { get; init; } = Array.Empty<CloudPlaylistSummary>();
        public IReadOnlyList<CloudSongItem> Favorites { get; init; } = Array.Empty<CloudSongItem>();
        public IReadOnlyList<CloudPluginSummary> Plugins { get; init; } = Array.Empty<CloudPluginSummary>();
        public CloudUserInfo? User { get; init; }

        public static CloudDataOverview FromJson(JsonElement j)
        {
            return new CloudDataOverview
            {
                HasData = j.TryGetProperty("has_data", out var hd) && hd.GetBoolean(),
                PlaylistsUploadedAt = Str(j, "playlists_uploaded_at"),
                PluginsUploadedAt = Str(j, "plugins_uploaded_at"),
                SettingsUploadedAt = Str(j, "settings_uploaded_at"),
                Stats = j.TryGetProperty("stats", out var s) ? CloudDataStats.FromJson(s) : new CloudDataStats(),
                Playlists = List(j, "playlists", CloudPlaylistSummary.FromJson),
                Favorites = List(j, "favorites", CloudSongItem.FromJson),
                Plugins = List(j, "plugins", CloudPluginSummary.FromJson),
                User = j.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object
                    ? CloudUserInfo.FromJson(u)
                    : null,
            };
        }

        internal static string Str(JsonElement j, string key) =>
            j.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : string.Empty;

        internal static IReadOnlyList<T> List<T>(JsonElement j, string key, Func<JsonElement, T> parse)
        {
            if (!j.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
                return Array.Empty<T>();
            var result = new List<T>();
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                    result.Add(parse(item));
            }
            return result;
        }
    }

    /// <summary>云端数据统计: 歌单/歌曲/收藏/插件数量。</summary>
    public sealed class CloudDataStats
    {
        public int PlaylistCount { get; init; }
        public int SongTotal { get; init; }
        public int FavoriteCount { get; init; }
        public int PluginCount { get; init; }

        public static CloudDataStats FromJson(JsonElement j) => new()
        {
            PlaylistCount = Int(j, "playlist_count"),
            SongTotal = Int(j, "song_total"),
            FavoriteCount = Int(j, "favorite_count"),
            PluginCount = Int(j, "plugin_count"),
        };

        private static int Int(JsonElement j, string key) =>
            j.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
    }

    /// <summary>云端歌单摘要。</summary>
    public sealed class CloudPlaylistSummary
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int SongCount { get; init; }
        public string CreatedAt { get; init; } = string.Empty;
        public string CoverUrl { get; init; } = string.Empty;

        public static CloudPlaylistSummary FromJson(JsonElement j) => new()
        {
            Id = CloudDataOverview.Str(j, "id"),
            Name = CloudDataOverview.Str(j, "name"),
            SongCount = j.TryGetProperty("song_count", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0,
            CreatedAt = CloudDataOverview.Str(j, "created_at"),
            CoverUrl = CloudDataOverview.Str(j, "cover_url"),
        };
    }

    /// <summary>云端歌单详情(含完整歌曲列表)。</summary>
    public sealed class CloudPlaylistDetail
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string CreatedAt { get; init; } = string.Empty;
        public string CoverUrl { get; init; } = string.Empty;
        public IReadOnlyList<CloudSongItem> Songs { get; init; } = Array.Empty<CloudSongItem>();

        /// <summary>服务端返回 playlist=null(歌单不存在)时为 null。</summary>
        public static CloudPlaylistDetail? FromJson(JsonElement j)
        {
            if (!j.TryGetProperty("playlist", out var p) || p.ValueKind != JsonValueKind.Object)
                return null;
            return new CloudPlaylistDetail
            {
                Id = CloudDataOverview.Str(p, "id"),
                Name = CloudDataOverview.Str(p, "name"),
                CreatedAt = CloudDataOverview.Str(p, "created_at"),
                CoverUrl = CloudDataOverview.Str(p, "cover_url"),
                Songs = CloudDataOverview.List(p, "songs", CloudSongItem.FromJson),
            };
        }
    }

    /// <summary>云端歌曲条目(歌单内歌曲或收藏)。</summary>
    public sealed class CloudSongItem
    {
        public string Title { get; init; } = string.Empty;
        public string Artist { get; init; } = string.Empty;
        public string Album { get; init; } = string.Empty;
        public int Duration { get; init; }
        public string Format { get; init; } = string.Empty;
        public string SourceType { get; init; } = string.Empty;
        /// <summary>云端歌曲唯一标识(删除时按它匹配); 个别旧数据可能为空。</summary>
        public string Path { get; init; } = string.Empty;

        /// <summary>online/plugin = 插件音源, local = 本地文件。</summary>
        public bool IsOnline => SourceType is "online" or "plugin";

        /// <summary>旧云端数据可能缺少 path 唯一标识, 无法定位删除。</summary>
        public bool CanDelete => !string.IsNullOrEmpty(Path);

        public static CloudSongItem FromJson(JsonElement j) => new()
        {
            Title = CloudDataOverview.Str(j, "title"),
            Artist = CloudDataOverview.Str(j, "artist"),
            Album = CloudDataOverview.Str(j, "album"),
            Duration = j.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : 0,
            Format = CloudDataOverview.Str(j, "format"),
            SourceType = CloudDataOverview.Str(j, "source_type"),
            Path = CloudDataOverview.Str(j, "path"),
        };

        /// <summary>时长 mm:ss; 无时长返回空串。</summary>
        public string DurationText => Duration <= 0
            ? string.Empty
            : $"{Duration / 60}:{Duration % 60:D2}";
    }

    /// <summary>云端插件摘要(不含脚本内容)。</summary>
    public sealed class CloudPluginSummary
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Format { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string Author { get; init; } = string.Empty;
        public bool Enabled { get; init; }
        public string SourceUrl { get; init; } = string.Empty;

        /// <summary>lx = LX 音源脚本, musicfree = MusicFree 插件。</summary>
        public bool IsLx => Format == "lx";

        public static CloudPluginSummary FromJson(JsonElement j) => new()
        {
            Id = CloudDataOverview.Str(j, "id"),
            Name = CloudDataOverview.Str(j, "name"),
            Format = CloudDataOverview.Str(j, "format"),
            Version = CloudDataOverview.Str(j, "version"),
            Author = CloudDataOverview.Str(j, "author"),
            Enabled = j.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True,
            SourceUrl = CloudDataOverview.Str(j, "source_url"),
        };
    }

    /// <summary>云端账号信息(来自数据库的注册资料)。</summary>
    public sealed class CloudUserInfo
    {
        public string UserId { get; init; } = string.Empty;
        public string Nickname { get; init; } = string.Empty;
        public string XymusicId { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string ClientType { get; init; } = string.Empty;
        public string CreatedAt { get; init; } = string.Empty;

        public static CloudUserInfo FromJson(JsonElement j) => new()
        {
            UserId = CloudDataOverview.Str(j, "user_id"),
            Nickname = CloudDataOverview.Str(j, "nickname"),
            XymusicId = CloudDataOverview.Str(j, "xymusic_id"),
            Email = CloudDataOverview.Str(j, "email"),
            ClientType = CloudDataOverview.Str(j, "client_type"),
            CreatedAt = CloudDataOverview.Str(j, "created_at"),
        };
    }
}
