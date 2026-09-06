using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Services.Plugins;
using WinUIMusicPlayer.Utils;

namespace WinUIMusicPlayer.View.SubView;

/// <summary>在线歌曲下载对话框: 音质/目录选择 + 独立歌词/封面选项(歌词封面始终内嵌歌曲文件)。</summary>
public sealed partial class DownloadDialog : ContentDialog
{
    private static readonly (string Key, string Quality)[] QualityItems =
    [
        ("DownloadQuality128k", "128k"),
        ("DownloadQuality320k", "320k"),
        ("DownloadQualityFlac", "flac"),
        ("DownloadQualityFlac24bit", "flac24bit"),
    ];

    /// <summary>选中音质(128k/320k/flac/flac24bit)。</summary>
    public string SelectedQuality { get; private set; } = "320k";
    /// <summary>下载目录。</summary>
    public string TargetDir { get; private set; } = string.Empty;
    /// <summary>是否额外保存独立 LRC 文件。</summary>
    public bool SaveLrc => SaveLrcCheckBox.IsChecked == true;
    /// <summary>是否额外保存独立封面文件。</summary>
    public bool SaveCover => SaveCoverCheckBox.IsChecked == true;

    public DownloadDialog(OnlineSong song)
    {
        InitializeComponent();
        Title = ToolUtils.GetString("DownloadDialogTitle");
        PrimaryButtonText = ToolUtils.GetString("DownloadConfirm");
        CloseButtonText = ToolUtils.GetString("DialogClose");
        DefaultButton = ContentDialogButton.Primary;
        IsPrimaryButtonEnabled = true;

        SongTitleText.Text = song.Title;
        SongArtistText.Text = $"{song.Artist} · {song.Album}";
        QualityLabel.Text = ToolUtils.GetString("DownloadQuality");
        DirLabel.Text = ToolUtils.GetString("DownloadDirLabel");
        ChooseDirButton.Content = ToolUtils.GetString("DownloadChooseDir");
        SaveLrcCheckBox.Content = ToolUtils.GetString("DownloadSaveLrc");
        SaveCoverCheckBox.Content = ToolUtils.GetString("DownloadSaveCover");
        EmbedNoteText.Text = ToolUtils.GetString("DownloadEmbedNote");

        // 音质下拉: 默认取持久化设置
        var quality = LxSources.NormalizeQuality(AppSettings.DownloadQuality) is { Length: > 0 } q ? q : "320k";
        foreach (var (key, value) in QualityItems)
        {
            var item = new ComboBoxItem { Content = ToolUtils.GetString(key), Tag = value };
            QualityComboBox.Items.Add(item);
            if (value == quality)
            {
                QualityComboBox.SelectedItem = item;
                SelectedQuality = value;
            }
        }
        if (QualityComboBox.SelectedItem is null && QualityComboBox.Items.Count > 0)
        {
            QualityComboBox.SelectedIndex = 1; // 320k
            SelectedQuality = "320k";
        }
        QualityComboBox.SelectionChanged += (_, _) =>
        {
            if (QualityComboBox.SelectedItem is ComboBoxItem { Tag: string tag })
                SelectedQuality = tag;
        };

        // 目录: 持久化设置 → 默认"音乐\XY Music"
        TargetDir = !string.IsNullOrEmpty(AppSettings.DownloadPath) && Directory.Exists(AppSettings.DownloadPath)
            ? AppSettings.DownloadPath
            : OnlineDownloadService.DefaultDownloadDir;
        DirText.Text = TargetDir;

        SaveLrcCheckBox.IsChecked = AppSettings.DownloadSaveLrc;
        SaveCoverCheckBox.IsChecked = AppSettings.DownloadSaveCover;
    }

    private async void ChooseDirButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            var folderPicker = new Microsoft.Windows.Storage.Pickers.FolderPicker(App.MainWindow.AppWindow.Id);
            var result = await folderPicker.PickSingleFolderAsync();
            if (result is null || string.IsNullOrEmpty(result.Path)) return;
            TargetDir = result.Path;
            DirText.Text = TargetDir;
        }
        catch (Exception ex)
        {
            App.GetLogger<DownloadDialog>().LogError(ex, "选择下载目录失败");
        }
    }

    /// <summary>确认后持久化用户选择(音质/目录/独立歌词/封面)。</summary>
    public void PersistChoices()
    {
        AppSettings.DownloadQuality = SelectedQuality;
        AppSettings.DownloadPath = TargetDir;
        AppSettings.DownloadSaveLrc = SaveLrc;
        AppSettings.DownloadSaveCover = SaveCover;
    }
}
