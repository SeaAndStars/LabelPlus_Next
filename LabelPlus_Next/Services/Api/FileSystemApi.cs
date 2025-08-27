using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Downloader;
using Newtonsoft.Json;
using RestSharp;

namespace LabelPlus_Next.Services.Api;

/// <summary>
/// 文件系统 API 客户端实现。
/// </summary>
public sealed class FileSystemApi : IFileSystemApi
{
    private readonly RestClient _client;
    private readonly string _baseUrl;

    public FileSystemApi(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _client = new RestClient(new RestClientOptions(_baseUrl));
    }

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

    // Raw upload without safety checks
    private async Task<FsPutResponse> PutRawAsync(string token, string filePath, byte[] content, bool asTask, CancellationToken cancellationToken)
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

    // Public upload now defaults to safe behavior
    public Task<FsPutResponse> PutAsync(string token, string filePath, byte[] content, bool asTask = false, CancellationToken cancellationToken = default)
        => SafePutAsync(token, filePath, content, asTask, cancellationToken);

    public async Task<FsPutResponse> SafePutAsync(string token, string filePath, byte[] content, bool asTask = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var existed = await GetAsync(token, filePath, cancellationToken: cancellationToken);
            if (existed.Code == 200 && existed.Data is not null && !existed.Data.IsDir)
            {
                var raw = ResolveRawUrl(existed.Data);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    byte[] oldBytes;
                    using (var http = new HttpClient())
                    {
                        // 尝试无授权访问签名链接，失败后带授权再试
                        using var req1 = new HttpRequestMessage(HttpMethod.Get, raw);
                        using var resp1 = await http.SendAsync(req1, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        if (resp1.IsSuccessStatusCode)
                        {
                            oldBytes = await resp1.Content.ReadAsByteArrayAsync(cancellationToken);
                        }
                        else
                        {
                            using var req2 = new HttpRequestMessage(HttpMethod.Get, raw);
                            req2.Headers.TryAddWithoutValidation("Authorization", token);
                            using var resp2 = await http.SendAsync(req2, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                            resp2.EnsureSuccessStatusCode();
                            oldBytes = await resp2.Content.ReadAsByteArrayAsync(cancellationToken);
                        }
                    }

                    var (dir, name) = SplitPath(filePath);
                    var ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                    var backupName = AppendTimestamp(name, ts);
                    var backupPath = CombinePath(dir, backupName);

                    // 上传备份
                    await PutRawAsync(token, backupPath, oldBytes, asTask, cancellationToken);
                }
            }
        }
        catch
        {
        }

        // 最终上传用户文件
        return await PutRawAsync(token, filePath, content, asTask, cancellationToken);
    }

    public async Task<IReadOnlyList<FsPutResponse>> PutManyAsync(string token, IEnumerable<FileUploadItem> items, int maxConcurrency = 4, bool asTask = false, CancellationToken cancellationToken = default)
    {
        var list = items?.ToList() ?? new List<FileUploadItem>();
        var results = new FsPutResponse[list.Count];
        using var sem = new SemaphoreSlim(Math.Max(1, maxConcurrency));
        var tasks = list.Select(async (item, idx) =>
        {
            await sem.WaitAsync(cancellationToken);
            try
            {
                results[idx] = await PutAsync(token, item.FilePath, item.Content, asTask, cancellationToken);
            }
            finally { sem.Release(); }
        });
        await Task.WhenAll(tasks);
        return results;
    }

    public async Task<DownloadResult> DownloadAsync(string token, string filePath, CancellationToken cancellationToken = default)
    {
        // 容错重试：处理上传后短暂不可见
        FsGetResponse meta = new() { Code = -1, Message = "init" };
        const int maxAttempts = 10;
        int attempt = 0;
        while (attempt++ < maxAttempts)
        {
            meta = await GetAsync(token, filePath, cancellationToken: cancellationToken);
            if (meta.Code == 200 && meta.Data is not null && !meta.Data.IsDir) break;

            var msg = meta.Message ?? string.Empty;
            bool retryable = meta.Code == -1 || meta.Code == 404 || meta.Code >= 500 || msg.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!retryable) break;

            var delayMs = Math.Min(1000, 100 * (1 << Math.Min(6, attempt)));
            try { await Task.Delay(delayMs, cancellationToken); } catch { }
        }
        if (meta.Code != 200 || meta.Data is null || meta.Data.IsDir)
            return new DownloadResult { Code = meta.Code, Message = meta.Message };

        var url = ResolveRawUrl(meta.Data);
        if (string.IsNullOrWhiteSpace(url))
            return new DownloadResult { Code = -1, Message = "raw_url not available" };

