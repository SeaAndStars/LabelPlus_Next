using System;
using System.Collections.Generic;
using System.Net.Http;
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

    /// <summary>
    /// �������ݡ�
    /// </summary>
    /// <inheritdoc />
    public async Task<FsSearchResponse> SearchAsync(string token, string parent, string keywords, int scope = 0, int page = 1, int perPage = 1, string password = "", CancellationToken cancellationToken = default)
    {
        var request = new RestRequest("/api/fs/search", Method.Post)
            .AddHeader("Authorization", token)
            .AddHeader("Content-Type", "application/json")
            .AddJsonBody(new { parent, keywords, scope, page, per_page = perPage, password });

        var response = await _client.ExecuteAsync(request, cancellationToken);
        if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
        {
            return new FsSearchResponse { Code = (int)response.StatusCode, Message = response.ErrorMessage ?? response.StatusDescription ?? "Request failed" };
        }

        var parsed = JsonConvert.DeserializeObject<FsSearchResponse>(response.Content);
        return parsed ?? new FsSearchResponse { Code = -1, Message = "Deserialize failed" };
    }

    /// <summary>
    /// ����Ŀ¼��
    /// </summary>
    /// <inheritdoc />
    public async Task<ApiResponse<object>> MkdirAsync(string token, string path, CancellationToken cancellationToken = default)
    {
        var request = new RestRequest("/api/fs/mkdir", Method.Post)
            .AddHeader("Authorization", token)
            .AddHeader("Content-Type", "application/json")
            .AddJsonBody(new { path });

        var response = await _client.ExecuteAsync(request, cancellationToken);
        if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
        {
            return new ApiResponse<object> { Code = (int)response.StatusCode, Message = response.ErrorMessage ?? response.StatusDescription ?? "Request failed" };
        }

        var parsed = JsonConvert.DeserializeObject<ApiResponse<object>>(response.Content);
        return parsed ?? new ApiResponse<object> { Code = -1, Message = "Deserialize failed" };
    }

    /// <summary>
    /// ������Ŀ��
    /// </summary>
    /// <inheritdoc />
    public async Task<ApiResponse<object>> CopyAsync(string token, string srcDir, string dstDir, IEnumerable<string> names, CancellationToken cancellationToken = default)
    {
        var request = new RestRequest("/api/fs/copy", Method.Post)
            .AddHeader("Authorization", token)
            .AddHeader("Content-Type", "application/json")
            .AddJsonBody(new { src_dir = srcDir, dst_dir = dstDir, names });

        var response = await _client.ExecuteAsync(request, cancellationToken);
        if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
        {
            return new ApiResponse<object> { Code = (int)response.StatusCode, Message = response.ErrorMessage ?? response.StatusDescription ?? "Request failed" };
        }

        var parsed = JsonConvert.DeserializeObject<ApiResponse<object>>(response.Content);
        return parsed ?? new ApiResponse<object> { Code = -1, Message = "Deserialize failed" };
    }

    /// <summary>
    /// �ϴ��ļ��ֽ����ݡ�
    /// </summary>
    /// <inheritdoc />
    public async Task<FsPutResponse> PutAsync(string token, string filePath, byte[] content, bool asTask = false, CancellationToken cancellationToken = default)
    {
        var request = new RestRequest("/api/fs/put", Method.Put)
            .AddHeader("Authorization", token)
            .AddHeader("File-Path", filePath)
            .AddHeader("As-Task", asTask ? "true" : "false")
            .AddHeader("Content-Type", "application/octet-stream")
            .AddParameter("application/octet-stream", content, ParameterType.RequestBody);

        var response = await _client.ExecuteAsync(request, cancellationToken);
        if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
        {
            return new FsPutResponse { Code = (int)response.StatusCode, Message = response.ErrorMessage ?? response.StatusDescription ?? "Request failed" };
        }

        var parsed = JsonConvert.DeserializeObject<FsPutResponse>(response.Content);
        return parsed ?? new FsPutResponse { Code = -1, Message = "Deserialize failed" };
    }

    /// <summary>
    /// ��ȫ�ϴ���������ͬ���ļ�����ͨ�� raw_url ����Ϊ��ʱ��������ļ������ϴ��û��ļ������⸲�ǡ�
    /// </summary>
    public async Task<FsPutResponse> SafePutAsync(string token, string filePath, byte[] content, bool asTask = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var existed = await GetAsync(token, filePath, cancellationToken: cancellationToken);
            if (existed.Code == 200 && existed.Data is not null && !existed.Data.IsDir)
            {
                var raw = existed.Data.RawUrl;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    byte[] oldBytes;
                    using (var http = new HttpClient())
                    using (var req = new HttpRequestMessage(HttpMethod.Get, raw))
                    using (var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        resp.EnsureSuccessStatusCode();
                        oldBytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
                    }

                    var (dir, name) = SplitPath(filePath);
                    var ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                    var backupName = AppendTimestamp(name, ts);
                    var backupPath = CombinePath(dir, backupName);

                    // �ϴ�����
                    await PutAsync(token, backupPath, oldBytes, asTask, cancellationToken);
                }
            }
        }
        catch
        {
            // ���Ա��ݲ�����쳣�����������ϴ��û��ļ�
        }

        // �����ϴ��û��ļ�
        return await PutAsync(token, filePath, content, asTask, cancellationToken);
    }

    private static (string dir, string name) SplitPath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return ("/", "");
        var idx = filePath.LastIndexOf('/');
        if (idx < 0) return ("/", filePath);
        var dir = idx == 0 ? "/" : filePath.Substring(0, idx);
        var name = idx == filePath.Length - 1 ? string.Empty : filePath[(idx + 1)..];
        return (dir, name);
    }

    private static string AppendTimestamp(string name, string ts)
    {
        if (string.IsNullOrEmpty(name)) return ts;
        var dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1)
            return name + "_" + ts;
        var baseName = name.Substring(0, dot);
        var ext = name.Substring(dot);
        return baseName + "_" + ts + ext;
    }

    private static string CombinePath(string dir, string name)
    {
        if (string.IsNullOrEmpty(dir) || dir == "/") return "/" + name.TrimStart('/');
        return dir.TrimEnd('/') + "/" + name.TrimStart('/');
    }
}
