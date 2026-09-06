using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Linq;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Services.Plugins;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.View.SubView;
using WinUIMusicPlayer.ViewModel;

namespace WinUIMusicPlayer.Helper;

/// <summary>底栏播放音质菜单(主界面底栏与播放详情页底栏共用):
/// 点击按钮弹出可选音质(128k/320k/flac/flac24bit), 选择后持久化为在线播放默认音质(Settings.json),
/// 之后播放其他歌曲也使用该音质; 正在播放在线歌曲时立即按新音质重新解析起播。
/// 歌曲不支持所选音质时由解析链路按"向下优先"自动回退(仅当次歌曲, 不改默认设置)。</summary>
public static class PlayQualityMenuHelper
{
    private static readonly (string ResKey, string Quality)[] QualityItems =
    [
        ("PlayQuality128k", "128k"),
        ("PlayQuality320k", "320k"),
        ("PlayQualityFlac", "flac"),
        ("PlayQualityFlac24bit", "flac24bit"),
    ];

    /// <summary>构建音质选择菜单(单选组, 打开时勾选态跟随持久化的默认音质)。</summary>
    public static MenuFlyout BuildMenu()
    {
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.Top };
        var preferred = OnlinePlaybackResolver.GetPreferredQuality();
        foreach (var (key, quality) in QualityItems)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = ToolUtils.GetString(key),
                GroupName = "PlayQualityGroup",
                Tag = quality,
                IsChecked = quality == preferred,
            };
            item.Click += (_, _) => Select(quality, item.Text);
            menu.Items.Add(item);
        }
        // 两个底栏的菜单实例独立: 每次打开前按最新设置刷新勾选态, 避免另一处改过后状态过期
        menu.Opened += (_, _) =>
        {
            var current = OnlinePlaybackResolver.GetPreferredQuality();
            foreach (var radio in menu.Items.OfType<RadioMenuFlyoutItem>())
                radio.IsChecked = radio.Tag as string == current;
        };
        return menu;
    }

    /// <summary>选择音质: 持久化为默认音质; 正在播放在线歌曲时按新音质重新解析播放(从头起播)。</summary>
    private static void Select(string quality, string displayName)
    {
        if (quality == OnlinePlaybackResolver.GetPreferredQuality()) return; // 重复选择不重播
        AppSettings.PreferredQuality = quality;
        _ = App.Services.GetRequiredService<Services.MusicDatabaseService>().SaveSettingAsync();
        ToastFlyout.ShowSuccess(string.Format(ToolUtils.GetString("PlayQualityChanged"), displayName));
        // 当前为在线歌曲: Path 还原为虚拟路径触发按新音质重新解析; 本地歌曲仅更新默认设置
        var music = App.Services.GetRequiredService<AppViewModel>().CurrentPlayingMusic;
        if (music is null) return;
        var virtualPath = !string.IsNullOrEmpty(music.OnlineVirtualPath) ? music.OnlineVirtualPath : music.Path;
        if (!OnlineMusicRegistry.IsOnlinePath(virtualPath)) return;
        music.Path = virtualPath;
        _ = App.Services.GetRequiredService<MusicBrowseViewModel>().PlayMusic(music, IsChangeList: false);
    }
}