        try
        {
            using var http = new HttpClient();
            DownloadResult? lastError = null;

            using (var req1 = new HttpRequestMessage(HttpMethod.Get, url))
            using (var resp1 = await http.SendAsync(req1, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                if (resp1.IsSuccessStatusCode)
                {
                    var bytesOk = await resp1.Content.ReadAsByteArrayAsync(cancellationToken);
                    return new DownloadResult { Code = 200, Content = bytesOk };
                }
                lastError = new DownloadResult { Code = (int)resp1.StatusCode, Message = resp1.ReasonPhrase ?? "Request failed" };
            }

            using (var req2 = new HttpRequestMessage(HttpMethod.Get, url))
            {
                req2.Headers.TryAddWithoutValidation("Authorization", token);
                using var resp2 = await http.SendAsync(req2, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (resp2.IsSuccessStatusCode)
                {
                    var bytesOk2 = await resp2.Content.ReadAsByteArrayAsync(cancellationToken);
                    return new DownloadResult { Code = 200, Content = bytesOk2 };
                }
                return new DownloadResult { Code = (int)resp2.StatusCode, Message = resp2.ReasonPhrase ?? lastError?.Message ?? "Request failed" };
            }
        }
        catch (HttpRequestException hex)
        {
            var code = (int?)(hex.StatusCode) ?? -1;
            if (code == 401 || code == 403)
                return new DownloadResult { Code = code, Message = hex.Message };
            return new DownloadResult { Code = -1, Message = hex.Message };
        }
        catch (Exception ex)
        {
            return new DownloadResult { Code = -1, Message = ex.Message };
        }
    }

    public async Task<IReadOnlyList<DownloadResult>> DownloadManyAsync(string token, IEnumerable<string> filePaths, int maxConcurrency = 4, CancellationToken cancellationToken = default)
    {
        var list = filePaths?.ToList() ?? new List<string>();
        var results = new DownloadResult[list.Count];
        using var sem = new SemaphoreSlim(Math.Max(1, maxConcurrency));
        var tasks = list.Select(async (file, idx) =>
        {
            await sem.WaitAsync(cancellationToken);
            try
            {
                results[idx] = await DownloadAsync(token, file, cancellationToken);
            }
            finally { sem.Release(); }
        });
        await Task.WhenAll(tasks);
        return results;
    }

    public async Task<DownloadResult> DownloadToFileAsync(string token, string filePath, string localPath, DownloadConfiguration? config = null, CancellationToken cancellationToken = default)
    {
        // 容错重试：处理上传后短暂不可见
        FsGetResponse meta = new() { Code = -1, Message = "init" };
        const int maxAttempts = 10;
        int attempt = 0;
        while (attempt++ < maxAttempts)
        {
            meta = await GetAsync(token, filePath, cancellationToken: cancellationToken);
            if (meta.Code == 200 && meta.Data is not null && !meta.Data.IsDir) break;

            var msg = meta.Message ?? string.Empty;
            bool retryable = meta.Code == -1 || meta.Code == 404 || meta.Code >= 500 || msg.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!retryable) break;

            var delayMs = Math.Min(1000, 100 * (1 << Math.Min(6, attempt)));
            try { await Task.Delay(delayMs, cancellationToken); } catch { }
        }
        if (meta.Code != 200 || meta.Data is null || meta.Data.IsDir)
            return new DownloadResult { Code = meta.Code, Message = meta.Message };

        var url = ResolveRawUrl(meta.Data);
        if (string.IsNullOrWhiteSpace(url))
            return new DownloadResult { Code = -1, Message = "raw_url not available" };

        try
        {
            var cfg = BuildDownloadConfig(config, needAuth: false, token: token);
            var service = new DownloadService(cfg);
            await service.DownloadFileTaskAsync(url, localPath);
            return new DownloadResult { Code = 200 };
        }
        catch (Exception ex)
        {
            return new DownloadResult { Code = -1, Message = ex.Message };
        }
    }

    public async Task<IReadOnlyList<DownloadResult>> DownloadManyToFilesAsync(string token, IEnumerable<(string remotePath, string localPath)> items, int maxConcurrency = 4, DownloadConfiguration? config = null, CancellationToken cancellationToken = default)
    {
        var list = items?.ToList() ?? new List<(string remotePath, string localPath)>();
        var results = new DownloadResult[list.Count];
        using var sem = new SemaphoreSlim(Math.Max(1, maxConcurrency));
        var tasks = list.Select(async (item, i) =>
        {
            await sem.WaitAsync(cancellationToken);
            try
            {
                results[i] = await DownloadToFileAsync(token, item.remotePath, item.localPath, config, cancellationToken);
            }
            finally { sem.Release(); }
        });
        await Task.WhenAll(tasks);
        return results;
    }

