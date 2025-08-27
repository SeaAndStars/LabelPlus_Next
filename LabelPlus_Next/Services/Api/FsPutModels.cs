using Newtonsoft.Json;

namespace LabelPlus_Next.Services.Api;

/// <summary>
/// /api/fs/put ��Ӧ��
/// </summary>
public sealed class FsPutResponse
{
    /// <summary>
    /// ҵ��״̬�롣
    /// </summary>
    [JsonProperty("code")] public int Code { get; set; }

    /// <summary>
    /// ���ݸ��أ�����������Ϣ��
    /// </summary>
    [JsonProperty("data")] public FsPutData? Data { get; set; }

    /// <summary>
    /// ��Ϣ��
    /// </summary>
    [JsonProperty("message")] public string? Message { get; set; }
}

/// <summary>
/// /api/fs/put ���ݶ���
/// </summary>
public sealed class FsPutData
{
    /// <summary>
    /// ������Ϣ��
    /// </summary>
    [JsonProperty("task")] public FsTask? Task { get; set; }
}

/// <summary>
/// ������Ϣͨ�ýṹ��
/// </summary>
public sealed class FsTask
{
    [JsonProperty("error")] public string? Error { get; set; }
    [JsonProperty("id")] public string? Id { get; set; }
    [JsonProperty("name")] public string? Name { get; set; }
    [JsonProperty("progress")] public long Progress { get; set; }
    [JsonProperty("state")] public long State { get; set; }
    [JsonProperty("status")] public string? Status { get; set; }
}
