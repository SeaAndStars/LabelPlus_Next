using Newtonsoft.Json;

namespace LabelPlus_Next.Services.Api;

/// <summary>
/// /api/fs/search 响应。
/// </summary>
public sealed class FsSearchResponse
{
    /// <summary>
    /// 业务状态码。
    /// </summary>
    [JsonProperty("code")] public int Code { get; set; }

    /// <summary>
    /// 数据负载。
    /// </summary>
    [JsonProperty("data")] public FsSearchData? Data { get; set; }

    /// <summary>
    /// 消息。
    /// </summary>
    [JsonProperty("message")] public string? Message { get; set; }
}

/// <summary>
/// 搜索数据对象。
/// </summary>
public sealed class FsSearchData
{
    /// <summary>
    /// 搜索结果列表。
    /// </summary>
    [JsonProperty("content")] public FsSearchItem[]? Content { get; set; }

    /// <summary>
    /// 结果总数。
    /// </summary>
    [JsonProperty("total")] public long Total { get; set; }
}

/// <summary>
/// 单个搜索结果项。
/// </summary>
public sealed class FsSearchItem
{
    /// <summary>
    /// 是否为目录。
    /// </summary>
    [JsonProperty("is_dir")] public bool IsDir { get; set; }

    /// <summary>
    /// 名称。
    /// </summary>
    [JsonProperty("name")] public string? Name { get; set; }

    /// <summary>
    /// 父路径。
    /// </summary>
    [JsonProperty("parent")] public string? Parent { get; set; }

    /// <summary>
    /// 大小。
    /// </summary>
    [JsonProperty("size")] public long Size { get; set; }

    /// <summary>
    /// 类型。
    /// </summary>
    [JsonProperty("type")] public long Type { get; set; }
}