    public async Task<IReadOnlyList<FsPutResponse>> SafeUploadAsync(string token, UploadRequest request, int maxConcurrency = 4, bool asTask = false, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        switch (request.Mode)
        {
            case UploadMode.Single:
            {
                var res = await SafePutAsync(token, NormalizeRemotePath(request.RemotePath!), request.Content!, asTask, cancellationToken);
                return new[] { res };
            }
            case UploadMode.Multiple:
            {
                var items = request.Items ?? Array.Empty<FileUploadItem>();
                // 预归一化路径
                var norm = items.Select(i => new FileUploadItem { FilePath = NormalizeRemotePath(i.FilePath), Content = i.Content }).ToList();
                return await PutManyAsync(token, norm, maxConcurrency, asTask, cancellationToken);
            }
            case UploadMode.Directory:
            {
                if (string.IsNullOrWhiteSpace(request.LocalDirectory)) return Array.Empty<FsPutResponse>();
                var localDir = Path.GetFullPath(request.LocalDirectory!);
                if (!Directory.Exists(localDir)) return Array.Empty<FsPutResponse>();
                var remoteBase = NormalizeRemotePath(request.RemoteBasePath ?? "/");

                // 枚举所有文件，构造成 FileUploadItem（保留相对目录结构）
                var files = Directory.EnumerateFiles(localDir, "*", SearchOption.AllDirectories).ToList();
                var relItems = new List<(string remotePath, string localPath)>();
                foreach (var f in files)
                {
                    var rel = Path.GetRelativePath(localDir, f).Replace('\\', '/');
                    var remotePath = CombinePath(remoteBase, rel);
                    relItems.Add((remotePath, f));
                }

                // 并发上传：逐个确保目录存在，再 SafePut
                var results = new FsPutResponse[relItems.Count];
                using var sem = new SemaphoreSlim(Math.Max(1, maxConcurrency));
                var tasks = relItems.Select(async (pair, idx) =>
                {
                    await sem.WaitAsync(cancellationToken);
                    try
                    {
                        // 目录存在性
                        await EnsureRemoteDirectoriesAsync(token, GetDirectoryOfRemote(pair.remotePath), cancellationToken);
                        byte[] bytes = await File.ReadAllBytesAsync(pair.localPath, cancellationToken);
                        results[idx] = await SafePutAsync(token, pair.remotePath, bytes, asTask, cancellationToken);
                    }
                    finally { sem.Release(); }
                });
                await Task.WhenAll(tasks);
                return results;
            }
            default:
                return Array.Empty<FsPutResponse>();
        }
    }

    private async Task EnsureRemoteDirectoriesAsync(string token, string? dir, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dir) || dir == "/") return;
        // 逐级创建：/a/b/c
        var parts = dir.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "/";
        foreach (var p in parts)
        {
            current = CombinePath(current, p);
            try
            {
                var res = await MkdirAsync(token, current, ct);
                // 200/201/409 都视为 OK（已存在）
                if (res.Code == 200 || res.Code == 201 || res.Code == 409) { }
            }
            catch { /* 忽略目录创建异常，后续上传可能仍然成功 */ }
        }
    }

    private string? ResolveRawUrl(FsGetData data)
    {
        if (!string.IsNullOrWhiteSpace(data.RawUrl)) return data.RawUrl;
        if (!string.IsNullOrWhiteSpace(data.Sign))
            return _baseUrl + "/d/" + Uri.EscapeDataString(data.Sign!);
        return null;
    }

    private static DownloadConfiguration BuildDownloadConfig(DownloadConfiguration? baseCfg, bool needAuth, string token)
    {
        if (baseCfg is null)
        {
            baseCfg = new DownloadConfiguration
            {
                BufferBlockSize = 10240,
                ChunkCount = 8,
                MaximumBytesPerSecond = 2 * 1024 * 1024,
                MaxTryAgainOnFailure = 5,
                MaximumMemoryBufferBytes = 50 * 1024 * 1024,
                ParallelDownload = true,
                ParallelCount = 4,
                Timeout = 10000,
                RangeDownload = false,
                ClearPackageOnCompletionWithFailure = true,
                MinimumSizeOfChunking = 102400,
                MinimumChunkSize = 10240,
                ReserveStorageSpaceBeforeStartingDownload = true,
                EnableLiveStreaming = false,
            };
        }
        baseCfg.RequestConfiguration ??= new RequestConfiguration();
        baseCfg.RequestConfiguration.Headers ??= new WebHeaderCollection();
        if (needAuth)
        {
            if (string.IsNullOrWhiteSpace(baseCfg.RequestConfiguration.Headers["Authorization"]))
            {
                baseCfg.RequestConfiguration.Headers["Authorization"] = token;
            }
        }
        else
        {
            baseCfg.RequestConfiguration.Headers.Remove("Authorization");
        }
        return baseCfg;
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

    private static string NormalizeRemotePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/";
        var p = path.Replace('\\', '/');
        if (!p.StartsWith('/')) p = "/" + p;
        // collapse multiple slashes
        while (p.Contains("//")) p = p.Replace("//", "/");
        return p;
    }

    private static string? GetDirectoryOfRemote(string remotePath)
    {
        var (d, _) = SplitPath(remotePath);
        return d;
    }
}
