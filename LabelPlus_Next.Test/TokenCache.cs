using System.Threading.Tasks;
using LabelPlus_Next.Services.Api;

namespace LabelPlus_Next.Test;

/// <summary>
/// 简单的令牌缓存：优先使用已有 Token；若无则调用登录接口获取并缓存到内存，供本次测试会话复用。
/// </summary>
internal static class TokenCache
{
    private static string? _cachedToken;

    /// <summary>
    /// 获取可用的 Token；若缺失则尝试登录获取并缓存。
    /// </summary>
    /// <param name="baseUrl">服务端基础地址。</param>
    /// <param name="tokenFromConfig">配置文件中的初始 Token（可为空）。</param>
    /// <param name="username">用户名（当需登录时使用）。</param>
    /// <param name="password">密码（当需登录时使用）。</param>
    /// <returns>返回可用的 Token，若无法获取则返回 null。</returns>
    public static async Task<string?> GetOrLoginAsync(string baseUrl, string? tokenFromConfig, string? username, string? password)
    {
        if (!string.IsNullOrWhiteSpace(_cachedToken))
            return _cachedToken;

        if (!string.IsNullOrWhiteSpace(tokenFromConfig))
        {
            _cachedToken = tokenFromConfig;
            return _cachedToken;
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return null;

        var auth = new AuthApi(baseUrl);
        var resp = await auth.LoginAsync(username, password);
        if (resp.Code == 200 && resp.Data is not null && !string.IsNullOrWhiteSpace(resp.Data.Token))
        {
            _cachedToken = resp.Data.Token;
            return _cachedToken;
        }

        return null;
    }
}
