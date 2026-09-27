using SQLite;

namespace WinUIMusicPlayer.Model
{
    /// <summary>
    /// 在线收藏歌曲: 插件歌曲不落 Music 表, 序列化 OnlineSong 独立存储, 收藏页与本地收藏合并展示。
    /// </summary>
    public class OnlineFavoriteSong
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public int Order { get; set; }
        /// <summary>在线歌曲序列化 JSON(含虚拟路径与插件原始数据, 播放时反序列化交给插件解析)。</summary>
        public string SongJson { get; set; } = string.Empty;
    }
}
