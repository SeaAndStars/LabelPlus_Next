namespace LabelPlus_Next.Services.Api;

/// <summary>
///     认证相关 API 抽象。
/// </summary>
public interface IAuthApi
{
    /// <summary>
    ///     登录接口。
    /// </summary>
    /// <param name="username">用户名。</param>
    /// <param name="password">密码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回包含业务码、消息与数据（令牌）的标准响应。</returns>
    Task<ApiResponse<LoginData>> LoginAsync(string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    ///     获取当前登录用户信息（已持有令牌）。
    /// </summary>
    Task<ApiResponse<MeData>> GetMeAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    ///     使用账号密码登录后再获取当前用户信息。
    /// </summary>
    Task<ApiResponse<MeData>> GetMeAsync(string username, string password, CancellationToken cancellationToken = default);
}
