using LabelPlus_Next.Services.Api;

namespace LabelPlus_Next.Test;

/// <summary>
///     �򵥵����ƻ��棺����ʹ������ Token����������õ�¼�ӿڻ�ȡ�����浽�ڴ棬�����β��ԻỰ���á�
/// </summary>
internal static class TokenCache
{
    private static string? _cachedToken;

    /// <summary>
    ///     ��ȡ���õ� Token����ȱʧ���Ե�¼��ȡ�����档
    /// </summary>
    /// <param name="baseUrl">����˻�����ַ��</param>
    /// <param name="tokenFromConfig">�����ļ��еĳ�ʼ Token����Ϊ�գ���</param>
    /// <param name="username">�û����������¼ʱʹ�ã���</param>
    /// <param name="password">���루�����¼ʱʹ�ã���</param>
    /// <returns>���ؿ��õ� Token�����޷���ȡ�򷵻� null��</returns>
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
