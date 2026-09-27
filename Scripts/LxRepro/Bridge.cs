using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;

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
                // 同域自动回带 Cookie(对齐弦予 capture_set_cookies): 插件未显式设置 Cookie 头时附加 Jar 中该域的值
                var uri = new Uri(url);
                if (options.Headers is null || !options.Headers.Keys.Any(k => k.Equals("Cookie", StringComparison.OrdinalIgnoreCase)))
                {
                    var jarHeader = CookieJar.HeaderFor(uri);
                    if (jarHeader.Length > 0) req.Headers.TryAddWithoutValidation("Cookie", jarHeader);
                }
                using var response = _client.Send(req, HttpCompletionOption.ResponseContentRead, cts.Token);

                // 捕获响应 Set-Cookie 到 Jar(对齐弦予 store.rs): 登录类插件依赖 cookie 在请求间保持
                CookieJar.Capture(response, uri);

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

        /// <summary>插件 HTTP Cookie Jar(对齐弦予 store.rs): 按 host 存储, 响应捕获 Set-Cookie,
        /// 同域请求自动回带, 供登录类插件(B站/QQ)在请求间保持会话。</summary>
        public static class CookieJar
        {
            private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _store = new();

            /// <summary>从响应捕获 Set-Cookie(忽略 Expires/Path 等属性, 简化持久)。</summary>
            public static void Capture(HttpResponseMessage response, Uri uri)
            {
                try
                {
                    IEnumerable<string> values;
                    if (!response.Headers.TryGetValues("Set-Cookie", out values)) return;
                    var jar = _store.GetOrAdd(uri.Host, _ => new ConcurrentDictionary<string, string>());
                    foreach (var raw in values)
                    {
                        // 形如 "name=value; Path=/; Expires=..." 取第一段
                        var first = raw.Split(';')[0].Trim();
                        var eq = first.IndexOf('=');
                        if (eq <= 0) continue;
                        var name = first[..eq].Trim();
                        var value = first[(eq + 1)..].Trim();
                        if (name.Length == 0) continue;
                        // 删除标记(过去时间/空值)则移除, 否则写入
                        if (value.Length == 0) jar.TryRemove(name, out _);
                        else jar[name] = value;
                    }
                }
                catch { /* Cookie 解析失败不阻断请求 */ }
            }

            /// <summary>该域的 Cookie 请求头值("a=1; b=2"); 无 cookie 返回空串。</summary>
            public static string HeaderFor(Uri uri)
            {
                if (!_store.TryGetValue(uri.Host, out var jar) || jar.IsEmpty) return string.Empty;
                return string.Join("; ", jar.Select(kv => $"{kv.Key}={kv.Value}"));
            }

            /// <summary>设置指定域的 cookie(供插件 cookies.set 模块)。</summary>
            public static void Set(string host, string name, string value)
            {
                if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(name)) return;
                var jar = _store.GetOrAdd(host, _ => new ConcurrentDictionary<string, string>());
                jar[name] = value ?? string.Empty;
            }

            /// <summary>读取指定域全部 cookie(供插件 cookies.get 模块)。</summary>
            public static Dictionary<string, string> For(string host)
                => _store.TryGetValue(host, out var jar) ? jar.ToDictionary(kv => kv.Key, kv => kv.Value) : [];

            /// <summary>清空全部 cookie(供插件 cookies.clearAll 模块)。</summary>
            public static void ClearAll() => _store.Clear();
        }
    }
