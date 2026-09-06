using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace WinUIMusicPlayer.Services.Plugins
{
    /// <summary>插件元数据(从插件导出对象读取)。</summary>
    public class PluginMetadata
    {
        [JsonPropertyName("platform")] public string Platform { get; set; } = string.Empty;
        [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
        [JsonPropertyName("author")] public string Author { get; set; } = string.Empty;
        [JsonPropertyName("srcUrl")] public string SrcUrl { get; set; } = string.Empty;
        [JsonPropertyName("appVersion")] public string AppVersion { get; set; } = string.Empty;
        [JsonPropertyName("supportedSearchType")] public string[] SupportedSearchType { get; set; } = [];
        [JsonPropertyName("methods")] public string[] Methods { get; set; } = [];
        [JsonPropertyName("userVariables")] public JsonUserVariable[]? UserVariables { get; set; }
    }

    public class JsonUserVariable
    {
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("hint")] public string Hint { get; set; } = string.Empty;
    }

    /// <summary>已安装插件清单条目(persisted)。</summary>
    public class PluginManifestEntry
    {
        [JsonPropertyName("hash")] public string Hash { get; set; } = string.Empty;
        [JsonPropertyName("fileName")] public string FileName { get; set; } = string.Empty;
        [JsonPropertyName("platform")] public string Platform { get; set; } = string.Empty;
        [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
        [JsonPropertyName("author")] public string Author { get; set; } = string.Empty;
        [JsonPropertyName("srcUrl")] public string SrcUrl { get; set; } = string.Empty;
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        /// <summary>插件格式: mf=MusicFree(默认), lx=洛雪用户脚本。</summary>
        [JsonPropertyName("format")] public string Format { get; set; } = "mf";
        /// <summary>LX 插件声明的音源(kw/kg/tx/wy/mg), 逗号分隔。仅 Format=lx 时有效。</summary>
        [JsonPropertyName("sources")] public string Sources { get; set; } = string.Empty;
    }

    /// <summary>插件管理页显示项。</summary>
    public partial class InstalledPluginItem : ObservableObject
    {
        public string Hash { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        [ObservableProperty] private string _platform = string.Empty;
        [ObservableProperty] private string _version = string.Empty;
        [ObservableProperty] private string _author = string.Empty;
        [ObservableProperty] private string _srcUrl = string.Empty;
        [ObservableProperty] private bool _enabled = true;
        [ObservableProperty] private string _status = string.Empty; // 空=正常, 否则加载错误
        public bool SupportsSearch { get; set; }
        /// <summary>插件声明的方法列表(仅运行中的插件可获取), 逗号分隔。</summary>
        public string MethodsText { get; set; } = string.Empty;
        /// <summary>插件声明的搜索类型(仅运行中的插件可获取), 逗号分隔。</summary>
        public string SearchTypesText { get; set; } = string.Empty;
        /// <summary>插件声明的用户变量(仅运行中的插件可获取), null=未声明。</summary>
        public JsonUserVariable[]? UserVariables { get; set; }
        /// <summary>是否声明了用户变量(控制"用户变量"菜单项显隐)。</summary>
        public bool HasUserVariables => UserVariables is { Length: > 0 };
        /// <summary>是否有可用的更新源(控制"检查更新"菜单项显隐)。</summary>
        public bool HasUpdateSource => !string.IsNullOrWhiteSpace(SrcUrl);
    }

    /// <summary>插件解析出的音源: URL + 下载所需请求头(如 B 站 Referer/Cookie, CDN 校验缺失会 403)。</summary>
    public sealed record OnlineMediaSource(string Url, Dictionary<string, string>? Headers);

    /// <summary>在线歌曲(插件搜索结果, 原始 JSON 保留用于 getMediaSource)。</summary>
    public class OnlineSong
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string Artwork { get; set; } = string.Empty;
        public double DurationSec { get; set; }
        public string Platform { get; set; } = string.Empty;
        public string PluginHash { get; set; } = string.Empty;
        public string PluginName { get; set; } = string.Empty;
        /// <summary>MusicFree musicItem 原始 JSON, 传回 getMediaSource 时使用; LX 歌曲为 musicInfo JSON。</summary>
        public string RawJson { get; set; } = string.Empty;
        /// <summary>是否为 LX 音源歌曲(经 LxMusicSdk 搜索, LX 插件解析 musicUrl)。</summary>
        public bool IsLx { get; set; }
        public string VirtualPath => OnlineMusicRegistry.BuildVirtualPath(PluginHash, Platform, Id);
    }

    /// <summary>插件搜索结果(按插件聚合, 含错误信息)。</summary>
    public class PluginSearchResult
    {
        public string PluginHash { get; set; } = string.Empty;
        public string PluginName { get; set; } = string.Empty;
        public bool IsEnd { get; set; }
        public OnlineSong[] Songs { get; set; } = [];
        public string? Error { get; set; }
    }

    /// <summary>插件网络歌单导入结果(importMusicSheet): 歌单名 + 歌曲列表, Error 非空表示失败。</summary>
    public class NetworkSheetImportResult
    {
        public string Title { get; set; } = string.Empty;
        public OnlineSong[] Songs { get; set; } = [];
        public string? Error { get; set; }
    }

    /// <summary>在线搜索目录条目(歌手/专辑/歌单), 点击后进入详情拉取歌曲列表。</summary>
    public class OnlineCatalogItem
    {
        /// <summary>平台原生 ID(LX 歌手为派生键=歌手名)。</summary>
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        /// <summary>副标题: 专辑→歌手; 歌单→创建者/曲目数。</summary>
        public string Subtitle { get; set; } = string.Empty;
        public string Artwork { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string PluginHash { get; set; } = string.Empty;
        public string PluginName { get; set; } = string.Empty;
        /// <summary>目录类型: artist / album / sheet。</summary>
        public string CatalogType { get; set; } = string.Empty;
        public bool IsLx { get; set; }
        /// <summary>LX 音源键(kw/kg/tx/wy/mg), 仅 IsLx 时有效。</summary>
        public string LxSource { get; set; } = string.Empty;
        /// <summary>原始 JSON: MF 传回 getArtistWorks/getAlbumInfo/getMusicSheetInfo; LX 为详情上下文。</summary>
        public string RawJson { get; set; } = string.Empty;
    }

    /// <summary>目录搜索结果(按插件聚合, 含错误信息)。</summary>
    public class PluginCatalogSearchResult
    {
        public string PluginHash { get; set; } = string.Empty;
        public string PluginName { get; set; } = string.Empty;
        public bool IsEnd { get; set; }
        public OnlineCatalogItem[] Items { get; set; } = [];
        public string? Error { get; set; }
    }

    /// <summary>
    /// 在线歌曲注册表: 虚拟路径(mfplugin://hash/platform/id) → OnlineSong。
    /// Music.Path 使用虚拟路径, 播放时经插件解析真实 URL。
    /// </summary>
    public static class OnlineMusicRegistry
    {
        private static readonly ConcurrentDictionary<string, OnlineSong> _songs = new();
        private static int _idCounter;

        public const string SchemePrefix = "mfplugin://";

        public static string BuildVirtualPath(string pluginHash, string platform, string id)
            => $"{SchemePrefix}{pluginHash}/{platform}/{Uri.EscapeDataString(id)}";

        public static bool IsOnlinePath(string path)
            => path is not null && path.StartsWith(SchemePrefix, StringComparison.Ordinal);

        public static void Register(OnlineSong song)
        {
            _songs[song.VirtualPath] = song;
        }

        public static bool TryGet(string path, out OnlineSong song)
        {
            song = null!;
            return IsOnlinePath(path) && _songs.TryGetValue(path, out song!);
        }

        /// <summary>为在线歌曲分配一个不与本地库冲突的负数 Id。</summary>
        public static int NextMusicId() => Interlocked.Decrement(ref _idCounter) - 1;
    }

    /// <summary>手动关联歌词的来源信息(供关联歌词弹窗展示当前关联与取消关联), 与手机版 RememberedLyricsAssociation 对齐。</summary>
    public sealed record LyricsAssociationMeta
    {
        /// <summary>来源: plugin(插件搜索) | local(本地文件导入)。</summary>
        public string Source { get; init; } = "plugin";
        public string? PluginName { get; init; }
        public string Title { get; init; } = "";
        public string Artist { get; init; } = "";
        public double DurationSec { get; init; }
    }

    /// <summary>
    /// 在线歌曲关联歌词: 虚拟路径 → 关联内容, 持久化到本地 JSON。
    /// 在线歌曲 Id 为负数临时值不能写歌词库(跨会话撞 Id), 故独立存储; 虚拟路径对同一首歌稳定(插件 hash + 平台 + 歌曲 id)。
    /// 本地歌曲的歌词内容写歌词库, 此处仅存关联元信息(键 local:{MusicId}), 供弹窗展示/取消关联。
    /// </summary>
    public static class OnlineLyricsLinkStore
    {
        public sealed record LinkedLyrics(string Lrc, string Trans, string? Krc, LyricsAssociationMeta? Meta = null);

        /// <summary>持久化条目上限, 超出丢弃(防无限膨胀; 单条歌词仅数 KB)。</summary>
        private const int MaxEntries = 500;

        private static readonly ConcurrentDictionary<string, LinkedLyrics> _links = new();
        private static readonly object _loadLock = new();
        private static bool _loaded;

        private static string StorePath => System.IO.Path.Combine(Utils.AppPaths.LocalFolder, "OnlineLyricsLinks.json");

        private static string? KeyOf(Model.Music music)
        {
            var p = music.OnlineVirtualPath;
            if (string.IsNullOrEmpty(p)) p = music.Path;
            return OnlineMusicRegistry.IsOnlinePath(p) ? p : null;
        }

        private static string LocalKey(int musicId) => $"local:{musicId}";

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_loadLock)
            {
                if (_loaded) return;
                try
                {
                    if (File.Exists(StorePath))
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(StorePath));
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            if (prop.Value.TryGetProperty("lrc", out var lrc) && lrc.ValueKind == JsonValueKind.String)
                            {
                                var trans = prop.Value.TryGetProperty("trans", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                                var krc = prop.Value.TryGetProperty("krc", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
                                _links[prop.Name] = new LinkedLyrics(lrc.GetString() ?? string.Empty, trans ?? string.Empty, krc, ReadMeta(prop.Value));
                            }
                        }
                    }
                }
                catch { /* 文件损坏时当作无关联, 不阻断加载 */ }
                _loaded = true;
            }
        }

        private static LyricsAssociationMeta? ReadMeta(JsonElement obj)
        {
            if (!obj.TryGetProperty("meta", out var m) || m.ValueKind != JsonValueKind.Object) return null;
            return new LyricsAssociationMeta
            {
                Source = m.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "plugin" : "plugin",
                PluginName = m.TryGetProperty("pluginName", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
                Title = m.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "",
                Artist = m.TryGetProperty("artist", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() ?? "" : "",
                DurationSec = m.TryGetProperty("durationSec", out var d) && d.TryGetDouble(out var dv) ? dv : 0,
            };
        }

        public static void Link(Model.Music music, string lrc, string trans, string? krc, LyricsAssociationMeta? meta = null)
        {
            EnsureLoaded(); // 先载入文件历史, 防全量写回时丢掉其他歌曲的关联
            var key = KeyOf(music);
            if (key is null) return;
            _links[key] = new LinkedLyrics(lrc, trans, krc, meta);
            _ = Task.Run(SaveAsync);
        }

        public static bool TryGet(Model.Music music, out LinkedLyrics linked)
        {
            EnsureLoaded();
            linked = null!;
            return KeyOf(music) is { } key && _links.TryGetValue(key, out linked!);
        }

        /// <summary>解除在线歌曲的手动关联歌词。</summary>
        public static void Unlink(Model.Music music)
        {
            EnsureLoaded();
            if (KeyOf(music) is not { } key) return;
            if (_links.TryRemove(key, out _)) _ = Task.Run(SaveAsync);
        }

        /// <summary>本地歌曲记录关联元信息(歌词内容在歌词库, 不在此存)。</summary>
        public static void LinkLocal(int musicId, LyricsAssociationMeta meta)
        {
            EnsureLoaded();
            if (musicId <= 0) return;
            _links[LocalKey(musicId)] = new LinkedLyrics(string.Empty, string.Empty, null, meta);
            _ = Task.Run(SaveAsync);
        }

        public static bool TryGetLocal(int musicId, out LyricsAssociationMeta meta)
        {
            EnsureLoaded();
            meta = null!;
            return musicId > 0 && _links.TryGetValue(LocalKey(musicId), out var linked) && linked.Meta is not null
                && (meta = linked.Meta) is not null;
        }

        /// <summary>解除本地歌曲的关联元信息。</summary>
        public static void UnlinkLocal(int musicId)
        {
            EnsureLoaded();
            if (musicId <= 0) return;
            if (_links.TryRemove(LocalKey(musicId), out _)) _ = Task.Run(SaveAsync);
        }

        /// <summary>全量写回(条目少、单条小, 直接覆盖写; 失败静默, 下次关联再试)。</summary>
        private static void SaveAsync()
        {
            try
            {
                // 快照后裁剪超限条目(ConcurrentDictionary 无插入序, 超限时全量裁到上限内)
                var snapshot = _links.ToArray();
                var entries = snapshot.Length > MaxEntries ? snapshot[..MaxEntries] : snapshot;

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StorePath)!);
                using var stream = File.Create(StorePath);
                using var writer = new Utf8JsonWriter(stream);
                writer.WriteStartObject();
                foreach (var (key, value) in entries)
                {
                    writer.WritePropertyName(key);
                    writer.WriteStartObject();
                    writer.WriteString("lrc", value.Lrc);
                    writer.WriteString("trans", value.Trans);
                    if (value.Krc is not null) writer.WriteString("krc", value.Krc);
                    if (value.Meta is { } m)
                    {
                        writer.WriteStartObject("meta");
                        writer.WriteString("source", m.Source);
                        if (m.PluginName is not null) writer.WriteString("pluginName", m.PluginName);
                        writer.WriteString("title", m.Title);
                        writer.WriteString("artist", m.Artist);
                        writer.WriteNumber("durationSec", m.DurationSec);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            catch { /* IO 失败不阻断关联流程 */ }
        }
    }

    /// <summary>
    /// 在线播放解析器: 把队列中的在线歌曲(VirtualPath)解析为可播放的本地缓存文件。
    /// 供在线搜索页与切歌拦截(MusicBrowseViewModel.PlayMusic)共用。
    /// </summary>
    public static class OnlinePlaybackResolver
    {
        /// <summary>当前解析令牌: 新一次点击取消上一次仍在进行的解析, 避免多个回退链并行堆积把网络与 UI 拖死。</summary>
        private static CancellationTokenSource? _resolutionCts;

        /// <summary>若 music 为未解析的在线歌曲(Path 仍然是虚拟路径)则解析音源并下载缓存, 就地更新 Path。</summary>
        public static async Task<(bool Ok, string? Error)> EnsurePlayableAsync(Model.Music music)
        {
            if (!OnlineMusicRegistry.TryGet(music.Path, out var song)) return (true, null);
            // 旧版跨端同步损坏数据(歌曲 id/原始 musicItem 丢失): 按 标题+歌手 在全部插件重新搜索匹配修复
            if (string.IsNullOrEmpty(song.Id) || string.IsNullOrEmpty(song.RawJson))
            {
                var repaired = await TryRepairBrokenSongAsync(music);
                if (repaired is null)
                    return (false, $"「{music.Title}」数据不完整(旧版同步损坏)，自动搜索匹配失败，请重新搜索添加");
                song = repaired;
                OnlineMusicRegistry.Register(repaired);
                // 后台持久化到歌单库: 修复后的完整数据随下次同步上传, 云端逐步恢复
                _ = Task.Run(() => RepairOnlineSongInDbAsync(music, repaired));
            }
            // 新解析取消旧解析: 回退链涉及全插件搜索+多候选下载, 不取消会并行堆积导致点击长时间无响应
            var previous = Interlocked.Exchange(ref _resolutionCts, new CancellationTokenSource());
            try { previous?.Cancel(); previous?.Dispose(); }
            catch { /* 已释放 */ }
            var ct = _resolutionCts!.Token;
            try
            {
                return await ResolveCoreAsync(music, song, ct);
            }
            catch (OperationCanceledException)
            {
                return (false, null); // 被更新的点击取代, 静默让位(调用方以 Error==null 识别)
            }
        }

        private static async Task<(bool Ok, string? Error)> ResolveCoreAsync(Model.Music music, OnlineSong song, CancellationToken ct)
        {
            var pluginManager = App.Services.GetRequiredService<PluginManagerService>();
            var expectedSec = song.DurationSec > 0 ? song.DurationSec : music.Duration.TotalSeconds;
            string? lastError = null;
            // VIP 试听兜底: 所有音源都只有试听版(如酷狗 60 秒)时先记下, 优先继续找完整版
            string? trialFile = null;
            OnlineSong? trialSong = null;
            // 主源逐档位回退最多 8 档(320k/high/flac/.../128k), 单档 10 秒不够用, 放宽到 20 秒
            var (source, error) = await GetMediaSourceWithTimeoutAsync(pluginManager, song, ct, TimeSpan.FromSeconds(20));
            if (source is not null)
            {
                var (cacheFile, downloadError) = await DownloadAudioWithTimeoutAsync(source.Url, source.Headers, ct, TimeSpan.FromSeconds(15));
                if (cacheFile is not null)
                {
                    if (!IsTrialVersion(cacheFile, expectedSec))
                    {
                        music.OnlineVirtualPath = song.VirtualPath;
                        music.Path = cacheFile;
                        return (true, null);
                    }
                    trialFile = cacheFile;
                    trialSong = song;
                }
                else error = downloadError;
                // LX 插件返回死链(下载 404)时用公共 API 按音质 hash 重新解析(对齐手机版兜底链路, 酷狗主场景)
                if (cacheFile is null && song.IsLx)
                {
                    var apiSource = await pluginManager.GetLxApiFallbackAsync(song);
                    if (apiSource is not null)
                    {
                        var (apiFile, _) = await DownloadAudioWithTimeoutAsync(apiSource.Url, apiSource.Headers, ct, TimeSpan.FromSeconds(15));
                        if (apiFile is not null)
                        {
                            if (!IsTrialVersion(apiFile, expectedSec))
                            {
                                music.OnlineVirtualPath = song.VirtualPath;
                                music.Path = apiFile;
                                return (true, null);
                            }
                            if (trialFile is null) { trialFile = apiFile; trialSong = song; }
                        }
                    }
                }
            }
            lastError = error;
            ct.ThrowIfCancellationRequested();
            // 主音源失败或仅试听(如酷狗代理故障/VIP 试听): 回退搜索可能耗时数十秒, 立即提示避免点击看似无响应
            View.SubView.ToastFlyout.ShowInfo($"正在为「{music.Title}」查找可用音源…");
            var candidates = await FindAlternateCandidatesAsync(music, song, ct);
            // 该插件取流已失败/超时的不再尝试(如汽水内部重试一次长达 45 秒), 整体限时 40 秒防止点击长时间无响应
            var failedPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deadline = DateTime.UtcNow.AddSeconds(40);
            foreach (var cand in candidates)
            {
                ct.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= deadline) break;
                if (string.IsNullOrEmpty(cand.PluginHash) || !failedPlugins.Add(cand.PluginHash)) continue;
                var (altSource, _) = await GetMediaSourceWithTimeoutAsync(pluginManager, cand, ct, TimeSpan.FromSeconds(12));
                if (altSource is null) continue; // 失败/超时插件已记入 failedPlugins, 其余候选自动跳过
                var (altFile, _) = await DownloadAudioWithTimeoutAsync(altSource.Url, altSource.Headers, ct, TimeSpan.FromSeconds(15));
                if (altFile is null) continue;
                if (IsTrialVersion(altFile, cand.DurationSec > 0 ? cand.DurationSec : expectedSec))
                {
                    // 也是试听版, 记为兜底后继续找完整版
                    if (trialFile is null) { trialFile = altFile; trialSong = cand; }
                    continue;
                }
                OnlineMusicRegistry.Register(cand);
                // 完整版写回歌单库: 修复数据随下次同步上传, 避免每次播放都重新跨插件搜索
                _ = Task.Run(() => RepairOnlineSongInDbAsync(music, cand));
                music.OnlineVirtualPath = cand.VirtualPath;
                music.Path = altFile;
                return (true, null);
            }
            ct.ThrowIfCancellationRequested();
            // 仅试听可用: 播放试听但不写回歌单库(避免锁死试听源, 来源恢复后自动回到完整版)
            if (trialFile is not null && trialSong is not null)
            {
                View.SubView.ToastFlyout.ShowInfo($"「{music.Title}」各音源均仅提供试听版");
                OnlineMusicRegistry.Register(trialSong);
                music.OnlineVirtualPath = trialSong.VirtualPath;
                music.Path = trialFile;
                return (true, null);
            }
            return (false, lastError);
        }

        /// <summary>主音源失败时按 标题+歌手 在其余插件中搜索同曲, 返回按匹配分降序的候选(排除已失败插件, 严格匹配标题+歌手/时长)。限时 8 秒。</summary>
        private static async Task<List<OnlineSong>> FindAlternateCandidatesAsync(Model.Music music, OnlineSong song, CancellationToken ct)
        {
            try
            {
                var pm = App.Services.GetRequiredService<PluginManagerService>();
                var hashes = pm.ActivePlugins
                    .Where(r => r.SupportsMethod("search") && r.Hash != song.PluginHash)
                    .Select(r => r.Hash).ToList();
                if (hashes.Count == 0) return [];
                var title = song.Title ?? music.Title ?? string.Empty;
                var artist = song.Artist ?? music.Author ?? string.Empty;
                var durationSec = song.DurationSec > 0 ? song.DurationSec : music.Duration.TotalSeconds;
                // 两轮检索: "标题 歌手" 优先, 无匹配时仅按标题; 搜索阶段整体限时, 慢插件(内部重试)不等它
                foreach (var query in new[] { $"{title} {artist}".Trim(), title })
                {
                    if (string.IsNullOrWhiteSpace(query)) continue;
                    var searchTask = Task.WhenAll(hashes.Select(h => SafeSearchAsync(pm, h, query)));
                    var done = await Task.WhenAny(searchTask, Task.Delay(TimeSpan.FromSeconds(8), ct));
                    if (done != searchTask) return [];
                    var candidates = searchTask.Result.Where(r => r is not null).SelectMany(r => r!).ToList();
                    if (candidates.Count == 0) continue;
                    var matches = RankCandidates(candidates, title, artist, durationSec, 5);
                    if (matches.Count > 0) return matches;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* 回退失败走原始错误流程 */ }
            return [];
        }

        /// <summary>限时取流(移植弦予"超时即放弃该插件"模型): 超时通过 CancellationToken 真正取消
        /// 插件内的逐档解析循环(Jint 档间检查), 引擎立即释放给后续调用, 不再出现旧版
        /// "超时后底层循环继续占用引擎数分钟, 该插件所有歌曲全部排队失败"的瘫痪。</summary>
        private static async Task<(OnlineMediaSource? Source, string? Error)> GetMediaSourceWithTimeoutAsync(
            PluginManagerService pm, OnlineSong song, CancellationToken ct, TimeSpan timeout)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var task = pm.GetMediaSourceAsync(song, "320k", timeoutCts.Token);
            var done = await Task.WhenAny(task, Task.Delay(timeout, timeoutCts.Token));
            if (done != task)
            {
                timeoutCts.Cancel(); // 触发插件内逐档循环的档间检查, 释放引擎
                ct.ThrowIfCancellationRequested(); // 先区分"被新点击取消"与"真超时"
                return (null, "音源接口响应超时");
            }
            timeoutCts.Cancel(); // 已完成, 正常回收(插件循环已结束, 取消无副作用)
            return await task; // 已完成: 直接取结果(含真实错误信息)
        }

        /// <summary>限时下载: 慢服务器不再无限等待(内部 HttpClient 超时 120 秒过长)。被取消时抛 OperationCanceledException。</summary>
        private static async Task<(string? File, string? Error)> DownloadAudioWithTimeoutAsync(
            string url, Dictionary<string, string>? headers, CancellationToken ct, TimeSpan timeout)
        {
            var task = DownloadAudioAsync(url, headers);
            var done = await Task.WhenAny(task, Task.Delay(timeout, ct));
            if (done != task)
            {
                ct.ThrowIfCancellationRequested(); // 先区分"被新点击取消"与"真超时"
                return (null, "音源下载超时");
            }
            return await task; // 已完成: 直接取结果(含真实错误信息)
        }

        /// <summary>按 标题+歌手 在所有支持搜索的插件中重新查找损坏歌曲, 严格匹配(标题归一化相等+歌手交集/时长接近)。</summary>
        private static async Task<OnlineSong?> TryRepairBrokenSongAsync(Model.Music music)
        {
            try
            {
                var pm = App.Services.GetRequiredService<PluginManagerService>();
                var hashes = pm.ActivePlugins.Where(r => r.SupportsMethod("search")).Select(r => r.Hash).ToList();
                if (hashes.Count == 0) return null;
                var title = music.Title ?? string.Empty;
                var artist = music.Author ?? string.Empty;
                // 两轮检索: "标题 歌手" 优先, 无匹配时仅按标题
                foreach (var query in new[] { $"{title} {artist}".Trim(), title })
                {
                    if (string.IsNullOrWhiteSpace(query)) continue;
                    var results = await Task.WhenAll(hashes.Select(h => SafeSearchAsync(pm, h, query)));
                    var candidates = results.Where(r => r is not null).SelectMany(r => r!).ToList();
                    var best = MatchBestCandidate(candidates, title, artist, music.Duration.TotalSeconds);
                    if (best is not null) return best;
                }
            }
            catch { /* 修复失败走原始错误流程 */ }
            return null;
        }

        private static async Task<List<OnlineSong>?> SafeSearchAsync(PluginManagerService pm, string hash, string query)
        {
            try
            {
                var result = await pm.SearchAsync(hash, query, 1);
                return result.Songs is { Length: > 0 } songs ? [.. songs] : null;
            }
            catch { return null; }
        }

        /// <summary>从候选中挑出与目标歌曲同名的前 N 个结果(按匹配分降序, 平台+ID 去重): 标题归一化后必须完全一致, 且歌手有交集或时长接近。</summary>
        internal static List<OnlineSong> RankCandidates(List<OnlineSong> candidates, string title, string artist, double durationSec, int maxCount)
        {
            var ranked = new List<(OnlineSong Song, int Score)>();
            var normTitle = NormalizeText(title);
            if (normTitle.Length == 0 || maxCount <= 0) return [];
            var wantArtists = SplitArtists(artist);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in candidates)
            {
                if (string.IsNullOrEmpty(c.Id) || string.IsNullOrEmpty(c.RawJson)) continue;
                var normCand = NormalizeText(c.Title ?? string.Empty);
                if (normCand.Length == 0 || normCand != normTitle) continue; // 标题必须严格一致, 避免错播
                // 同平台同 ID 只保留首个(评分更高的), 多个 LX 插件会返回同一首歌
                if (!seen.Add($"{c.Platform}|{c.Id}")) continue;
                var score = 100;
                var artistOverlap = false;
                if (wantArtists.Count > 0)
                {
                    var candArtists = SplitArtists(c.Artist ?? string.Empty);
                    var overlap = candArtists.Intersect(wantArtists).Count();
                    if (overlap > 0)
                    {
                        artistOverlap = true;
                        score += 40 * Math.Min(overlap, 2);
                    }
                }
                var durationClose = false;
                if (durationSec > 0 && c.DurationSec > 0)
                {
                    var diff = Math.Abs(c.DurationSec - durationSec);
                    if (diff <= 4) { durationClose = true; score += 30; }
                    else if (diff <= 10) score += 10;
                }
                // 无歌手且无时长信息时仅按标题(聊胜于无); 有信息则至少一项吻合
                if (wantArtists.Count > 0 && durationSec > 0 && !artistOverlap && !durationClose) continue;
                ranked.Add((c, score));
            }
            return [.. ranked.OrderByDescending(r => r.Score).Take(maxCount).Select(r => r.Song)];
        }

        /// <summary>从候选中挑出与损坏歌曲同名的最佳结果: 标题归一化后必须完全一致, 且歌手有交集或时长接近。</summary>
        internal static OnlineSong? MatchBestCandidate(List<OnlineSong> candidates, string title, string artist, double durationSec)
            => RankCandidates(candidates, title, artist, durationSec, 1).FirstOrDefault();

        /// <summary>归一化文本: 仅保留字母数字(含中文), 转小写。用于跨端标题比较。</summary>
        internal static string NormalizeText(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        /// <summary>拆分歌手为归一化集合(逗号/顿号/斜杠/&)。</summary>
        internal static System.Collections.Generic.HashSet<string> SplitArtists(string s)
        {
            var set = new System.Collections.Generic.HashSet<string>();
            foreach (var part in s.Split([',', '、', '/', '&', ';', '，', '；'], System.StringSplitOptions.RemoveEmptyEntries))
            {
                var n = NormalizeText(part);
                if (n.Length > 0) set.Add(n);
            }
            return set;
        }

        /// <summary>把修复后的歌曲数据写回歌单库中匹配的损坏条目(所有歌单内同标题同歌手的坏条目)。</summary>
        private static async Task RepairOnlineSongInDbAsync(Model.Music music, OnlineSong repaired)
        {
            try
            {
                var db = App.Services.GetRequiredService<Services.MusicDatabaseService>();
                await db.RepairOnlineSongJsonAsync(music.Title, music.Author, repaired);
            }
            catch { /* 写回失败不影响本次播放 */ }
        }

        /// <summary>下载在线音源到缓存目录, 相同 URL 直接复用已有文件。返回 (文件路径, 错误)。</summary>
        public static async Task<(string? File, string? Error)> DownloadAudioAsync(string url, Dictionary<string, string>? headers = null)
        {
            try
            {
                var cacheDir = System.IO.Path.Combine(Utils.AppPaths.LocalFolder, "OnlineCache");
                System.IO.Directory.CreateDirectory(cacheDir);
                var urlHash = System.IO.Hashing.XxHash64.HashToUInt64(System.Text.Encoding.UTF8.GetBytes(url));
                var name = urlHash.ToString("x16");
                // 从 URL 提取扩展名, 兜底 .mp3 (BASS 按内容探测格式, 扩展名仅用于命名)
                var ext = ".mp3";
                var fileName = System.IO.Path.GetFileName(url.Split('?')[0]);
                var dot = fileName.LastIndexOf('.');
                if (dot > 0 && fileName.Length - dot is >= 2 and <= 5)
                {
                    var candidate = fileName[dot..].ToLowerInvariant();
                    if (candidate.All(c => char.IsLetterOrDigit(c) || c == '.')) ext = candidate;
                }
                var file = System.IO.Path.Combine(cacheDir, name + ext);
                if (System.IO.File.Exists(file))
                {
                    // 旧版本曾把音源代理返回的 JSON 错误体当音频缓存(如酷狗代理故障), 首字节 { 或 < 一定是非音频, 删除重下
                    if (IsGarbageCacheFile(file))
                    {
                        try { System.IO.File.Delete(file); } catch { /* 删除失败走重新下载覆盖 */ }
                    }
                    else return (file, null);
                }

                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(120);
                using var resp = await SendFollowingDowngradeRedirectsAsync(http, url, headers);
                resp.EnsureSuccessStatusCode();
                // 网易云 VIP/版权受限歌曲会重定向到 404 版权页(HTML), 不能当音频保存
                var mediaType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (mediaType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                    return (null, "该歌曲暂无可用音源(可能为 VIP 或版权受限歌曲)");
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                if (bytes.Length == 0) return (null, "音源内容为空");
                // 部分音源代理(如酷狗 kg.php)服务故障时返回 HTTP 200 + JSON 错误体, 保存后 BASS 无法播放且无报错
                if (DescribeNonAudioBody(bytes) is { } bodyError) return (null, bodyError);
                await System.IO.File.WriteAllBytesAsync(file, bytes);
                return (file, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        /// <summary>缓存文件首字节为 { 或 < 时是 JSON/HTML 错误体(非音频), 属旧版本误存的垃圾缓存。</summary>
        private static bool IsGarbageCacheFile(string file)
        {
            try
            {
                using var fs = System.IO.File.OpenRead(file);
                int b = fs.ReadByte();
                return b == '{' || b == '<';
            }
            catch { return false; }
        }

        /// <summary>判断缓存文件是否为 VIP 试听版: 估算时长明显短于原曲(如酷狗 60 秒试听)且原曲不短。</summary>
        private static bool IsTrialVersion(string file, double expectedSec)
        {
            if (expectedSec < 90) return false; // 原曲本身较短时不校验, 避免把短歌误判为试听
            var est = EstimateMp3DurationSeconds(file);
            return est is > 5 && est < expectedSec * 0.55;
        }

        /// <summary>估算 MP3 文件时长(秒): VBR 读 Xing/Info 帧数, CBR 按 文件大小*8/码率。非 MP3 或解析失败返回 null。</summary>
        private static double? EstimateMp3DurationSeconds(string file)
        {
            try
            {
                using var fs = System.IO.File.OpenRead(file);
                var buf = new byte[16384];
                int read = fs.Read(buf, 0, buf.Length);
                if (read < 8) return null;
                int i = 0;
                // 跳过 ID3v2 标签
                if (buf[0] == (byte)'I' && buf[1] == (byte)'D' && buf[2] == (byte)'3')
                {
                    if (read < 10) return null;
                    int tagSize = (buf[6] << 21) | (buf[7] << 14) | (buf[8] << 7) | buf[9];
                    i = 10 + tagSize;
                    if (i < 0 || i + 8 >= read) return null;
                }
                // 找帧同步字
                while (i + 4 <= read)
                {
                    if (buf[i] == 0xFF && (buf[i + 1] & 0xE0) == 0xE0) break;
                    i++;
                }
                if (i + 4 > read) return null;
                // MP3 帧头: 字节0=同步FF, 字节1=[同步3位|版本2位|层2位|保护1位], 字节2=[码率4位|采样率2位|...]
                int hdr1 = buf[i + 1], hdr2 = buf[i + 2];
                int versionBits = (hdr1 >> 3) & 3;
                int layerBits = (hdr1 >> 1) & 3;
                if (versionBits == 1 || layerBits != 1) return null; // 保留版本 / 非 Layer III
                int bitIdx = (hdr2 >> 4) & 0xF;
                int srIdx = (hdr2 >> 2) & 3;
                if (bitIdx is 0 or 15 || srIdx == 3) return null;
                // VBR: Xing/Info 头内有总帧数, 时长最准
                for (int j = i + 4; j + 12 < read && j <= i + 64; j++)
                {
                    bool isXing = buf[j] == (byte)'X' && buf[j + 1] == (byte)'i' && buf[j + 2] == (byte)'n' && buf[j + 3] == (byte)'g';
                    bool isInfo = buf[j] == (byte)'I' && buf[j + 1] == (byte)'n' && buf[j + 2] == (byte)'f' && buf[j + 3] == (byte)'o';
                    if (!isXing && !isInfo) continue;
                    int flags = (buf[j + 4] << 24) | (buf[j + 5] << 16) | (buf[j + 6] << 8) | buf[j + 7];
                    if ((flags & 1) != 0)
                    {
                        int frames = (buf[j + 8] << 24) | (buf[j + 9] << 16) | (buf[j + 10] << 8) | buf[j + 11];
                        if (frames > 0)
                        {
                            double sampleRate = SampleRate(versionBits, srIdx);
                            if (sampleRate > 0)
                                return frames * (versionBits == 3 ? 1152 : 576) / sampleRate;
                        }
                    }
                    break;
                }
                // CBR: 音频数据总大小 * 8 / 码率
                int[] bitratesV1 = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
                int[] bitratesV2 = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
                int bitrate = versionBits == 3 ? bitratesV1[bitIdx] : bitratesV2[bitIdx];
                if (bitrate <= 0) return null;
                var len = new System.IO.FileInfo(file).Length;
                return (len - i) * 8.0 / (bitrate * 1000);
            }
            catch { return null; }
        }

        /// <summary>MP3 采样率表: versionBits 3=MPEG1, 2=MPEG2, 0=MPEG2.5。</summary>
        private static double SampleRate(int versionBits, int srIdx) => (versionBits, srIdx) switch
        {
            (3, 0) => 44100, (3, 1) => 48000, (3, 2) => 32000,
            (2, 0) => 22050, (2, 1) => 24000, (2, 2) => 16000,
            (0, 0) => 11025, (0, 1) => 12000, (0, 2) => 8000,
            _ => 0,
        };

        /// <summary>
        /// 识别下载到的非音频响应体并生成可读错误: 音源代理故障常以 HTTP 200 + JSON 返回错误
        /// (如酷狗代理 {"code":201,"msg":"error"}), 不拦截会被当音频缓存导致播放无提示失败。非错误体返回 null。
        /// </summary>
        internal static string? DescribeNonAudioBody(byte[] bytes)
        {
            if (bytes.Length == 0) return null;
            var first = bytes[0];
            if (first == '<')
                return "该歌曲暂无可用音源(可能为 VIP 或版权受限歌曲)";
            if (first != '{') return null;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(bytes));
                var root = doc.RootElement;
                string? msg = null;
                if (root.TryGetProperty("msg", out var m) && m.ValueKind == JsonValueKind.String) msg = m.GetString();
                else if (root.TryGetProperty("message", out var m2) && m2.ValueKind == JsonValueKind.String) msg = m2.GetString();
                var code = root.TryGetProperty("code", out var c) && (c.ValueKind == JsonValueKind.Number || c.ValueKind == JsonValueKind.String)
                    ? (c.ValueKind == JsonValueKind.Number ? c.GetDouble().ToString() : c.GetString()) : null;
                if (code is not null || msg is not null)
                    return $"音源服务暂不可用{(code is null ? "" : $"({code})")}{(msg is null ? "" : $": {msg}")}";
                return "音源服务返回了错误数据";
            }
            catch { return "音源服务返回了错误数据"; }
        }

        /// <summary>
        /// 发送 GET 并手动跟随降级重定向(https→http): SocketsHttpHandler 出于安全策略不自动跟随降级跳转,
        /// 而网易云 outer/url 对所有歌曲一律 302 到 http CDN, 不补跳会直接把 302 当最终响应导致播放失败。
        /// 自动重定向能处理的跳转(如 http→https)仍由 HttpClient 完成, 这里只补它拒绝的那一跳, 最多 10 跳防环。
        /// </summary>
        public static async Task<System.Net.Http.HttpResponseMessage> SendFollowingDowngradeRedirectsAsync(
            System.Net.Http.HttpClient http, string url, Dictionary<string, string>? headers,
            System.Net.Http.HttpCompletionOption completion = System.Net.Http.HttpCompletionOption.ResponseContentRead)
        {
            var current = url;
            for (int hop = 0; ; hop++)
            {
                var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, current);
                ApplyMediaHeaders(req, current, headers);
                var resp = await http.SendAsync(req, completion);
                // 3xx 且带 Location: 自动跟随被拒(降级)时手动补跳
                if ((int)resp.StatusCode is >= 300 and < 400
                    && resp.Headers.Location is not null && hop < 10)
                {
                    current = new Uri(new Uri(current), resp.Headers.Location).ToString();
                    resp.Dispose();
                    continue;
                }
                return resp;
            }
        }

        /// <summary>音源下载公共请求头: 插件返回的 headers 优先(如 B 站 Referer/Cookie), B 站域名兜底注入, 全站附带浏览器 UA。</summary>
        public static void ApplyMediaHeaders(System.Net.Http.HttpRequestMessage req, string url, Dictionary<string, string>? headers)
        {
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (headers is not null)
                foreach (var (k, v) in headers)
                    if (!string.IsNullOrWhiteSpace(k) && v is not null) merged[k.Trim()] = v.Trim();
            // B 站 CDN 校验 Referer/Origin: 插件未返回 headers 时按域名兜底
            if (!merged.ContainsKey("Referer") && IsBilibiliMediaHost(url))
            {
                merged["Referer"] = "https://www.bilibili.com/";
                merged.TryAdd("Origin", "https://www.bilibili.com");
            }
            // 空 UA 会被多数 CDN 拒绝
            merged.TryAdd("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            foreach (var (k, v) in merged)
                req.Headers.TryAddWithoutValidation(k, v);
        }

        /// <summary>B 站音源/图片 CDN 域名(upos 镜像, api, 图床)。</summary>
        private static bool IsBilibiliMediaHost(string url)
        {
            try
            {
                var host = new Uri(url).Host.ToLowerInvariant();
                return host.EndsWith("bilibili.com") || host.EndsWith("bilivideo.com")
                    || host.EndsWith("bilivideo.cn") || host.EndsWith("hdslb.com")
                    || host.EndsWith("acgvideo.com") || host.EndsWith("biliapi.net")
                    || host.EndsWith("bilibili.tv");
            }
            catch { return false; }
        }
    }
}
