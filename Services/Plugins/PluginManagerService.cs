using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinUIMusicPlayer.Utils;

namespace WinUIMusicPlayer.Services.Plugins
{
    /// <summary>
    /// 插件管理服务: 安装/启用/卸载/持久化 + 在线搜索/解析音源。
    /// Jint 引擎非线程安全, 所有插件调用通过 Task.Run 在后台线程执行。
    /// </summary>
    public class PluginManagerService
    {
        private readonly ILogger<PluginManagerService> _logger;
        private readonly ConcurrentDictionary<string, PluginRuntime> _runtimes = new();
        /// <summary>LX(洛雪)插件运行时: 搜索走内置 SDK, 插件仅解析 musicUrl/lyric/pic。</summary>
        private readonly ConcurrentDictionary<string, LxPluginRuntime> _lxRuntimes = new();
        private readonly List<PluginManifestEntry> _manifest = new();
        private readonly object _manifestGate = new();
        private bool _initialized;

        private static string PluginDir => Path.Combine(AppPaths.LocalFolder, "Plugins");
        private static string StorageDir => Path.Combine(PluginDir, "storage");
        private static string ManifestFile => Path.Combine(PluginDir, "plugins.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

        public event Action? PluginsChanged;

        public PluginManagerService(ILogger<PluginManagerService> logger)
        {
            _logger = logger;
        }

        public IEnumerable<PluginRuntime> ActivePlugins => _runtimes.Values;

        public IEnumerable<LxPluginRuntime> ActiveLxPlugins => _lxRuntimes.Values;

        /// <summary>按清单(插件管理页)顺序返回启用插件的运行时(MF 与 LX 二选一), 供在线搜索 Tab 排序。</summary>
        public List<(PluginRuntime? Mf, LxPluginRuntime? Lx)> GetActiveRuntimesInOrder()
        {
            lock (_manifestGate)
            {
                return _manifest
                    .Where(e => e.Enabled)
                    .Select(e =>
                    {
                        _runtimes.TryGetValue(e.Hash, out var mf);
                        _lxRuntimes.TryGetValue(e.Hash, out var lx);
                        return (Mf: mf, Lx: lx);
                    })
                    .Where(t => t.Mf is not null || t.Lx is not null)
                    .ToList();
            }
        }

        /// <summary>按插件管理页拖拽后的顺序重排插件清单并持久化(同时决定在线搜索 Tab 顺序)。</summary>
        public (bool Ok, string? Error) ReorderPlugins(IReadOnlyList<string> orderedHashes)
        {
            lock (_manifestGate)
            {
                if (orderedHashes.Count != _manifest.Count ||
                    orderedHashes.Any(h => _manifest.All(e => e.Hash != h)))
                    return (false, "插件列表已变化, 请刷新后重试");
                var map = _manifest.ToDictionary(e => e.Hash);
                _manifest.Clear();
                foreach (var hash in orderedHashes)
                    _manifest.Add(map[hash]);
                SaveManifestLocked();
            }
            PluginsChanged?.Invoke();
            return (true, (string?)null);
        }

        /// <summary>启动时加载所有已安装插件(后台线程执行 Jint)。</summary>
        public Task InitializeAsync()
        {
            return Task.Run(() =>
            {
                lock (_manifestGate)
                {
                    _manifest.Clear();
                    try
                    {
                        if (File.Exists(ManifestFile))
                        {
                            var list = JsonSerializer.Deserialize<List<PluginManifestEntry>>(File.ReadAllText(ManifestFile), JsonOptions);
                            if (list is not null) _manifest.AddRange(list);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "读取插件清单失败");
                    }
                    _initialized = true;
                }

                foreach (var entry in _manifest.Where(e => e.Enabled).ToList())
                    LoadPluginRuntime(entry);
            });
        }

        private void LoadPluginRuntime(PluginManifestEntry entry)
        {
            try
            {
                var file = Path.Combine(PluginDir, entry.FileName);
                if (!File.Exists(file))
                {
                    _logger.LogWarning("插件文件不存在: {File}", entry.FileName);
                    return;
                }
                var code = File.ReadAllText(file);
                if (entry.Format == "lx" || LxPluginRuntime.IsLxScript(code))
                {
                    var lx = LxPluginRuntime.Load(code, entry.Hash, _logger);
                    _lxRuntimes[entry.Hash] = lx;
                    return;
                }
                var runtime = PluginRuntime.Load(
                    code, entry.Hash,
                    Path.Combine(StorageDir, entry.Hash[..8] + ".json"),
                    App.GetAppVersion(),
                    PluginUserVariablesStore.Get(entry.Platform), _logger);
                _runtimes[entry.Hash] = runtime;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载插件 {Platform} 失败: {Message}", entry.Platform, ex.Message);
            }
        }

        private void SaveManifestLocked()
        {
            try
            {
                Directory.CreateDirectory(PluginDir);
                File.WriteAllText(ManifestFile, JsonSerializer.Serialize(_manifest, JsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存插件清单失败");
            }
        }

        /// <summary>从插件源码安装插件。返回 (hash, error)。</summary>
        public Task<(string? Hash, string? Error)> InstallFromCodeAsync(string code)
            => Task.Run(() => InstallFromCode(code));

        public Task<(string? Hash, string? Error)> InstallFromFileAsync(string filePath)
        {
            return Task.Run(() =>
            {
                try
                {
                    var code = File.ReadAllText(filePath);
                    return InstallFromCode(code);
                }
                catch (Exception ex)
                {
                    return ((string?)null, $"无法读取文件: {ex.Message}");
                }
            });
        }

        private (string? Hash, string? Error) InstallFromCode(string code)
        {
            if (!_initialized) return (null, "插件服务未初始化");
            if (string.IsNullOrWhiteSpace(code)) return (null, "插件代码为空");

            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
            var fileName = hash[..16] + ".js";
            if (LxPluginRuntime.IsLxScript(code))
                return InstallLxFromCode(code, hash, fileName);

            PluginRuntime? runtime = null;
            PluginManifestEntry? entry = null;
            try
            {
                Directory.CreateDirectory(PluginDir);
                Directory.CreateDirectory(StorageDir);
                runtime = PluginRuntime.Load(code, hash, Path.Combine(StorageDir, hash[..8] + ".json"), App.GetAppVersion(), null, _logger);
                // 平台已确定: 存在该平台的用户变量时带变量重载, 保证安装即生效
                var savedVars = PluginUserVariablesStore.Get(runtime.Metadata.Platform);
                if (savedVars.Count > 0)
                {
                    runtime.Dispose();
                    runtime = PluginRuntime.Load(code, hash, Path.Combine(StorageDir, hash[..8] + ".json"), App.GetAppVersion(), savedVars, _logger);
                }
                File.WriteAllText(Path.Combine(PluginDir, fileName), code);
                entry = new PluginManifestEntry
                {
                    Hash = hash,
                    FileName = fileName,
                    Platform = runtime.Metadata.Platform,
                    Version = runtime.Metadata.Version,
                    Author = runtime.Metadata.Author,
                    SrcUrl = runtime.Metadata.SrcUrl,
                    Enabled = true,
                };
            }
            catch (Exception ex)
            {
                runtime?.Dispose();
                return (null, $"插件加载失败: {ex.Message}");
            }

            lock (_manifestGate)
            {
                _manifest.RemoveAll(e => e.Hash == hash);
                _manifest.Add(entry);
                SaveManifestLocked();
            }
            _runtimes.AddOrUpdate(hash, runtime, (_, old) => { old.Dispose(); return runtime; });
            PluginsChanged?.Invoke();
            return (hash, null);
        }

        /// <summary>安装 LX(洛雪)用户脚本: 搜索走内置 SDK, 插件声明音源(kw/kg/tx/wy/mg)负责解析。</summary>
        private (string? Hash, string? Error) InstallLxFromCode(string code, string hash, string fileName)
        {
            LxPluginRuntime? lx = null;
            try
            {
                Directory.CreateDirectory(PluginDir);
                lx = LxPluginRuntime.Load(code, hash, _logger);
                if (lx.Sources.Count == 0)
                {
                    lx.Dispose();
                    return (null, "LX 插件未声明可用的音源(musicUrl/lyric/pic)");
                }
                File.WriteAllText(Path.Combine(PluginDir, fileName), code);
                var entry = new PluginManifestEntry
                {
                    Hash = hash,
                    FileName = fileName,
                    Platform = lx.ScriptInfo.Name,
                    Version = lx.ScriptInfo.Version,
                    Author = lx.ScriptInfo.Author,
                    SrcUrl = lx.ScriptInfo.DownloadUrl,
                    Enabled = true,
                    Format = "lx",
                    Sources = string.Join(",", lx.Sources.Keys),
                };
                lock (_manifestGate)
                {
                    _manifest.RemoveAll(e => e.Hash == hash);
                    _manifest.Add(entry);
                    SaveManifestLocked();
                }
                _lxRuntimes.AddOrUpdate(hash, lx, (_, old) => { old.Dispose(); return lx; });
                PluginsChanged?.Invoke();
                return (hash, null);
            }
            catch (Exception ex)
            {
                lx?.Dispose();
                return (null, $"LX 插件加载失败: {ex.Message}");
            }
        }

        public Task<(bool Ok, string? Error)> UninstallAsync(string hash)
        {
            return Task.Run(() =>
            {
                string? platform = null;
                lock (_manifestGate)
                {
                    var entry = _manifest.FirstOrDefault(e => e.Hash == hash);
                    if (entry is null) return (false, "插件不存在");
                    platform = entry.Platform;
                    _manifest.Remove(entry);
                    SaveManifestLocked();
                    try { File.Delete(Path.Combine(PluginDir, entry.FileName)); } catch { }
                    try { File.Delete(Path.Combine(StorageDir, hash[..8] + ".json")); } catch { }
                }
                // 同步清理该平台的用户变量, 避免残留
                if (!string.IsNullOrEmpty(platform)) PluginUserVariablesStore.Remove(platform);
                if (_runtimes.TryRemove(hash, out var runtime)) runtime.Dispose();
                if (_lxRuntimes.TryRemove(hash, out var lxRuntime)) lxRuntime.Dispose();
                PluginsChanged?.Invoke();
                return (true, (string?)null);
            });
        }

        /// <summary>一键卸载全部插件(MF+LX): 清空清单/脚本/存储/用户变量并释放全部运行时。</summary>
        public Task<(int Count, string? Error)> UninstallAllAsync()
        {
            return Task.Run(() =>
            {
                List<PluginManifestEntry> entries;
                lock (_manifestGate)
                {
                    entries = [.. _manifest];
                    _manifest.Clear();
                    SaveManifestLocked();
                }
                foreach (var entry in entries)
                {
                    try { File.Delete(Path.Combine(PluginDir, entry.FileName)); } catch { }
                    try { File.Delete(Path.Combine(StorageDir, entry.Hash[..8] + ".json")); } catch { }
                    if (!string.IsNullOrEmpty(entry.Platform)) PluginUserVariablesStore.Remove(entry.Platform);
                }
                foreach (var (_, runtime) in _runtimes) runtime.Dispose();
                _runtimes.Clear();
                foreach (var (_, lxRuntime) in _lxRuntimes) lxRuntime.Dispose();
                _lxRuntimes.Clear();
                PluginsChanged?.Invoke();
                return (entries.Count, (string?)null);
            });
        }

        public Task<(bool Ok, string? Error)> SetEnabledAsync(string hash, bool enabled)
        {
            return Task.Run(() =>
            {
                lock (_manifestGate)
                {
                    var entry = _manifest.FirstOrDefault(e => e.Hash == hash);
                    if (entry is null) return (false, "插件不存在");
                    entry.Enabled = enabled;
                    SaveManifestLocked();
                }
                if (enabled)
                {
                    lock (_manifestGate)
                    {
                        var entry = _manifest.First(e => e.Hash == hash);
                        LoadPluginRuntime(entry);
                    }
                }
                else if (_runtimes.TryRemove(hash, out var runtime))
                {
                    runtime.Dispose();
                }
                else if (_lxRuntimes.TryRemove(hash, out var lxRuntime))
                {
                    lxRuntime.Dispose();
                }
                PluginsChanged?.Invoke();
                return (true, (string?)null);
            });
        }

        /// <summary>读取所有已安装插件(清单条目+脚本源码), 账号云同步上传用。</summary>
        public List<(PluginManifestEntry Entry, string Code)> GetPluginSyncItems()
        {
            var result = new List<(PluginManifestEntry, string)>();
            lock (_manifestGate)
            {
                foreach (var e in _manifest)
                {
                    try
                    {
                        var file = Path.Combine(PluginDir, e.FileName);
                        if (!File.Exists(file)) continue;
                        var code = File.ReadAllText(file);
                        if (Encoding.UTF8.GetByteCount(code) > 5 * 1024 * 1024) continue;
                        result.Add((e, code));
                    }
                    catch { /* 单个损坏插件不阻止其它插件同步 */ }
                }
            }
            result.Sort((a, b) => string.CompareOrdinal(a.Item1.Hash, b.Item1.Hash));
            return result;
        }

        /// <summary>获取管理页展示用的插件列表(含已禁用与加载失败的)。</summary>
        public List<InstalledPluginItem> GetInstalledItems()
        {
            lock (_manifestGate)
            {
                return _manifest.Select(e =>
                {
                    var active = _runtimes.TryGetValue(e.Hash, out var rt);
                    var lxActive = _lxRuntimes.TryGetValue(e.Hash, out var lx);
                    var runtimeActive = active || lxActive;
                    InstalledPluginItem item;
                    if (e.Format == "lx")
                    {
                        // LX 插件: 搜索走内置 SDK, 元数据显示音源与支持的 action
                        var actions = lxActive
                            ? string.Join(", ", lx!.Sources.Values.SelectMany(s => s.Actions).Distinct())
                            : string.Empty;
                        item = new InstalledPluginItem
                        {
                            Hash = e.Hash,
                            FileName = e.FileName,
                            Platform = e.Platform,
                            Version = e.Version,
                            Author = e.Author,
                            SrcUrl = e.SrcUrl,
                            Enabled = e.Enabled,
                            Status = e.Enabled && !lxActive ? "加载失败" : string.Empty,
                            SupportsSearch = lxActive && lx!.Sources.Count > 0,
                            MethodsText = actions,
                            SearchTypesText = lxActive ? string.Join(", ", lx!.Sources.Keys) : string.Empty,
                            UserVariables = null,
                        };
                    }
                    else
                    {
                        item = new InstalledPluginItem
                        {
                            Hash = e.Hash,
                            FileName = e.FileName,
                            Platform = e.Platform,
                            Version = e.Version,
                            Author = e.Author,
                            SrcUrl = e.SrcUrl,
                            Enabled = e.Enabled,
                            Status = e.Enabled && !runtimeActive ? "加载失败" : string.Empty,
                            SupportsSearch = active && rt!.SupportsMethod("search"),
                            // 仅运行中的插件能拿到运行时元数据(方法/搜索类型/用户变量声明), 已禁用的显示清单信息
                            MethodsText = active ? string.Join(", ", rt!.Metadata.Methods) : string.Empty,
                            SearchTypesText = active ? string.Join(", ", rt!.Metadata.SupportedSearchType) : string.Empty,
                            UserVariables = active ? rt!.Metadata.UserVariables : null,
                        };
                    }
                    return item;
                }).ToList();
            }
        }

        /// <summary>读取某插件平台的用户变量(用于编辑弹窗初始化)。</summary>
        public Dictionary<string, string> GetUserVariables(string platform)
            => PluginUserVariablesStore.Get(platform);

        /// <summary>保存某插件平台的用户变量, 并在插件运行中时热重载运行时使变量立即生效。</summary>
        public Task<(bool Ok, string? Error)> SetUserVariablesAsync(string hash, Dictionary<string, string> values)
        {
            return Task.Run(() =>
            {
                PluginManifestEntry? entry = null;
                lock (_manifestGate)
                    entry = _manifest.FirstOrDefault(e => e.Hash == hash);
                if (entry is null) return (false, "插件不存在");
                if (entry.Format == "lx") return (false, "LX 插件不支持用户变量");
                PluginUserVariablesStore.Set(entry.Platform, values);
                // 重载运行时让新变量立即注入 env
                if (_runtimes.TryGetValue(hash, out var old))
                {
                    try
                    {
                        var file = Path.Combine(PluginDir, entry.FileName);
                        if (File.Exists(file))
                        {
                            var runtime = PluginRuntime.Load(
                                File.ReadAllText(file), entry.Hash,
                                Path.Combine(StorageDir, entry.Hash[..8] + ".json"),
                                App.GetAppVersion(),
                                PluginUserVariablesStore.Get(entry.Platform), _logger);
                            if (_runtimes.TryUpdate(hash, runtime, old))
                            {
                                old.Dispose();
                            }
                            else
                            {
                                runtime.Dispose();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "用户变量保存后重载插件 {Platform} 失败: {Message}", entry.Platform, ex.Message);
                        return (false, $"变量已保存, 但重载插件失败: {ex.Message}");
                    }
                }
                PluginsChanged?.Invoke();
                return (true, (string?)null);
            });
        }

        /// <summary>从更新源下载并检查插件更新。返回 (状态, 新版本号, 新代码, 错误)。
        /// 状态: latest=已是最新 / update=有新版本 / 其他=失败。</summary>
        public Task<(string State, string NewVersion, string? Code, string? Error)> CheckUpdateAsync(string hash)
        {
            return Task.Run(async () =>
            {
                PluginManifestEntry? entry = null;
                lock (_manifestGate)
                    entry = _manifest.FirstOrDefault(e => e.Hash == hash);
                if (entry is null) return ("error", "", (string?)null, "插件不存在");
                if (string.IsNullOrWhiteSpace(entry.SrcUrl)) return ("error", "", (string?)null, "插件未提供更新源");

                try
                {
                    using var http = new System.Net.Http.HttpClient();
                    http.Timeout = TimeSpan.FromSeconds(60);
                    var code = await http.GetStringAsync(entry.SrcUrl.Trim());
                    if (string.IsNullOrWhiteSpace(code)) return ("error", "", (string?)null, "更新源返回空内容");

                    var newHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
                    // 代码指纹一致即视为最新(版本号可能未变化)
                    if (newHash == entry.Hash) return ("latest", entry.Version, (string?)null, null);

                    if (LxPluginRuntime.IsLxScript(code))
                    {
                        // LX 脚本: 仅解析 UserScript 头部元数据, 无需启动引擎
                        var lxInfo = LxPluginRuntime.ParseScriptInfo(code);
                        return ("update", lxInfo.Version, code, null);
                    }

                    // 临时加载解析新版本号(仅读取元数据后立即释放)
                    var tmp = PluginRuntime.Load(code, newHash, Path.Combine(StorageDir, newHash[..8] + ".json"), App.GetAppVersion(), null, _logger);
                    try
                    {
                        return ("update", tmp.Metadata.Version, code, null);
                    }
                    finally
                    {
                        tmp.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    return ("error", "", (string?)null, ex.Message);
                }
            });
        }

        /// <summary>用新代码替换插件(保留启用状态与平台用户变量, 迁移旧数据存储)。</summary>
        public Task<(bool Ok, string? Error)> ReplacePluginAsync(string oldHash, string code)
        {
            return Task.Run(() =>
            {
                PluginManifestEntry? oldEntry = null;
                lock (_manifestGate)
                    oldEntry = _manifest.FirstOrDefault(e => e.Hash == oldHash);
                if (oldEntry is null) return (false, "插件不存在");

                var newHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
                if (newHash == oldHash) return (true, null); // 代码未变化

                // 安装前迁移旧数据存储到新 hash 对应文件, 新运行时加载即可读到
                var oldStorage = Path.Combine(StorageDir, oldHash[..8] + ".json");
                var newStorage = Path.Combine(StorageDir, newHash[..8] + ".json");
                try
                {
                    if (File.Exists(oldStorage) && !File.Exists(newStorage))
                        File.Copy(oldStorage, newStorage);
                }
                catch { /* 存储迁移失败不影响更新 */ }

                var (installedHash, error) = InstallFromCode(code);
                if (installedHash is null) return (false, error);

                lock (_manifestGate)
                {
                    var oldStill = _manifest.FirstOrDefault(e => e.Hash == oldHash);
                    if (oldStill is not null)
                    {
                        _manifest.Remove(oldStill);
                        SaveManifestLocked();
                    }
                    // 继承旧启用状态
                    var newEntry = _manifest.FirstOrDefault(e => e.Hash == installedHash);
                    if (newEntry is not null && newEntry.Enabled != oldEntry.Enabled)
                    {
                        newEntry.Enabled = oldEntry.Enabled;
                        SaveManifestLocked();
                    }
                    try { File.Delete(Path.Combine(PluginDir, oldEntry.FileName)); } catch { }
                    try { File.Delete(oldStorage); } catch { }
                }
                if (_runtimes.TryRemove(oldHash, out var runtime)) runtime.Dispose();
                if (_lxRuntimes.TryRemove(oldHash, out var lxRuntime)) lxRuntime.Dispose();
                PluginsChanged?.Invoke();
                return (true, (string?)null);
            });
        }

        // ---------------- 在线能力 ----------------

        /// <summary>LX 音源搜索: 内置 SDK 直连平台接口(kw/kg/tx/wy/mg), 结果交给 LX 插件解析播放。</summary>
        public Task<PluginSearchResult> SearchLxAsync(string source, string keyword, int page)
        {
            return Task.Run(async () =>
            {
                var result = new PluginSearchResult { PluginHash = $"lx:{source}" };
                var runtime = _lxRuntimes.Values.FirstOrDefault(r => r.SupportsSource(source));
                if (runtime is null)
                {
                    result.Error = "没有启用的 LX 插件支持该音源";
                    return result;
                }
                result.PluginName = LxSources.DisplayName(source);
                try
                {
                    var search = await LxMusicSdk.SearchAsync(source, keyword, page, 30);
                    var songs = new List<OnlineSong>();
                    foreach (var item in search.List)
                    {
                        // interval "mm:ss" → 秒
                        var durationSec = 0.0;
                        var parts = item.Interval.Split(':');
                        if (parts.Length == 2 && int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var s))
                            durationSec = m * 60 + s;
                        songs.Add(new OnlineSong
                        {
                            Id = item.Songmid,
                            Title = item.Name,
                            Artist = item.Singer,
                            Album = item.AlbumName,
                            Artwork = item.Img ?? string.Empty,
                            DurationSec = durationSec,
                            Platform = source,
                            PluginHash = runtime.Hash,
                            PluginName = result.PluginName,
                            RawJson = JsonSerializer.Serialize(item),
                            IsLx = true,
                        });
                    }
                    result.Songs = [.. songs];
                    result.IsEnd = page >= search.AllPage || search.List.Count == 0;
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                }
                return result;
            });
        }

        /// <summary>搜索: MusicFree 约定 search(keyword, page, type)。type 1=音乐。</summary>
        public Task<PluginSearchResult> SearchAsync(string pluginHash, string keyword, int page, string searchType = "music")
        {
            return Task.Run(() =>
            {
                var result = new PluginSearchResult { PluginHash = pluginHash };
                if (!_runtimes.TryGetValue(pluginHash, out var runtime) || !runtime.SupportsMethod("search"))
                {
                    result.Error = "插件不可用";
                    return result;
                }
                result.PluginName = runtime.Metadata.Platform;
                try
                {
                    var args = JsonSerializer.Serialize(new object[] { keyword, page, searchType });
                    var state = runtime.CallMethodState("search", args);
                    if (state.Error is not null)
                    {
                        result.Error = state.Error;
                        return result;
                    }
                    if (state.Json is null)
                    {
                        result.Error = "插件未返回结果";
                        return result;
                    }
                    using var doc = JsonDocument.Parse(state.Json);
                    var root = doc.RootElement;
                    result.IsEnd = root.TryGetProperty("isEnd", out var isEnd) && isEnd.ValueKind == JsonValueKind.True;
                    if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    {
                        var songs = new List<OnlineSong>();
                        foreach (var item in data.EnumerateArray())
                        {
                            var song = ParseMusicItem(item, runtime);
                            if (song is not null) songs.Add(song);
                        }
                        result.Songs = [.. songs];
                    }
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                }
                return result;
            });
        }

        /// <summary>网络歌单导入: MusicFree 约定 importMusicSheet(url) 返回 {title, musicList}。</summary>
        public Task<NetworkSheetImportResult> ImportMusicSheetAsync(string pluginHash, string url)
        {
            return Task.Run(() =>
            {
                var result = new NetworkSheetImportResult();
                if (!_runtimes.TryGetValue(pluginHash, out var runtime) || !runtime.SupportsMethod("importMusicSheet"))
                {
                    result.Error = "插件不支持导入歌单";
                    return result;
                }
                try
                {
                    var args = JsonSerializer.Serialize(new object[] { url });
                    var state = runtime.CallMethodState("importMusicSheet", args);
                    if (state.Error is not null)
                    {
                        result.Error = state.Error;
                        return result;
                    }
                    if (state.Json is null)
                    {
                        result.Error = "插件未返回结果";
                        return result;
                    }
                    using var doc = JsonDocument.Parse(state.Json);
                    var root = doc.RootElement;
                    // 响应形态对齐弦予 pluginImportMusicSheet: 直接数组 / {title, musicList} / {data: []} 三种
                    JsonElement list;
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        list = root;
                    }
                    else if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                            result.Title = title.GetString() ?? string.Empty;
                        if (!root.TryGetProperty("musicList", out list) || list.ValueKind != JsonValueKind.Array)
                        {
                            if (!root.TryGetProperty("data", out list) || list.ValueKind != JsonValueKind.Array)
                            {
                                result.Error = "插件返回的数据格式不正确";
                                return result;
                            }
                        }
                    }
                    else
                    {
                        result.Error = "插件返回的数据格式不正确";
                        return result;
                    }
                    var songs = new List<OnlineSong>();
                    foreach (var item in list.EnumerateArray())
                    {
                        var song = ParseMusicItem(item, runtime);
                        if (song is not null) songs.Add(song);
                    }
                    if (songs.Count == 0)
                    {
                        result.Error = "歌单为空，或歌单不是公开歌单";
                        return result;
                    }
                    result.Songs = [.. songs];
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                }
                return result;
            });
        }

        private static OnlineSong? ParseMusicItem(JsonElement item, PluginRuntime runtime)
        {
            try
            {
                if (item.ValueKind != JsonValueKind.Object) return null;
                string GetStr(params string[] names)
                {
                    foreach (var n in names)
                        if (item.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.GetString()))
                            return v.GetString()!;
                    return string.Empty;
                }
                var id = GetStr("id");
                // MusicFree 约定 id 为 string, 但部分插件(如 QQ 音乐)返回数字, 统一转字符串
                if (string.IsNullOrEmpty(id) && item.TryGetProperty("id", out var idv))
                {
                    if (idv.ValueKind == JsonValueKind.Number && idv.TryGetInt64(out var idn)) id = idn.ToString();
                }
                if (string.IsNullOrEmpty(id)) return null;
                double duration = 0;
                if (item.TryGetProperty("duration", out var d))
                {
                    if (d.ValueKind == JsonValueKind.Number) duration = d.GetDouble();
                    else if (d.ValueKind == JsonValueKind.String && double.TryParse(d.GetString(), out var dd)) duration = dd;
                }
                var artwork = string.Empty;
                if (item.TryGetProperty("artwork", out var aw))
                {
                    if (aw.ValueKind == JsonValueKind.String) artwork = aw.GetString() ?? string.Empty;
                    else if (aw.ValueKind == JsonValueKind.Array && aw.GetArrayLength() > 0 && aw[0].ValueKind == JsonValueKind.String)
                        artwork = aw[0].GetString() ?? string.Empty;
                }
                return new OnlineSong
                {
                    Id = id,
                    Title = GetStr("title") ?? "(未知标题)",
                    Artist = GetStr("artist", "singer"),
                    Album = GetStr("album"),
                    Artwork = artwork,
                    DurationSec = duration,
                    Platform = runtime.Metadata.Platform,
                    PluginHash = runtime.Hash,
                    PluginName = runtime.Metadata.Platform,
                    RawJson = item.GetRawText(),
                };
            }
            catch { return null; }
        }

        // ---------------- 目录搜索(歌手/专辑/歌单) ----------------

        /// <summary>MusicFree 目录搜索: search(keyword, page, type), type=artist/album/sheet。</summary>
        public Task<PluginCatalogSearchResult> SearchCatalogAsync(string pluginHash, string keyword, int page, string searchType)
        {
            return Task.Run(() =>
            {
                var result = new PluginCatalogSearchResult { PluginHash = pluginHash };
                if (!_runtimes.TryGetValue(pluginHash, out var runtime) || !runtime.SupportsMethod("search"))
                {
                    result.Error = "插件不可用";
                    return result;
                }
                result.PluginName = runtime.Metadata.Platform;
                try
                {
                    var args = JsonSerializer.Serialize(new object[] { keyword, page, searchType });
                    var state = runtime.CallMethodState("search", args);
                    if (state.Error is not null)
                    {
                        result.Error = state.Error;
                        return result;
                    }
                    if (state.Json is null)
                    {
                        result.Error = "插件未返回结果";
                        return result;
                    }
                    using var doc = JsonDocument.Parse(state.Json);
                    var root = doc.RootElement;
                    result.IsEnd = root.TryGetProperty("isEnd", out var isEnd) && isEnd.ValueKind == JsonValueKind.True;
                    if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    {
                        var items = new List<OnlineCatalogItem>();
                        foreach (var item in data.EnumerateArray())
                        {
                            var catalog = ParseCatalogItem(item, runtime, searchType);
                            if (catalog is not null) items.Add(catalog);
                        }
                        result.Items = [.. items];
                    }
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                }
                return result;
            });
        }

        /// <summary>MusicFree 目录条目解析(宽字段名: artist={id,name,avatar}, album={id,title,artist,artwork}, sheet={id,title,artwork,artist})。</summary>
        private static OnlineCatalogItem? ParseCatalogItem(JsonElement item, PluginRuntime runtime, string searchType)
        {
            try
            {
                if (item.ValueKind != JsonValueKind.Object) return null;
                string GetStr(params string[] names)
                {
                    foreach (var n in names)
                        if (item.TryGetProperty(n, out var v))
                        {
                            if (v.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.GetString())) return v.GetString()!;
                            if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
                        }
                    return string.Empty;
                }
                var id = GetStr("id", "ID");
                if (id.Length == 0) return null;
                var title = GetStr("title", "name");
                if (title.Length == 0) return null;
                return new OnlineCatalogItem
                {
                    Id = id,
                    Title = title,
                    // 专辑→歌手; 歌单→创建者; 歌手→无副标题
                    Subtitle = GetStr("artist", "author", "creator", "nickname"),
                    Artwork = GetStr("artwork", "avatar", "pic", "img", "cover"),
                    Platform = runtime.Metadata.Platform,
                    PluginHash = runtime.Hash,
                    PluginName = runtime.Metadata.Platform,
                    CatalogType = searchType,
                    RawJson = item.GetRawText(),
                };
            }
            catch { return null; }
        }

