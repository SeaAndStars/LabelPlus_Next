using Newtonsoft.Json;

namespace LabelPlus_Next.Services.Api;

/// <summary>
///     通用 API 响应包装类型。
/// </summary>
/// <typeparam name="T">数据负载类型。</typeparam>
public sealed class ApiResponse<T>
{
    /// <summary>
    ///     业务状态码，200 表示成功。
    /// </summary>
    [JsonProperty("code")] public int Code { get; set; }

    /// <summary>
    ///     数据负载。
    /// </summary>
    [JsonProperty("data")] public T? Data { get; set; }

    /// <summary>
    ///     人类可读的提示消息。
    /// </summary>
    [JsonProperty("message")] public string? Message { get; set; }
}

/// <summary>
///     登录成功后返回的数据模型。
/// </summary>
public sealed class LoginData
{
    /// <summary>
    ///     访问令牌。
    /// </summary>
    [JsonProperty("token")] public string Token { get; set; } = string.Empty;
}

public sealed class MeData
{
    [JsonProperty("base_path")] public string? BasePath { get; set; }
    [JsonProperty("disabled")] public bool Disabled { get; set; }
    [JsonProperty("id")] public long Id { get; set; }
    [JsonProperty("otp")] public bool Otp { get; set; }
    [JsonProperty("password")] public string? Password { get; set; }
    [JsonProperty("permission")] public long Permission { get; set; }
    [JsonProperty("role")] public long Role { get; set; }
    [JsonProperty("sso_id")] public string? SsoId { get; set; }
    [JsonProperty("username")] public string? Username { get; set; }
}
