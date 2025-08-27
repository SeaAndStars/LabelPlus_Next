using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RestSharp;

namespace LabelPlus_Next.Services.Api;

/// <summary>
/// �ļ�ϵͳ API �ͻ���ʵ�֡�
/// </summary>
public sealed class FileSystemApi : IFileSystemApi
{
    /// <summary>
    /// RestSharp �ͻ���ʵ����
    /// </summary>
    private readonly RestClient _client;

    /// <summary>
    /// ʹ�û�����ַ�����ͻ���ʵ����
    /// </summary>
    /// <param name="baseUrl">����˻�����ַ��</param>
    public FileSystemApi(string baseUrl)
    {
        _client = new RestClient(new RestClientOptions(baseUrl));
    }

    /// <summary>
    /// �г�ָ��·�����ݡ�
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
    /// ��ȡ�������顣
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