        /// <summary>LX 音源目录搜索(歌手/专辑派生 + 歌单原生接口)。</summary>
        public Task<PluginCatalogSearchResult> SearchLxCatalogAsync(string source, string keyword, string searchType, int page)
        {
            return Task.Run(async () =>
            {
                var result = new PluginCatalogSearchResult { PluginHash = $"lx:{source}" };
                var runtime = _lxRuntimes.Values.FirstOrDefault(r => r.SupportsSource(source));
                if (runtime is null)
                {
                    result.Error = "没有启用的 LX 插件支持该音源";
                    return result;
                }
                result.PluginName = LxSources.DisplayName(source);
                try
                {
                    var entries = await LxMusicSdk.CatalogSearchAsync(source, keyword, searchType, page, 30);
                    var items = entries.Select(e => new OnlineCatalogItem
                    {
                        Id = e.Id,
                        Title = e.Title,
                        Subtitle = e.Subtitle,
                        Artwork = e.Artwork,
                        Platform = source,
                        PluginHash = runtime.Hash,
                        PluginName = result.PluginName,
                        CatalogType = searchType,
                        IsLx = true,
                        LxSource = source,
                        RawJson = e.RawJson,
                    }).ToList();
                    result.Items = [.. items];
                    // 歌手/专辑派生自歌曲搜索(AllPage 为歌曲分页); 歌单接口无稳定总数, 不足一页视为结束
                    result.IsEnd = searchType == "sheet" ? items.Count < 30 : page >= await GetLxCatalogAllPageAsync(source, keyword, searchType);
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                }
                return result;
            });
        }

