using Jint;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WinUIMusicPlayer.Services.Plugins
{
    /// <summary>LX 用户脚本头信息(UserScript 元数据)。</summary>
    public class LxScriptInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Homepage { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
    }

    /// <summary>LX 插件初始化信息中声明的单个音源。</summary>
    public class LxSourceInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "music";
        public List<string> Actions { get; set; } = [];
        public List<string> Qualities { get; set; } = [];
    }

    /// <summary>标准 LX 音源键与显示名(与移动端/洛雪生态一致)。</summary>
    public static class LxSources
    {
        public const string Kw = "kw", Kg = "kg", Tx = "tx", Wy = "wy", Mg = "mg";

        public static readonly IReadOnlyDictionary<string, string> SourceNames = new Dictionary<string, string>
        {
            [Kw] = "酷我音乐", [Kg] = "酷狗音乐", [Tx] = "QQ音乐", [Wy] = "网易云音乐", [Mg] = "咪咕音乐",
        };

        public static readonly string[] StandardOrder = [Kw, Kg, Tx, Wy, Mg];

        public static string DisplayName(string source) => SourceNames.TryGetValue(source, out var name) ? name : source;

        /// <summary>归一化音质档位(洛雪/LX 生态键)。</summary>
        public static string NormalizeQuality(string? q) => q switch
        {
            "128k" or "128" => "128k",
            "320k" or "320" => "320k",
            "flac" or "2000" or "2000k" or "sq" => "flac",
            "flac24bit" or "hires" or "hr" or "zq24" => "flac24bit",
            null or "" => "",
            _ => q,
        };
    }

    /// <summary>LX 插件 musicUrl 响应(归一化后)。</summary>
    public class LxMusicUrlResult
    {
        public string Url { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public Dictionary<string, string>? Headers { get; set; }
    }

    /// <summary>
    /// 落雪(LX)插件运行时: 适配 lx-music-desktop 用户音源脚本格式(参考 XianYu-Music-Desktop lxPluginEngine)。
    /// 插件通过 on(EVENT_NAMES.request, handler) 注册请求处理器, 通过 lx.send(EVENT_NAMES.inited, {sources}) 声明初始化完成;
    /// 宿主提供 lx.request(HTTP 同步桥接)/lx.utils(crypto/buffer/zlib) 等 API。
    /// 搜索由应用内置 SDK 完成, 插件仅负责 musicUrl/lyric/pic 解析。
    /// </summary>
    public sealed class LxPluginRuntime : IDisposable
    {
        private readonly Engine _engine;
        private readonly ILogger _logger;
        private readonly object _gate = new();
        private bool _disposed;

        public string Hash { get; }
        public string Code { get; }
        public LxScriptInfo ScriptInfo { get; }
        /// <summary>初始化声明的音源表(source key → 信息)。</summary>
        public Dictionary<string, LxSourceInfo> Sources { get; } = [];

        private static readonly Regex HeaderLineRegex = new(@"^\s*(?:\/\/|\*)?\s?@\s?([\w-]+)\s+(.+)$", RegexOptions.Compiled);
        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        private LxPluginRuntime(string code, string hash, LxScriptInfo scriptInfo, Engine engine, ILogger logger)
        {
            Code = code;
            Hash = hash;
            ScriptInfo = scriptInfo;
            _engine = engine;
            _logger = logger;
        }

        /// <summary>检测代码是否为 LX 用户脚本。
        /// 识别依据 LX API 特征(解构 globalThis.lx / EVENT_NAMES 事件握手)且非 CommonJS 导出;
        /// 元数据头两种风格均支持: ==UserScript== 标记与 /*! 块注释 @name/@version(ikun 等源无标记)。</summary>
        public static bool IsLxScript(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            // MusicFree 插件是 CommonJS 导出(module.exports = {platform:...}), LX 脚本没有
            if (code.Contains("module.exports")) return false;
            return code.Contains("globalThis.lx") || code.Contains("lx.EVENT_NAMES") ||
                   code.Contains("EVENT_NAMES.request") || code.Contains("EVENT_NAMES.inited") ||
                   code.Contains("on(EVENT_NAMES") || code.Contains("send(EVENT_NAMES");
        }

        /// <summary>解析 UserScript 头部元数据(支持 // 与 /* 两种注释风格)。</summary>
        public static LxScriptInfo ParseScriptInfo(string code)
        {
            var info = new LxScriptInfo();
            var lines = code.Replace("\r\n", "\n").Split('\n');
            bool inHeader = false;
            foreach (var rawLine in lines.Take(200))
            {
                var line = rawLine.Trim();
                if (!inHeader)
                {
                    // ==UserScript== 标记头 或 /*! 块注释元数据头(ikun 等源: @name/@version)
                    if (line.StartsWith("// ==UserScript==") || line.StartsWith("/* ==UserScript==") ||
                        line.StartsWith("//==UserScript==") || line.StartsWith("/*==UserScript==") ||
                        line.StartsWith("/*"))
                        inHeader = true;
                    continue;
                }
                if (line.StartsWith("// ==/UserScript==") || line.StartsWith("//==/UserScript==") || line.StartsWith("*/"))
                {
                    inHeader = false; // 允许后续再出现正式 UserScript 头
                    continue;
                }
                var m = HeaderLineRegex.Match(rawLine);
                if (!m.Success) continue;
                var key = m.Groups[1].Value.ToLowerInvariant();
                var value = m.Groups[2].Value.Trim();
                switch (key)
                {
                    case "name": info.Name = value.Length > 60 ? value[..60] : value; break;
                    case "version": info.Version = value.Length > 36 ? value[..36] : value; break;
                    case "author": info.Author = value.Length > 56 ? value[..56] : value; break;
                    case "description": info.Description = value.Length > 100 ? value[..100] : value; break;
                    case "homepage": case "homepageurl": info.Homepage = value; break;
                    case "downloadurl": case "downloadurl2": info.DownloadUrl = value; break;
                }
            }
            return info;
        }

        /// <summary>加载并初始化 LX 插件。后台线程调用。</summary>
        public static LxPluginRuntime Load(string code, string hash, ILogger logger)
        {
            var scriptInfo = ParseScriptInfo(code);
            var engine = new Engine(options => options
                .TimeoutInterval(TimeSpan.FromMinutes(2))
                .MaxStatements(20_000_000)
                .LimitRecursion(10_000));

            var runtime = new LxPluginRuntime(code, hash, scriptInfo, engine, logger);

            var http = PluginHttpBridge.Shared;
            engine.SetValue("__hostHttp", new Func<string, string, string>((url, optionsJson) =>
                http.Request(url, optionsJson)));
            engine.SetValue("__hostLog", new Action<string, string>((level, msg) =>
                logger.Log(level switch
                {
                    "error" => LogLevel.Error,
                    "warn" => LogLevel.Warning,
                    "debug" => LogLevel.Debug,
                    _ => LogLevel.Information
                }, "[LX:{Name}] {Message}", scriptInfo.Name, msg)));
            // zlib/压缩桥接: base64 进出, 自动识别 zlib/gzip/raw deflate
            engine.SetValue("__hostInflate", new Func<string, string>(base64 =>
                Convert.ToBase64String(Inflate(Convert.FromBase64String(base64)))));
            engine.SetValue("__hostDeflate", new Func<string, string>(base64 =>
                Convert.ToBase64String(Deflate(Convert.FromBase64String(base64)))));
            engine.SetValue("__hostMd5Hex", new Func<string, string>(input =>
                Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant()));
            engine.SetValue("__hostRandomBytes", new Func<int, string>(size =>
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(Math.Clamp(size, 0, 65536)))));
            engine.SetValue("__hostAesEncrypt", new Func<string, string, string, string, string>((dataB64, mode, keyB64, ivB64) =>
                Convert.ToBase64String(AesEncrypt(Convert.FromBase64String(dataB64), mode, Convert.FromBase64String(keyB64),
                    string.IsNullOrEmpty(ivB64) ? null : Convert.FromBase64String(ivB64)))));

            engine.Execute(BuildBootstrap(code, scriptInfo));
            runtime.ReadInitState();
            return runtime;
        }

        /// <summary>读取初始化结果(引擎内已同步完成握手)。</summary>
        private void ReadInitState()
        {
            var stateJson = _engine.Evaluate("JSON.stringify(globalThis.__lxState)").AsString();
            var state = JsonSerializer.Deserialize<LxEngineState>(stateJson, JsonOpts)!;
            if (state.InitError is not null)
                throw new InvalidOperationException($"LX 插件初始化失败: {state.InitError}");
            if (!state.Inited)
                throw new InvalidOperationException("LX 插件未发送 inited 事件(初始化超时)");
            // 引导握手把音源存在 __lxState.initInfo.sources(非顶层 sources)
            foreach (var (source, info) in state.InitInfo?.Sources ?? [])
            {
                if (info?.Type != "music") continue;
                var actions = info.Actions?.Where(a => a is "musicUrl" or "lyric" or "pic").ToList() ?? [];
                if (actions.Count == 0) continue;
                Sources[source] = new LxSourceInfo
                {
                    Name = info.Name ?? string.Empty,
                    Type = "music",
                    Actions = actions,
                    Qualities = info.Qualities ?? [],
                };
            }
            if (Sources.Count == 0)
                _logger.LogWarning("LX 插件 {Name} 未声明有效音源", ScriptInfo.Name);
        }

        private class LxEngineState
        {
            public bool Inited { get; set; }
            public string? InitError { get; set; }
            public LxRawInitInfo? InitInfo { get; set; }
        }

        private class LxRawInitInfo
        {
            public Dictionary<string, LxRawSourceInfo>? Sources { get; set; }
        }

        private class LxRawSourceInfo
        {
            public string? Name { get; set; }
            public string? Type { get; set; }
            public List<string>? Actions { get; set; }
            /// <summary>LX 官方音质字段拼写为 qualitys。</summary>
            [JsonPropertyName("qualitys")]
            public List<string>? Qualities { get; set; }
        }

        public bool SupportsSource(string source) => Sources.ContainsKey(source);

        public bool SupportsAction(string source, string action) =>
            Sources.TryGetValue(source, out var info) && info.Actions.Contains(action);

        /// <summary>请求插件解析音乐直链(musicUrl 动作)。quality: 128k/320k/flac/flac24bit 等。</summary>
        public (LxMusicUrlResult? Result, string? Error) GetMusicUrl(string source, string musicInfoJson, string quality)
        {
            var (json, error) = InvokeRequestHandler(source, "musicUrl", musicInfoJson, quality);
            if (error is not null) return (null, error);
            if (json is null) return (null, "插件未返回播放地址");
            var normalized = FindUrlValue(JsonDocument.Parse(json).RootElement);
            if (normalized is null || normalized.Url.Length == 0 || !normalized.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return (null, "插件返回的播放地址无效");
            return (normalized, null);
        }

        /// <summary>请求插件解析歌词(lyric 动作)。</summary>
        public (string? Lrc, string? Translation, string? Error) GetLyric(string source, string musicInfoJson)
        {
            var (json, error) = InvokeRequestHandler(source, "lyric", musicInfoJson, null);
            if (error is not null)
                return (null, null, Regex.IsMatch(error, @"action\s+not\s+support|not\s+support", RegexOptions.IgnoreCase)
                    ? "不支持歌词" : error);
            if (json is null) return (null, null, "插件未返回歌词");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string Pick(params string[] names)
            {
                foreach (var n in names)
                    if (root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.GetString()))
                        return v.GetString()!;
                return string.Empty;
            }
            var lrc = Pick("lyric", "rawLrc", "lrc", "lxlyric", "yrc", "qrc", "eslrc");
            var tlyric = Pick("tlyric", "translation", "translateLyric");
            if (lrc.Length == 0) return (null, null, "歌词内容为空");
            return (lrc, tlyric.Length > 0 ? tlyric : null, null);
        }

        /// <summary>请求插件解析封面(pic 动作)。</summary>
        public (string? Url, string? Error) GetPic(string source, string musicInfoJson)
        {
            var (json, error) = InvokeRequestHandler(source, "pic", musicInfoJson, null);
            if (error is not null) return (null, error);
            if (json is null) return (null, "插件未返回封面");
            var raw = JsonDocument.Parse(json).RootElement;
            var url = raw.ValueKind == JsonValueKind.String ? raw.GetString() : null;
            if (string.IsNullOrEmpty(url) || !url!.StartsWith("http", StringComparison.OrdinalIgnoreCase) || url.Length > 2048)
                return (null, "插件返回的封面无效");
            return (url, null);
        }

        /// <summary>
        /// 调用插件注册的 request 处理器: requestHandler({source, action, info: {musicInfo, type}})。
        /// 同时兼容处理器返回值与 lx.send(EVENT_NAMES.request, {..., result}) 两种应答方式(返回值优先)。
        /// </summary>
        private (string? Json, string? Error) InvokeRequestHandler(string source, string action, string musicInfoJson, string? quality)
        {
            lock (_gate)
            {
                if (_disposed) return (null, "插件已释放");
                var typeJson = quality is null ? "null" : JsonSerializer.Serialize(quality);
                var callJs = $$"""
                    globalThis.__lxCall = (function(){
                        var state = globalThis.__lxState;
                        state.lastHandlerResult = undefined;
                        state.lastRequestResult = null;
                        state.lastRequestError = null;
                        if (!state.requestHandler) return {error: '插件未注册请求处理器'};
                        try {
                            Promise.resolve(state.requestHandler({
                                source: {{JsonSerializer.Serialize(source)}},
                                action: {{JsonSerializer.Serialize(action)}},
                                info: { type: {{typeJson}}, musicInfo: {{musicInfoJson}} }
                            })).then(function(r){
                                state.lastHandlerResult = r;
                            }, function(e){
                                state.lastRequestError = (e && e.message) ? String(e.message) : String(e);
                            });
                            return {ok: true};
                        } catch (e) {
                            return {error: (e && e.message) ? String(e.message) : String(e)};
                        }
                    })();
                    """;
                _engine.Execute(callJs);
                var callJson = _engine.Evaluate("JSON.stringify(globalThis.__lxCall)").AsString();
                var call = JsonSerializer.Deserialize<JsonElement>(callJson);
                if (call.TryGetProperty("error", out var callErr)) return (null, callErr.GetString());

                // 引擎 promise 队列已在 Execute 后排空, 读取最终结果
                var outcomeJson = _engine.Evaluate("""
                    JSON.stringify((function(){
                        var state = globalThis.__lxState;
                        var out = {};
                        if (state.lastRequestError) { out.error = state.lastRequestError; return out; }
                        var r = (state.lastHandlerResult !== undefined && state.lastHandlerResult !== null)
                            ? state.lastHandlerResult
                            : ((state.lastRequestResult !== undefined && state.lastRequestResult !== null) ? state.lastRequestResult : undefined);
                        if (r !== undefined && r !== null) {
                            try { JSON.stringify(r); out.result = r; } catch (e) { out.error = 'result not serializable'; }
                        }
                        return out;
                    })())
                    """).AsString();
                using var outcome = JsonDocument.Parse(outcomeJson);
                if (outcome.RootElement.TryGetProperty("error", out var err)) return (null, err.GetString());
                if (outcome.RootElement.TryGetProperty("result", out var result))
                    return (result.GetRawText(), null);
                return (null, null);
            }
        }

        // ---------------- 响应归一化 ----------------

        private static readonly string[] UrlKeys = ["url", "musicUrl", "audioUrl", "playUrl", "link", "src", "path", "data"];

        /// <summary>从任意结构中提取直链(深度 4, 参考 XianYu findLxUrlValue)。</summary>
        private static LxMusicUrlResult? FindUrlValue(JsonElement value, int depth = 0)
        {
            if (depth > 4) return null;
            if (value.ValueKind == JsonValueKind.String)
            {
                var s = value.GetString()!;
                return s.Length > 0 ? new LxMusicUrlResult { Url = s } : null;
            }
            if (value.ValueKind != JsonValueKind.Object) return null;
            foreach (var key in UrlKeys)
            {
                if (!value.TryGetProperty(key, out var child)) continue;
                if (child.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
                if (child.ValueKind is JsonValueKind.String or JsonValueKind.Object)
                {
                    var nested = FindUrlValue(child, depth + 1);
                    if (nested is not null)
                    {
                        if (value.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String) nested.Type = t.GetString()!;
                        else if (value.TryGetProperty("quality", out var q) && q.ValueKind == JsonValueKind.String) nested.Type = q.GetString()!;
                        if (value.TryGetProperty("headers", out var h) && h.ValueKind == JsonValueKind.Object)
                        {
                            nested.Headers = [];
                            foreach (var p in h.EnumerateObject())
                                if (p.Value.ValueKind == JsonValueKind.String) nested.Headers[p.Name] = p.Value.GetString()!;
                        }
                        return nested;
                    }
                }
            }
            return null;
        }

        // ---------------- 压缩/加密宿主实现 ----------------

        /// <summary>自动识别 zlib/gzip/raw deflate 并解压。</summary>
        private static byte[] Inflate(byte[] data)
        {
            using var output = new MemoryStream();
            if (data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b)
            {
                using var input = new MemoryStream(data);
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                gzip.CopyTo(output);
            }
            else if (data.Length >= 2 && (data[0] & 0x0f) == 8 && ((data[0] << 8) | data[1]) % 31 == 0)
            {
                using var input = new MemoryStream(data);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                zlib.CopyTo(output);
            }
            else
            {
                using var input = new MemoryStream(data);
                using var raw = new DeflateStream(input, CompressionMode.Decompress);
                raw.CopyTo(output);
            }
            return output.ToArray();
        }

        private static byte[] Deflate(byte[] data)
        {
            using var output = new MemoryStream();
            using (var input = new MemoryStream(data))
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                input.CopyTo(zlib);
            }
            return output.ToArray();
        }

        private static byte[] AesEncrypt(byte[] data, string mode, byte[] key, byte[]? iv)
        {
            using var aes = Aes.Create();
            aes.Mode = mode.ToLowerInvariant() switch
            {
                "ecb" => System.Security.Cryptography.CipherMode.ECB,
                _ => System.Security.Cryptography.CipherMode.CBC,
            };
            aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
            var keyLen = key.Length is 16 or 24 or 32 ? key.Length : 32;
            var paddedKey = new byte[keyLen];
            Array.Copy(key, paddedKey, Math.Min(key.Length, keyLen));
            aes.Key = paddedKey;
            if (aes.Mode == System.Security.Cryptography.CipherMode.CBC)
            {
                var paddedIv = new byte[16];
                if (iv is { Length: > 0 }) Array.Copy(iv, paddedIv, Math.Min(iv.Length, 16));
                aes.IV = paddedIv;
            }
            using var encryptor = aes.CreateEncryptor();
            return encryptor.TransformFinalBlock(data, 0, data.Length);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
            }
        }

        // ---------------- JS 引导 ----------------

        /// <summary>构建 LX 插件执行环境(参考 lx-music-desktop preload + XianYu lxPluginEngine)。</summary>
        private static string BuildBootstrap(string code, LxScriptInfo scriptInfo)
        {
            var scriptInfoJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["name"] = scriptInfo.Name,
                ["description"] = scriptInfo.Description,
                ["version"] = scriptInfo.Version,
                ["author"] = scriptInfo.Author,
                ["homepage"] = scriptInfo.Homepage,
                ["rawScript"] = string.Empty,
            });
            var bootstrap = $$"""
                (function(){
                var EVENT_NAMES = { request: 'request', inited: 'inited', updateAlert: 'updateAlert' };
                var eventNames = Object.values(EVENT_NAMES);
                var state = globalThis.__lxState = {
                    inited: false, initError: null, initInfo: null,
                    requestHandler: null, initedHandlers: [],
                    lastHandlerResult: undefined, lastRequestResult: null, lastRequestError: null,
                };
                function log(level, msg) { try { __hostLog(level, msg); } catch (e) {} }
                function argsMsg() { return Array.prototype.slice.call(arguments).map(function(a){ try { return typeof a === 'object' ? JSON.stringify(a) : String(a); } catch (e) { return String(a); } }).join(' '); }
                // 完整替换 console: 部分插件调用 groupEnd 等扩展方法, Jint 默认无此实现
                globalThis.console = {
                    log: function(){ log('info', argsMsg.apply(null, arguments)); },
                    info: function(){ log('info', argsMsg.apply(null, arguments)); },
                    warn: function(){ log('warn', argsMsg.apply(null, arguments)); },
                    error: function(){ log('error', argsMsg.apply(null, arguments)); },
                    debug: function(){ log('debug', argsMsg.apply(null, arguments)); },
                    group: function(){}, groupCollapsed: function(){}, groupEnd: function(){},
                    table: function(){}, trace: function(){}, dir: function(){},
                };
                // setTimeout/setInterval: 同步立即执行(宿主 HTTP 为同步桥接, 整个链路在一次引擎执行内完成)
                globalThis.setTimeout = function(fn){ if (typeof fn === 'function') { try { fn(); } catch (e) { log('error', 'setTimeout: ' + (e && e.message)); } } return 0; };
                globalThis.clearTimeout = function(){};
                globalThis.setInterval = globalThis.setTimeout;
                globalThis.clearInterval = function(){};

                // ---- 字节/编码辅助 ----
                var __B64CH = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
                function __u8ToB64(u8) {
                    var out = '', i;
                    for (i = 0; i + 2 < u8.length; i += 3) {
                        var n = (u8[i] << 16) | (u8[i+1] << 8) | u8[i+2];
                        out += __B64CH[(n >> 18) & 63] + __B64CH[(n >> 12) & 63] + __B64CH[(n >> 6) & 63] + __B64CH[n & 63];
                    }
                    var rem = u8.length - i;
                    if (rem === 1) { var n1 = u8[i] << 16; out += __B64CH[(n1 >> 18) & 63] + __B64CH[(n1 >> 12) & 63] + '=='; }
                    else if (rem === 2) { var n2 = (u8[i] << 16) | (u8[i+1] << 8); out += __B64CH[(n2 >> 18) & 63] + __B64CH[(n2 >> 12) & 63] + __B64CH[(n2 >> 6) & 63] + '='; }
                    return out;
                }
                function __b64ToU8(b64) {
                    b64 = String(b64).replace(/[^A-Za-z0-9+/=]/g, '');
                    var clean = b64.replace(/=/g, '');
                    var len = clean.length, outLen = Math.floor(len * 3 / 4);
                    var u8 = new Uint8Array(outLen), p = 0, i;
                    var rev = {}; for (i = 0; i < 64; i++) rev[__B64CH[i]] = i;
                    for (i = 0; i + 3 < len; i += 4) {
                        var n = (rev[clean[i]] << 18) | (rev[clean[i+1]] << 12) | (rev[clean[i+2]] << 6) | rev[clean[i+3]];
                        u8[p++] = (n >> 16) & 255; u8[p++] = (n >> 8) & 255; u8[p++] = n & 255;
                    }
                    var rem = len - i;
                    if (rem === 2) { var n2 = (rev[clean[i]] << 12) | (rev[clean[i+1]] << 6); u8[p++] = (n2 >> 16) & 255; }
                    else if (rem === 3) { var n3 = (rev[clean[i]] << 18) | (rev[clean[i+1]] << 12) | (rev[clean[i+2]] << 6); u8[p++] = (n3 >> 16) & 255; u8[p++] = (n3 >> 8) & 255; }
                    return u8;
                }
                function __u8ToHex(u8) { var s = ''; for (var i = 0; i < u8.length; i++) s += (u8[i] < 16 ? '0' : '') + u8[i].toString(16); return s; }
                function __hexToU8(hex) {
                    hex = String(hex).replace(/[^0-9a-fA-F]/g, '');
                    var u8 = new Uint8Array(hex.length >> 1);
                    for (var i = 0; i < u8.length; i++) u8[i] = parseInt(hex.substr(i*2, 2), 16);
                    return u8;
                }
                function __anyToU8(v) {
                    if (v == null) return new Uint8Array(0);
                    if (v instanceof Uint8Array) return v;
                    if (typeof v === 'string') { return __strToU8(v); }
                    if (typeof v === 'number') return new Uint8Array([v & 255]);
                    if (typeof v === 'object' && typeof v.length === 'number') {
                        var u8 = new Uint8Array(v.length);
                        for (var i = 0; i < v.length; i++) u8[i] = v[i] & 255;
                        return u8;
                    }
                    return new Uint8Array(0);
                }
                function __strToU8(s) {
                    s = unescape(encodeURIComponent(String(s)));
                    var u8 = new Uint8Array(s.length);
                    for (var i = 0; i < s.length; i++) u8[i] = s.charCodeAt(i) & 255;
                    return u8;
                }
                function __b64HexToU8(s, format) { return format === 'hex' ? __hexToU8(s) : __b64ToU8(s); }
                function __objToForm(obj) {
                    var parts = [];
                    for (var k in obj) { if (obj[k] == null) continue; parts.push(encodeURIComponent(k) + '=' + encodeURIComponent(String(obj[k]))); }
                    return parts.join('&');
                }

                // ---- needle 风格 HTTP: lx.request(url, options, callback(err, resp, body)) ----
                function lxRequest(url, options, callback) {
                    options = options || {};
                    var method = (options.method || 'get').toLowerCase();
                    var headers = {};
                    var h = options.headers || {};
                    for (var k in h) { if (Object.prototype.hasOwnProperty.call(h, k)) headers[String(k)] = String(h[k]); }
                    var body = null; var bodyIsBase64 = false;
                    if (options.body != null) {
                        if (typeof options.body === 'string') { body = options.body; }
                        else if (typeof options.body === 'object' && options.body instanceof Uint8Array) { body = __u8ToB64(options.body); bodyIsBase64 = true; }
                        else if (typeof options.body === 'object') {
                            body = JSON.stringify(options.body);
                            if (!headers['Content-Type'] && !headers['content-type']) headers['Content-Type'] = 'application/json';
                        }
                    } else if (options.form != null) {
                        if (typeof options.form === 'string') { body = options.form; }
                        else { body = __objToForm(options.form); }
                        if (!headers['Content-Type'] && !headers['content-type']) headers['Content-Type'] = 'application/x-www-form-urlencoded';
                    } else if (options.formData != null) {
                        if (typeof options.formData === 'string') { body = options.formData; }
                        else { body = __objToForm(options.formData); }
                        if (!headers['Content-Type'] && !headers['content-type']) headers['Content-Type'] = 'application/x-www-form-urlencoded';
                    }
                    var reqOpts = { method: method, headers: headers };
                    if (bodyIsBase64) reqOpts.bodyBase64 = body; else if (body != null) reqOpts.body = body;
                    if (options.timeout != null && isFinite(options.timeout)) reqOpts.timeoutMs = Math.round(options.timeout);
                    var raw;
                    try { raw = __hostHttp(String(url), JSON.stringify(reqOpts)); }
                    catch (e) { try { callback(e, null, null); } catch (e2) {} return function(){}; }
                    var resp;
                    try { resp = JSON.parse(raw); } catch (e) { resp = { error: 'bad host response' }; }
                    if (resp.error) {
                        try { callback(new Error(resp.error), null, null); } catch (e) {}
                        return function(){};
                    }
                    var bodyOut = resp.bodyBase64 ? __b64ToU8(resp.bodyBase64) : (resp.body || '');
                    var parsed = bodyOut;
                    if (typeof bodyOut === 'string') { try { parsed = JSON.parse(bodyOut); } catch (e) {} }
                    var respObj = { statusCode: resp.statusCode, statusMessage: resp.statusText, headers: resp.headers || {}, bytes: bodyOut && bodyOut.length || 0, raw: bodyOut, body: parsed };
                    try { callback(null, respObj, parsed); } catch (e) { log('error', 'request callback: ' + (e && e.message)); }
                    return function(){};
                }

                function handleInit(info) {
                    if (state.inited) return;
                    if (!info || typeof info !== 'object') { state.initError = 'Missing required parameter init info'; return; }
                    state.inited = true;
                    state.initInfo = info;
                }

                var lx = {
                    EVENT_NAMES: EVENT_NAMES,
                    request: lxRequest,
                    send: function(eventName, data) {
                        return new Promise(function(resolve, reject){
                            if (eventNames.indexOf(eventName) < 0) return reject(new Error('The event is not supported: ' + eventName));
                            switch (eventName) {
                                case EVENT_NAMES.inited: handleInit(data); resolve(); break;
                                case EVENT_NAMES.updateAlert: resolve(); break;
                                case EVENT_NAMES.request:
                                    // send-back 应答方式: 插件通过 lx.send(request, {..., result}) 回传
                                    state.lastRequestResult = data && data.result !== undefined ? data.result : data;
                                    resolve(); break;
                                default: reject(new Error('Unknown event name: ' + eventName));
                            }
                        });
                    },
                    on: function(eventName, handler) {
                        if (eventNames.indexOf(eventName) < 0) return Promise.reject(new Error('The event is not supported: ' + eventName));
                        if (typeof handler !== 'function') return Promise.reject(new Error('handler is not a function'));
                        if (eventName === EVENT_NAMES.request) state.requestHandler = handler;
                        else if (eventName === EVENT_NAMES.inited) state.initedHandlers.push(handler);
                        return Promise.resolve();
                    },
                    utils: {
                        crypto: {
                            aesEncrypt: function(buffer, mode, key, iv) {
                                var data = __anyToU8(buffer); var keyB = __anyToU8(key); var ivB = iv == null ? null : __anyToU8(iv);
                                var modeStr = typeof mode === 'string' ? (/ecb/i.test(mode) ? 'ecb' : 'cbc') : 'cbc';
                                var out = __hostAesEncrypt(__u8ToB64(data), modeStr, __u8ToB64(keyB), ivB ? __u8ToB64(ivB) : '');
                                return __b64ToU8(out);
                            },
                            rsaEncrypt: function(buffer) { return buffer; },
                            randomBytes: function(size) { return __b64ToU8(__hostRandomBytes(size | 0)); },
                            md5: function(str) { return __hostMd5Hex(String(str)); },
                        },
                        buffer: {
                            from: function(input, encoding) {
                                if (typeof input === 'string' && (encoding === 'base64' || encoding === 'hex')) {
                                    return __b64HexToU8(input, encoding);
                                }
                                return __anyToU8(input);
                            },
                            bufToString: function(buf, format) {
                                var u8 = __anyToU8(buf);
                                if (format === 'base64') return __u8ToB64(u8);
                                if (format === 'hex') return __u8ToHex(u8);
                                var s = ''; for (var i = 0; i < u8.length; i++) s += String.fromCharCode(u8[i]);
                                return s;
                            },
                        },
                        zlib: {
                            inflate: function(buf) { return __b64ToU8(__hostInflate(__u8ToB64(__anyToU8(buf)))); },
                            inflateSync: function(buf) { return __b64ToU8(__hostInflate(__u8ToB64(__anyToU8(buf)))); },
                            deflate: function(data) { return __b64ToU8(__hostDeflate(__u8ToB64(__anyToU8(data)))); },
                        },
                    },
                    currentScriptInfo: {{scriptInfoJson}},
                    version: '2.0.0',
                    env: 'desktop',
                };
                globalThis.lx = lx;
                // LX 脚本惯例: 顶层解构 const { EVENT_NAMES } = lx; on(EVENT_NAMES.request, ...)
                globalThis.on = function(eventName, handler) { return lx.on(eventName, handler); };
                globalThis.send = function(eventName, data) { return lx.send(eventName, data); };
                // 兼容 on(inited) 包裹 send(inited) 的脚本: 脚本执行完未初始化时触发一次
                globalThis.__fireLxInited = function() {
                    for (var i = 0; i < state.initedHandlers.length; i++) {
                        try { state.initedHandlers[i]({ source: 'lx', action: 'inited' }); } catch (e) {}
                    }
                };
                })();
                {{code}}
                ;
                (function(){
                if (globalThis.__lxState && !globalThis.__lxState.inited && globalThis.__fireLxInited) globalThis.__fireLxInited();
                })();
                """;
            return bootstrap;
        }
    }
}
