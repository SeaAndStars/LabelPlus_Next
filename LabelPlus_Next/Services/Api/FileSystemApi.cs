using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RestSharp;

namespace LabelPlus_Next.Services.Api;

/// <summary>
/// 文件系统 API 客户端实现。
/// </summary>
public sealed class FileSystemApi : IFileSystemApi
{
    /// <summary>
    /// RestSharp 客户端实例。
    /// </summary>
    private readonly RestClient _client;

    /// <summary>
    /// 使用基础地址创建客户端实例。
    /// </summary>
    /// <param name="baseUrl">服务端基础地址。</param>
    public FileSystemApi(string baseUrl)
    {
        _client = new RestClient(new RestClientOptions(baseUrl));
    }

    /// <summary>
    /// 列出指定路径内容。
    /// </summary>
    /// <inheritdoc />
    public async Task<FsListResponse> ListAsync(string token, string path, int page = 1, int perPage = 0, bool refresh = true, string password = "", CancellationToken cancellationToken = default)
    {
        var request = new RestRequest("/api/fs/list", Method.Post)
            .AddHeader("Authorization", token)
            .AddHeader("Content-Type", "application/json")
            .AddJsonBody(new { path, password, page, per_page = perPage, refresh });

        var response = await _client.ExecuteAsync(request, cancellationToken);
        if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
        {
            return new FsListResponse { Code = (int)response.StatusCode, Message = response.ErrorMessage ?? response.StatusDescription ?? "Request failed" };
        }

        var parsed = JsonConvert.DeserializeObject<FsListResponse>(response.Content);
        return parsed ?? new FsListResponse { Code = -1, Message = "Deserialize failed" };
    }

    /// <summary>
    /// 获取单项详情。
    /// </summary>
    /// <inheritdoc />
    public async Task<FsGetResponse> GetAsync(string token, string path, int page = 1, int perPage = 0, bool refresh = true, string password = "", CancellationToken cancellationToken = default)
    {
        var request = new RestRequest("/api/fs/get", Method.Post)
            .AddHeader("Authorization", token)
            .AddHeader("Content-Type", "application/json")
            .AddJsonBody(new { path, password, page, per_page = perPage, refresh });

        var response = await _client.ExecuteAsync(request, cancellationToken);
        if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
        {
            return new FsGetResponse { Code = (int)response.StatusCode, Message = response.ErrorMessage ?? response.StatusDescription ?? "Request failed" };
        }

        var parsed = JsonConvert.DeserializeObject<FsGetResponse>(response.Content);
        return parsed ?? new FsGetResponse { Code = -1, Message = "Deserialize failed" };
    }
}