        private async Task<int> GetLxCatalogAllPageAsync(string source, string keyword, string searchType)
        {
            // 派生目录的分页以歌曲搜索 AllPage 为准(每次派生整页歌手/专辑)
            var search = await LxMusicSdk.SearchAsync(source, keyword, 1, 30);
            return Math.Max(1, search.AllPage);
        }

        /// <summary>目录详情歌曲列表: MF 走 getArtistWorks/getAlbumInfo/getMusicSheetInfo; LX 歌手=按名搜索, 专辑/歌单=原生接口+搜索回退。</summary>
        public Task<PluginSearchResult> GetCatalogDetailAsync(OnlineCatalogItem item, int page)
        {
            return Task.Run(async () =>
            {
                var result = new PluginSearchResult { PluginHash = item.PluginHash, PluginName = item.PluginName };
                try
                {
                    if (item.IsLx)
                    {
                        var runtime = _lxRuntimes.Values.FirstOrDefault(r => r.SupportsSource(item.LxSource));
                        if (runtime is null)
                        {
                            result.Error = "没有启用的 LX 插件支持该音源";
                            return result;
                        }
                        List<LxSearchResultItem> list;
                        if (item.CatalogType == "artist")
                        {
                            // 歌手详情: 按歌手名搜索(XianYu 同款)
                            var search = await LxMusicSdk.SearchAsync(item.LxSource, item.Title, page, 30);
                            list = search.List;
                            result.IsEnd = page >= search.AllPage || list.Count == 0;
                        }
                        else if (item.CatalogType == "album")
                        {
                            list = await LxMusicSdk.GetAlbumSongsAsync(item.LxSource, item.Id, item.Title, page, 30);
                            if (list.Count == 0 && page == 1)
                            {
                                // 回退: 专辑名搜索并按专辑名过滤
                                var search = await LxMusicSdk.SearchAsync(item.LxSource, item.Title, page, 30);
                                var norm = item.Title.Trim().ToLowerInvariant();
                                list = search.List.Where(s =>
                                {
                                    var al = s.AlbumName?.Trim().ToLowerInvariant() ?? string.Empty;
                                    return al.Length > 0 && (al == norm || al.Contains(norm) || norm.Contains(al));
                                }).ToList();
                                if (list.Count == 0) list = search.List; // 精确过滤仍为空则放宽
                            }
                            result.IsEnd = list.Count < 30;
                        }
                        else
                        {
                            list = await LxMusicSdk.GetPlaylistTracksAsync(item.LxSource, item.Id, page, 30);
                            if (list.Count == 0 && page == 1)
                            {
                                // 回退: 歌单名搜索
                                var search = await LxMusicSdk.SearchAsync(item.LxSource, item.Title, page, 30);
                                list = search.List;
                            }
                            result.IsEnd = list.Count < 30;
                        }
                        result.Songs = [.. list.Select(s => ToLxOnlineSong(s, runtime, item.LxSource, result.PluginName))];
                    }
                    else
                    {
                        if (!_runtimes.TryGetValue(item.PluginHash, out var runtime))
                        {
                            result.Error = "插件不可用或已卸载";
                            return result;
                        }
                        // MF 详情方法分发: getArtistWorks(artist, page, "music") / getAlbumInfo(album, page) / getMusicSheetInfo(sheet, page)
                        var method = item.CatalogType switch
                        {
                            "artist" => "getArtistWorks",
                            "album" => "getAlbumInfo",
                            _ => "getMusicSheetInfo",
                        };
                        if (!runtime.SupportsMethod(method))
                        {
                            result.Error = $"插件不支持 {method}";
                            return result;
                        }
                        using var doc = JsonDocument.Parse(item.RawJson);
                        var rawItem = doc.RootElement.Deserialize<object>();
                        var args = item.CatalogType == "artist"
                            ? JsonSerializer.Serialize(new object[] { rawItem, page, "music" })
                            : JsonSerializer.Serialize(new object[] { rawItem, page });
                        var state = runtime.CallMethodState(method, args);
                        if (state.Error is not null)
                        {
                            result.Error = state.Error;
                            return result;
                        }
                        if (state.Json is null)
                        {
                            result.Error = "插件未返回结果";
                            return result;
                        }
                        using var res = JsonDocument.Parse(state.Json);
                        var root = res.RootElement;
                        result.IsEnd = root.TryGetProperty("isEnd", out var isEnd) && isEnd.ValueKind == JsonValueKind.True;
                        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                        {
                            var songs = new List<OnlineSong>();
                            foreach (var song in data.EnumerateArray())
                            {
                                var music = ParseMusicItem(song, runtime);
                                if (music is not null) songs.Add(music);
                            }
                            result.Songs = [.. songs];
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                }
                return result;
            });
        }

        /// <summary>LX 跨音源候选(对齐弦予 findAlternativeLxSource): LX 某音源解析失败/仅试听时,
        /// 在其余 LX 音源(kw/kg/tx/wy/mg)按 标题+歌手 搜索同名歌。LX 五音源共用一个插件 hash,
        /// 按插件排除的常规回退无法覆盖, 单独在此提供。返回未匹配的原始候选(调用方 RankCandidates 严格匹配)。</summary>
        public async Task<List<OnlineSong>> SearchLxAlternatesAsync(OnlineSong failed, string title, string artist)
        {
            var result = new List<OnlineSong>();
            var query = $"{title} {artist}".Trim();
            if (string.IsNullOrWhiteSpace(query)) return result;
            foreach (var source in LxSources.StandardOrder)
            {
                if (source == failed.Platform) continue;
                // 无支持该音源的启用 LX 插件则没有解析能力, 跳过
                var runtime = _lxRuntimes.Values.FirstOrDefault(r => r.SupportsSource(source));
                if (runtime is null) continue;
                try
                {
                    var search = await LxMusicSdk.SearchAsync(source, query, 1, 20);
                    // 每音源取前 6 条进候选池, 由 RankCandidates 统一严格匹配
                    foreach (var item in search.List.Take(6))
                        result.Add(ToLxOnlineSong(item, runtime, source, LxSources.DisplayName(source)));
                }
                catch { /* 单音源搜索失败继续下一个 */ }
            }
            return result;
        }

        /// <summary>LX 歌曲条目 → OnlineSong(musicInfo JSON 交给 LX 插件解析播放)。</summary>
        private static OnlineSong ToLxOnlineSong(LxSearchResultItem item, LxPluginRuntime runtime, string source, string pluginName)
        {
            var durationSec = 0.0;
            var parts = item.Interval.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var s))
                durationSec = m * 60 + s;
            return new OnlineSong
            {
                Id = item.Songmid,
                Title = item.Name,
                Artist = item.Singer,
                Album = item.AlbumName,
                Artwork = item.Img ?? string.Empty,
                DurationSec = durationSec,
                Platform = source,
                PluginHash = runtime.Hash,
                PluginName = pluginName,
                RawJson = JsonSerializer.Serialize(item),
                IsLx = true,
            };
        }

