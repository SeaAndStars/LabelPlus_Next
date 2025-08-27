using Newtonsoft.Json;

namespace LabelPlus_Next.Services.Api;

/// <summary>
/// ͨ�� API ��Ӧ��װ���͡�
/// </summary>
/// <typeparam name="T">���ݸ������͡�</typeparam>
public sealed class ApiResponse<T>
{
    /// <summary>
    /// ҵ��״̬�룬200 ��ʾ�ɹ���
    /// </summary>
    [JsonProperty("code")] public int Code { get; set; }

    /// <summary>
    /// ���ݸ��ء�
    /// </summary>
    [JsonProperty("data")] public T? Data { get; set; }

    /// <summary>
    /// ����ɶ�����ʾ��Ϣ��
    /// </summary>
    [JsonProperty("message")] public string? Message { get; set; }
}

/// <summary>
/// ��¼�ɹ��󷵻ص�����ģ�͡�
/// </summary>
public sealed class LoginData
{
    /// <summary>
    /// �������ơ�
    /// </summary>
    [JsonProperty("token")] public string Token { get; set; } = string.Empty;
}
