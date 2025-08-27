using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RestSharp;

namespace LabelPlus_Next.Services.Api;

/// <summary>
/// ��֤ API �ͻ���ʵ�֣����� RestSharp �� Newtonsoft.Json��
/// </summary>
public sealed class AuthApi : IAuthApi
{
    /// <summary>
    /// RestSharp �ͻ���ʵ����
    /// </summary>
    private readonly RestClient _client;

    /// <summary>
    /// ʹ�û�����ַ�����ͻ���ʵ����
    /// </summary>
    /// <param name="baseUrl">����˻�����ַ��</param>
    public AuthApi(string baseUrl)
    {
        _client = new RestClient(new RestClientOptions(baseUrl));
    }

    /// <summary>
    /// ʹ���ⲿ HttpClient �������ַ�����ͻ���ʵ�������ڲ��Ի������ӣ���
    /// </summary>
    /// <param name="httpClient">�ⲿ HttpClient��</param>
    /// <param name="baseUrl">����˻�����ַ��</param>
    public AuthApi(HttpClient httpClient, string baseUrl)
    {
        _client = new RestClient(httpClient, new RestClientOptions(baseUrl));
    }

    /// <inheritdoc />
    public async Task<ApiResponse<LoginData>> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var requestBody = JsonConvert.SerializeObject(new { username, password });
        var request = new RestRequest("/api/auth/login", Method.Post)
            .AddHeader("Content-Type", "application/json")
            .AddStringBody(requestBody, DataFormat.Json);

        var response = await _client.ExecuteAsync(request, cancellationToken);
        if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
        {
            return new ApiResponse<LoginData> { Code = (int)response.StatusCode, Message = response.ErrorMessage ?? response.StatusDescription ?? "Request failed" };
        }

        var result = JsonConvert.DeserializeObject<ApiResponse<LoginData>>(response.Content);
        return result ?? new ApiResponse<LoginData> { Code = -1, Message = "Deserialize failed" };
    }
}
