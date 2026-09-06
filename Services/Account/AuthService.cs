using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using WinUIMusicPlayer.Utils;

namespace WinUIMusicPlayer.Services.Account
{
    /// <summary>
    /// XY Music 账号服务: 与手机版/服务端相同的 MD5 签名协议。
    /// POST {baseUrl}/?action={action}, 头 X-Timestamp/X-Nonce/X-Sign = md5(timestamp+nonce+body+secret)。
    /// 凭证持久化到 %LOCALAPPDATA%\XYMusic\Auth\。
    /// </summary>
    public partial class AuthService : ObservableObject
    {
        public const string DefaultBaseUrl = "https://cosn.xymusic.cc:8081/api";
        public const string DefaultApiSecret = "53dab6e42c380c4502f73b40fc2e9af9c2ee523ecb92b6884ad17156c9c762af";

        private static readonly HttpClient _http = new()
        {
            // 禁用 HttpClient 级整体超时, 由每次请求的 CancellationTokenSource 按需控制
            // (云同步分块上传/下载需要 60s 级超时, HttpClient.Timeout 是硬上限无法被请求级超时延长)
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };

        private readonly ILogger<AuthService> _logger;
        private string _baseUrl = DefaultBaseUrl;
        private string _apiSecret = DefaultApiSecret;

        private static string AuthDir => Path.Combine(AppPaths.LocalFolder, "Auth");
        private static string TokenFile => Path.Combine(AuthDir, "auth-token.txt");
        private static string UserFile => Path.Combine(AuthDir, "user.json");
        private static string DeviceIdFile => Path.Combine(AuthDir, "device-id.txt");

        [ObservableProperty]
        private AuthUser? _currentUser;

        public bool IsLoggedIn => CurrentUser is not null;
        public event Action? LoginStateChanged;

        public string AccountId => CurrentUser?.XyMusicId?.Trim() ?? string.Empty;
        private string? _token;

        public AuthService(ILogger<AuthService> logger)
        {
            _logger = logger;
            Directory.CreateDirectory(AuthDir);
        }

        // ─── 启动恢复 ─────────────────────────────────

        /// <summary>启动时从磁盘恢复登录态(读取 token + user.json)。</summary>
        public void RestoreFromDisk()
        {
            try
            {
                if (!File.Exists(TokenFile)) return;
                var token = File.ReadAllText(TokenFile).Trim();
                if (string.IsNullOrEmpty(token)) return;
                if (!File.Exists(UserFile)) return;
                using var doc = JsonDocument.Parse(File.ReadAllText(UserFile));
                CurrentUser = AuthUser.FromJson(doc.RootElement);
                _token = token;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "恢复登录态失败");
            }
        }

        private string DeviceId()
        {
            try
            {
                if (File.Exists(DeviceIdFile))
                {
                    var id = File.ReadAllText(DeviceIdFile).Trim();
                    if (!string.IsNullOrEmpty(id)) return id;
                }
                var guid = Guid.NewGuid().ToString("N");
                File.WriteAllText(DeviceIdFile, guid);
                return guid;
            }
            catch
            {
                return Guid.NewGuid().ToString("N");
            }
        }

        // ─── 签名请求 ─────────────────────────────────

