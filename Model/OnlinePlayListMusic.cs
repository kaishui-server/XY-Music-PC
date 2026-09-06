using SQLite;

namespace WinUIMusicPlayer.Model
{
    /// <summary>
    /// 在线歌单歌曲条目: 在线歌曲仅存插件链接(OnlineSong 序列化 JSON, 播放时再解析下载), 本地歌曲存 MusicId 引用。
    /// </summary>
    public class OnlinePlayListMusic
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public int PlayListId { get; set; }
        public int Order { get; set; }
        /// <summary>本地歌曲引用(MusicId &gt; 0 时有效, SongJson 为空)。</summary>
        public int MusicId { get; set; }
        /// <summary>在线歌曲序列化 JSON(含虚拟路径与插件原始数据, 播放时反序列化交给插件解析)。</summary>
        public string SongJson { get; set; } = string.Empty;
    }
}
