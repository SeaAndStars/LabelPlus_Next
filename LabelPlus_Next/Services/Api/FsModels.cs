using Newtonsoft.Json;

namespace LabelPlus_Next.Services.Api;

/// <summary>
/// /api/fs/list �ӿڵ���Ӧ������
/// </summary>
public sealed class FsListResponse
{
    /// <summary>
    /// ҵ��״̬�룬200 ��ʾ�ɹ���
    /// </summary>
    [JsonProperty("code")] public int Code { get; set; }

    /// <summary>
    /// ���ݸ��ء�
    /// </summary>
    [JsonProperty("data")] public FsListData? Data { get; set; }

    /// <summary>
    /// ����ɶ�����Ϣ��
    /// </summary>
    [JsonProperty("message")] public string? Message { get; set; }
}

/// <summary>
/// �б����ݶ���
/// </summary>
public sealed class FsListData
{
    /// <summary>
    /// �ļ�/�ļ��������б�
    /// </summary>
    [JsonProperty("content")] public FsItem[]? Content { get; set; }

    /// <summary>
    /// ������Ӧͷ��Ϣ������У���
    /// </summary>
    [JsonProperty("header")] public string? Header { get; set; }

    /// <summary>
    /// �ṩ����
    /// </summary>
    [JsonProperty("provider")] public string? Provider { get; set; }

    /// <summary>
    /// Ŀ¼˵����README����
    /// </summary>
    [JsonProperty("readme")] public string? Readme { get; set; }

    /// <summary>
    /// ����������
    /// </summary>
    [JsonProperty("total")] public long Total { get; set; }

    /// <summary>
    /// ��ǰ�����Ƿ����д��Ȩ�ޡ�
    /// </summary>
    [JsonProperty("write")] public bool Write { get; set; }
}

/// <summary>
/// �����ļ�/�ļ�����Ŀ��
/// </summary>
public sealed class FsItem
{
    /// <summary>
    /// ����ʱ�䣨�ַ�����ʽ����
    /// </summary>
    [JsonProperty("created", NullValueHandling = NullValueHandling.Ignore)] public string? Created { get; set; }

    /// <summary>
    /// ��ϣ��Ϣ������Ϊ�����ȱʡ����
    /// </summary>
    [JsonProperty("hash_info")] public object? HashInfo { get; set; }

    /// <summary>
    /// �����ֶΣ�hashinfo��
    /// </summary>
    [JsonProperty("hashinfo", NullValueHandling = NullValueHandling.Ignore)] public string? Hashinfo { get; set; }

    /// <summary>
    /// �Ƿ�ΪĿ¼��
    /// </summary>
    [JsonProperty("is_dir")] public bool IsDir { get; set; }

    /// <summary>
    /// ����޸�ʱ�䣨�ַ�����ʽ����
    /// </summary>
    [JsonProperty("modified")] public string? Modified { get; set; }

    /// <summary>
    /// ���ơ�
    /// </summary>
    [JsonProperty("name")] public string? Name { get; set; }

    /// <summary>
    /// ǩ����
    /// </summary>
    [JsonProperty("sign")] public string? Sign { get; set; }

    /// <summary>
    /// ��С���ֽڣ���
    /// </summary>
    [JsonProperty("size")] public long Size { get; set; }

    /// <summary>
    /// ����ͼ��ַ��
    /// </summary>
    [JsonProperty("thumb")] public string? Thumb { get; set; }

    /// <summary>
    /// ���ͣ��ɷ���˶������ֵ����
    /// </summary>
    [JsonProperty("type")] public long Type { get; set; }
}
