using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WinUIMusicPlayer.Model;
using WinUIMusicPlayer.Services;
using WinUIMusicPlayer.Services.Plugins;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.ViewModel;

namespace WinUIMusicPlayer.View.SubView;

/// <summary>搜索结果候选条目(供 ListView 绑定, 持有 OnlineSong 供插件拉词)。</summary>
public sealed class LyricsCandidateItem
{
    public required OnlineSong Song { get; init; }
    public required string Title { get; init; }
    public required string SubTitle { get; init; }
    public required string Duration { get; init; }
}

/// <summary>关联歌词对话框: 走已安装插件搜索歌曲候选 → 点击候选拉取歌词关联到当前歌曲; 底部保留本地文件导入入口。</summary>
public sealed partial class LyricsLinkDialog : ContentDialog
{
    private readonly Music _music;
    private readonly ILogger<LyricsLinkDialog> _logger;
    private readonly PluginManagerService _pluginManager;
    private CancellationTokenSource? _searchCts;
    private bool _sourcesLoading;

    public LyricsLinkDialog(Music music)
        {
            InitializeComponent();
            _music = music;
            _logger = App.GetLogger<LyricsLinkDialog>();
            _pluginManager = App.Services.GetRequiredService<PluginManagerService>();
            Title = ToolUtils.GetString("LinkLyricsTitle");
            CloseButtonText = ToolUtils.GetString("DialogClose");
            ImportLocalButton.Content = ToolUtils.GetString("LinkLyricsImportLocal");
            AssociatedTitleText.Text = ToolUtils.GetString("LinkLyricsLinkedTitle");
            UnlinkButton.Content = ToolUtils.GetString("LinkLyricsUnlink");

            SongTitleText.Text = music.Title;
            SongArtistText.Text = music.Author;
            SearchBox.Text = string.IsNullOrWhiteSpace(music.Author) ? music.Title : $"{music.Title} {music.Author}";
            SearchBox.PlaceholderText = ToolUtils.GetString("LinkLyricsSearchPlaceholder");

            LoadCurrentAssociation();
            LoadSources();

            Loaded += (_, _) =>
            {
                // 有可用插件时进入即自动搜索
                if (SourceComboBox.Items.Count > 0) _ = RunSearchAsync();
            };
        }

        private bool IsOnlineMusic => OnlineMusicRegistry.IsOnlinePath(_music.OnlineVirtualPath ?? _music.Path);

        /// <summary>加载当前歌曲的手动关联(在线: 虚拟路径关联; 本地: 关联元信息), 无关联时卡片隐藏。</summary>
        private void LoadCurrentAssociation()
        {
            if (IsOnlineMusic)
            {
                if (OnlineLyricsLinkStore.TryGet(_music, out var linked) && linked.Meta is { } meta)
                    ShowAssociationCard(meta);
            }
            else if (OnlineLyricsLinkStore.TryGetLocal(_music.Id, out var meta))
            {
                ShowAssociationCard(meta);
            }
        }

        /// <summary>展示"已关联歌词"卡片: 来源·标题 / 歌手·时长(与手机版 _AssociatedLyricsCard 一致)。</summary>
        private void ShowAssociationCard(LyricsAssociationMeta meta)
        {
            var source = meta.Source == "plugin"
                ? (string.IsNullOrWhiteSpace(meta.PluginName)
                    ? ToolUtils.GetString("LinkLyricsPluginSource")
                    : string.Format(ToolUtils.GetString("LinkLyricsPluginSourceWith"), meta.PluginName))
                : ToolUtils.GetString("LinkLyricsLocalSource");
            var title = string.IsNullOrWhiteSpace(meta.Title) ? ToolUtils.GetString("LinkLyricsCurrentSong") : meta.Title;
            var artist = string.IsNullOrWhiteSpace(meta.Artist) ? ToolUtils.GetString("LinkLyricsUnknownArtist") : meta.Artist;
            var duration = meta.DurationSec > 0 ? TimeSpan.FromSeconds(meta.DurationSec).ToString(@"m\:ss") : "--:--";
            AssociatedSourceText.Text = $"{source} · {title}";
            AssociatedSubText.Text = $"{artist} · {duration}";
            AssociationPanel.Visibility = Visibility.Visible;
        }

