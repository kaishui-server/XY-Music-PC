using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Services;
using WinUIMusicPlayer.Services.Plugins;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.View.SubView;

namespace WinUIMusicPlayer.Helper;

/// <summary>在线歌曲下载流程(底栏下载按钮共用): 解析歌曲 → 下载选项弹窗 → 悬浮进度小弹窗 → 后台下载。</summary>
public static class DownloadFlowHelper
{
    /// <summary>当前活动的进度小弹窗(开始新下载时移除旧弹窗)。</summary>
    private static DownloadProgressFlyout? _currentFlyout;

    public static async void Start(XamlRoot xamlRoot, Music? music)
    {
        try
        {
            var song = OnlineDownloadService.ResolveOnlineSong(music);
            if (song is null)
            {
                var warn = new ContentDialog
                {
                    Title = ToolUtils.GetString("DownloadDialogTitle"),
                    Content = ToolUtils.GetString("DownloadFileNotOnline"),
                    CloseButtonText = ToolUtils.GetString("DialogClose"),
                    XamlRoot = xamlRoot,
                    RequestedTheme = AppSettings.ElementTheme,
                };
                _ = await warn.ShowAsync();
                return;
            }

            // 同歌查重: 上次下载目录已存在"歌手 - 歌名"文件时先确认是否重新下载
            var existing = OnlineDownloadService.FindExistingDownload(song);
            if (existing is not null)
            {
                var reconfirm = new ContentDialog
                {
                    Title = ToolUtils.GetString("DownloadDialogTitle"),
                    Content = ToolUtils.GetString("DownloadAlreadyExists"),
                    PrimaryButtonText = ToolUtils.GetString("DownloadRedownload"),
                    CloseButtonText = ToolUtils.GetString("DialogClose"),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = xamlRoot,
                    RequestedTheme = AppSettings.ElementTheme,
                };
                if (await reconfirm.ShowAsync() != ContentDialogResult.Primary) return;
            }

            var dialog = new DownloadDialog(song)
            {
                XamlRoot = xamlRoot,
                RequestedTheme = AppSettings.ElementTheme,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            // 持久化用户选择(音质/目录/独立歌词/封面)
            dialog.PersistChoices();
            _ = App.Services.GetRequiredService<MusicDatabaseService>().SaveSettingAsync();

            // 悬浮小弹窗: 挂到 MainWindow 根 Grid(Grid 叠放不推挤布局; 模糊可采样背后壁纸/页面)
            _currentFlyout?.Close();
            var flyout = new DownloadProgressFlyout(ToolUtils.GetString("DownloadProgressTitle"));
            _currentFlyout = flyout;
            App.MainWindow.RootGrid.Children.Add(flyout);
            flyout.Show();

            var quality = dialog.SelectedQuality;
            var dir = dialog.TargetDir;
            var saveLrc = dialog.SaveLrc;
            var saveCover = dialog.SaveCover;
            // 预取资源串(后台线程不能访问 ResourceLoader)
            var successFormat = ToolUtils.GetString("DownloadSuccessFormat");
            var failedFormat = ToolUtils.GetString("DownloadFailedFormat");
            var lrcNote = ToolUtils.GetString("DownloadLrcSavedNote");
            var coverNote = ToolUtils.GetString("DownloadCoverSavedNote");

            _ = Task.Run(async () =>
            {
                var (ok, message, lrcSaved, coverSaved) = await OnlineDownloadService.DownloadAsync(
                    song, quality, dir, saveLrc, saveCover,
                    flyout.UpdateStatus, flyout.UpdateProgress);
                var text = ok
                    ? string.Format(successFormat, message)
                       + (lrcSaved ? $"\n{lrcNote}" : string.Empty)
                       + (coverSaved ? $"\n{coverNote}" : string.Empty)
                    : string.Format(failedFormat, message);
                flyout.Complete(ok, text);
            });
        }
        catch (Exception ex)
        {
            App.GetLogger<Music>().LogError(ex, "下载流程异常");
        }
    }
}
