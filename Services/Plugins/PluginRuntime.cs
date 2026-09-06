using Jint;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinUIMusicPlayer.Services.Plugins
{
    /// <summary>
    /// 单个 MusicFree 插件的 Jint 运行时。
    /// 线程模型: 引擎非线程安全, 所有调用经 _gate 串行化; 公共方法均在后台线程执行。
    /// </summary>
    public sealed class PluginRuntime : IDisposable
    {
        private readonly Engine _engine;
        private readonly ILogger _logger;
        private readonly object _gate = new();
        public PluginMetadata Metadata { get; }
        public string Hash { get; }
        public string Code { get; }
        public string StorageFile { get; }
        private readonly ConcurrentDictionary<string, string> _storage = new();
        private bool _disposed;

        private static readonly string[] KnownLibNames =
        [
            "crypto-js", "dayjs", "big-integer", "qs", "he", "pako"
        ];
        private static readonly Dictionary<string, string> LibModuleMap = new()
        {
            ["crypto-js.min.js"] = "crypto-js",
            ["dayjs.min.js"] = "dayjs",
            ["big-integer.min.js"] = "big-integer",
            ["qs.min.js"] = "qs",
            ["he.min.js"] = "he",
            ["pako.min.js"] = "pako",
        };
        private static readonly Lazy<Dictionary<string, string>> LibSources = new(LoadLibSources);
        private static readonly string BootstrapSource = LoadBootstrap();
        private static readonly IReadOnlyDictionary<string, string> EmptyUserVars = new Dictionary<string, string>();

        private static string JsLibDir => Path.Combine(AppContext.BaseDirectory, "Plugins", "js-libs");

        private static string LoadBootstrap()
        {
            return File.ReadAllText(Path.Combine(JsLibDir, "_bootstrap.js"));
        }

        private static Dictionary<string, string> LoadLibSources()
        {
            var result = new Dictionary<string, string>();
            foreach (var (file, module) in LibModuleMap)
            {
                var path = Path.Combine(JsLibDir, file);
                if (File.Exists(path))
                    result[module] = File.ReadAllText(path);
            }
            return result;
        }

        private PluginRuntime(string code, string hash, PluginMetadata metadata, Engine engine, string storageFile, ILogger logger)
        {
            Code = code;
            Hash = hash;
            Metadata = metadata;
            _engine = engine;
            StorageFile = storageFile;
            _logger = logger;
        }

        /// <summary>加载插件代码并初始化运行时。在后台线程调用。</summary>
        public static PluginRuntime Load(string code, string hash, string storageFile, string appVersion, ILogger logger)
            => Load(code, hash, storageFile, appVersion, null, logger);

        /// <summary>加载插件代码并注入用户变量。在后台线程调用。</summary>
        public static PluginRuntime Load(string code, string hash, string storageFile, string appVersion, IReadOnlyDictionary<string, string>? userVariables, ILogger logger)
        {
            var engine = new Engine(options => options
                .TimeoutInterval(TimeSpan.FromMinutes(2))
                .MaxStatements(10_000_000)
                .LimitRecursion(5000));

            var storage = LoadStorage(storageFile);

            engine.SetValue("__hostLog", new Action<string, string>((level, msg) =>
                logger.Log(level switch
                {
                    "error" => LogLevel.Error,
                    "warn" => LogLevel.Warning,
                    "debug" => LogLevel.Debug,
                    _ => LogLevel.Information
                }, "[{Plugin}] {Message}", hash[..8], msg)));

            var http = PluginHttpBridge.Shared;
            engine.SetValue("__hostHttp", new Func<string, string, string>((url, optionsJson) =>
                http.Request(url, optionsJson)));

            engine.SetValue("__storageGet", new Func<string, string?>(key =>
                storage.TryGetValue(key, out var v) ? v : null));
            engine.SetValue("__storageSet", new Action<string, string>((key, value) =>
            {
                storage[key] = value;
                SaveStorage(storageFile, storage);
            }));
            engine.SetValue("__storageRemove", new Action<string>(key =>
            {
                storage.TryRemove(key, out _);
                SaveStorage(storageFile, storage);
            }));

            // 1. 引导(宿主全局 + require + axios 垫片)
            engine.Execute(BootstrapSource);

            // 2. 预注册 JS 库
            foreach (var (module, source) in LibSources.Value)
            {
                var wrapped = "__defineModule('" + module + "', function(module, exports){\n" + source + "\n});";
                engine.Execute(wrapped);
            }

            // 3. 以 CommonJS 形式执行插件代码
            var loader = BuildPluginLoader(code, appVersion, userVariables);
            engine.Execute(loader);
            engine.Execute("""
                globalThis.__pluginMeta = (function(){
                    var p = globalThis.__pluginInstance || {};
                    function s(v){ return typeof v === 'string' ? v : ''; }
                    var methods = ['search','getMediaSource','getLyric','getMusicInfo','getAlbumInfo',
                                   'getArtistWorks','importMusicSheet','getTopLists','getTopListDetail',
                                   'getRecommendSheetTags','getRecommendSheetsByTag']
                        .filter(function(m){ return typeof p[m] === 'function'; });
                    var st = Array.isArray(p.supportedSearchType) ? p.supportedSearchType.filter(function(t){return typeof t === 'string';}) : [];
                    return {
                        platform: s(p.platform), version: s(p.version), author: s(p.author),
                        srcUrl: s(p.srcUrl), appVersion: s(p.appVersion),
                        supportedSearchType: st, methods: methods, userVariables: Array.isArray(p.userVariables) ? p.userVariables : null
                    };
                })();
            """);
            var metaJson = engine.Evaluate("JSON.stringify(globalThis.__pluginMeta)").AsString();
            var metadata = JsonSerializer.Deserialize<PluginMetadata>(metaJson, JsonOptions) ?? new PluginMetadata();

            if (string.IsNullOrWhiteSpace(metadata.Platform))
                throw new InvalidOperationException("插件未导出有效的 platform 字段");

            return new PluginRuntime(code, hash, metadata, engine, storageFile, logger);
        }

        private static string BuildPluginLoader(string code, string appVersion, IReadOnlyDictionary<string, string>? userVariables)
        {
            var userVarsJson = JsonSerializer.Serialize(userVariables ?? EmptyUserVars);
            var envSetup = $$"""
                globalThis.__userVars = {{userVarsJson}};
                globalThis.__pluginEnv = {
                    getUserVariables: function(){ return globalThis.__userVars; },
                    get userVariables(){ return globalThis.__userVars; },
                    os: 'win32', appVersion: {{JsonSerializer.Serialize(appVersion)}}, lang: 'zh-CN'
                };
                """ + """
                // 垫片挂全局而非包装函数参数: 插件内常见 const { Buffer } = require("buffer") 等声明,
                // 与同名函数参数冲突会抛 "Identifier has already been declared"(const 重声明参数是语法错误),
                // 而 globalThis 属性可被 const 声明合法遮蔽。
                if (!globalThis.Buffer) globalThis.Buffer = globalThis.require('buffer');
                if (!globalThis.process) globalThis.process = {platform:'win32', version:'v20.0.0', env:{}, nextTick:function(f){Promise.resolve().then(f);}};
                if (!globalThis.global) globalThis.global = globalThis;
                """;
            // 仅保留 CommonJS 标准参数(require/module/exports)与 env, 其余垫片(console/URL/Buffer 等)走 globalThis
            var wrapper = """
                (function(require, __musicfree_require, module, exports, env){
                """ + code + """
                ;
                globalThis.__pluginInstance = (module.exports && module.exports.default && typeof module.exports.default === 'object')
                    ? module.exports.default
                    : (module.exports && Object.keys(module.exports).length ? module.exports : null);
                })
                """;
            var argsJs = """
                (globalThis.require, globalThis.require, globalThis.__module, globalThis.__module.exports,
                 globalThis.__pluginEnv)
                """;
            return envSetup + "\n" +
                "globalThis.__module = {exports:{}};\n" +
                wrapper + argsJs + ";";
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private static ConcurrentDictionary<string, string> LoadStorage(string storageFile)
        {
            var result = new ConcurrentDictionary<string, string>();
            try
            {
                if (File.Exists(storageFile))
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(storageFile));
                    if (dict is not null)
                        foreach (var kv in dict) result[kv.Key] = kv.Value;
                }
            }
            catch { /* 损坏则重置 */ }
            return result;
        }

        private static void SaveStorage(string storageFile, ConcurrentDictionary<string, string> storage)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(storageFile)!);
                File.WriteAllText(storageFile, JsonSerializer.Serialize(storage));
            }
            catch { /* 保存失败忽略 */ }
        }

        public bool SupportsMethod(string method) => Metadata.Methods.Contains(method);

        /// <summary>当前正在执行的插件调用的起始时刻(ticks), 0 表示空闲。供熔断判断。</summary>
        private long _callInProgressSinceTicks;

        /// <summary>调用插件方法(返回 state JSON: {done,json,error,unsupported})。后台线程调用。
        /// 引擎锁熔断: Jint 单引擎串行, 超时不取消的调用(插件内部 HTTP 卡住最长 90 秒)会占住 _gate,
        /// 后续调用(如音质逐档回退)在锁上排队堆积, 表现为该插件所有歌曲长时间无法解析;
        /// 已有调用执行超过 5 秒时新调用直接返回"插件正忙", 不再排队。</summary>
        public PluginCallState CallMethodState(string method, string argsJson)
        {
            var since = Interlocked.Read(ref _callInProgressSinceTicks);
            if (since != 0 && DateTime.UtcNow.Ticks - since > TimeSpan.FromSeconds(5).Ticks)
                return new PluginCallState { Error = "插件正忙(上一次调用未返回)，请稍后重试" };
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(PluginRuntime));
                Interlocked.Exchange(ref _callInProgressSinceTicks, DateTime.UtcNow.Ticks);
                try
                {
                    _engine.Evaluate($"__callPluginMethod({JsonSerializer.Serialize(method)}, {argsJson})");
                    var stateJson = _engine.Evaluate("""
                        (function(){
                            var s = globalThis.__lastCallResult;
                            if (!s) return JSON.stringify({done:false, error:'internal: no state'});
                            return JSON.stringify({done: !!s.done, json: s.json === undefined ? null : s.json,
                                                   error: s.error === undefined ? null : s.error, unsupported: !!s.unsupported});
                        })()
                        """).AsString();
                    return JsonSerializer.Deserialize<PluginCallState>(stateJson, JsonOptions) ?? new PluginCallState { Error = "internal" };
                }
                finally
                {
                    Interlocked.Exchange(ref _callInProgressSinceTicks, 0);
                }
            }
        }

        public class PluginCallState
        {
            public bool Done { get; set; }
            public string? Json { get; set; }
            public string? Error { get; set; }
            public bool Unsupported { get; set; }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
            }
        }
    }

    /// <summary>共享 HTTP 桥: 插件沙箱内的同步请求由宿主 HttpClient 执行。</summary>
    public sealed class PluginHttpBridge
    {
        public static readonly PluginHttpBridge Shared = new();

        private readonly HttpClient _client;
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
        // 响应序列化使用 camelCase, 与插件侧 axios 垫片读取的字段名(parsed.statusCode/parsed.body 等)一致
        private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private PluginHttpBridge()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip
                    | System.Net.DecompressionMethods.Deflate
                    | System.Net.DecompressionMethods.Brotli,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                UseCookies = false,
            };
            _client = new HttpClient(handler);
            _client.Timeout = TimeSpan.FromSeconds(90);
        }

        /// <summary>同步执行 HTTP 请求(仅在后台线程调用)。返回 JSON: {statusCode,statusText,headers,body,bodyBase64} 或 {error}。</summary>
        public string Request(string url, string optionsJson)
        {
            try
            {
                var options = JsonSerializer.Deserialize<RequestOptions>(optionsJson, JsonOptions) ?? new RequestOptions();
                using var req = new HttpRequestMessage(new HttpMethod(options.Method?.ToUpperInvariant() ?? "GET"), url);
                if (options.Headers is not null)
                {
                    foreach (var kv in options.Headers)
                        req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
                if (!string.IsNullOrEmpty(options.Body))
                {
                    req.Content = new StringContent(options.Body, Encoding.UTF8);
                    // StringContent 会设置默认 Content-Type, 还原插件指定值
                    if (options.Headers is not null)
                    {
                        foreach (var kv in options.Headers)
                        {
                            if (kv.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                            {
                                req.Content.Headers.Remove("Content-Type");
                                req.Content.Headers.TryAddWithoutValidation("Content-Type", kv.Value);
                            }
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(options.BodyBase64))
                {
                    req.Content = new ByteArrayContent(Convert.FromBase64String(options.BodyBase64));
                    if (options.Headers is not null)
                    {
                        foreach (var kv in options.Headers)
                        {
                            if (kv.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                                req.Content.Headers.TryAddWithoutValidation("Content-Type", kv.Value);
                        }
                    }
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Clamp(options.TimeoutMs ?? 15000, 1000, 90000)));
                using var response = _client.Send(req, HttpCompletionOption.ResponseContentRead, cts.Token);

                var headers = new Dictionary<string, string>();
                foreach (var h in response.Headers) headers[h.Key] = string.Join(", ", h.Value);
                foreach (var h in response.Content.Headers) headers[h.Key] = string.Join(", ", h.Value);

                var bytes = response.Content.ReadAsByteArrayAsync(cts.Token).GetAwaiter().GetResult();
                var result = new ResponsePayload
                {
                    StatusCode = (int)response.StatusCode,
                    StatusText = response.ReasonPhrase ?? string.Empty,
                    Headers = headers,
                };
                if (options.WantBinary)
                    result.BodyBase64 = Convert.ToBase64String(bytes);
                else
                    result.Body = Encoding.UTF8.GetString(bytes);
                return JsonSerializer.Serialize(result, PayloadOptions);
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new ResponsePayload { Error = ex.Message }, PayloadOptions);
            }
        }

        public class RequestOptions
        {
            public string? Method { get; set; }
            public Dictionary<string, string>? Headers { get; set; }
            public string? Body { get; set; }
            public string? BodyBase64 { get; set; }
            public double? TimeoutMs { get; set; }
            public bool WantBinary { get; set; }
        }

        public class ResponsePayload
        {
            public int StatusCode { get; set; }
            public string? StatusText { get; set; }
            public Dictionary<string, string>? Headers { get; set; }
            public string? Body { get; set; }
            public string? BodyBase64 { get; set; }
            public string? Error { get; set; }
        }
    }
}