        /// <summary>插件哈希无法识别(如手机端同步歌曲携带手机插件文件名)时, 按平台名回退匹配本地唯一启用插件。</summary>
        public string? ResolvePluginHashByPlatform(string pluginHash, string platform)
        {
            if (_runtimes.ContainsKey(pluginHash)) return pluginHash;
            if (string.IsNullOrWhiteSpace(platform)) return null;
            var matches = _runtimes.Values
                .Where(r => string.Equals(r.Metadata.Platform?.Trim(), platform.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            // 多个同平台插件时无法确定来源, 不绑定避免错用不兼容插件
            return matches.Count == 1 ? matches[0].Hash : null;
        }

        /// <summary>
        /// 跨端兼容规范化在线歌曲(手机端同步快照缺失平台/id/isLx 等字段):
        /// 从 RawJson 回填 platform 与 id、推断 isLx、插件哈希按平台回退匹配本地插件。
        /// </summary>
        public void NormalizeOnlineSong(OnlineSong song)
        {
            if (string.IsNullOrEmpty(song.RawJson))
            {
                // 无原始数据时仅做哈希回退
                if (!string.IsNullOrEmpty(song.PluginHash) && !song.IsLx)
                {
                    var resolved = ResolvePluginHashByPlatform(song.PluginHash, song.Platform);
                    if (resolved is not null) song.PluginHash = resolved;
                }
                return;
            }
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(song.RawJson);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return;
                // platform 缺失: MusicFree musicItem 带 platform; LX musicInfo 带 source(音源 key)
                if (string.IsNullOrEmpty(song.Platform))
                {
                    if (doc.RootElement.TryGetProperty("platform", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String)
                        song.Platform = p.GetString() ?? string.Empty;
                    else if (doc.RootElement.TryGetProperty("source", out var src) && src.ValueKind == System.Text.Json.JsonValueKind.String)
                        song.Platform = src.GetString() ?? string.Empty;
                }
                // isLx 推断: source 是标准音源 key 且无 platform 字段
                if (!song.IsLx
                    && doc.RootElement.TryGetProperty("source", out var src2) && src2.ValueKind == System.Text.Json.JsonValueKind.String
                    && LxSources.StandardOrder.Contains(src2.GetString())
                    && !doc.RootElement.TryGetProperty("platform", out _))
                    song.IsLx = true;
                // id 缺失: musicItem/musicInfo 带 id(字符串或数字)
                if (string.IsNullOrEmpty(song.Id) && doc.RootElement.TryGetProperty("id", out var idv))
                {
                    song.Id = idv.ValueKind switch
                    {
                        System.Text.Json.JsonValueKind.String => idv.GetString() ?? string.Empty,
                        System.Text.Json.JsonValueKind.Number => idv.GetRawText(),
                        _ => string.Empty,
                    };
                }
            }
            catch { /* 损坏 JSON 忽略 */ }
            // LX 歌曲显示名单点维护: 存量数据里的旧音源名(如"小蜗音乐")刷新为当前标准名
            if (song.IsLx && !string.IsNullOrEmpty(song.Platform))
                song.PluginName = LxSources.DisplayName(song.Platform);
            // 插件哈希回退: 手机端 pluginId 是插件文件名而非桌面内容哈希
            if (!string.IsNullOrEmpty(song.PluginHash) && !song.IsLx)
            {
                var resolved = ResolvePluginHashByPlatform(song.PluginHash, song.Platform);
                if (resolved is not null && resolved != song.PluginHash)
                {
                    song.PluginHash = resolved;
                    if (string.IsNullOrEmpty(song.PluginName))
                        song.PluginName = GetInstalledItems().FirstOrDefault(i => i.Hash == resolved)?.Platform ?? string.Empty;
                }
            }
        }

        /// <summary>解析在线歌曲播放地址: MF 走 getMediaSource(返回 url+headers, 如 B 站 Referer/Cookie), LX 走 musicUrl。
        /// ct 由外层限时器传入: 超时后逐档循环立即停止, 释放引擎给后续调用(对齐弦予版"超时即放弃该插件"模型)。
        /// qualityDownFirst: 播放链路为 true 时回退序为"向下优先"(首选不支持先降档, 降到最低仍不可用再升档), 仅影响本次调用。</summary>
        public Task<(OnlineMediaSource? Source, string? Error)> GetMediaSourceAsync(OnlineSong song, string quality = "320k", CancellationToken ct = default, bool qualityDownFirst = false)
        {
            return Task.Run<(OnlineMediaSource?, string?)>(async () =>
            {
                if (song.IsLx)
                {
                    // LX 歌曲: RawJson 为 musicInfo, 先交给 LX 插件解析直链, 失败后走公共 API 兜底(与手机版一致)
                    var lx = ResolveLxRuntime(song);
                    var normalized = LxSources.NormalizeQuality(quality);
                    if (normalized.Length == 0) normalized = "320k";
                    // 插件逐档位回退(对齐手机版 pluginQualityCandidates): 自定义 LX 音源通常只支持部分音质档;
                    // 播放链路按"向下优先"排序([首选, 更低档由高到低, 更高档由低到高]), 下载链路保持原 [首选,320k,128k]
                    var pluginQualities = qualityDownFirst
                        ? OrderDownFirst((string[])["128k", "320k", "flac", "flac24bit"], normalized).ToArray()
                        : new[] { normalized, "320k", "128k" }
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    string? lastError = null;
                    if (lx is not null && lx.SupportsAction(song.Platform, "musicUrl"))
                    {
                        foreach (var q in pluginQualities)
                        {
                            ct.ThrowIfCancellationRequested(); // 超时止损: 档间检查, 释放引擎
                            var (result, error) = lx.GetMusicUrl(song.Platform, song.RawJson, q);
                            if (result is not null) return (new OnlineMediaSource(result.Url, result.Headers), null);
                            lastError = error;
                        }
                    }
                    else lastError = lx is null ? "没有启用的 LX 插件支持该音源" : "LX 插件不支持获取音源";
                    // 公共 API 兜底(对齐手机版 lxResolveUrl): 插件失败(如内部接口 404)时酷狗等音源仍可解析;
                    // kg 的解析 ID 是各音质 hash 而非 songmid, 插件用错 ID 返回死链/404 是 LX 酷狗播放失败主因
                    var apiSource = await ResolveLxUrlViaApiAsync(song, normalized, qualityDownFirst);
                    if (apiSource is not null) return (apiSource, null);
                    return (null, lastError);
                }
                if (!_runtimes.TryGetValue(song.PluginHash, out var runtime))
                    return (null, $"未找到来源插件({song.Platform}), 请在插件管理中安装并启用对应平台插件后重试");
                if (string.IsNullOrEmpty(song.RawJson))
                    return (null, "歌曲数据不完整，请重新从云端同步");
                if (!runtime.SupportsMethod("getMediaSource"))
                    return (null, "插件不支持获取音源");
                // ===== MF 音质解析(移植弦予版 pluginEngineMedia.runPluginGetMusicInfo) =====
                // 1. 内部音质键 → MusicFree 固有四级键: 插件内部 QUALITY_MAPPING 只认 low/standard/high/super,
                //    直传 '320k'/'flac' 等原生键会查不到映射而回退默认档或报"不支持音质"
                // 2. 对齐 MusicFree 官方 getQualityOrder('asc'): [首选, ...更高侧, ...更低侧] 逐级尝试
                // 3. MusicFree 官方语义: getMediaSource 返回空时取 musicItem.qualities[quality].url 预解析直链
                // 4. 四级键全部报"不支持音质"时补试原生键(320k/flac/128k): 部分插件只认原生键
                string? mfLastError = null;
                try
                {
                    using var doc = JsonDocument.Parse(song.RawJson);
                    var musicItem = doc.RootElement.Deserialize<object>();
                    // 单档解析: 返回音源或 null(错误写入 mfLastError)。响应形态(对齐弦予 _toMediaSource):
                    // {url, headers} 对象或纯字符串 URL
                    OnlineMediaSource? TryGet(string q)
                    {
                        var args = JsonSerializer.Serialize(new object[] { musicItem!, q });
                        var state = runtime.CallMethodState("getMediaSource", args);
                        if (state.Error is not null)
                        {
                            mfLastError = state.Error;
                            return null;
                        }
                        if (state.Json is null) { mfLastError = "插件未返回音源"; return null; }
                        using var res = JsonDocument.Parse(state.Json);
                        if (res.RootElement.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(url.GetString()))
                            return new OnlineMediaSource(url.GetString()!, ParseMediaHeaders(res.RootElement));
                        if (res.RootElement.ValueKind == JsonValueKind.String && res.RootElement.GetString() is { Length: > 0 } rawUrl && IsHttpUrl(rawUrl))
                            return new OnlineMediaSource(rawUrl.Trim(), null);
                        mfLastError = "插件未返回有效音源 URL";
                        return null;
                    }
                    // 播放链路"向下优先": [首选, ...更低侧(由近到远), ...更高侧(由近到远)]; 下载链路保持官方 asc 序
                    var order = MfQualityOrder(quality, qualityDownFirst);
                    var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var mfQ in order)
                    {
                        ct.ThrowIfCancellationRequested(); // 超时止损: 立即停止逐级, 释放引擎
                        tried.Add(mfQ);
                        try
                        {
                            var swQ = System.Diagnostics.Stopwatch.StartNew();
                            var src = TryGet(mfQ);
                            if (swQ.ElapsedMilliseconds > 800)
                                _logger.LogInformation("[播放计时] 档位[{q}] 耗时 {ms}ms 成功={ok}", mfQ, swQ.ElapsedMilliseconds, src is not null);
                            if (src is not null) return (src, null);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { mfLastError = ex.Message; }
                        // [歌曲级错误] 歌曲不存在/版权限制/VIP 等换音质无意义, 立即停止逐级(移植弦予 isSongLevelError)
                        if (mfLastError is not null && IsSongLevelError(mfLastError)) break;
                        // MusicFree 官方语义: 该档解析失败/为空时, 取 musicItem.qualities[mfQ].url 预解析直链兜底
                        var preUrl = ExtractQualityUrl(song.RawJson, mfQ);
                        if (preUrl is not null) return (preUrl, null);
                    }
                    // [原生键补试] 四级键全部报"不支持音质"时, 插件实际只认 flac/320k/128k 等原生键,
                    // 逐个补试避免"该歌曲不支持low音质"直接失败(移植弦予 buildNativePluginQualityPairs);
                    // 播放链路同样"向下优先"排序原生键
                    if (mfLastError is not null && IsUnsupportedQualityError(mfLastError))
                    {
                        var nativeCandidates = new[] { quality, "320k", "flac", "128k", "flac24bit", "192k" };
                        if (qualityDownFirst)
                            nativeCandidates = OrderDownFirst(nativeCandidates, quality).ToArray();
                        foreach (var nq in nativeCandidates
                                     .Where(q => !string.IsNullOrWhiteSpace(q) && !tried.Contains(q))
                                     .Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            ct.ThrowIfCancellationRequested();
                            try
                            {
                                var src = TryGet(nq);
                                if (src is not null) return (src, null);
                                if (mfLastError is not null && IsSongLevelError(mfLastError)) break;
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex) { mfLastError = ex.Message; }
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { mfLastError = ex.Message; }
                // 直链兜底(与手机版 _extractDirectUrl 一致): 歌单导入的 rawData 自带可播地址时直接使用
                var direct = ExtractDirectUrl(song.RawJson);
                if (direct is not null) return (direct, null);
                return (null, mfLastError);
            });
        }

        /// <summary>LX 公共 API 直链解析客户端(对齐手机版 rust url_resolver)。</summary>
        private static readonly System.Net.Http.HttpClient LxApiHttp = CreateLxApiHttp();

        private static System.Net.Http.HttpClient CreateLxApiHttp()
        {
            var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "lx-music request");
            return client;
        }

        /// <summary>LX 公共 API 直链兜底(对齐手机版 rust resolve_url_via_api):
        /// 主 API https://lxmusicapi.onrender.com/url/{source}/{id}/{quality}, 备用 ts.tempmusics.tk。
        /// ID 规则: kg 优先 _types[quality].hash → hash → songmid(酷狗按音质 hash 解析, 用 songmid 会得到死链);
        /// kw/tx/wy 用 songmid; mg 用 copyrightId → songmid。downFirst 时候选音质按"向下优先"排序。</summary>
        private static async Task<OnlineMediaSource?> ResolveLxUrlViaApiAsync(OnlineSong song, string preferredQuality, bool downFirst = false)
        {
            try
            {
                using var doc = JsonDocument.Parse(song.RawJson);
                var root = doc.RootElement;
                string songmid = GetStringProp(root, "songmid");
                string? hash = GetStringProp(root, "hash") is { Length: > 0 } h ? h : null;
                string? copyrightId = GetStringProp(root, "copyrightId") is { Length: > 0 } c ? c : null;
                var qualityBase = new[] { preferredQuality, "320k", "flac", "128k" }
                    .Where(q => !string.IsNullOrWhiteSpace(q));
                var qualities = downFirst
                    ? OrderDownFirst(qualityBase, preferredQuality)
                    : qualityBase.Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var q in qualities)
                {
                    // 各音源解析 ID: kg 按音质取 hash; 其他音源用 songmid / copyrightId
                    string id;
                    if (song.Platform == LxSources.Kg)
                    {
                        var typeHash = root.TryGetProperty("_types", out var types) && types.ValueKind == JsonValueKind.Object
                            && types.TryGetProperty(q, out var entry) && entry.ValueKind == JsonValueKind.Object
                            && GetStringProp(entry, "hash") is { Length: > 0 } th ? th : null;
                        id = typeHash ?? hash ?? songmid;
                    }
                    else if (song.Platform == LxSources.Mg)
                        id = copyrightId ?? songmid;
                    else
                        id = songmid;
                    if (string.IsNullOrEmpty(id)) continue;
                    foreach (var apiBase in new[] { "https://lxmusicapi.onrender.com", "http://ts.tempmusics.tk" })
                    {
                        try
                        {
                            using var resp = await LxApiHttp.GetAsync($"{apiBase}/url/{song.Platform}/{Uri.EscapeDataString(id)}/{q}");
                            if (!resp.IsSuccessStatusCode) continue;
                            using var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                            // 响应形如 {code:0, data:"http://..."}; data 为空串表示该档位不可用
                            if (body.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String
                                && data.GetString() is { Length: > 0 } url && url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                return new OnlineMediaSource(url, null);
                        }
                        catch { /* 单个 API 失败换下一个 */ }
                    }
                }
            }
            catch { /* RawJson 损坏等不阻断 */ }
            return null;
        }

        private static string GetStringProp(JsonElement obj, string name)
            => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

        /// <summary>是否为"不支持音质"错误(移植弦予 isUnsupportedQualityError): 此类错误换音质键可能解决。
        /// 覆盖英文 "Unsupported ... quality" 单词形态(如 Bilibili 插件 "Unsupported Bilibili audio quality: low")。</summary>
        private static bool IsUnsupportedQualityError(string message)
            => System.Text.RegularExpressions.Regex.IsMatch(message, @"不支持.*音质|音质.*不支持|unsupported.*quality|quality.*unsupported|quality.*not\s+support|not\s+support.*quality",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>是否为歌曲级错误(移植弦予 isSongLevelError): 歌曲不存在/版权/VIP 等, 换音质无意义, 立即停止逐级。</summary>
        private static bool IsSongLevelError(string message)
            => System.Text.RegularExpressions.Regex.IsMatch(message,
                @"歌曲不存在|歌曲已下架|已?下架|版权.{0,4}(限制|保护|原因)|需要?登录|地区限制|需要?\s*(VIP|会员|付费)|VIP歌曲|会员歌曲|付费歌曲|无版权|暂无版权",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>内部音质键 → MusicFree 四级键(移植弦予 qualityKeyToMfQuality):
        /// mgg/128k→low, 192k→standard, 320k/high→high, flac/flac24bit/hires/vinyl/dolby/atmos/master/lossless/super→super。</summary>
        private static string QualityToMfKey(string quality) => quality?.ToLowerInvariant() switch
        {
            "mgg" or "96k" or "128k" or "128" or "low" => "low",
            "192k" or "192" or "standard" => "standard",
            "320k" or "320" or "exhigh" or "high" => "high",
            "flac" or "sq" or "flac24bit" or "flac24" or "hires" or "hi-res" or "vinyl" or "dolby" or "atmos" or "atmos_plus" or "master" or "lossless" or "super" => "super",
            _ => "high", // 未知音质按 320k 档处理(弦予同款兜底)
        };

        /// <summary>MusicFree 官方 getQualityOrder('asc') 顺序(移植弦予 mfAscOrder):
        /// [首选, ...更高侧, ...更低侧], 如 high → [high, super, standard, low]。
        /// downFirst=true 为播放链路"向下优先"序: [首选, ...更低侧(由近到远), ...更高侧(由近到远)],
        /// 如 high → [high, standard, low, super]。</summary>
        private static string[] MfQualityOrder(string quality, bool downFirst = false)
        {
            var order = new[] { "low", "standard", "high", "super" };
            var baseIdx = Array.IndexOf(order, QualityToMfKey(quality));
            if (baseIdx < 0) baseIdx = 2; // high
            var result = new List<string> { order[baseIdx] };
            if (downFirst)
            {
                for (var i = baseIdx - 1; i >= 0; i--) result.Add(order[i]);
                for (var i = baseIdx + 1; i < order.Length; i++) result.Add(order[i]);
            }
            else
            {
                for (var i = baseIdx + 1; i < order.Length; i++) result.Add(order[i]);
                for (var i = baseIdx - 1; i >= 0; i--) result.Add(order[i]);
            }
            return result.ToArray();
        }

        /// <summary>原生/LX 音质键档位序: 128k &lt; 192k &lt; 320k &lt; flac &lt; flac24bit, 未知键按 320k 档。</summary>
        private static int NativeQualityRank(string q) => q?.ToLowerInvariant() switch
        {
            "128k" or "128" or "mgg" or "96k" => 0,
            "192k" or "192" => 1,
            "320k" or "320" or "exhigh" => 2,
            "flac" or "sq" => 3,
            "flac24bit" or "hires" or "hi-res" or "hr" or "zq24" => 4,
            _ => 2,
        };

        /// <summary>播放链路"向下优先"回退序(用户语义: 首选不支持先降档, 降到最低仍不可用再升档):
        /// [首选, 低于首选的档位(由高到低), 高于首选的档位(由低到高)], 忽略大小写去重。</summary>
        private static IEnumerable<string> OrderDownFirst(IEnumerable<string> candidates, string preferred)
        {
            var prefRank = NativeQualityRank(preferred);
            var list = candidates.Where(q => !string.IsNullOrWhiteSpace(q)).ToList();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var q in new[] { preferred }
                         .Concat(list.Where(q => NativeQualityRank(q) < prefRank).OrderByDescending(NativeQualityRank))
                         .Concat(list.Where(q => NativeQualityRank(q) > prefRank).OrderBy(NativeQualityRank)))
            {
                if (seen.Add(q)) yield return q;
            }
        }

        /// <summary>从 musicItem.qualities 取预解析直链(移植弦予 MusicFree 官方语义):
        /// { qualities: { high: {url}, ... } }; 键兼容四级键与原生键。</summary>
        private static OnlineMediaSource? ExtractQualityUrl(string rawJson, string mfQuality)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                if (!doc.RootElement.TryGetProperty("qualities", out var qs) || qs.ValueKind != JsonValueKind.Object) return null;
                foreach (var key in new[] { mfQuality, "320k", "128k", "flac" })
                {
                    if (!qs.TryGetProperty(key, out var entry) || entry.ValueKind != JsonValueKind.Object) continue;
                    if (entry.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String && u.GetString() is { Length: > 0 } url && IsHttpUrl(url))
                        return new OnlineMediaSource(url.Trim(), null);
                }
                return null;
            }
            catch { return null; }
        }

        /// <summary>LX 公共 API 直链兜底(对外): 供播放链路在插件返回死链(下载 404)时按音质 hash 重新解析。</summary>
        public Task<OnlineMediaSource?> GetLxApiFallbackAsync(OnlineSong song, string quality = "320k", bool qualityDownFirst = false)
        {
            var normalized = LxSources.NormalizeQuality(quality);
            if (normalized.Length == 0) normalized = "320k";
            return ResolveLxUrlViaApiAsync(song, normalized, qualityDownFirst);
        }

        /// <summary>解析插件音源结果中的 headers 对象(B 站等 CDN 校验 Referer/Cookie, 缺失会 403)。</summary>
        private static Dictionary<string, string>? ParseMediaHeaders(JsonElement root)
        {
            if (!root.TryGetProperty("headers", out var h) || h.ValueKind != JsonValueKind.Object) return null;
            var headers = new Dictionary<string, string>();
            foreach (var p in h.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Name))
                    headers[p.Name] = p.Value.GetString() ?? string.Empty;
            }
            return headers.Count > 0 ? headers : null;
        }

        /// <summary>所有音质档位均失败时从歌曲 rawData 提取可播直链(与手机版 _extractDirectUrl 一致):
        /// 依次尝试 url/playUrl/play_url/src 字段及 qualities 对象内嵌的 url。</summary>
        private static OnlineMediaSource? ExtractDirectUrl(string rawJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                var root = doc.RootElement;
                foreach (var key in new[] { "url", "playUrl", "play_url", "src" })
                {
                    if (root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String &&
                        v.GetString() is { Length: > 0 } s && IsHttpUrl(s))
                        return new OnlineMediaSource(s.Trim(), null);
                }
                if (root.TryGetProperty("qualities", out var qs) && qs.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in qs.EnumerateObject())
                    {
                        if (p.Value.ValueKind != JsonValueKind.Object) continue;
                        if (p.Value.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String &&
                            u.GetString() is { Length: > 0 } s && IsHttpUrl(s))
                            return new OnlineMediaSource(s.Trim(), null);
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsHttpUrl(string value) =>
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

        /// <summary>获取在线歌词: MF 走 getLyric, LX 走插件 lyric 动作。</summary>
        public Task<(string? Lrc, string? Translation, string? Error)> GetLyricAsync(OnlineSong song)
        {
            return Task.Run(() =>
            {
                if (song.IsLx)
                {
                    var lx = ResolveLxRuntime(song);
                    if (lx is null) return (null, null, (string?)"没有启用的 LX 插件支持该音源");
                    if (!lx.SupportsAction(song.Platform, "lyric"))
                        return (null, null, (string?)"LX 插件不支持获取歌词");
                    try
                    {
                        var (lrc, tlyric, error) = lx.GetLyric(song.Platform, song.RawJson);
                        if (error is not null) return (null, null, error);
                        // QQ 音源可能返回 QRC 加密 hex, 应用层解密
                        return (QrcLyricDecryptor.AutoDecrypt(lrc), QrcLyricDecryptor.AutoDecrypt(tlyric), null);
                    }
                    catch (Exception ex)
                    {
                        return (null, null, ex.Message);
                    }
                }
                if (!_runtimes.TryGetValue(song.PluginHash, out var runtime) || !runtime.SupportsMethod("getLyric"))
                    return ((string?)null, (string?)null, (string?)"插件不支持获取歌词");
                try
                {
                    using var doc = JsonDocument.Parse(song.RawJson);
                    var args = JsonSerializer.Serialize(new object[] { doc.RootElement.Deserialize<object>() });
                    var state = runtime.CallMethodState("getLyric", args);
                    if (state.Error is not null) return (null, null, state.Error);
                    if (state.Json is null) return (null, null, null);
                    using var res = JsonDocument.Parse(state.Json);
                    string? lrc = null, trans = null;
                    // 主歌词: MusicFree 标准 lrc 字段优先, QQ 插件用 rawLrc(可能为 QRC 加密 hex)
                    if (res.RootElement.TryGetProperty("lrc", out var l) && l.ValueKind == JsonValueKind.String) lrc = l.GetString();
                    else if (res.RootElement.TryGetProperty("rawLrc", out var rl) && rl.ValueKind == JsonValueKind.String) lrc = rl.GetString();
                    else if (res.RootElement.TryGetProperty("lyric", out var ly) && ly.ValueKind == JsonValueKind.String) lrc = ly.GetString();
                    if (res.RootElement.TryGetProperty("translation", out var t) && t.ValueKind == JsonValueKind.String) trans = t.GetString();
                    // QQ 音源 rawLrc/translation 为 QRC 加密 hex 时应用层解密, 否则原样返回
                    return (QrcLyricDecryptor.AutoDecrypt(lrc), QrcLyricDecryptor.AutoDecrypt(trans), null);
                }
                catch (Exception ex)
                {
                    return ((string?)null, (string?)null, ex.Message);
                }
            });
        }

        /// <summary>解析 LX 歌曲对应的插件运行时: 优先歌曲记录的插件, 失效时回退到任意支持该音源的插件。</summary>
        private LxPluginRuntime? ResolveLxRuntime(OnlineSong song)
        {
            if (_lxRuntimes.TryGetValue(song.PluginHash, out var runtime) && runtime.SupportsSource(song.Platform))
                return runtime;
            return _lxRuntimes.Values.FirstOrDefault(r => r.SupportsSource(song.Platform));
        }
    }
}
