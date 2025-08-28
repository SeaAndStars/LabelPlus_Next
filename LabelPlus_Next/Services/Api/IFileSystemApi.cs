using Downloader;

namespace LabelPlus_Next.Services.Api;

/// <summary>
///     文件系统相关 API 抽象。
/// </summary>
public interface IFileSystemApi
{
    /// <summary>
    ///     列出指定路径的文件与文件夹。
    /// </summary>
    /// <param name="token">授权令牌（例如 Bearer xxxxxx 或服务端要求的格式）。</param>
    /// <param name="path">要列出的路径，例如 /、/test。</param>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="perPage">每页大小，0 表示由服务端决定或不分页。</param>
    /// <param name="refresh">是否刷新缓存。</param>
    /// <param name="password">目录访问密码（如需要）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回列表结果与元数据。</returns>
    Task<FsListResponse> ListAsync(
        string token,
        string path,
        int page = 1,
        int perPage = 0,
        bool refresh = true,
        string password = "",
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     获取指定路径的单项详情（文件或目录）。
    /// </summary>
    /// <param name="token">授权令牌。</param>
    /// <param name="path">目标路径，例如 /test/a.txt。</param>
    /// <param name="page">页码（部分实现保留该字段）。</param>
    /// <param name="perPage">每页大小（部分实现保留该字段）。</param>
    /// <param name="refresh">是否刷新缓存。</param>
    /// <param name="password">访问密码（如需要）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回该项的详细信息。</returns>
    Task<FsGetResponse> GetAsync(
        string token,
        string path,
        int page = 1,
        int perPage = 0,
        bool refresh = true,
        string password = "",
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     搜索指定父目录下的内容。
    /// </summary>
    /// <param name="token">授权令牌。</param>
    /// <param name="parent">父目录路径，例如 /local。</param>
    /// <param name="keywords">关键字。</param>
    /// <param name="scope">搜索范围（由服务端定义，0 为默认）。</param>
    /// <param name="page">页码。</param>
    /// <param name="perPage">每页条目数量。</param>
    /// <param name="password">访问密码（如需要）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回搜索结果。</returns>
    Task<FsSearchResponse> SearchAsync(
        string token,
        string parent,
        string keywords,
        int scope = 0,
        int page = 1,
        int perPage = 1,
        string password = "",
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     创建目录。
    /// </summary>
    /// <param name="token">授权令牌。</param>
    /// <param name="path">要创建的目标目录绝对路径，例如 /tt。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回标准响应，code=200 表示成功。</returns>
    Task<ApiResponse<object>> MkdirAsync(
        string token,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     复制条目：从 srcDir 复制 names 指定的文件/目录到 dstDir。
    /// </summary>
    /// <param name="token">授权令牌。</param>
    /// <param name="srcDir">源目录绝对路径，例如 /test/a。</param>
    /// <param name="dstDir">目标目录绝对路径，例如 /test/b。</param>
    /// <param name="names">要复制的条目名称集合（相对于 srcDir）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回标准响应。</returns>
    Task<ApiResponse<object>> CopyAsync(
        string token,
        string srcDir,
        string dstDir,
        IEnumerable<string> names,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     上传文件到指定路径（PUT /api/fs/put）。默认不以任务上传，便于上层并发控制。
    /// </summary>
    /// <param name="token">授权令牌。</param>
    /// <param name="filePath">目标文件绝对路径（File-Path 头）。</param>
    /// <param name="content">文件字节内容。</param>
    /// <param name="asTask">是否以任务形式上传（As-Task 头）。默认 false。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回任务信息或结果描述。</returns>
    Task<FsPutResponse> PutAsync(
        string token,
        string filePath,
        byte[] content,
        bool asTask = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     安全上传：若远端已存在同名文件，则先通过 raw_url 备份为“_yyyyMMddHHmmss”后缀的新文件，再上传用户文件，避免覆盖。
    /// </summary>
    /// <param name="token">授权令牌。</param>
    /// <param name="filePath">目标文件绝对路径。</param>
    /// <param name="content">用户文件内容。</param>
    /// <param name="asTask">是否以任务形式上传（默认 false）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回最终上传（用户文件）的响应。</returns>
    Task<FsPutResponse> SafePutAsync(
        string token,
        string filePath,
        byte[] content,
        bool asTask = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     并发上传多个文件，受 maxConcurrency 控制。
    /// </summary>
    /// <param name="token">授权令牌。</param>
    /// <param name="items">要上传的文件集合。</param>
    /// <param name="maxConcurrency">最大并发度（默认 4）。</param>
    /// <param name="asTask">是否以任务上传（默认 false）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按输入顺序返回各上传结果。</returns>
    Task<IReadOnlyList<FsPutResponse>> PutManyAsync(
        string token,
        IEnumerable<FileUploadItem> items,
        int maxConcurrency = 4,
        bool asTask = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     下载单个文件（先 Get 拿 raw_url，再 HTTP 下载）。
    /// </summary>
    /// <param name="token">授权令牌。</param>
    /// <param name="filePath">文件绝对路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>下载结果，包含状态码与内容。</returns>
    Task<DownloadResult> DownloadAsync(
        string token,
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     并发下载多个文件，受 maxConcurrency 控制。
    /// </summary>
    /// <param name="token">授权令牌。</param>
    /// <param name="filePaths">文件路径集合。</param>
    /// <param name="maxConcurrency">最大并发度（默认 4）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按输入顺序返回各下载结果。</returns>
    Task<IReadOnlyList<DownloadResult>> DownloadManyAsync(
        string token,
        IEnumerable<string> filePaths,
        int maxConcurrency = 4,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     使用 Downloader 支持多线程与断点续传下载单个文件到本地路径。
    /// </summary>
    /// <param name="token">授权令牌。</param>
    /// <param name="filePath">远端文件路径（用于 Get 获取 raw_url）。</param>
    /// <param name="localPath">本地保存完整文件路径。</param>
    /// <param name="config">Downloader 的下载配置（可选），未提供则使用默认推荐配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<DownloadResult> DownloadToFileAsync(
        string token,
        string filePath,
        string localPath,
        DownloadConfiguration? config = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     使用 Downloader 并发下载多个文件（带断点续传），受 maxConcurrency 控制。
    /// </summary>
    Task<IReadOnlyList<DownloadResult>> DownloadManyToFilesAsync(
        string token,
        IEnumerable<(string remotePath, string localPath)> items,
        int maxConcurrency = 4,
        DownloadConfiguration? config = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     统一的安全上传入口，支持单文件、多文件及目录上传。
    /// </summary>
    /// <param name="token">授权令牌。</param>
    /// <param name="request">上传请求。</param>
    /// <param name="maxConcurrency">最大并发度（默认 4）。</param>
    /// <param name="asTask">是否以任务上传（默认 false）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按输入顺序返回各上传结果。</returns>
    Task<IReadOnlyList<FsPutResponse>> SafeUploadAsync(
        string token,
        UploadRequest request,
        int maxConcurrency = 4,
        bool asTask = false,
        CancellationToken cancellationToken = default);
}
