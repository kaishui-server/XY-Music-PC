using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WinUIMusicPlayer.Services.Plugins
{
    /// <summary>LX 搜索结果的单个音质档位。</summary>
    public class LxSearchTypeItem
    {
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("size")] public string? Size { get; set; }
        [JsonPropertyName("hash")] public string? Hash { get; set; }
    }

    /// <summary>LX 搜索结果项(与 LX 生态 musicInfo 字段对齐, 直接作为插件 musicUrl/lyric/pic 的 info 入参)。</summary>
    public class LxSearchResultItem
    {
        [JsonPropertyName("songmid")] public string Songmid { get; set; } = string.Empty;
        [JsonPropertyName("hash")] public string? Hash { get; set; }
        [JsonPropertyName("strMediaMid")] public string? StrMediaMid { get; set; }
        [JsonPropertyName("songId")] public string? SongId { get; set; }
        [JsonPropertyName("albumId")] public string AlbumId { get; set; } = string.Empty;
        [JsonPropertyName("albumMid")] public string? AlbumMid { get; set; }
        [JsonPropertyName("copyrightId")] public string? CopyrightId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("singer")] public string Singer { get; set; } = string.Empty;
        [JsonPropertyName("albumName")] public string AlbumName { get; set; } = string.Empty;
        [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
        [JsonPropertyName("interval")] public string Interval { get; set; } = "00:00";
        [JsonPropertyName("img")] public string? Img { get; set; }
        [JsonPropertyName("lrcUrl")] public string? LrcUrl { get; set; }
        [JsonPropertyName("mrcUrl")] public string? MrcUrl { get; set; }
        [JsonPropertyName("trcUrl")] public string? TrcUrl { get; set; }
        [JsonPropertyName("types")] public List<LxSearchTypeItem> Types { get; set; } = [];
        [JsonPropertyName("_types")] public Dictionary<string, LxSearchTypeItem> UnderTypes { get; set; } = [];
    }

    /// <summary>LX 搜索结果(分页信息 + 列表)。</summary>
    public class LxSearchResult
    {
        public List<LxSearchResultItem> List { get; set; } = [];
        public int AllPage { get; set; }
        public int Limit { get; set; }
        public int Total { get; set; }
        public string Source { get; set; } = string.Empty;
    }

    /// <summary>LX 目录搜索条目(歌手/专辑/歌单)。</summary>
    public class LxCatalogEntry
    {
        /// <summary>平台原生 ID(歌手为派生键=歌手名; 专辑可能回退为专辑名)。</summary>
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        /// <summary>副标题: 专辑→歌手; 歌单→创建者。</summary>
        public string Subtitle { get; set; } = string.Empty;
        public string Artwork { get; set; } = string.Empty;
        /// <summary>详情拉取上下文 JSON(source/id/name 或歌单原始条目)。</summary>
        public string RawJson { get; set; } = string.Empty;
    }

    /// <summary>
    /// LX 内置音源搜索 SDK(kw/kg/tx/wy/mg 五平台原生搜索)。
    /// 参考 XianYu-Music-Desktop lxMusicSdk.ts: 搜索由宿主直接完成, LX 插件仅负责 musicUrl/lyric/pic 解析。
    /// </summary>
    public static class LxMusicSdk
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                UseCookies = false,
            };
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        }

        /// <summary>按音源分发搜索。</summary>
        public static Task<LxSearchResult> SearchAsync(string source, string keyword, int page, int limit)
        {
            return source switch
            {
                LxSources.Kw => SearchKwAsync(keyword, page, limit <= 0 ? 30 : limit),
                LxSources.Kg => SearchKgAsync(keyword, page, limit <= 0 ? 30 : limit),
                LxSources.Tx => SearchTxAsync(keyword, page, limit <= 0 ? 50 : limit),
                LxSources.Wy => SearchWyAsync(keyword, page, limit <= 0 ? 30 : limit),
                LxSources.Mg => SearchMgAsync(keyword, page, limit <= 0 ? 20 : limit),
                _ => Task.FromException<LxSearchResult>(new ArgumentException($"未知 LX 音源: {source}")),
            };
        }

        // ==================== 目录搜索(歌手/专辑/歌单) ====================

        /// <summary>
        /// 目录搜索(参考 XianYu lxCatalogSearch): 歌单走各平台原生接口;
        /// 歌手/专辑由歌曲搜索结果聚合派生(LX 音源协议无独立歌手/专辑搜索)。
        /// </summary>
        public static async Task<List<LxCatalogEntry>> CatalogSearchAsync(string source, string keyword, string type, int page, int limit)
        {
            if (type == "sheet") return await SearchPlaylistsAsync(source, keyword, page, limit <= 0 ? 30 : limit);
            var search = await SearchAsync(source, keyword, page, limit <= 0 ? 30 : limit);
            return type == "artist" ? DeriveArtists(search.List) : DeriveAlbums(search.List);
        }

        private static readonly Regex ArtistSplitRegex = new(@"[、,/&]", RegexOptions.Compiled);

        /// <summary>从歌曲搜索结果派生歌手列表(按歌手名去重, 头像回退歌曲封面)。</summary>
        private static List<LxCatalogEntry> DeriveArtists(List<LxSearchResultItem> songs)
        {
            var map = new Dictionary<string, LxCatalogEntry>();
            foreach (var song in songs)
            {
                if (string.IsNullOrWhiteSpace(song.Singer)) continue;
                foreach (var rawName in ArtistSplitRegex.Split(song.Singer))
                {
                    var name = WebUtility.HtmlDecode(rawName).Trim();
                    if (name.Length == 0) continue;
                    var key = name.ToLowerInvariant();
                    if (map.ContainsKey(key)) continue;
                    map[key] = new LxCatalogEntry
                    {
                        Id = name, // 派生键: 歌手名(详情=按名搜索, XianYu 同款)
                        Title = name,
                        Artwork = song.Img ?? string.Empty,
                        RawJson = JsonSerializer.Serialize(new { source = song.Source, name }),
                    };
                }
            }
            return [.. map.Values];
        }

        /// <summary>从歌曲搜索结果派生专辑列表(按专辑 ID 去重, albumId 缺失回退专辑名)。</summary>
        private static List<LxCatalogEntry> DeriveAlbums(List<LxSearchResultItem> songs)
        {
            var map = new Dictionary<string, LxCatalogEntry>();
            foreach (var song in songs)
            {
                var name = song.AlbumName?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                var id = !string.IsNullOrEmpty(song.AlbumId) ? song.AlbumId : name;
                var key = $"{song.Source}:{id}";
                if (map.ContainsKey(key)) continue;
                map[key] = new LxCatalogEntry
                {
                    Id = id,
                    Title = name,
                    Subtitle = song.Singer,
                    Artwork = song.Img ?? string.Empty,
                    RawJson = JsonSerializer.Serialize(new { source = song.Source, id, name }),
                };
            }
            return [.. map.Values];
        }

        /// <summary>歌单条目宽松字段提取。</summary>
        private static string? FirstValue(JsonElement item, params string[] keys)
        {
            foreach (var key in keys)
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var v))
                {
                    if (v.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.GetString())) return v.GetString();
                    if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
                }
            return null;
        }

        /// <summary>歌单原生搜索结果归一化(宽字段名兼容各平台)。</summary>
        private static List<LxCatalogEntry> NormalizePlaylists(string source, JsonElement rawItems)
        {
            var result = new List<LxCatalogEntry>();
            var seen = new HashSet<string>();
            if (rawItems.ValueKind != JsonValueKind.Array) return result;
            foreach (var group in rawItems.EnumerateArray())
            {
                // 咪咕 resultList 为二维数组
                IEnumerable<JsonElement> entries = group.ValueKind == JsonValueKind.Array ? group.EnumerateArray() : [group];
                foreach (var raw in entries)
                {
                    if (raw.ValueKind != JsonValueKind.Object) continue;
                    var id = FirstValue(raw, "id", "ID", "playlistId", "playlistid", "specialid", "dissid", "disstid", "songListId", "songlistId", "musicListId", "rid");
                    var title = FirstValue(raw, "title", "name", "playlistName", "specialname", "dissname", "songListName", "songlistName", "NAME");
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(title)) continue;
                    if (!seen.Add($"{source}:{id}")) continue;

                    var cover = FirstValue(raw, "coverUrl", "coverImgUrl", "img", "imgurl", "pic", "picUrl", "pic_url", "PIC", "album_pic_url", "hts_pic") ?? string.Empty;
                    if (cover.StartsWith("//")) cover = "https:" + cover;
                    else if (cover.StartsWith("http://")) cover = cover.Replace("http://", "https://");

                    string creator = FirstValue(raw, "artist", "author", "nickname", "uname", "UNAME") ?? string.Empty;
                    if (creator.Length == 0 && raw.TryGetProperty("creator", out var c) && c.ValueKind == JsonValueKind.Object)
                        creator = FirstValue(c, "name", "nickname") ?? string.Empty;

                    var trackCount = FirstValue(raw, "trackCount", "trackcount", "songCount", "song_count", "songnum", "SONGNUM");
                    var subtitle = creator;
                    if (int.TryParse(trackCount, out var tc) && tc > 0)
                        subtitle = subtitle.Length == 0 ? $"{tc}" : $"{subtitle} · {tc}";

                    result.Add(new LxCatalogEntry
                    {
                        Id = id!,
                        Title = DecodeName(Regex.Replace(title!, "<[^>]*>", "")),
                        Subtitle = subtitle,
                        Artwork = cover,
                        RawJson = raw.GetRawText(),
                    });
                }
            }
            return result;
        }

        /// <summary>TX 歌单搜索 Desktop 兜底(musicu.fcg 无签名通道, Mobile 通道被风控时使用)。</summary>
        private static async Task<List<LxCatalogEntry>> TxSheetSearchDesktopFallbackAsync(string keyword, int page, int limit)
        {
            var body = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["comm"] = new Dictionary<string, object> { ["ct"] = 19, ["cv"] = 1859, ["uin"] = "0" },
                ["req"] = new Dictionary<string, object>
                {
                    ["module"] = "music.search.SearchCgiService",
                    ["method"] = "DoSearchForQQMusicDesktop",
                    ["param"] = new Dictionary<string, object> { ["search_type"] = 3, ["query"] = keyword, ["page_num"] = page, ["num_per_page"] = limit },
                },
            });
            var data = await PostJsonAsync("https://u.y.qq.com/cgi-bin/musicu.fcg", body, new Dictionary<string, string>
            {
                ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36",
                ["Content-Type"] = "application/json",
                ["Referer"] = "https://y.qq.com/",
            });
            JsonElement list = default;
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("req", out var req) && req.TryGetProperty("data", out var reqData)
                && reqData.TryGetProperty("body", out var bodyEl) && bodyEl.TryGetProperty("songlist", out var sl) && sl.TryGetProperty("list", out var listEl))
                list = listEl;
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
                throw new HttpRequestException("TX 歌单 Desktop 兜底无结果");
            return NormalizePlaylists(LxSources.Tx, list);
        }

        /// <summary>歌单原生搜索(kw/kg/tx/wy/mg 各平台公开接口)。</summary>
        private static async Task<List<LxCatalogEntry>> SearchPlaylistsAsync(string source, string keyword, int page, int limit)
        {
            switch (source)
            {
                case LxSources.Kw:
                {
                    // 新 API 优先, 失败回退旧 r.s 接口(单引号 JSON)
                    try
                    {
                        var data = await GetJsonAsync($"https://www.kuwo.cn/api/www/search/searchPlayListBykeyWord?key={Uri.EscapeDataString(keyword)}&pn={page}&rn={limit}",
                            new Dictionary<string, string> { ["csrf"] = "ABCDEF", ["Cookie"] = "kw_token=ABCDEF", ["Referer"] = "https://www.kuwo.cn/" });
                        if (data.TryGetProperty("data", out var d))
                        {
                            var listEl = d.ValueKind == JsonValueKind.Object && d.TryGetProperty("list", out var l) && l.ValueKind == JsonValueKind.Array ? l : d;
                            var entries = NormalizePlaylists(source, listEl);
                            if (entries.Count > 0) return entries;
                        }
                    }
                    catch { /* 回退旧接口 */ }
                    var old = await GetLooseJsonAsync($"https://search.kuwo.cn/r.s?client=kt&all={Uri.EscapeDataString(keyword)}&pn={page - 1}&rn={limit}&ft=playlist&encoding=utf8&rformat=json",
                        new Dictionary<string, string> { ["Referer"] = "https://www.kuwo.cn/" });
                    return NormalizePlaylists(source, old.TryGetProperty("abslist", out var abs) ? abs : old);
                }
                case LxSources.Kg:
                {
                    var data = await GetJsonAsync($"https://songsearch.kugou.com/special_search?keyword={Uri.EscapeDataString(keyword)}&page={page}&pagesize={limit}&userid=-1&clientver=&platform=WebFilter&filter=0&iscorrection=1&privilege_filter=0");
                    JsonElement lists = default;
                    if (data.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object
                        && (d.TryGetProperty("lists", out var l) || d.TryGetProperty("list", out l)) && l.ValueKind == JsonValueKind.Array)
                        lists = l;
                    return NormalizePlaylists(source, lists);
                }
                case LxSources.Wy:
                {
                    var offset = limit * (page - 1);
                    var data = await GetJsonAsync($"https://music.163.com/api/search/get/web?s={Uri.EscapeDataString(keyword)}&type=1000&offset={offset}&limit={limit}",
                        new Dictionary<string, string>
                        {
                            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
                            ["Referer"] = "https://music.163.com",
                            ["Cookie"] = "MUSIC_A=1",
                        });
                    return NormalizePlaylists(source, data.TryGetProperty("result", out var r) && r.TryGetProperty("playlists", out var pl) ? pl : default);
                }
                case LxSources.Tx:
                {
                    var searchId = (DateTime.UtcNow.Ticks % 10000000000L).ToString();
                    var requestBody = JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        ["comm"] = new Dictionary<string, object>
                        {
                            ["ct"] = "24", ["cv"] = "4747474", ["v"] = "4747474", ["tmeAppID"] = "qqmusic", ["format"] = "json",
                            ["inCharset"] = "utf-8", ["outCharset"] = "utf-8", ["platform"] = "yqq.json", ["needNewCode"] = 0, ["uin"] = "0", ["guid"] = "0",
                        },
                        ["req"] = new Dictionary<string, object>
                        {
                            ["module"] = "music.search.SearchCgiService",
                            ["method"] = "DoSearchForQQMusicMobile",
                            ["param"] = new Dictionary<string, object>
                            {
                                ["search_type"] = 3, ["searchid"] = searchId, ["query"] = keyword,
                                ["page_num"] = page, ["num_per_page"] = limit, ["highlight"] = 0, ["nqc_flag"] = 0, ["multi_zhida"] = 0, ["cat"] = 2, ["grp"] = 1, ["sin"] = 0, ["sem"] = 0,
                            },
                        },
                    });
                    List<LxCatalogEntry> entries;
                    try
                    {
                        var sign = ZzcSign(requestBody);
                        var data = await PostJsonAsync($"https://u.y.qq.com/cgi-bin/musics.fcg?sign={sign}", requestBody, new Dictionary<string, string>
                        {
                            ["User-Agent"] = "Mozilla/5.0 (Linux; Android 12; EBG-AN10) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/107.0.5304.141 Mobile Safari/537.36",
                            ["Content-Type"] = "application/json",
                            ["Referer"] = "https://y.qq.com/",
                        });
                        JsonElement list = default;
                        if (data.TryGetProperty("req", out var req) && req.TryGetProperty("data", out var reqData) && reqData.TryGetProperty("body", out var body))
                        {
                            if (body.TryGetProperty("item_songlist", out var isl) && isl.ValueKind == JsonValueKind.Array) list = isl;
                            else if (body.TryGetProperty("songlist", out var sl) && sl.TryGetProperty("list", out var slList) && slList.ValueKind == JsonValueKind.Array) list = slList;
                        }
                        entries = NormalizePlaylists(source, list);
                    }
                    catch
                    {
                        entries = [];
                    }
                    // Mobile 通道被风控/降级返回空时走无签名 Desktop 兜底(实测稳定)
                    return entries.Count > 0 ? entries : await TxSheetSearchDesktopFallbackAsync(keyword, page, limit);
                }
                default:
                {
                    // MG(咪咕): searchSwitch 仅开 songlist
                    var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                    var (sign, deviceId) = MgCreateSignature(time, keyword);
                    var searchSwitch = Uri.EscapeDataString("""{"song":0,"album":0,"singer":0,"tagSong":0,"mvSong":0,"bestShow":0,"songlist":1,"lyricSong":0}""");
                    var data = await GetJsonAsync($"https://jadeite.migu.cn/music_search/v3/search/searchAll?isCorrect=0&isCopyright=1&searchSwitch={searchSwitch}&pageSize={limit}&text={Uri.EscapeDataString(keyword)}&pageNo={page}&sort=0&sid=USS",
                        new Dictionary<string, string>
                        {
                            ["uiVersion"] = "A_music_3.6.1",
                            ["deviceId"] = deviceId,
                            ["timestamp"] = time,
                            ["sign"] = sign,
                            ["channel"] = "0146921",
                            ["User-Agent"] = "Mozilla/5.0 (Linux; Android 11)",
                        });
                    JsonElement list = default;
                    if (data.TryGetProperty("songListResultData", out var srd) || data.TryGetProperty("songlistResultData", out srd))
                    {
                        if (srd.TryGetProperty("resultList", out var rl) && rl.ValueKind == JsonValueKind.Array) list = rl;
                        else if (srd.TryGetProperty("list", out var l) && l.ValueKind == JsonValueKind.Array) list = l;
                    }
                    return NormalizePlaylists(source, list);
                }
            }
        }

        // ==================== 通用工具 ====================

        private static string DecodeName(string? s) => s is null ? string.Empty : WebUtility.HtmlDecode(s);

        private static string FormatPlayTime(double seconds)
        {
            if (double.IsNaN(seconds) || seconds <= 0) return "00:00";
            var t = TimeSpan.FromSeconds(seconds);
            return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}";
        }

        private static string SizeFormate(double bytes)
        {
            if (bytes <= 0) return "0B";
            if (bytes < 1024) return $"{bytes:F0}B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024:F1}KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024 / 1024:F1}MB";
            return $"{bytes / 1024 / 1024 / 1024:F1}GB";
        }

        private static string Md5Hex(string input) =>
            Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

        private static string Sha1Hex(string input) =>
            Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

        private static async Task<JsonElement> GetJsonAsync(string url, Dictionary<string, string>? headers = null)
        {
            var body = await GetStringAsync(url, headers);
            return JsonDocument.Parse(body).RootElement;
        }

        private static async Task<string> GetStringAsync(string url, Dictionary<string, string>? headers = null)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (headers is not null)
                foreach (var kv in headers)
                    req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead);
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)resp.StatusCode} for {url}");
            return await resp.Content.ReadAsStringAsync();
        }

        private static async Task<JsonElement> PostJsonAsync(string url, string body, Dictionary<string, string>? headers = null)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (headers is not null)
                foreach (var kv in headers)
                    req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead);
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)resp.StatusCode} for {url}");
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        }

        /// <summary>
        /// 酷我旧搜索接口(search.kuwo.cn/r.s)返回 Python 风格单引号 JSON({x0027}ARTISTPIC{x0027}:...),
        /// 标准解析必然失败; 状态机转换: 字符串定界符 ' → ", 字符串内的 " 转义, 保留原有反斜杠转义。
        /// </summary>
        private static JsonElement ParseLooseJson(string text)
        {
            var sb = new StringBuilder(text.Length + 64);
            bool inStr = false;
            for (int i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (!inStr)
                {
                    if (ch == '\'') { inStr = true; sb.Append('"'); }
                    else sb.Append(ch);
                }
                else if (ch == '\\')
                {
                    sb.Append(ch);
                    if (i + 1 < text.Length) sb.Append(text[++i]);
                }
                else if (ch == '\'')
                {
                    inStr = false;
                    sb.Append('"');
                }
                else
                {
                    sb.Append(ch == '"' ? "\\\"" : ch);
                }
            }
            return JsonDocument.Parse(sb.ToString()).RootElement;
        }

        private static async Task<JsonElement> GetLooseJsonAsync(string url, Dictionary<string, string>? headers = null)
        {
            var body = await GetStringAsync(url, headers);
            try { return JsonDocument.Parse(body).RootElement; }
            catch (JsonException) { return ParseLooseJson(body); }
        }

        // JsonElement 取值辅助
        private static string? JStr(JsonElement e, params string[] names)
        {
            foreach (var n in names)
                if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v))
                {
                    if (v.ValueKind == JsonValueKind.String) { var s = v.GetString(); if (!string.IsNullOrEmpty(s)) return s; }
                    else if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
                }
            return null;
        }

        private static double JNum(JsonElement e, params string[] names)
        {
            foreach (var n in names)
                if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v))
                {
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
                    if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out var s)) return s;
                }
            return 0;
        }

        private static bool Has(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out _);

        // ==================== KW (酷我) Search ====================

        private static readonly Regex KwMinfoRegex = new(@"level:(\w+),bitrate:(\d+),format:(\w+),size:([\w.]+)", RegexOptions.Compiled);

        /// <summary>酷我封面: 多字段候选, 完整 URL 归一化域名, 相对路径拼 img3 前缀。</summary>
        private static string? KwSearchCover(JsonElement info)
        {
            foreach (var key in new[] { "web_albumpic_short", "web_album_pic", "album_pic", "albumpic_short", "albumpic", "pic" })
            {
                var v = JStr(info, key);
                if (string.IsNullOrWhiteSpace(v)) continue;
                var s = v.Trim();
                if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var norm = NormalizeKuwoCoverUrl(s);
                    if (norm is not null) return norm;
                }
                else
                {
                    var built = BuildKuwoAlbumCoverUrl(s);
                    if (built is not null) return built;
                }
            }
            return null;
        }

        /// <summary>web_albumpic_short 相对路径(120/xxx.jpg) → 可用 HTTPS 封面(替换尺寸段为 500)。</summary>
        private static string? BuildKuwoAlbumCoverUrl(string shortPath, int size = 500)
        {
            if (string.IsNullOrWhiteSpace(shortPath)) return null;
            var s = shortPath.Trim().TrimStart('/');
            if (s.Length == 0) return null;
            s = Regex.Replace(s, @"^\d+/", $"{size}/");
            return $"https://img3.kuwo.cn/star/albumcover/{s}";
        }

        /// <summary>酷我旧 CDN 域名(img*.kwcdn.kuwo.cn)换更稳定的 img3.kuwo.cn。</summary>
        private static string? NormalizeKuwoCoverUrl(string url)
        {
            var outUrl = url.Trim();
            if (outUrl.Length == 0) return null;
            outUrl = Regex.Replace(outUrl, "^http://", "https://", RegexOptions.IgnoreCase);
            outUrl = Regex.Replace(outUrl, @"^https://img\d+\.kwcdn\.kuwo\.cn/", "https://img3.kuwo.cn/", RegexOptions.IgnoreCase);
            return outUrl;
        }

        private static List<LxSearchResultItem>? KwHandleResult(JsonElement rawData)
        {
            var result = new List<LxSearchResultItem>();
            if (rawData.ValueKind != JsonValueKind.Array) return result;
            foreach (var info in rawData.EnumerateArray())
            {
                var musicrid = JStr(info, "MUSICRID") ?? JStr(info, "musicrid");
                if (string.IsNullOrEmpty(musicrid)) continue;
                var songId = musicrid.Replace("MUSIC_", "");
                var nMinfo = JStr(info, "N_MINFO") ?? JStr(info, "n_minfo");
                if (string.IsNullOrEmpty(nMinfo)) return null; // 降级响应, 触发外层重试
                var types = new List<LxSearchTypeItem>();
                var underTypes = new Dictionary<string, LxSearchTypeItem>();
                foreach (var item in nMinfo.Split(';'))
                {
                    var match = KwMinfoRegex.Match(item);
                    if (!match.Success) continue;
                    var bitrate = match.Groups[2].Value;
                    var size = match.Groups[4].Value;
                    switch (bitrate)
                    {
                        case "4000":
                            types.Add(new LxSearchTypeItem { Type = "flac24bit", Size = size });
                            underTypes["flac24bit"] = new LxSearchTypeItem { Type = "flac24bit", Size = size.ToUpperInvariant() };
                            break;
                        case "2000":
                            types.Add(new LxSearchTypeItem { Type = "flac", Size = size });
                            underTypes["flac"] = new LxSearchTypeItem { Type = "flac", Size = size.ToUpperInvariant() };
                            break;
                        case "320":
                            types.Add(new LxSearchTypeItem { Type = "320k", Size = size });
                            underTypes["320k"] = new LxSearchTypeItem { Type = "320k", Size = size.ToUpperInvariant() };
                            break;
                        case "128":
                            types.Add(new LxSearchTypeItem { Type = "128k", Size = size });
                            underTypes["128k"] = new LxSearchTypeItem { Type = "128k", Size = size.ToUpperInvariant() };
                            break;
                    }
                }
                types.Reverse();
                var interval = JNum(info, "DURATION");
                result.Add(new LxSearchResultItem
                {
                    Name = DecodeName(JStr(info, "SONGNAME")),
                    Singer = DecodeName(JStr(info, "ARTIST"))?.Replace("&", "、") ?? string.Empty,
                    Source = LxSources.Kw,
                    Songmid = songId,
                    AlbumId = DecodeName(JStr(info, "ALBUMID")) ?? string.Empty,
                    Interval = double.IsNaN(interval) ? "00:00" : FormatPlayTime(interval),
                    AlbumName = DecodeName(JStr(info, "ALBUM")) ?? string.Empty,
                    Img = KwSearchCover(info),
                    Types = types,
                    UnderTypes = underTypes,
                });
            }
            return result;
        }

        private static async Task<LxSearchResult> SearchKwAsync(string str, int page, int limit, int retryNum = 0)
        {
            if (retryNum > 2) throw new HttpRequestException("KW search: try max num");
            var url = $"http://search.kuwo.cn/r.s?client=kt&all={Uri.EscapeDataString(str)}&pn={page - 1}&rn={limit}&uid=794762570&ver=kwplayer_ar_9.2.2.1&vipver=1&show_copyright_off=1&newver=1&ft=music&cluster=0&strategy=2012&encoding=utf8&rformat=json&vermerge=1&mobi=1&issubtitle=1";
            var result = await GetLooseJsonAsync(url);
            var total = JStr(result, "TOTAL") ?? "0";
            var show = JStr(result, "SHOW") ?? "1";
            if (total != "0" && show == "0") return await SearchKwAsync(str, page, limit, retryNum + 1);
            if (!result.TryGetProperty("abslist", out var absList) || absList.ValueKind != JsonValueKind.Array)
                return await SearchKwAsync(str, page, limit, retryNum + 1);
            var list = KwHandleResult(absList);
            if (list is null) return await SearchKwAsync(str, page, limit, retryNum + 1);
            if (!int.TryParse(total, out var totalNum)) totalNum = 0;
            return new LxSearchResult { List = list, AllPage = (int)Math.Ceiling(totalNum / (double)limit), Limit = limit, Total = totalNum, Source = LxSources.Kw };
        }

        // ==================== KG (酷狗) Search ====================

        /// <summary>酷狗封面: Image 字段含 {size} 占位符, 替换为实际尺寸并升级 HTTPS。</summary>
        private static string? BuildKugouCoverUrl(string? url, int size = 480)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var u = url.Trim();
            u = Regex.Replace(u, "^http://", "https://", RegexOptions.IgnoreCase);
            return u.Replace("{size}", size.ToString());
        }

        private static LxSearchResultItem KgFilterData(JsonElement rawData)
        {
            var types = new List<LxSearchTypeItem>();
            var underTypes = new Dictionary<string, LxSearchTypeItem>();
            double fileSize = JNum(rawData, "FileSize");
            if (fileSize != 0)
            {
                var size = SizeFormate(fileSize);
                var hash = JStr(rawData, "FileHash");
                types.Add(new LxSearchTypeItem { Type = "128k", Size = size, Hash = hash });
                underTypes["128k"] = new LxSearchTypeItem { Type = "128k", Size = size, Hash = hash };
            }
            double hqSize = JNum(rawData, "HQFileSize");
            if (hqSize != 0)
            {
                var size = SizeFormate(hqSize);
                var hash = JStr(rawData, "HQFileHash");
                types.Add(new LxSearchTypeItem { Type = "320k", Size = size, Hash = hash });
                underTypes["320k"] = new LxSearchTypeItem { Type = "320k", Size = size, Hash = hash };
            }
            double sqSize = JNum(rawData, "SQFileSize");
            if (sqSize != 0)
            {
                var size = SizeFormate(sqSize);
                var hash = JStr(rawData, "SQFileHash");
                types.Add(new LxSearchTypeItem { Type = "flac", Size = size, Hash = hash });
                underTypes["flac"] = new LxSearchTypeItem { Type = "flac", Size = size, Hash = hash };
            }
            double resSize = JNum(rawData, "ResFileSize");
            if (resSize != 0)
            {
                var size = SizeFormate(resSize);
                var hash = JStr(rawData, "ResFileHash");
                types.Add(new LxSearchTypeItem { Type = "flac24bit", Size = size, Hash = hash });
                underTypes["flac24bit"] = new LxSearchTypeItem { Type = "flac24bit", Size = size, Hash = hash };
            }
            string? imgUrl;
            if (Has(rawData, "Image") && JStr(rawData, "Image") is { Length: > 0 } img)
                imgUrl = BuildKugouCoverUrl(img);
            else if (rawData.TryGetProperty("trans_param", out var tp) && tp.ValueKind == JsonValueKind.Object)
                imgUrl = BuildKugouCoverUrl(JStr(tp, "union_cover"));
            else
                imgUrl = null;
            var singers = string.Empty;
            if (rawData.TryGetProperty("Singers", out var singerArr) && singerArr.ValueKind == JsonValueKind.Array)
                singers = string.Join("、", singerArr.EnumerateArray().Select(s => DecodeName(JStr(s, "name"))).Where(n => !string.IsNullOrEmpty(n)));
            return new LxSearchResultItem
            {
                Singer = singers,
                Name = DecodeName(JStr(rawData, "SongName")) ?? string.Empty,
                AlbumName = DecodeName(JStr(rawData, "AlbumName")) ?? string.Empty,
                AlbumId = JStr(rawData, "AlbumID") ?? string.Empty,
                Songmid = JStr(rawData, "Audioid") ?? string.Empty,
                Source = LxSources.Kg,
                Interval = FormatPlayTime(JNum(rawData, "Duration")),
                Img = imgUrl,
                Hash = JStr(rawData, "FileHash"),
                Types = types,
                UnderTypes = underTypes,
            };
        }

        private static List<LxSearchResultItem> KgHandleResult(JsonElement rawData)
        {
            var ids = new HashSet<string>();
            var list = new List<LxSearchResultItem>();
            if (rawData.ValueKind != JsonValueKind.Array) return list;
            foreach (var item in rawData.EnumerateArray())
            {
                var key = (JStr(item, "Audioid") ?? "") + (JStr(item, "FileHash") ?? "");
                if (ids.Contains(key)) continue;
                ids.Add(key);
                list.Add(KgFilterData(item));
                if (item.TryGetProperty("Grp", out var grp) && grp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var childItem in grp.EnumerateArray())
                    {
                        var childKey = (JStr(childItem, "Audioid") ?? "") + (JStr(childItem, "FileHash") ?? "");
                        if (ids.Contains(childKey)) continue;
                        ids.Add(childKey);
                        list.Add(KgFilterData(childItem));
                    }
                }
            }
            return list;
        }

        private static async Task<LxSearchResult> SearchKgAsync(string str, int page, int limit, int retryNum = 0)
        {
            if (++retryNum > 3) throw new HttpRequestException("KG search: try max num");
            var url = $"https://songsearch.kugou.com/song_search_v2?keyword={Uri.EscapeDataString(str)}&page={page}&pagesize={limit}&userid=0&clientver=&platform=WebFilter&filter=2&iscorrection=1&privilege_filter=0&area_code=1";
            var result = await GetJsonAsync(url);
            if (JNum(result, "error_code") != 0) return await SearchKgAsync(str, page, limit, retryNum);
            var list = new List<LxSearchResultItem>();
            if (result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("lists", out var lists) && lists.ValueKind == JsonValueKind.Array)
                list = KgHandleResult(lists);
            var total = (int)JNum(data, "total");
            return new LxSearchResult { List = list, AllPage = (int)Math.Ceiling(total / (double)limit), Limit = limit, Total = total, Source = LxSources.Kg };
        }

        // ==================== TX (QQ音乐) Search ====================

        private static readonly int[] TxPart1Indexes = [23, 14, 6, 36, 16, 40, 7, 19];
        private static readonly int[] TxPart2Indexes = [16, 1, 32, 12, 19, 27, 8, 5];
        private static readonly int[] TxScrambleValues = [89, 39, 179, 150, 218, 82, 58, 252, 177, 52, 186, 123, 120, 64, 242, 133, 143, 161, 121, 179];

        /// <summary>zzc 签名: SHA1 后按索引取字符 + 混淆值 XOR + base64。</summary>
        private static string ZzcSign(string text)
        {
            var hash = Sha1Hex(text);
            var part1 = new string(TxPart1Indexes.Select(i => hash[i]).ToArray());
            var part2 = new string(TxPart2Indexes.Select(i => hash[i]).ToArray());
            var part3 = new byte[TxScrambleValues.Length];
            for (var i = 0; i < TxScrambleValues.Length; i++)
                part3[i] = (byte)(TxScrambleValues[i] ^ Convert.ToInt32(hash.Substring(i * 2, 2), 16));
            var b64 = Convert.ToBase64String(part3).Replace("/", "").Replace("+", "").Replace("=", "");
            return $"zzc{part1}{b64}{part2}".ToLowerInvariant();
        }

        private static string CreateTxSearchRequestBody(string str, int page, int limit)
        {
            // 移动端接口(落雪官方验证有效): 需携带完整设备参数, 否则返回降级响应(仅 direct_result2 有歌曲)
            var searchId = (DateTime.UtcNow.Ticks % 10000000000L).ToString();
            var body = new Dictionary<string, object>
            {
                ["comm"] = new Dictionary<string, object>
                {
                    ["ct"] = "11", ["cv"] = "14090508", ["v"] = "14090508", ["tmeAppID"] = "qqmusic",
                    ["phonetype"] = "EBG-AN10", ["deviceScore"] = "553.47", ["devicelevel"] = "50", ["newdevicelevel"] = "20",
                    ["rom"] = "HuaWei/EMOTION/EmotionUI_14.2.0", ["os_ver"] = "12",
                    ["OpenUDID"] = "0", ["OpenUDID2"] = "0", ["QIMEI36"] = "0", ["udid"] = "0", ["chid"] = "0",
                    ["aid"] = "0", ["oaid"] = "0", ["taid"] = "0", ["tid"] = "0", ["wid"] = "0", ["uid"] = "0", ["sid"] = "0",
                    ["modeSwitch"] = "6", ["teenMode"] = "0", ["ui_mode"] = "2", ["nettype"] = "1020", ["v4ip"] = "",
                },
                ["req"] = new Dictionary<string, object>
                {
                    ["module"] = "music.search.SearchCgiService",
                    ["method"] = "DoSearchForQQMusicMobile",
                    ["param"] = new Dictionary<string, object>
                    {
                        ["search_type"] = 0,
                        ["searchid"] = searchId,
                        ["query"] = str,
                        ["page_num"] = page,
                        ["num_per_page"] = limit,
                        ["highlight"] = 0, ["nqc_flag"] = 0, ["multi_zhida"] = 0, ["cat"] = 2, ["grp"] = 1, ["sin"] = 0, ["sem"] = 0,
                    },
                },
            };
            return JsonSerializer.Serialize(body);
        }

        /// <summary>解析 TX 搜索响应中的歌曲列表(宽松字段提取, 兼容多种响应结构)。</summary>
        private static List<LxSearchResultItem> TxHandleResult(JsonElement rawList)
        {
            var list = new List<LxSearchResultItem>();
            if (rawList.ValueKind != JsonValueKind.Array) return list;
            foreach (var rawItem in rawList.EnumerateArray())
            {
                var item = rawItem;
                foreach (var key in new[] { "song", "songInfo", "musicInfo", "item", "doc" })
                {
                    if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var nested) && nested.ValueKind == JsonValueKind.Object)
                    {
                        item = nested;
                        break;
                    }
                }
                if (item.ValueKind != JsonValueKind.Object) continue;
                // 仅要求 mid 或 id 存在: file/media_mid 可能为空或缺失, 过严会导致列表被静默过滤为空
                var songmid = JStr(item, "mid", "songmid", "songMid", "strMediaMid", "mediaMid", "mediamid", "song_mid", "songMID", "id", "songid") ?? string.Empty;
                var songId = JStr(item, "id", "songid", "songId", "songID");
                if (songmid.Length == 0 && songId is null) continue;

                var types = new List<LxSearchTypeItem>();
                var underTypes = new Dictionary<string, LxSearchTypeItem>();
                if (item.TryGetProperty("file", out var file) && file.ValueKind == JsonValueKind.Object)
                {
                    void AddType(string sizeKey, string typeKey)
                    {
                        var size = JNum(file, sizeKey);
                        if (size > 0)
                        {
                            var s = SizeFormate(size);
                            types.Add(new LxSearchTypeItem { Type = typeKey, Size = s });
                            underTypes[typeKey] = new LxSearchTypeItem { Type = typeKey, Size = s };
                        }
                    }
                    AddType("size_128mp3", "128k");
                    AddType("size_320mp3", "320k");
                    AddType("size_flac", "flac");
                    AddType("size_hires", "flac24bit");
                    AddType("size_master", "master");
                    AddType("size_atmos", "atmos");
                    AddType("size_dolby", "dolby");
                }

                JsonElement album = default;
                if (item.TryGetProperty("album", out var al) && al.ValueKind == JsonValueKind.Object) album = al;
                else if (item.TryGetProperty("albumInfo", out var ali) && ali.ValueKind == JsonValueKind.Object) album = ali;
                else if (item.TryGetProperty("album_info", out var alu) && alu.ValueKind == JsonValueKind.Object) album = alu;
                var albumId = album.ValueKind == JsonValueKind.Object
                    ? JStr(album, "mid") ?? JStr(item, "albumMid", "albummid", "album_mid", "albumMID", "albumid", "albumId") ?? string.Empty
                    : JStr(item, "albumMid", "albummid", "album_mid", "albumMID", "albumid", "albumId") ?? string.Empty;
                var albumName = album.ValueKind == JsonValueKind.Object
                    ? JStr(album, "name") ?? JStr(album, "title") ?? JStr(item, "albumName", "albumname", "album_name", "albumTitle") ?? string.Empty
                    : JStr(item, "albumName", "albumname", "album_name", "albumTitle") ?? string.Empty;

                string singers;
                if (item.TryGetProperty("singer", out var singerArr) && singerArr.ValueKind == JsonValueKind.Array)
                    singers = string.Join("、", singerArr.EnumerateArray().Select(s => JStr(s, "name")).Where(n => !string.IsNullOrEmpty(n)));
                else if (item.TryGetProperty("singers", out var singersArr) && singersArr.ValueKind == JsonValueKind.Array)
                    singers = string.Join("、", singersArr.EnumerateArray().Select(s => JStr(s, "name")).Where(n => !string.IsNullOrEmpty(n)));
                else
                    singers = JStr(item, "singerList", "singerName", "singername", "singer_name") ?? string.Empty;

                var strMediaMid = string.Empty;
                if (file.ValueKind == JsonValueKind.Object) strMediaMid = JStr(file, "media_mid") ?? string.Empty;
                if (strMediaMid.Length == 0) strMediaMid = JStr(item, "strMediaMid", "mediaMid", "mediamid", "media_mid", "mediaMID") ?? string.Empty;
                var interval = JNum(item, "interval", "duration", "time_public");
                var displayName = JStr(item, "title", "name", "songname", "songName", "song_name") ?? string.Empty;

                string? img;
                if (albumId.Length == 0 || albumId == "空")
                {
                    var firstSingerMid = singerArr.ValueKind == JsonValueKind.Array && singerArr.GetArrayLength() > 0
                        ? JStr(singerArr[0], "mid") : null;
                    img = firstSingerMid is null ? null : $"https://y.gtimg.cn/music/photo_new/T001R500x500M000{firstSingerMid}.jpg";
                }
                else
                {
                    img = $"https://y.gtimg.cn/music/photo_new/T002R500x500M000{albumId}.jpg";
                }

                list.Add(new LxSearchResultItem
                {
                    Singer = singers,
                    Name = DecodeName(Regex.Replace(displayName, "<[^>]*>", "")),
                    AlbumName = albumName,
                    AlbumId = albumId,
                    AlbumMid = albumId,
                    Source = LxSources.Tx,
                    Interval = FormatPlayTime(interval),
                    SongId = songId,
                    Songmid = songmid,
                    StrMediaMid = strMediaMid,
                    Img = img,
                    Types = types,
                    UnderTypes = underTypes,
                });
            }
            return list;
        }

        private static List<JsonElement> PickArrayFromTxNode(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array) return node.EnumerateArray().ToList();
            if (node.ValueKind != JsonValueKind.Object) return [];
            foreach (var key in new[] { "list", "songlist", "itemlist", "items", "item_song", "item_audio", "grp", "song", "songInfo", "musicInfo", "item", "docs", "records", "results", "result", "value", "values", "data" })
            {
                if (node.TryGetProperty(key, out var direct) && direct.ValueKind == JsonValueKind.Array)
                    return direct.EnumerateArray().ToList();
            }
            return [];
        }

        /// <summary>TX 搜索歌曲列表提取: 常规候选 → direct_result 直达结果 → 深度 BFS 兜底。</summary>
        private static List<JsonElement> PickTxSearchRawList(JsonElement data)
        {
            JsonElement body = data;
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("body", out var bodyEl) && bodyEl.ValueKind == JsonValueKind.Object)
                body = bodyEl;

            List<JsonElement> FromNode(JsonElement songNode)
            {
                if (songNode.ValueKind != JsonValueKind.Object) return [];
                foreach (var key in new[] { "list", "songlist", "itemlist", "items", "item_song" })
                {
                    if (songNode.TryGetProperty(key, out var l) && l.ValueKind == JsonValueKind.Array)
                    {
                        var arr = l.EnumerateArray().ToList();
                        if (arr.Count > 0 && TxHandleResult(JsonSerializer.SerializeToElement(arr)).Count > 0) return arr;
                    }
                }
                return [];
            }

            // 常规候选: body.song.list / body.songlist / body.item_song / data.song ...
            JsonElement songNode = body;
            if (body.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "song", "songlist", "item_song", "item_audio" })
                {
                    if (body.TryGetProperty(key, out var sn) && sn.ValueKind == JsonValueKind.Object)
                    {
                        var arr = FromNode(sn);
                        if (arr.Count > 0) return arr;
                    }
                }
            }
            if (data.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "song", "songlist", "item_song" })
                {
                    if (data.TryGetProperty(key, out var sn) && sn.ValueKind == JsonValueKind.Object)
                    {
                        var arr = FromNode(sn);
                        if (arr.Count > 0) return arr;
                    }
                }
            }

            // direct_result / direct_result2 直达结果(可能是分组数组, 仅歌曲分组可播放)
            foreach (var root in new[] { body, data })
            {
                if (root.ValueKind != JsonValueKind.Object) continue;
                foreach (var dk in new[] { "direct_result", "direct_result2" })
                {
                    if (!root.TryGetProperty(dk, out var dr)) continue;
                    var groups = dr.ValueKind == JsonValueKind.Array ? dr.EnumerateArray().ToList() : [dr];
                    foreach (var g in groups)
                    {
                        if (g.ValueKind != JsonValueKind.Object) continue;
                        foreach (var gk in new[] { "grp", "song", "item_song", "item_audio" })
                        {
                            if (g.TryGetProperty(gk, out var ga) && ga.ValueKind == JsonValueKind.Array)
                            {
                                var arr = ga.EnumerateArray().ToList();
                                if (arr.Count > 0 && TxHandleResult(JsonSerializer.SerializeToElement(arr)).Count > 0) return arr;
                            }
                        }
                    }
                }
            }

            // 深度 BFS 兜底
            return FindTxSongListDeep(body);
        }

        private static List<JsonElement> FindTxSongListDeep(JsonElement root)
        {
            var seen = new HashSet<string>();
            var queue = new Queue<(JsonElement Node, int Depth)>();
            queue.Enqueue((root, 0));
            while (queue.Count > 0)
            {
                var (node, depth) = queue.Dequeue();
                if (node.ValueKind != JsonValueKind.Object && node.ValueKind != JsonValueKind.Array) continue;
                var nodeKey = node.GetRawText();
                if (!seen.Add(nodeKey.Length > 256 ? nodeKey[..256] : nodeKey)) continue;

                if (node.ValueKind == JsonValueKind.Array)
                {
                    var arr = node.EnumerateArray().ToList();
                    if (arr.Count > 0 && TxHandleResult(JsonSerializer.SerializeToElement(arr)).Count > 0) return arr;
                    if (depth >= 6) continue;
                    foreach (var item in arr.Take(80))
                        if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                            queue.Enqueue((item, depth + 1));
                    continue;
                }

                foreach (var key in new[] { "song", "songlist", "item_song", "item_audio", "grp", "direct_result", "direct_result2", "musicInfo", "songInfo", "list", "items", "data", "docs", "records", "result" })
                {
                    if (node.TryGetProperty(key, out var child) && child.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        queue.Enqueue((child, depth + 1));
                }
            }
            return [];
        }

        private static int GetTxSearchTotal(JsonElement data, int fallbackCount, int limit)
        {
            var total = JNum(data, "estimate_sum");
            if (total <= 0 && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("body", out var body))
                total = JNum(body, "total");
            if (total <= 0 && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("body", out var body2) && body2.TryGetProperty("song", out var song) && song.ValueKind == JsonValueKind.Object)
                total = JNum(song, "totalnum", "total", "total_num");
            if (total > 0) return (int)total;
            return fallbackCount > 0 ? fallbackCount : limit;
        }

        /// <summary>经典 Web 搜索接口兜底: Mobile 接口被持续风控(reqCode 2001)时使用。</summary>
        private static async Task<LxSearchResult> TxSearchWebFallbackAsync(string str, int page, int limit)
        {
            var url = $"https://c.y.qq.com/soso/fcgi-bin/client_search_cp?format=json&inCharset=utf-8&outCharset=utf-8&cr=1&platform=h5&catZhida=0&w={Uri.EscapeDataString(str)}&p={page}&n={limit}";
            var result = await GetJsonAsync(url, new Dictionary<string, string>
            {
                ["User-Agent"] = "Mozilla/5.0 (iPhone; CPU iPhone OS 14_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/14.0 Mobile/15E148 Safari/604.1",
                ["Referer"] = "https://y.qq.com/",
            });
            JsonElement song = default;
            if (result.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object && dataEl.TryGetProperty("song", out var songEl))
                song = songEl;
            var rawList = song.ValueKind == JsonValueKind.Object && song.TryGetProperty("list", out var listEl) && listEl.ValueKind == JsonValueKind.Array
                ? listEl.EnumerateArray().ToList() : [];
            var items = TxHandleResult(JsonSerializer.SerializeToElement(rawList));
            if (items.Count == 0) throw new HttpRequestException("TX web fallback: 无有效歌曲");
            var total = (int)JNum(song, "totalnum", "num");
            if (total == 0) total = rawList.Count > 0 ? rawList.Count : items.Count;
            return new LxSearchResult { List = items, AllPage = (int)Math.Ceiling(total / (double)limit), Limit = limit, Total = total, Source = LxSources.Tx };
        }

        private static async Task<LxSearchResult> SearchTxAsync(string str, int page, int limit, int retryNum = 0)
        {
            if (retryNum > 4)
                return await TxSearchWebFallbackAsync(str, page, limit);

            try
            {
                var requestBody = CreateTxSearchRequestBody(str, page, limit);
                var sign = ZzcSign(requestBody);
                var url = $"https://u.y.qq.com/cgi-bin/musics.fcg?sign={sign}";
                var mobileBody = await PostJsonAsync(url, requestBody, new Dictionary<string, string>
                {
                    ["User-Agent"] = "Mozilla/5.0 (Linux; Android 12; EBG-AN10) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/107.0.5304.141 Mobile Safari/537.36",
                    ["Content-Type"] = "application/json",
                    ["Referer"] = "https://y.qq.com/",
                });
                var reqCode = mobileBody.ValueKind == JsonValueKind.Object && mobileBody.TryGetProperty("req", out var req) && req.ValueKind == JsonValueKind.Object
                    ? (int)JNum(req, "code") : -1;
                var mobileOk = (int)JNum(mobileBody, "code") == 0 && reqCode == 0;
                var rawList = new List<JsonElement>();
                JsonElement reqData = mobileBody;
                if (mobileOk && mobileBody.TryGetProperty("req", out var req2) && req2.TryGetProperty("data", out var reqDataEl))
                {
                    reqData = reqDataEl;
                    rawList = PickTxSearchRawList(reqData);
                }
                if (rawList.Count > 0)
                {
                    var list = TxHandleResult(JsonSerializer.SerializeToElement(rawList));
                    var total = GetTxSearchTotal(reqData, list.Count, limit);
                    return new LxSearchResult { List = list, AllPage = (int)Math.Ceiling(total / (double)limit), Limit = limit, Total = total, Source = LxSources.Tx };
                }

                // 风控(2001)需更长间隔; 空列表通常是降级响应, 稍后重试
                var backoff = reqCode == 2001 ? 2000 * (retryNum + 1) : 500 * (retryNum + 1);
                await Task.Delay(backoff);
                return await SearchTxAsync(str, page, limit, retryNum + 1);
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception)
            {
                await Task.Delay(500 * (retryNum + 1));
                return await SearchTxAsync(str, page, limit, retryNum + 1);
            }
        }

        // ==================== WY (网易云) Search ====================

        /// <summary>网易云 picId 加密路径段(与官方 CDN 一致): XOR magic → MD5 → URL-safe base64。</summary>
        private static string EncryptNeteasePicId(string picId)
        {
            const string magic = "3go8&$8*3*3h0k(2)2";
            var id = Encoding.UTF8.GetBytes(picId);
            var magicBytes = Encoding.UTF8.GetBytes(magic);
            var xored = new byte[id.Length];
            for (var i = 0; i < id.Length; i++)
                xored[i] = (byte)(id[i] ^ magicBytes[i % magicBytes.Length]);
            var hash = MD5.HashData(xored);
            return Convert.ToBase64String(hash).Replace('/', '_').Replace('+', '-');
        }

        /// <summary>由 picId 生成网易云封面 CDN URL(仅可靠的非零整数串)。</summary>
        private static string? NeteasePicIdToUrl(string? picId)
        {
            if (string.IsNullOrWhiteSpace(picId)) return null;
            var id = picId.Trim();
            if (id.Length == 0 || id == "0" || !id.All(char.IsDigit)) return null;
            try { return $"https://p1.music.126.net/{EncryptNeteasePicId(id)}/{id}.jpg"; }
            catch { return null; }
        }

        private static async Task<LxSearchResult> SearchWyAsync(string str, int page, int limit, int retryNum = 0)
        {
            if (++retryNum > 3) throw new HttpRequestException("WY search: try max num");
            var offset = limit * (page - 1);
            var url = $"https://music.163.com/api/search/get/web?s={Uri.EscapeDataString(str)}&type=1&offset={offset}&limit={limit}";
            var result = await GetJsonAsync(url, new Dictionary<string, string>
            {
                ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/69.0.3497.100 Safari/537.36",
                ["Referer"] = "https://music.163.com",
                ["Cookie"] = "MUSIC_A=1",
            });
            if ((int)JNum(result, "code") != 200)
                return await SearchWyAsync(str, page, limit, retryNum);

            var list = new List<LxSearchResultItem>();
            JsonElement songs = default;
            if (result.TryGetProperty("result", out var resultEl) && resultEl.ValueKind == JsonValueKind.Object && resultEl.TryGetProperty("songs", out var songsEl) && songsEl.ValueKind == JsonValueKind.Array)
                songs = songsEl;
            if (songs.ValueKind == JsonValueKind.Array)
            {
                foreach (var song in songs.EnumerateArray())
                {
                    var types = new List<LxSearchTypeItem>();
                    var underTypes = new Dictionary<string, LxSearchTypeItem>();
                    // 网易云搜索接口多数场景不返回 hq/sq 标志; 普遍提供 320k/flac, 声明 + 探测回落
                    var hasHq = Has(song, "hq");
                    var hasSq = Has(song, "sq");
                    if (hasHq) { types.Add(new LxSearchTypeItem { Type = "320k" }); underTypes["320k"] = new(); }
                    if (hasSq) { types.Add(new LxSearchTypeItem { Type = "flac" }); underTypes["flac"] = new(); }
                    types.Add(new LxSearchTypeItem { Type = "128k" }); underTypes["128k"] = new();
                    if (!hasHq) { types.Add(new LxSearchTypeItem { Type = "320k" }); underTypes["320k"] = new(); }
                    if (!hasSq) { types.Add(new LxSearchTypeItem { Type = "flac" }); underTypes["flac"] = new(); }
                    types.Add(new LxSearchTypeItem { Type = "flac24bit" }); underTypes["flac24bit"] = new();
                    types.Add(new LxSearchTypeItem { Type = "master" }); underTypes["master"] = new();
                    types.Reverse();

                    var artists = new List<string>();
                    if (song.TryGetProperty("artists", out var artistArr) && artistArr.ValueKind == JsonValueKind.Array)
                        foreach (var a in artistArr.EnumerateArray())
                        {
                            var n = JStr(a, "name");
                            if (!string.IsNullOrEmpty(n)) artists.Add(n);
                        }
                    JsonElement al = default;
                    if (song.TryGetProperty("album", out var albumEl) && albumEl.ValueKind == JsonValueKind.Object) al = albumEl;

                    // 封面: 完整 picUrl(http→https) → picId 加密 URL
                    string? img = null;
                    var picUrl = JStr(al, "picUrl");
                    if (!string.IsNullOrEmpty(picUrl))
                        img = Regex.Replace(picUrl!, "^http://", "https://", RegexOptions.IgnoreCase);
                    if (img is null)
                        img = NeteasePicIdToUrl(JStr(al, "picId_str") ?? JStr(al, "pic_str") ?? JStr(al, "picId") ?? JStr(al, "pic"));

                    list.Add(new LxSearchResultItem
                    {
                        Singer = string.Join("、", artists),
                        Name = JStr(song, "name") ?? string.Empty,
                        AlbumName = JStr(al, "name") ?? string.Empty,
                        AlbumId = JStr(al, "id") ?? string.Empty,
                        Source = LxSources.Wy,
                        Interval = FormatPlayTime(JNum(song, "duration") / 1000),
                        Songmid = JStr(song, "id") ?? string.Empty,
                        Img = img,
                        Types = types,
                        UnderTypes = underTypes,
                    });
                }
            }
            var total = (int)JNum(resultEl, "songCount");
            return new LxSearchResult { List = list, AllPage = (int)Math.Ceiling(total / (double)limit), Limit = limit, Total = total, Source = LxSources.Wy };
        }

        // ==================== MG (咪咕) Search ====================

        /// <summary>咪咕签名: md5(keyword + signatureMd5 + salt + deviceId + time)。</summary>
        private static (string Sign, string DeviceId) MgCreateSignature(string time, string str)
        {
            const string deviceId = "963B7AA0D21511ED807EE5846EC87D20";
            const string signatureMd5 = "6cdc72a439cef99a3418d2a78aa28c73";
            var sign = Md5Hex($"{str}{signatureMd5}yyapp2d16148780a1dcc7408e06336b98cfd50{deviceId}{time}");
            return (sign, deviceId);
        }

        private static List<LxSearchResultItem> MgFilterData(JsonElement rawData)
        {
            var list = new List<LxSearchResultItem>();
            var ids = new HashSet<string>();
            if (rawData.ValueKind != JsonValueKind.Array) return list;
            foreach (var item in rawData.EnumerateArray())
            {
                // resultList 是二维数组(每页多组)
                var entries = item.ValueKind == JsonValueKind.Array ? item.EnumerateArray().ToList() : [item];
                foreach (var data in entries)
                {
                    if (data.ValueKind != JsonValueKind.Object) continue;
                    var songId = JStr(data, "songId");
                    var copyrightId = JStr(data, "copyrightId");
                    if (string.IsNullOrEmpty(songId) || string.IsNullOrEmpty(copyrightId) || ids.Contains(copyrightId)) continue;
                    ids.Add(copyrightId);

                    var types = new List<LxSearchTypeItem>();
                    var underTypes = new Dictionary<string, LxSearchTypeItem>();
                    if (data.TryGetProperty("audioFormats", out var formats) && formats.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var type in formats.EnumerateArray())
                        {
                            var formatType = JStr(type, "formatType");
                            var size = SizeFormate(JNum(type, "asize", "isize"));
                            switch (formatType)
                            {
                                case "PQ":
                                    types.Add(new LxSearchTypeItem { Type = "128k", Size = size });
                                    underTypes["128k"] = new LxSearchTypeItem { Type = "128k", Size = size };
                                    break;
                                case "HQ":
                                    types.Add(new LxSearchTypeItem { Type = "320k", Size = size });
                                    underTypes["320k"] = new LxSearchTypeItem { Type = "320k", Size = size };
                                    break;
                                case "SQ":
                                    types.Add(new LxSearchTypeItem { Type = "flac", Size = size });
                                    underTypes["flac"] = new LxSearchTypeItem { Type = "flac", Size = size };
                                    break;
                                case "ZQ24":
                                    types.Add(new LxSearchTypeItem { Type = "flac24bit", Size = size });
                                    underTypes["flac24bit"] = new LxSearchTypeItem { Type = "flac24bit", Size = size };
                                    break;
                            }
                        }
                    }

                    var img = JStr(data, "img3") ?? JStr(data, "img2") ?? JStr(data, "img1");
                    if (!string.IsNullOrEmpty(img) && !img!.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        img = "http://d.musicapp.migu.cn" + img;

                    var singers = string.Empty;
                    if (data.TryGetProperty("singerList", out var singerList) && singerList.ValueKind == JsonValueKind.Array)
                        singers = string.Join("、", singerList.EnumerateArray().Select(s => JStr(s, "name")).Where(n => !string.IsNullOrEmpty(n)));

                    list.Add(new LxSearchResultItem
                    {
                        Singer = singers,
                        Name = JStr(data, "name") ?? string.Empty,
                        AlbumName = JStr(data, "album") ?? string.Empty,
                        AlbumId = JStr(data, "albumId") ?? string.Empty,
                        Songmid = songId!,
                        CopyrightId = copyrightId,
                        Source = LxSources.Mg,
                        Interval = FormatPlayTime(JNum(data, "duration")),
                        Img = img,
                        LrcUrl = JStr(data, "lrcUrl"),
                        MrcUrl = JStr(data, "mrcurl"),
                        TrcUrl = JStr(data, "trcUrl"),
                        Types = types,
                        UnderTypes = underTypes,
                    });
                }
            }
            return list;
        }

        private static async Task<LxSearchResult> SearchMgAsync(string str, int page, int limit, int retryNum = 0)
        {
            if (++retryNum > 3) throw new HttpRequestException("MG search: try max num");
            var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var (sign, deviceId) = MgCreateSignature(time, str);
            var url = $"https://jadeite.migu.cn/music_search/v3/search/searchAll?isCorrect=0&isCopyright=1&searchSwitch=%7B%22song%22%3A1%2C%22album%22%3A0%2C%22singer%22%3A0%2C%22tagSong%22%3A1%2C%22mvSong%22%3A0%2C%22bestShow%22%3A1%2C%22songlist%22%3A0%2C%22lyricSong%22%3A0%7D&pageSize={limit}&text={Uri.EscapeDataString(str)}&pageNo={page}&sort=0&sid=USS";
            var result = await GetJsonAsync(url, new Dictionary<string, string>
            {
                ["uiVersion"] = "A_music_3.6.1",
                ["deviceId"] = deviceId,
                ["timestamp"] = time,
                ["sign"] = sign,
                ["channel"] = "0146921",
                ["User-Agent"] = "Mozilla/5.0 (Linux; U; Android 11.0.0; zh-cn; MI 11 Build/OPR1.170623.032) AppleWebKit/534.30 (KHTML, like Gecko) Version/4.0 Mobile Safari/534.30",
            });
            var code = JStr(result, "code") ?? (int)JNum(result, "code") + "";
            if (code != "000000") throw new HttpRequestException(JStr(result, "info") ?? "MG搜索失败");
            var list = new List<LxSearchResultItem>();
            var totalCount = 0;
            if (result.TryGetProperty("songResultData", out var songResult) && songResult.ValueKind == JsonValueKind.Object)
            {
                if (songResult.TryGetProperty("resultList", out var resultList) && resultList.ValueKind == JsonValueKind.Array)
                    list = MgFilterData(resultList);
                totalCount = (int)JNum(songResult, "totalCount");
            }
            return new LxSearchResult { List = list, AllPage = (int)Math.Ceiling(totalCount / (double)limit), Limit = limit, Total = totalCount, Source = LxSources.Mg };
        }

        // ==================== 目录详情(专辑歌曲/歌单曲目) ====================

        /// <summary>由专辑/歌单接口的简化条目构造 LxSearchResultItem(types 空, 播放时走音质回退)。</summary>
        private static LxSearchResultItem BuildSimpleItem(string source, string songmid, string name, string singer, string albumName, string albumId, double durationSec, string? img, string? copyrightId = null)
            => new()
            {
                Source = source,
                Songmid = songmid,
                Name = DecodeName(name),
                Singer = DecodeName(singer),
                AlbumName = DecodeName(albumName),
                AlbumId = albumId,
                Interval = FormatPlayTime(durationSec),
                Img = img,
                CopyrightId = copyrightId,
                Types = [],
                UnderTypes = new Dictionary<string, LxSearchTypeItem>(),
            };

        /// <summary>检测专辑 ID 有效性(tx 为字母数字组合, 其余为纯数字; 专辑名回退时调 API 必失败)。</summary>
        private static bool IsValidAlbumId(string source, string albumId)
        {
            if (string.IsNullOrEmpty(albumId)) return false;
            return source == LxSources.Tx
                ? System.Text.RegularExpressions.Regex.IsMatch(albumId, "^[A-Za-z0-9]{6,}$")
                : albumId.All(char.IsDigit) && albumId.Length > 0;
        }

        /// <summary>获取专辑歌曲列表(kw/kg/tx/wy/mg 公开接口); ID 无效或接口失败返回空, 由调用方走搜索回退。</summary>
        public static async Task<List<LxSearchResultItem>> GetAlbumSongsAsync(string source, string albumId, string albumName, int page, int limit)
        {
            if (!IsValidAlbumId(source, albumId)) return [];
            limit = limit <= 0 ? 30 : limit;
            try
            {
                switch (source)
                {
                    case LxSources.Kw:
                    {
                        var data = await GetJsonAsync($"https://www.kuwo.cn/api/www/album/albumInfo?albumid={albumId}&pn={page}&rn={limit}",
                            new Dictionary<string, string> { ["csrf"] = "ABCDEF", ["Cookie"] = "kw_token=ABCDEF", ["Referer"] = "https://www.kuwo.cn/" });
                        var list = data.TryGetProperty("data", out var d) && d.TryGetProperty("musicList", out var ml) && ml.ValueKind == JsonValueKind.Array ? ml : default;
                        if (list.ValueKind != JsonValueKind.Array) return [];
                        return list.EnumerateArray().Select(m => BuildSimpleItem(source,
                            JStr(m, "rid") ?? JStr(m, "id") ?? string.Empty,
                            JStr(m, "name") ?? string.Empty,
                            JStr(m, "artist") ?? string.Empty,
                            JStr(m, "album") ?? albumName,
                            JStr(m, "albumid") ?? albumId,
                            JNum(m, "duration"),
                            JStr(m, "pic"))).ToList();
                    }
                    case LxSources.Kg:
                    {
                        var data = await GetJsonAsync($"https://mobilecdn.kugou.com/api/v3/album/song?albumid={albumId}&page={page}&pagesize={limit}");
                        var info = data.TryGetProperty("data", out var d) && d.TryGetProperty("info", out var inf) && inf.ValueKind == JsonValueKind.Array ? inf : default;
                        if (info.ValueKind != JsonValueKind.Array) return [];
                        return info.EnumerateArray().Select(KgFilterData).ToList();
                    }
                    case LxSources.Tx:
                    {
                        var requestBody = JsonSerializer.Serialize(new Dictionary<string, object>
                        {
                            ["comm"] = new Dictionary<string, object> { ["ct"] = "24", ["cv"] = "0" },
                            ["req"] = new Dictionary<string, object>
                            {
                                ["module"] = "music.musichallSong.PlaySingerSongs",
                                ["method"] = "GetAlbumSongList",
                                ["param"] = new Dictionary<string, object> { ["albumMid"] = albumId, ["songBegin"] = (page - 1) * limit, ["songNum"] = limit },
                            },
                        });
                        var sign = ZzcSign(requestBody);
                        var resp = await PostJsonAsync($"https://u.y.qq.com/cgi-bin/musics.fcg?sign={sign}", requestBody, new Dictionary<string, string>
                        {
                            ["User-Agent"] = "Mozilla/5.0 (Linux; Android 12; EBG-AN10) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/107.0.5304.141 Mobile Safari/537.36",
                            ["Content-Type"] = "application/json",
                            ["Referer"] = "https://y.qq.com/",
                        });
                        var songList = resp.TryGetProperty("req", out var req) && req.TryGetProperty("data", out var reqData) && reqData.TryGetProperty("songList", out var sl) && sl.ValueKind == JsonValueKind.Array ? sl : default;
                        if (songList.ValueKind != JsonValueKind.Array) return [];
                        // 每项可能包在 songInfo 里
                        return TxHandleResult(JsonSerializer.SerializeToElement(songList.EnumerateArray()
                            .Select(s => s.TryGetProperty("songInfo", out var si) && si.ValueKind == JsonValueKind.Object ? si : s).ToList()));
                    }
                    case LxSources.Wy:
                    {
                        var data = await GetJsonAsync($"https://music.163.com/api/album/{albumId}", new Dictionary<string, string>
                        {
                            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
                            ["Referer"] = "https://music.163.com",
                            ["Cookie"] = "MUSIC_A=1",
                        });
                        var songs = data.TryGetProperty("songs", out var ss) && ss.ValueKind == JsonValueKind.Array ? ss : default;
                        if (songs.ValueKind != JsonValueKind.Array) return [];
                        return songs.EnumerateArray().Select(song =>
                        {
                            var al = song.TryGetProperty("album", out var alEl) && alEl.ValueKind == JsonValueKind.Object ? alEl : default;
                            string singers = string.Empty;
                            if (song.TryGetProperty("artists", out var ars) && ars.ValueKind == JsonValueKind.Array)
                                singers = string.Join("、", ars.EnumerateArray().Select(a => JStr(a, "name")).Where(n => !string.IsNullOrEmpty(n)));
                            return BuildSimpleItem(source,
                                JStr(song, "id") ?? string.Empty,
                                JStr(song, "name") ?? string.Empty,
                                singers,
                                al.ValueKind == JsonValueKind.Object ? JStr(al, "name") ?? albumName : albumName,
                                al.ValueKind == JsonValueKind.Object ? JStr(al, "id") ?? albumId : albumId,
                                JNum(song, "duration") / 1000,
                                al.ValueKind == JsonValueKind.Object ? JStr(al, "picUrl") : null);
                        }).ToList();
                    }
                    default:
                    {
                        var data = await GetJsonAsync($"https://m.music.migu.cn/migu/remoting/cms_album_song_list_tag?albumId={albumId}&pageNo={page}&pageSize={limit}");
                        var list = data.TryGetProperty("resultList", out var rl) && rl.ValueKind == JsonValueKind.Array ? rl
                            : data.TryGetProperty("list", out var l) && l.ValueKind == JsonValueKind.Array ? l : default;
                        if (list.ValueKind != JsonValueKind.Array) return [];
                        return list.EnumerateArray().Select(item => BuildSimpleItem(source,
                            JStr(item, "songId") ?? JStr(item, "id") ?? string.Empty,
                            JStr(item, "name") ?? JStr(item, "songName") ?? string.Empty,
                            MgSingerNames(item),
                            JStr(item, "album") ?? JStr(item, "albumName") ?? albumName,
                            JStr(item, "albumId") ?? albumId,
                            JNum(item, "duration"),
                            JStr(item, "img3") ?? JStr(item, "img2") ?? JStr(item, "img1"),
                            JStr(item, "copyrightId"))).ToList();
                    }
                }
            }
            catch
            {
                return [];
            }
        }

        /// <summary>咪咕条目歌手名拼接。</summary>
        private static string MgSingerNames(JsonElement item)
        {
            if (item.TryGetProperty("singerList", out var sl) && sl.ValueKind == JsonValueKind.Array)
                return string.Join("、", sl.EnumerateArray().Select(s => JStr(s, "name")).Where(n => !string.IsNullOrEmpty(n)));
            if (item.TryGetProperty("singers", out var ss) && ss.ValueKind == JsonValueKind.Array)
                return string.Join("、", ss.EnumerateArray().Select(s => JStr(s, "name")).Where(n => !string.IsNullOrEmpty(n)));
            return string.Empty;
        }

        /// <summary>TX 歌单曲目 Web 兜底(经典 fcg_ucc_getcdinfo 接口, 不依赖签名风控体系)。</summary>
        private static async Task<List<LxSearchResultItem>> TxSheetTracksWebFallbackAsync(string playlistId, int page, int limit)
        {
            var url = $"https://c.y.qq.com/qzone/fcg-bin/fcg_ucc_getcdinfo_byids_cp.fcg?type=1&json=1&utf8=1&onlysong=0&new_format=1&disstid={Uri.EscapeDataString(playlistId)}&format=json&g_tk=5381&loginUin=0&hostUin=0&inCharset=utf8&outCharset=utf-8&notice=0&platform=jq&needNewCode=0";
            var result = await GetJsonAsync(url, new Dictionary<string, string>
            {
                ["User-Agent"] = "Mozilla/5.0 (iPhone; CPU iPhone OS 14_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/14.0 Mobile/15E148 Safari/604.1",
                ["Referer"] = "https://y.qq.com/",
            });
            var songAll = result.TryGetProperty("data", out var d) && d.TryGetProperty("cdlist", out var cd) && cd.ValueKind == JsonValueKind.Array && cd.GetArrayLength() > 0
                && cd[0].TryGetProperty("songlist", out var slst) && slst.ValueKind == JsonValueKind.Array ? slst.EnumerateArray().ToList() : [];
            var start = (page - 1) * limit;
            var pageSongs = songAll.Skip(start).Take(limit).ToList();
            return TxHandleResult(JsonSerializer.SerializeToElement(pageSongs));
        }

        /// <summary>获取歌单曲目列表(kw/kg/tx/wy/mg 公开接口); 接口失败返回空, 由调用方走搜索回退。</summary>
        public static async Task<List<LxSearchResultItem>> GetPlaylistTracksAsync(string source, string playlistId, int page, int limit)
        {
            if (string.IsNullOrEmpty(playlistId)) return [];
            limit = limit <= 0 ? 30 : limit;
            try
            {
                switch (source)
                {
                    case LxSources.Kw:
                    {
                        var data = await GetJsonAsync($"https://www.kuwo.cn/api/www/playlist/playListInfo?pid={playlistId}&pn={page}&rn={limit}",
                            new Dictionary<string, string> { ["csrf"] = "ABCDEF", ["Cookie"] = "kw_token=ABCDEF", ["Referer"] = "https://www.kuwo.cn/" });
                        var list = data.TryGetProperty("data", out var d) && d.TryGetProperty("musicList", out var ml) && ml.ValueKind == JsonValueKind.Array ? ml : default;
                        if (list.ValueKind != JsonValueKind.Array) return [];
                        return list.EnumerateArray().Select(m => BuildSimpleItem(source,
                            JStr(m, "rid") ?? JStr(m, "id") ?? string.Empty,
                            JStr(m, "name") ?? string.Empty,
                            JStr(m, "artist") ?? string.Empty,
                            JStr(m, "album") ?? string.Empty,
                            JStr(m, "albumid") ?? string.Empty,
                            JNum(m, "duration"),
                            JStr(m, "pic"))).ToList();
                    }
                    case LxSources.Kg:
                    {
                        var data = await GetJsonAsync($"https://mobilecdn.kugou.com/api/v3/song/special/getSongList?specialid={playlistId}&page={page}&pagesize={limit}");
                        var info = data.TryGetProperty("data", out var d) && d.TryGetProperty("info", out var inf) && inf.ValueKind == JsonValueKind.Array ? inf : default;
                        if (info.ValueKind != JsonValueKind.Array) return [];
                        return info.EnumerateArray().Select(KgFilterData).ToList();
                    }
                    case LxSources.Tx:
                    {
                        var requestBody = JsonSerializer.Serialize(new Dictionary<string, object>
                        {
                            ["comm"] = new Dictionary<string, object> { ["ct"] = "24", ["cv"] = "0" },
                            ["req"] = new Dictionary<string, object>
                            {
                                ["module"] = "music.srfDissInfo.aiDissInfo",
                                ["method"] = "uniform_get_Dissinfo",
                                ["param"] = new Dictionary<string, object>
                                {
                                    ["disstid"] = playlistId, ["song_num"] = limit, ["song_begin"] = (page - 1) * limit,
                                    ["userinfo"] = 0, ["tag"] = 1, ["is_pull_album_info"] = 1,
                                },
                            },
                        });
                        List<LxSearchResultItem> items;
                        try
                        {
                            var sign = ZzcSign(requestBody);
                            var resp = await PostJsonAsync($"https://u.y.qq.com/cgi-bin/musics.fcg?sign={sign}", requestBody, new Dictionary<string, string>
                            {
                                ["User-Agent"] = "Mozilla/5.0 (Linux; Android 12; EBG-AN10) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/107.0.5304.141 Mobile Safari/537.36",
                                ["Content-Type"] = "application/json",
                                ["Referer"] = "https://y.qq.com/",
                            });
                            var songlist = resp.TryGetProperty("req", out var req) && req.TryGetProperty("data", out var reqData) && reqData.TryGetProperty("songlist", out var sl) && sl.ValueKind == JsonValueKind.Array ? sl : default;
                            items = songlist.ValueKind != JsonValueKind.Array ? [] : TxHandleResult(JsonSerializer.SerializeToElement(songlist));
                        }
                        catch
                        {
                            items = [];
                        }
                        // Mobile 接口被风控/降级返回空时走经典 Web 接口兜底
                        return items.Count > 0 ? items : await TxSheetTracksWebFallbackAsync(playlistId, page, limit);
                    }
                    case LxSources.Wy:
                    {
                        var offset = (page - 1) * limit;
                        var data = await GetJsonAsync($"https://music.163.com/api/v6/playlist/detail?id={playlistId}&n={limit}&offset={offset}", new Dictionary<string, string>
                        {
                            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
                            ["Referer"] = "https://music.163.com",
                            ["Cookie"] = "MUSIC_A=1",
                        });
                        var tracks = data.TryGetProperty("playlist", out var pl) && pl.TryGetProperty("tracks", out var tr) && tr.ValueKind == JsonValueKind.Array ? tr : default;
                        if (tracks.ValueKind != JsonValueKind.Array) return [];
                        return tracks.EnumerateArray().Select(song =>
                        {
                            var al = song.TryGetProperty("album", out var alEl) && alEl.ValueKind == JsonValueKind.Object ? alEl
                                : song.TryGetProperty("al", out var alEl2) && alEl2.ValueKind == JsonValueKind.Object ? alEl2 : default;
                            var arEl = song.TryGetProperty("artists", out var ars) && ars.ValueKind == JsonValueKind.Array ? ars
                                : song.TryGetProperty("ar", out var ars2) && ars2.ValueKind == JsonValueKind.Array ? ars2 : default;
                            var singers = arEl.ValueKind == JsonValueKind.Array
                                ? string.Join("、", arEl.EnumerateArray().Select(a => JStr(a, "name")).Where(n => !string.IsNullOrEmpty(n))) : string.Empty;
                            return BuildSimpleItem(source,
                                JStr(song, "id") ?? string.Empty,
                                JStr(song, "name") ?? string.Empty,
                                singers,
                                al.ValueKind == JsonValueKind.Object ? JStr(al, "name") ?? string.Empty : string.Empty,
                                al.ValueKind == JsonValueKind.Object ? JStr(al, "id") ?? string.Empty : string.Empty,
                                (JNum(song, "duration") is var dt && dt > 0 ? dt : JNum(song, "dt")) / 1000,
                                al.ValueKind == JsonValueKind.Object ? JStr(al, "picUrl") : null);
                        }).ToList();
                    }
                    default:
                    {
                        var data = await GetJsonAsync($"https://m.music.migu.cn/migu/remoting/playlist_callback?playlistId={playlistId}&pageNo={page}&pageSize={limit}");
                        var list = data.TryGetProperty("list", out var l) && l.ValueKind == JsonValueKind.Array ? l
                            : data.TryGetProperty("resultList", out var rl) && rl.ValueKind == JsonValueKind.Array ? rl : default;
                        if (list.ValueKind != JsonValueKind.Array) return [];
                        return list.EnumerateArray().Select(item => BuildSimpleItem(source,
                            JStr(item, "songId") ?? JStr(item, "id") ?? string.Empty,
                            JStr(item, "name") ?? JStr(item, "songName") ?? string.Empty,
                            MgSingerNames(item),
                            JStr(item, "album") ?? JStr(item, "albumName") ?? string.Empty,
                            JStr(item, "albumId") ?? string.Empty,
                            JNum(item, "duration"),
                            JStr(item, "img3") ?? JStr(item, "img2") ?? JStr(item, "img1"),
                            JStr(item, "copyrightId"))).ToList();
                    }
                }
            }
            catch
            {
                return [];
            }
        }
    }
}
