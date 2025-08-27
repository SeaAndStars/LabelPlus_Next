using Newtonsoft.Json;

namespace LabelPlus_Next.Services.Api;

/// <summary>
/// /api/fs/search ��Ӧ��
/// </summary>
public sealed class FsSearchResponse
{
    /// <summary>
    /// ҵ��״̬�롣
    /// </summary>
    [JsonProperty("code")] public int Code { get; set; }

    /// <summary>
    /// ���ݸ��ء�
    /// </summary>
    [JsonProperty("data")] public FsSearchData? Data { get; set; }

    /// <summary>
    /// ��Ϣ��
    /// </summary>
    [JsonProperty("message")] public string? Message { get; set; }
}

/// <summary>
/// �������ݶ���
/// </summary>
public sealed class FsSearchData
{
    /// <summary>
    /// ��������б�
    /// </summary>
    [JsonProperty("content")] public FsSearchItem[]? Content { get; set; }

    /// <summary>
    /// ���������
    /// </summary>
    [JsonProperty("total")] public long Total { get; set; }
}

/// <summary>
/// ������������
/// </summary>
public sealed class FsSearchItem
{
    /// <summary>
    /// �Ƿ�ΪĿ¼��
    /// </summary>
    [JsonProperty("is_dir")] public bool IsDir { get; set; }

    /// <summary>
    /// ���ơ�
    /// </summary>
    [JsonProperty("name")] public string? Name { get; set; }

    /// <summary>
    /// ��·����
    /// </summary>
    [JsonProperty("parent")] public string? Parent { get; set; }

    /// <summary>
    /// ��С��
    /// </summary>
    [JsonProperty("size")] public long Size { get; set; }

    /// <summary>
    /// ���͡�
    /// </summary>
    [JsonProperty("type")] public long Type { get; set; }
}
