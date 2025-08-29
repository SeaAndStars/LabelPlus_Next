using Newtonsoft.Json;

namespace LabelPlus_Next.Services.Api;

/// <summary>
///     /api/fs/list 接口的响应根对象。
/// </summary>
public sealed class FsListResponse
{
    /// <summary>
    ///     业务状态码，200 表示成功。
    /// </summary>
    [JsonProperty("code")] public int Code { get; set; }

    /// <summary>
    ///     数据负载。
    /// </summary>
    [JsonProperty("data")] public FsListData? Data { get; set; }

    /// <summary>
    ///     人类可读的消息。
    /// </summary>
    [JsonProperty("message")] public string? Message { get; set; }
}

/// <summary>
///     列表数据对象。
/// </summary>
public sealed class FsListData
{
    /// <summary>
    ///     文件/文件夹内容列表。
    /// </summary>
    [JsonProperty("content")] public FsItem[]? Content { get; set; }

    /// <summary>
    ///     额外响应头信息（如果有）。
    /// </summary>
    [JsonProperty("header")] public string? Header { get; set; }

    /// <summary>
    ///     提供方。
    /// </summary>
    [JsonProperty("provider")] public string? Provider { get; set; }

    /// <summary>
    ///     目录说明（README）。
    /// </summary>
    [JsonProperty("readme")] public string? Readme { get; set; }

    /// <summary>
    ///     内容总数。
    /// </summary>
    [JsonProperty("total")] public long Total { get; set; }

    /// <summary>
    ///     当前令牌是否具有写入权限。
    /// </summary>
    [JsonProperty("write")] public bool Write { get; set; }
}

/// <summary>
///     单个文件/文件夹条目。
/// </summary>
public sealed class FsItem
{
    /// <summary>
    ///     创建时间（字符串格式）。
    /// </summary>
    [JsonProperty("created", NullValueHandling = NullValueHandling.Ignore)] public string? Created { get; set; }

    /// <summary>
    ///     哈希信息（可能为对象或缺省）。
    /// </summary>
    [JsonProperty("hash_info")] public object? HashInfo { get; set; }

    /// <summary>
    ///     兼容字段：hashinfo。
    /// </summary>
    [JsonProperty("hashinfo", NullValueHandling = NullValueHandling.Ignore)] public string? Hashinfo { get; set; }

    /// <summary>
    ///     是否为目录。
    /// </summary>
    [JsonProperty("is_dir")] public bool IsDir { get; set; }

    /// <summary>
    ///     最后修改时间（字符串格式）。
    /// </summary>
    [JsonProperty("modified")] public string? Modified { get; set; }

    /// <summary>
    ///     名称。
    /// </summary>
    [JsonProperty("name")] public string? Name { get; set; }

    /// <summary>
    ///     签名。
    /// </summary>
    [JsonProperty("sign")] public string? Sign { get; set; }

    /// <summary>
    ///     大小（字节）。
    /// </summary>
    [JsonProperty("size")] public long Size { get; set; }

    /// <summary>
    ///     缩略图地址。
    /// </summary>
    [JsonProperty("thumb")] public string? Thumb { get; set; }

    /// <summary>
    ///     类型（由服务端定义的数值）。
    /// </summary>
    [JsonProperty("type")] public long Type { get; set; }
}

/// <summary>
///     批量上传条目。
/// </summary>
public sealed class FileUploadItem
{
    /// <summary>
    ///     目标文件绝对路径。
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    ///     文件字节内容。
    /// </summary>
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

/// <summary>
///     下载结果。
/// </summary>
public sealed class DownloadResult
{
    /// <summary>
    ///     业务状态码（200 代表成功，或 401 未授权）。
    /// </summary>
    public int Code { get; set; }

    /// <summary>
    ///     文件内容（成功时）。
    /// </summary>
    public byte[]? Content { get; set; }

    /// <summary>
    ///     错误消息（失败时）。
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
///     统一上传请求：支持 单文件、多文件、目录 三种模式。
/// </summary>
public sealed class UploadRequest
{

    private UploadRequest() { }
    public UploadMode Mode { get; private set; }

    // Single file
    public string? RemotePath { get; private set; }
    public byte[]? Content { get; private set; }

    // Multiple files
    public IReadOnlyList<FileUploadItem>? Items { get; private set; }

    // Directory
    public string? LocalDirectory { get; private set; }
    public string? RemoteBasePath { get; private set; }

    public static UploadRequest FromFile(string remotePath, byte[] content)
        => new()
            { Mode = UploadMode.Single, RemotePath = remotePath, Content = content };

    public static UploadRequest FromFiles(IEnumerable<FileUploadItem> items)
        => new()
            { Mode = UploadMode.Multiple, Items = new List<FileUploadItem>(items ?? Array.Empty<FileUploadItem>()) };

    public static UploadRequest FromDirectory(string localDirectory, string remoteBasePath)
        => new()
            { Mode = UploadMode.Directory, LocalDirectory = localDirectory, RemoteBasePath = remoteBasePath };
}

public enum UploadMode
{
    Single,
    Multiple,
    Directory
}

/// <summary>
///     上传进度数据。
/// </summary>
public sealed class UploadProgress
{
    public int Completed { get; set; }
    public int Total { get; set; }
    public string? CurrentRemotePath { get; set; }
    // Bytes uploaded for the current file
    public long BytesSent { get; set; }
    public long? BytesTotal { get; set; }
    // Current speed in MB/s (may be null if not measurable)
    public double? SpeedMBps { get; set; }
}