        /// <summary>发起带签名的账号请求, 校验 code===200 并返回 data。</summary>
        public async Task<JsonElement> RequestActionAsync(string action, object body, int? timeoutMs = null)
        {
            var bodyJson = JsonSerializer.Serialize(body);
            var url = $"{_baseUrl}/?action={action}";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var nonce = Guid.NewGuid().ToString("N");
            var signInput = $"{timestamp}{nonce}{bodyJson}{_apiSecret}";
            var sign = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(signInput))).ToLowerInvariant();

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
            req.Headers.Add("X-Timestamp", timestamp);
            req.Headers.Add("X-Nonce", nonce);
            req.Headers.Add("X-Sign", sign);

            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs ?? 25000));
            using var resp = await _http.SendAsync(req, cts.Token);
            var text = await resp.Content.ReadAsStringAsync();
            if (text.Contains("宝塔WAF") || text.Contains("缓冲区溢出"))
                throw new AuthException("服务器WAF拦截: 请求体过大");
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (!root.TryGetProperty("code", out var codeEl) || codeEl.GetInt32() != 200)
            {
                var code = root.TryGetProperty("code", out var c) ? c.GetInt32() : -1;
                var msg = root.TryGetProperty("msg", out var m) ? m.GetString() : string.Empty;
                throw new AuthException(string.IsNullOrEmpty(msg) ? $"请求失败（code {code}）" : msg);
            }
            return root.TryGetProperty("data", out var data) ? data.Clone() : default;
        }

        // ─── 登录/注册 ─────────────────────────────────

        public async Task LoginAsync(string xymusicId, string password)
        {
            var data = await RequestActionAsync("user_login", new
            {
                xymusic_id = xymusicId.Trim(),
                password,
                device_id = DeviceId(),
                client_type = "desktop",
                app_version = "1.0.0",
                os_version = Environment.OSVersion.VersionString,
                device_model = "Windows PC",
                platform = "windows",
            });
            if (data.ValueKind == JsonValueKind.Undefined)
                throw new AuthException("登录响应无效");
            var token = data.TryGetProperty("token", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(token)) throw new AuthException("登录响应无效");
            SaveCredentials(token!, data);
            CurrentUser = AuthUser.FromJson(data);
            LoginStateChanged?.Invoke();
        }

        public async Task<string?> RegisterAsync(string xymusicId, string nickname, string password, string email, string code, HumanCaptchaPayload? captcha)
        {
            var body = new
            {
                xymusic_id = xymusicId.Trim(),
                nickname = nickname.Trim(),
                password,
                email = email.Trim(),
                verify_code = code.Trim(),
                device_id = DeviceId(),
                client_type = "desktop",
                app_version = "1.0.0",
                os_version = Environment.OSVersion.VersionString,
                device_model = "Windows PC",
                platform = "windows",
                captcha_id = captcha?.CaptchaId,
                captcha_answer = captcha?.CaptchaAnswer,
            };
            var data = await RequestActionAsync("register", body);
            var token = data.TryGetProperty("token", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(token)) throw new AuthException("注册响应无效");
            SaveCredentials(token!, data);
            CurrentUser = AuthUser.FromJson(data);
            LoginStateChanged?.Invoke();
            return data.TryGetProperty("registration_notice", out var n) ? n.GetString() : null;
        }

        public async Task SendVerifyCodeAsync(string email, string type, HumanCaptchaPayload? captcha, string? xymusicId = null)
        {
            var body = new
            {
                email,
                type,
                xymusic_id = string.IsNullOrWhiteSpace(xymusicId) ? null : xymusicId.Trim(),
                captcha_id = captcha?.CaptchaId,
                captcha_answer = captcha?.CaptchaAnswer,
            };
            var data = await RequestActionAsync("send_verify_code", body);
            _ = data; // 服务端返回 message 仅供参考, 失败会抛异常
        }

        public async Task<HumanCaptcha> FetchCaptchaAsync()
        {
            var data = await RequestActionAsync("get_captcha", new { purpose = "auth" }, 12000);
            string Id(string k1, string k2, string k3)
            {
                foreach (var k in new[] { k1, k2, k3 })
                    if (data.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.GetString()))
                        return v.GetString()!;
                return string.Empty;
            }
            var captcha = new HumanCaptcha
            {
                CaptchaId = Id("captcha_id", "captchaId", "id"),
                Question = Id("question", "captcha_question", "captchaQuestion"),
            };
            if (string.IsNullOrEmpty(captcha.CaptchaId) || string.IsNullOrEmpty(captcha.Question))
                throw new AuthException("验证题返回内容无效，请点击换一题重试");
            return captcha;
        }

        public async Task VerifyCaptchaAsync(HumanCaptchaPayload payload)
        {
            await RequestActionAsync("verify_captcha", new
            {
                purpose = "auth",
                captcha_id = payload.CaptchaId,
                captcha_answer = payload.CaptchaAnswer,
            }, 12000);
        }

        // ─── 资料/头像/密码 ─────────────────────────────

        public async Task RefreshProfileAsync()
        {
            if (string.IsNullOrEmpty(AccountId)) return;
            var data = await RequestActionAsync("get_user_info", new { xymusic_id = AccountId }, 15000);
            var user = AuthUser.FromJson(data);
            if (_token is not null) SaveCredentials(_token, data);
            CurrentUser = user;
            LoginStateChanged?.Invoke();
        }

        /// <summary>上传头像(缩放到 256px JPEG, base64 data URL, 与手机版相同的审核接口)。</summary>
        public async Task<string> UploadAvatarAsync(byte[] bytes)
        {
            if (string.IsNullOrEmpty(AccountId)) throw new AuthException("请先登录");
            if (bytes.Length == 0) throw new AuthException("请选择有效的图片");

            var encoded = await ResizeToJpegAsync(bytes, 256);
            var avatarData = $"data:image/jpeg;base64,{Convert.ToBase64String(encoded)}";
            if (avatarData.Length > 195 * 1024)
                throw new AuthException("图片压缩后仍然过大，请选择更简单的图片");

            var data = await RequestActionAsync("upload_avatar", new
            {
                xymusic_id = AccountId,
                avatar_data = avatarData,
            }, 55000);
            var status = data.TryGetProperty("status", out var s) ? s.GetString()?.Trim().ToLowerInvariant() : "";
            if (status == "approved")
            {
                await RefreshProfileAsync();
                return "头像已通过审核并生效";
            }
            return "头像已上传，等待管理员审核";
        }

        /// <summary>查询头像审核状态: none / pending / rejected。</summary>
        public async Task<string> GetAvatarStatusAsync()
        {
            if (string.IsNullOrEmpty(AccountId)) return "none";
            try
            {
                var data = await RequestActionAsync("get_avatar_status", new { xymusic_id = AccountId }, 15000);
                var status = data.TryGetProperty("status", out var s) ? s.GetString()?.Trim().ToLowerInvariant() : "none";
                return status switch
                {
                    "pending" => "pending",
                    "rejected" => "rejected",
                    _ => "none",
                };
            }
            catch
            {
                return "none";
            }
        }

        public async Task<string> UpdateNicknameAsync(string nickname)
        {
            if (string.IsNullOrEmpty(AccountId)) throw new AuthException("请先登录");
            if (string.IsNullOrWhiteSpace(nickname)) throw new AuthException("昵称不能为空");
            var data = await RequestActionAsync("update_profile", new
            {
                token = _token ?? string.Empty,
                xymusic_id = AccountId,
                username = nickname.Trim(),
                nickname = nickname.Trim(),
                avatar = string.Empty,
            });
            var status = data.TryGetProperty("status", out var s) ? s.GetString() : string.Empty;
            if (status == "approved")
            {
                await RefreshProfileAsync();
                return "昵称已修改";
            }
            return status == "pending" ? "昵称已提交审核" : "昵称修改已提交";
        }

        public async Task<string> ChangePasswordAsync(string oldPassword, string newPassword)
        {
            if (string.IsNullOrEmpty(AccountId)) throw new AuthException("请先登录");
            if (newPassword.Length < 6) throw new AuthException("新密码长度不能少于 6 位");
            await RequestActionAsync("change_password", new
            {
                xymusic_id = AccountId,
                old_password = oldPassword,
                new_password = newPassword,
            });
            return "密码修改成功";
        }

        public async Task<string> ResetPasswordAsync(string email, string code, string newPassword, HumanCaptchaPayload? captcha)
        {
            if (newPassword.Length < 6) throw new AuthException("新密码长度不能少于 6 位");
            await RequestActionAsync("reset_password", new
            {
                email = email.Trim(),
                verify_code = code.Trim(),
                new_password = newPassword,
                captcha_id = captcha?.CaptchaId,
                captcha_answer = captcha?.CaptchaAnswer,
            });
            return "密码修改成功，请使用新密码登录";
        }

        public void Logout()
        {
            try
            {
                if (File.Exists(TokenFile)) File.Delete(TokenFile);
                if (File.Exists(UserFile)) File.Delete(UserFile);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理凭证文件失败");
            }
            _token = null;
            CurrentUser = null;
            LoginStateChanged?.Invoke();
        }

        // ─── 内部 ─────────────────────────────────────

        private void SaveCredentials(string token, JsonElement userData)
        {
            _token = token;
            File.WriteAllText(TokenFile, token);
            var user = AuthUser.FromJson(userData);
            File.WriteAllText(UserFile, JsonSerializer.Serialize(user));
        }

        /// <summary>图片缩放到最长边 maxSide 的 JPEG, 逐步降质至 135KB 内。</summary>
        private static async Task<byte[]> ResizeToJpegAsync(byte[] bytes, int maxSide)
        {
            var tempDir = await StorageFolder.GetFolderFromPathAsync(Path.GetTempPath());
            var srcFile = await tempDir.CreateFileAsync($"avatar_src_{Guid.NewGuid():N}.tmp", CreationCollisionOption.ReplaceExisting);
            try
            {
                await FileIO.WriteBytesAsync(srcFile, bytes);
                using var stream = await srcFile.OpenReadAsync();
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                uint srcW = decoder.OrientedPixelWidth, srcH = decoder.OrientedPixelHeight;
                var transform = new Windows.Graphics.Imaging.BitmapTransform();
                if (Math.Max(srcW, srcH) > maxSide)
                {
                    var scale = maxSide / (double)Math.Max(srcW, srcH);
                    transform.ScaledWidth = (uint)Math.Max(1, Math.Round(srcW * scale));
                    transform.ScaledHeight = (uint)Math.Max(1, Math.Round(srcH * scale));
                }
                var provider = await decoder.GetPixelDataAsync(
                    Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                    Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                    transform,
                    Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation,
                    Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
                var pixels = provider.DetachPixelData();
                var w = (int)transform.ScaledWidth;
                var h = (int)transform.ScaledHeight;
                if (w == 0) w = (int)srcW;
                if (h == 0) h = (int)srcH;

                // 白底填充(透明 PNG 转 JPEG 避免黑底)
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i + 3] < 255)
                    {
                        var a = pixels[i + 3];
                        pixels[i] = (byte)((pixels[i] * a + 255 * (255 - a)) / 255);
                        pixels[i + 1] = (byte)((pixels[i + 1] * a + 255 * (255 - a)) / 255);
                        pixels[i + 2] = (byte)((pixels[i + 2] * a + 255 * (255 - a)) / 255);
                        pixels[i + 3] = 255;
                    }
                }

                // 逐步降质重编码, 直至 135KB 内
                int quality = 80;
                while (true)
                {
                    var outFile = await tempDir.CreateFileAsync($"avatar_out_{Guid.NewGuid():N}.tmp", CreationCollisionOption.ReplaceExisting);
                    try
                    {
                        using var outStream = await outFile.OpenAsync(FileAccessMode.ReadWrite);
                        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
                            Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId, outStream);
                        var props = new Windows.Graphics.Imaging.BitmapPropertySet
                        {
                            { "System.Image.Quality", new Windows.Graphics.Imaging.BitmapTypedValue((double)quality / 100.0, Windows.Foundation.PropertyType.Single) }
                        };
                        await encoder.BitmapProperties.SetPropertiesAsync(props);
                        encoder.SetPixelData(
                            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                            (uint)w, (uint)h, 96, 96, pixels);
                        await encoder.FlushAsync();
                        var encoded = File.ReadAllBytes(outFile.Path);
                        if (encoded.Length <= 135 * 1024 || quality <= 35) return encoded;
                        quality -= 10;
                    }
                    finally
                    {
                        await outFile.DeleteAsync();
                    }
                }
            }
            finally
            {
                await srcFile.DeleteAsync();
            }
        }
    }

    public class AuthException : Exception
    {
        public AuthException(string message) : base(message) { }
    }
}
