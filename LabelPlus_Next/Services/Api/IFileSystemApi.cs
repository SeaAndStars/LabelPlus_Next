using System.Threading;
using System.Threading.Tasks;

namespace LabelPlus_Next.Services.Api;

/// <summary>
/// 文件系统相关 API 抽象。
/// </summary>
public interface IFileSystemApi
{
    /// <summary>
    /// 列出指定路径的文件与文件夹。
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
    /// 获取指定路径的单项详情（文件或目录）。
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
}
