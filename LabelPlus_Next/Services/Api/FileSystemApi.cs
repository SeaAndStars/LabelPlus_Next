using System;
using System.Collections.Generic;
using System.Net.Http;
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

    /// <summary>
    /// 搜索内容。
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
    /// 创建目录。
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
    /// 复制条目。
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
    /// 上传文件字节内容。
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
    /// 安全上传：若已有同名文件，先通过 raw_url 备份为加时间戳的新文件，再上传用户文件，避免覆盖。
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

                    // 上传备份
                    await PutAsync(token, backupPath, oldBytes, asTask, cancellationToken);
                }
            }
        }
        catch
        {
            // 忽略备份步骤的异常，继续尝试上传用户文件
        }

        // 最终上传用户文件
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
