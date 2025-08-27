using Newtonsoft.Json;

namespace LabelPlus_Next.Services.Api;

/// <summary>
/// /api/fs/get 接口响应。
/// </summary>
public sealed class FsGetResponse
{
    /// <summary>
    /// 业务状态码，200 表示成功。
    /// </summary>
    [JsonProperty("code")] public int Code { get; set; }

    /// <summary>
    /// 数据负载。
    /// </summary>
    [JsonProperty("data")] public FsGetData? Data { get; set; }

    /// <summary>
    /// 人类可读的消息。
    /// </summary>
    [JsonProperty("message")] public string? Message { get; set; }
}

/// <summary>
/// /api/fs/get 数据对象。
/// </summary>
public sealed class FsGetData
{
    [JsonProperty("created")] public string? Created { get; set; }
    [JsonProperty("hash_info")] public object? HashInfo { get; set; }
    [JsonProperty("hashinfo")] public string? Hashinfo { get; set; }
    [JsonProperty("header")] public string? Header { get; set; }
    [JsonProperty("is_dir")] public bool IsDir { get; set; }
    [JsonProperty("modified")] public string? Modified { get; set; }
    [JsonProperty("name")] public string? Name { get; set; }
    [JsonProperty("provider")] public string? Provider { get; set; }
    [JsonProperty("raw_url")] public string? RawUrl { get; set; }
    [JsonProperty("readme")] public string? Readme { get; set; }
    [JsonProperty("related")] public object? Related { get; set; }
    [JsonProperty("sign")] public string? Sign { get; set; }
    [JsonProperty("size")] public long Size { get; set; }
    [JsonProperty("thumb")] public string? Thumb { get; set; }
    [JsonProperty("type")] public long Type { get; set; }
}
