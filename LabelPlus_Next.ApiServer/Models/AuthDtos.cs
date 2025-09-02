using System.Text.Json.Serialization;

namespace LabelPlus_Next.ApiServer.Models;

public sealed class LoginRequest
{
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("password")] public string? Password { get; set; }
}

public sealed class LoginData
{
    [JsonPropertyName("token")] public string Token { get; set; } = string.Empty;
}

public sealed class MeData
{
    [JsonPropertyName("base_path")] public string? BasePath { get; set; }
    [JsonPropertyName("disabled")] public bool Disabled { get; set; }
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("otp")] public bool Otp { get; set; }
    [JsonPropertyName("password")] public string? Password { get; set; }
    [JsonPropertyName("permission")] public long Permission { get; set; }
    [JsonPropertyName("role")] public long Role { get; set; }
    [JsonPropertyName("sso_id")] public string? SsoId { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
}

