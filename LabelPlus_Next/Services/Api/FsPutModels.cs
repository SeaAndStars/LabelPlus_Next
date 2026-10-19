using Newtonsoft.Json;

namespace LabelPlus_Next.Services.Api;

/// <summary>
///     /api/fs/put 响应。
/// </summary>
public sealed class FsPutResponse
{
    /// <summary>
    ///     业务状态码。
    /// </summary>
    [JsonProperty("code")] public int Code { get; set; }

    /// <summary>
    ///     数据负载：包含任务信息。
    /// </summary>
    [JsonProperty("data")] public FsPutData? Data { get; set; }

    /// <summary>
    ///     消息。
    /// </summary>
    [JsonProperty("message")] public string? Message { get; set; }
}

/// <summary>
///     /api/fs/put 数据对象。
/// </summary>
public sealed class FsPutData
{
    /// <summary>
    ///     任务信息。
    /// </summary>
    [JsonProperty("task")] public FsTask? Task { get; set; }
}

/// <summary>
///     任务信息通用结构。
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
