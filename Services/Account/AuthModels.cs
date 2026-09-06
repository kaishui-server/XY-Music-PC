using System.Text.Json.Serialization;

namespace WinUIMusicPlayer.Services.Account
{
    /// <summary>XY Music 账号用户(与手机版 AuthUser 字段一致)。</summary>
    public class AuthUser
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
        [JsonPropertyName("nickname")] public string Nickname { get; set; } = string.Empty;
        [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
        [JsonPropertyName("avatar")] public string? Avatar { get; set; }
        [JsonPropertyName("xymusic_id")] public string? XyMusicId { get; set; }
        [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;

        public static AuthUser FromJson(System.Text.Json.JsonElement j)
        {
            string Str(string name)
            {
                if (j.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
                    return v.GetString() ?? string.Empty;
                return string.Empty;
            }
            var username = Str("username");
            var nickname = Str("nickname");
            if (string.IsNullOrEmpty(nickname)) nickname = username;
            var id = Str("user_id"); if (string.IsNullOrEmpty(id)) id = Str("id");
            var xymusic = Str("xymusic_id"); if (string.IsNullOrEmpty(xymusic)) xymusic = Str("ciyuanxi_id");
            string? avatar = null;
            if (j.TryGetProperty("avatar_url", out var a1) && a1.ValueKind == System.Text.Json.JsonValueKind.String)
                avatar = a1.GetString();
            else if (j.TryGetProperty("avatar", out var a2) && a2.ValueKind == System.Text.Json.JsonValueKind.String)
                avatar = a2.GetString();
            return new AuthUser
            {
                Id = id,
                Username = username,
                Nickname = nickname,
                Email = Str("email"),
                Avatar = avatar,
                XyMusicId = xymusic,
                Role = Str("role"),
            };
        }
    }

    /// <summary>人机验证题目(服务端算术题)。</summary>
    public class HumanCaptcha
    {
        public string CaptchaId { get; set; } = string.Empty;
        public string Question { get; set; } = string.Empty;
    }

    /// <summary>人机验证结果载荷(id + 答案), 并入请求体 captcha 字段。</summary>
    public record HumanCaptchaPayload(string CaptchaId, string CaptchaAnswer);
}
