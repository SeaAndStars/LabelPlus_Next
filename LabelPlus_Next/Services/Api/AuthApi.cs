using Newtonsoft.Json;
using RestSharp;

namespace LabelPlus_Next.Services.Api;

/// <summary>
///     认证 API 客户端实现，基于 RestSharp 与 Newtonsoft.Json。
/// </summary>
public sealed class AuthApi : IAuthApi
{
    private readonly RestClient _client;
    private const string DefaultUserAgent = "pan.baidu.com";

    public AuthApi(string baseUrl)
    {
        _client = new RestClient(new RestClientOptions(baseUrl));
        _client.AddDefaultHeader("User-Agent", DefaultUserAgent);
    }

    public AuthApi(HttpClient httpClient, string baseUrl)
    {
        _client = new RestClient(httpClient, new RestClientOptions(baseUrl));
        _client.AddDefaultHeader("User-Agent", DefaultUserAgent);
    }

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

    public async Task<ApiResponse<MeData>> GetMeAsync(string token, CancellationToken cancellationToken = default)
    {
        var request = new RestRequest("/api/me", Method.Get)
            .AddHeader("Authorization", token);
        var response = await _client.ExecuteAsync(request, cancellationToken);
        if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
        {
            return new ApiResponse<MeData> { Code = (int)response.StatusCode, Message = response.ErrorMessage ?? response.StatusDescription ?? "Request failed" };
        }
        try
        {
            var result = JsonConvert.DeserializeObject<ApiResponse<MeData>>(response.Content);
            return result ?? new ApiResponse<MeData> { Code = -1, Message = "Deserialize failed" };
        }
        catch (Exception ex)
        {
            return new ApiResponse<MeData> { Code = -1, Message = ex.Message };
        }
    }

    public async Task<ApiResponse<MeData>> GetMeAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var login = await LoginAsync(username, password, cancellationToken);
        if (login.Code != 200 || login.Data is null || string.IsNullOrWhiteSpace(login.Data.Token))
        {
            return new ApiResponse<MeData> { Code = login.Code, Message = login.Message ?? "Login failed" };
        }
        return await GetMeAsync(login.Data.Token, cancellationToken);
    }
}