        /// <summary>取消关联: 在线歌曲移除会话级关联; 本地歌曲清除歌词库条目并复位搜索标志(恢复自动探测), 随后刷新歌词。</summary>
        private async void UnlinkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (IsOnlineMusic)
                {
                    OnlineLyricsLinkStore.Unlink(_music);
                }
                else
                {
                    var db = App.Services.GetRequiredService<MusicDatabaseService>();
                    await db.ClearLyricsAsync(_music.Id);
                    _music.IsLrcSearched = false;
                    _music.IsKrcSearched = false;
                    await db.UpdateMusicInfo(_music);
                    OnlineLyricsLinkStore.UnlinkLocal(_music.Id);
                }
                AssociationPanel.Visibility = Visibility.Collapsed;
                ToastFlyout.ShowSuccess(ToolUtils.GetString("LinkLyricsUnlinked"));
                // 重新探测歌词(回到自动搜索/本地同名文件链路)
                App.Services.GetRequiredService<AppViewModel>().LoadLyricsToUI(_music);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"取消关联歌词失败: {ex.Message}");
            }
        }

    /// <summary>来源下拉 = 已启用插件(MF 按平台名, LX 按音源), 顺序与在线搜索页 Tab 一致(= 插件管理页排序)。</summary>
    private void LoadSources()
    {
        _sourcesLoading = true;
        try
        {
            var sources = new List<(string Hash, string Name)>();
            var usedLxSources = new HashSet<string>();
            // MF 插件一个插件一个来源; LX 插件按其覆盖音源生成多个来源(重复音源先到先得), 与在线搜索页 Tab 生成逻辑一致
            foreach (var (mf, lx) in _pluginManager.GetActiveRuntimesInOrder())
            {
                if (mf is not null)
                {
                    if (mf.SupportsMethod("search"))
                        sources.Add((mf.Hash, mf.Metadata.Platform));
                }
                else if (lx is not null)
                {
                    foreach (var source in LxSources.StandardOrder)
                    {
                        if (usedLxSources.Contains(source) || !lx.SupportsSource(source)) continue;
                        usedLxSources.Add(source);
                        sources.Add(($"lx:{source}", LxSources.DisplayName(source)));
                    }
                }
            }

            foreach (var (hash, name) in sources)
                SourceComboBox.Items.Add(new ComboBoxItem { Content = name, Tag = hash });
            if (SourceComboBox.Items.Count > 0)
            {
                SourceComboBox.SelectedIndex = 0;
            }
            else
            {
                // 没有任何可用插件: 提示, 仍可走底部本地文件导入
                ToastFlyout.ShowWarning(ToolUtils.GetString("OnlineSearchNoPlugins"));
                SearchBox.IsEnabled = false;
                SearchButton.IsEnabled = false;
            }
        }
        finally
        {
            _sourcesLoading = false;
        }
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            _ = RunSearchAsync();
        }
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e) => _ = RunSearchAsync();

    private void SourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_sourcesLoading) return;
        if (string.IsNullOrWhiteSpace(SearchBox.Text)) return;
        _ = RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        var keyword = SearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(keyword)) return;
        if (SourceComboBox.SelectedItem is not ComboBoxItem { Tag: string hash }) return;

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        ResultList.Items.Clear();
        ResultList.IsEnabled = true;
        ToastFlyout.ShowInfo(ToolUtils.GetString("LinkLyricsSearching"));

        try
        {
            // 与在线搜索页同款分发: lx: 前缀走 LX 音源 SDK, 其余走 MusicFree 插件 search
            var result = hash.StartsWith("lx:", StringComparison.Ordinal)
                ? await _pluginManager.SearchLxAsync(hash[3..], keyword, 1)
                : await _pluginManager.SearchAsync(hash, keyword, 1);
            ct.ThrowIfCancellationRequested();

            if (result.Error is not null)
            {
                ToastFlyout.ShowError($"{ToolUtils.GetString("LinkLyricsSearchFailed")}: {result.Error}");
                return;
            }
            if (result.Songs is not { Length: > 0 })
            {
                ToastFlyout.ShowInfo(ToolUtils.GetString("LinkLyricsNoResults"));
                return;
            }

            foreach (var s in result.Songs)
            {
                var duration = s.DurationSec > 0
                    ? TimeSpan.FromSeconds(s.DurationSec).ToString(@"mm\:ss")
                    : string.Empty;
                ResultList.Items.Add(new LyricsCandidateItem
                {
                    Song = s,
                    Title = s.Title,
                    SubTitle = string.IsNullOrEmpty(s.Album) ? s.Artist : $"{s.Artist} · {s.Album}",
                    Duration = duration,
                });
            }
        }
        catch (OperationCanceledException)
        {
            // 被新一轮搜索取消, 不提示
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"关联歌词搜索失败: {ex.Message}");
            ToastFlyout.ShowError(ToolUtils.GetString("LinkLyricsSearchFailed"));
        }
    }

    private async void ResultList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not LyricsCandidateItem item) return;
        ResultList.IsEnabled = false;
        ToastFlyout.ShowInfo(ToolUtils.GetString("LinkLyricsLinking"));

        try
        {
            // 按候选拉词: MF 走 getLyric, LX 走插件 lyric 动作
            var (lyrics, trans, error) = await _pluginManager.GetLyricAsync(item.Song);
            if (string.IsNullOrWhiteSpace(lyrics))
            {
                ResultList.IsEnabled = true;
                var reason = string.IsNullOrEmpty(error)
                    ? ToolUtils.GetString("LinkLyricsLinkFailed")
                    : $"{ToolUtils.GetString("LinkLyricsLinkFailed")}: {error}";
                ToastFlyout.ShowError(reason);
                return;
            }

            // 在线歌曲: 会话级关联(虚拟路径为键, 不落库); 本地歌曲: 写入歌词库(清旧 Krc, 其优先级高于 Lyrics)
            var sourceName = (SourceComboBox.SelectedItem as ComboBoxItem)?.Content as string;
            var meta = new LyricsAssociationMeta
            {
                Source = "plugin",
                PluginName = string.IsNullOrWhiteSpace(sourceName) ? item.Song.PluginName : sourceName,
                Title = item.Title,
                Artist = item.Song.Artist,
                DurationSec = item.Song.DurationSec,
            };
            if (IsOnlineMusic)
            {
                OnlineLyricsLinkStore.Link(_music, lyrics, trans ?? string.Empty, null, meta);
            }
            else
            {
                var db = App.Services.GetRequiredService<MusicDatabaseService>();
                await db.SaveLyricsAsync(_music.Id, lyrics, trans, null, null);
                // 标记已搜索, 防止自动联网搜索覆盖用户手动关联
                _music.IsLrcSearched = true;
                _music.IsKrcSearched = true;
                await db.UpdateMusicInfo(_music);
                OnlineLyricsLinkStore.LinkLocal(_music.Id, meta);
            }

            Hide();
            // 立即按新关联刷新歌词
            App.Services.GetRequiredService<AppViewModel>().LoadLyricsToUI(_music);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"关联歌词写入失败: {ex.Message}");
            ResultList.IsEnabled = true;
            ToastFlyout.ShowError(ToolUtils.GetString("LinkLyricsLinkFailed"));
        }
    }

    /// <summary>本地文件导入(保留原有能力): 选择 .lrc/.krc/.qrc/.txt → 按格式写入歌词库。</summary>
    private async void ImportLocalButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(App.MainWindow.AppWindow.Id)
            {
                SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.MusicLibrary,
            };
            picker.FileTypeFilter.Add(".lrc");
            picker.FileTypeFilter.Add(".krc");
            picker.FileTypeFilter.Add(".qrc");
            picker.FileTypeFilter.Add(".txt");
            var picked = await picker.PickSingleFileAsync();
            if (picked is null) return;

            string content;
            try { content = await File.ReadAllTextAsync(picked.Path); }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"关联歌词: 读取文件失败 {picked.Path}");
                return;
            }
            if (string.IsNullOrWhiteSpace(content)) return;

            // 时间轴特征校验: 普通行 [mm:ss.xx] / 增强逐字 <mm:ss.xx> / KRC 行 [int,int]
            if (!Regex.IsMatch(content, @"\[\d{1,2}:\d{2}")
                && !Regex.IsMatch(content, @"<\d{1,2}:\d{2}[\.:]\d{2,3}>")
                && !Regex.IsMatch(content, @"^\[\d+,\d+\]", RegexOptions.Multiline))
            {
                ToastFlyout.ShowError(ToolUtils.GetString("NoRecognizableLyrics"));
                return;
            }

            // KRC(^[int,int]) 与 QRC(<mm:ss.xx>) 走 Krc 通道(加载链路逐字优先), 其余入 LRC; 二者互斥防残留压制
            string? lyrics = null, krc = null;
            bool isKrcOrQrc =
                Regex.IsMatch(content, @"^\[\d+,\d+\]", RegexOptions.Multiline)
                || Regex.IsMatch(content, @"<\d{2}:\d{2}\.\d{2,3}>");
            if (isKrcOrQrc) krc = content;
            else lyrics = content;

            // 在线歌曲: 会话级关联; 本地歌曲: 写入歌词库
            var localMeta = new LyricsAssociationMeta
            {
                Source = "local",
                Title = _music.Title,
                Artist = _music.Author,
                DurationSec = _music.Duration.TotalSeconds,
            };
            if (IsOnlineMusic)
            {
                OnlineLyricsLinkStore.Link(_music, lyrics ?? string.Empty, string.Empty, krc, localMeta);
            }
            else
            {
                var db = App.Services.GetRequiredService<MusicDatabaseService>();
                var (_, trans, _, tKrc) = await db.GetLyricsAsync(_music.Id);
                await db.SaveLyricsAsync(_music.Id, lyrics, trans, krc, tKrc);
                _music.IsLrcSearched = true;
                _music.IsKrcSearched = true;
                await db.UpdateMusicInfo(_music);
                OnlineLyricsLinkStore.LinkLocal(_music.Id, localMeta);
            }

            Hide();
            App.Services.GetRequiredService<AppViewModel>().LoadLyricsToUI(_music);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关联歌词本地导入异常");
        }
    }
}
