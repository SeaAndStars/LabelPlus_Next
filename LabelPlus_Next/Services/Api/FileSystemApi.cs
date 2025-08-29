using Downloader;
using Newtonsoft.Json;
using NLog;
using RestSharp;
using System.Net;

namespace LabelPlus_Next.Services.Api;

/// <summary>
///     文件系统 API 客户端实现。
/// </summary>
public sealed class FileSystemApi : IFileSystemApi
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly string _baseUrl;

    private readonly RestClient _client;

    public FileSystemApi(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _client = new RestClient(new RestClientOptions(_baseUrl));
        Logger.Info("FileSystemApi created for {base}", _baseUrl);
    }

    public async Task<FsListResponse> ListAsync(string token, string path, int page = 1, int perPage = 0, bool refresh = true, string password = "", CancellationToken cancellationToken = default)
    {
        var request = new RestRequest("/api/fs/list", Method.Post)
            .AddHeader("Authorization", token)
            .AddHeader("Content-Type", "application/json")
            .AddJsonBody(new { path, password, page, per_page = perPage, refresh });
        Logger.Debug("ListAsync path={path} page={page} perPage={perPage} refresh={refresh}", path, page, perPage, refresh);
        var response = await _client.ExecuteAsync(request, cancellationToken);
        var code = (int)response.StatusCode;
        if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
        {
            Logger.Warn("ListAsync fail {code} {msg}", code, response.ErrorMessage ?? response.StatusDescription);
            return new FsListResponse { Code = code, Message = response.ErrorMessage ?? response.StatusDescription ?? "Request failed" };
        }
        var parsed = JsonConvert.DeserializeObject<FsListResponse>(response.Content);
        Logger.Info("ListAsync ok code={code} items={count}", parsed?.Code, parsed?.Data?.Content?.Length);
        return parsed ?? new FsListResponse { Code = -1, Message = "Deserialize failed" };
    }

    public async Task<FsGetResponse> GetAsync(string token, string path, int page = 1, int perPage = 0, bool refresh = true, string password = "", CancellationToken cancellationToken = default)
    {
        var request = new RestRequest("/api/fs/get", Method.Post)
            .AddHeader("Authorization", token)
            .AddHeader("Content-Type", "application/json")
            .AddJsonBody(new { path, password, page, per_page = perPage, refresh });
        Logger.Debug("GetAsync path={path}", path);
        var response = await _client.ExecuteAsync(request, cancellationToken);
        var code = (int)response.StatusCode;
        if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
        {
            Logger.Warn("GetAsync fail {code} {msg}", code, response.ErrorMessage ?? response.StatusDescription);
            return new FsGetResponse { Code = code, Message = response.ErrorMessage ?? response.StatusDescription ?? "Request failed" };
        }
        var parsed = JsonConvert.DeserializeObject<FsGetResponse>(response.Content);
        Logger.Info("GetAsync ok code={code} name={name} isDir={dir}", parsed?.Code, parsed?.Data?.Name, parsed?.Data?.IsDir);
        return parsed ?? new FsGetResponse { Code = -1, Message = "Deserialize failed" };
    }

    public async Task<FsSearchResponse> SearchAsync(string token, string parent, string keywords, int scope = 0, int page = 1, int perPage = 1, string password = "", CancellationToken cancellationToken = default)
    {
        var request = new RestRequest("/api/fs/search", Method.Post)
            .AddHeader("Authorization", token)
            .AddHeader("Content-Type", "application/json")
            .AddJsonBody(new { parent, keywords, scope, page, per_page = perPage, password });
        Logger.Debug("SearchAsync parent={parent} keywords={kw} scope={scope}", parent, keywords, scope);
        var response = await _client.ExecuteAsync(request, cancellationToken);
        var code = (int)response.StatusCode;
        if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
        {
            Logger.Warn("SearchAsync fail {code} {msg}", code, response.ErrorMessage ?? response.StatusDescription);
            return new FsSearchResponse { Code = code, Message = response.ErrorMessage ?? response.StatusDescription ?? "Request failed" };
        }
        var parsed = JsonConvert.DeserializeObject<FsSearchResponse>(response.Content);
        Logger.Info("SearchAsync ok code={code} count={count}", parsed?.Code, parsed?.Data?.Content?.Length);
        return parsed ?? new FsSearchResponse { Code = -1, Message = "Deserialize failed" };
    }

    public async Task<ApiResponse<object>> MkdirAsync(string token, string path, CancellationToken cancellationToken = default)
    {
        var request = new RestRequest("/api/fs/mkdir", Method.Post)
            .AddHeader("Authorization", token)
            .AddHeader("Content-Type", "application/json")
            .AddJsonBody(new { path });
        Logger.Debug("MkdirAsync path={path}", path);
        var response = await _client.ExecuteAsync(request, cancellationToken);
        var code = (int)response.StatusCode;
        if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
        {
            Logger.Warn("MkdirAsync fail {code} {msg}", code, response.ErrorMessage ?? response.StatusDescription);
            return new ApiResponse<object> { Code = code, Message = response.ErrorMessage ?? response.StatusDescription ?? "Request failed" };
        }
        var parsed = JsonConvert.DeserializeObject<ApiResponse<object>>(response.Content);
        Logger.Info("MkdirAsync ok code={code}", parsed?.Code);
        return parsed ?? new ApiResponse<object> { Code = -1, Message = "Deserialize failed" };
    }

    public async Task<ApiResponse<object>> CopyAsync(string token, string srcDir, string dstDir, IEnumerable<string> names, CancellationToken cancellationToken = default)
    {
        var request = new RestRequest("/api/fs/copy", Method.Post)
            .AddHeader("Authorization", token)
            .AddHeader("Content-Type", "application/json")
            .AddJsonBody(new { src_dir = srcDir, dst_dir = dstDir, names });
        Logger.Debug("CopyAsync src={src} dst={dst} names={count}", srcDir, dstDir, names?.Count());
        var response = await _client.ExecuteAsync(request, cancellationToken);
        var code = (int)response.StatusCode;
        if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
        {
            Logger.Warn("CopyAsync fail {code} {msg}", code, response.ErrorMessage ?? response.StatusDescription);
            return new ApiResponse<object> { Code = code, Message = response.ErrorMessage ?? response.StatusDescription ?? "Request failed" };
        }
        var parsed = JsonConvert.DeserializeObject<ApiResponse<object>>(response.Content);
        Logger.Info("CopyAsync ok code={code}", parsed?.Code);
        return parsed ?? new ApiResponse<object> { Code = -1, Message = "Deserialize failed" };
    }

    public Task<FsPutResponse> PutAsync(string token, string filePath, byte[] content, bool asTask = false, CancellationToken cancellationToken = default)
        => SafePutAsync(token, filePath, content, asTask, cancellationToken);

    public async Task<FsPutResponse> SafePutAsync(string token, string filePath, byte[] content, bool asTask = false, CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.Debug("SafePut start file={file}", filePath);
            var existed = await GetAsync(token, filePath, cancellationToken: cancellationToken);
            if (existed.Code == 200 && existed.Data is not null && !existed.Data.IsDir)
            {
                var raw = existed.Data.RawUrl;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    byte[] oldBytes;
                    using (var http = new HttpClient())
                    {
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

                    Logger.Info("SafePut backup -> {backup}", backupPath);
                    await PutRawAsync(token, backupPath, oldBytes, asTask, cancellationToken);
                }
                else
                {
                    Logger.Warn("SafePut: raw_url is empty for {file}", filePath);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "SafePut pre-backup failed for {file}", filePath);
        }

        var res = await PutRawAsync(token, filePath, content, asTask, cancellationToken);
        Logger.Info("SafePut completed {file} -> code={code}", filePath, res.Code);
        return res;
    }

    public async Task<IReadOnlyList<FsPutResponse>> PutManyAsync(string token, IEnumerable<FileUploadItem> items, int maxConcurrency = 20, bool asTask = false, CancellationToken cancellationToken = default) => await PutManyAsync(token, items, null, maxConcurrency, asTask, cancellationToken);

    public async Task<DownloadResult> DownloadAsync(string token, string filePath, CancellationToken cancellationToken = default)
    {
        Logger.Debug("DownloadAsync path={path}", filePath);
        FsGetResponse meta = new() { Code = -1, Message = "init" };
        const int maxAttempts = 10;
        var attempt = 0;
        while (attempt++ < maxAttempts)
        {
            meta = await GetAsync(token, filePath, cancellationToken: cancellationToken);
            if (meta.Code == 200 && meta.Data is not null && !meta.Data.IsDir) break;

            var msg = meta.Message ?? string.Empty;
            var retryable = meta.Code == -1 || meta.Code == 404 || meta.Code >= 500 || msg.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!retryable) break;

            var delayMs = Math.Min(1000, 100 * (1 << Math.Min(6, attempt)));
            try { await Task.Delay(delayMs, cancellationToken); }
            catch { }
        }
        if (meta.Code != 200 || meta.Data is null || meta.Data.IsDir)
        {
            Logger.Warn("DownloadAsync meta fail code={code} msg={msg}", meta.Code, meta.Message);
            return new DownloadResult { Code = meta.Code, Message = meta.Message };
        }

        var url = meta.Data.RawUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            Logger.Warn("DownloadAsync: raw_url not available for {path}", filePath);
            return new DownloadResult { Code = -1, Message = "raw_url not available" };
        }

        try
        {
            using var http = new HttpClient();
            // 尝试不带鉴权，强制非压缩
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                req.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (resp.IsSuccessStatusCode)
                {
                    var bytesOk = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
                    Logger.Info("DownloadAsync ok(size={size}) no-auth", bytesOk.Length);
                    return new DownloadResult { Code = 200, Content = bytesOk };
                }
            }
            // 失败则携带 Authorization 重试
            using (var req2 = new HttpRequestMessage(HttpMethod.Get, url))
            {
                req2.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
                req2.Headers.TryAddWithoutValidation("Authorization", token);
                using var resp2 = await http.SendAsync(req2, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (resp2.IsSuccessStatusCode)
                {
                    var bytesOk2 = await resp2.Content.ReadAsByteArrayAsync(cancellationToken);
                    Logger.Info("DownloadAsync ok(size={size}) with-auth", bytesOk2.Length);
                    return new DownloadResult { Code = 200, Content = bytesOk2 };
                }
                Logger.Warn("DownloadAsync fail http={code} reason={reason}", (int)resp2.StatusCode, resp2.ReasonPhrase);
                return new DownloadResult { Code = (int)resp2.StatusCode, Message = resp2.ReasonPhrase ?? "Request failed" };
            }
        }
        catch (HttpRequestException hex)
        {
            var code = (int?)hex.StatusCode ?? -1;
            Logger.Warn(hex, "DownloadAsync http error code={code}", code);
            return new DownloadResult { Code = code, Message = hex.Message };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "DownloadAsync exception");
            return new DownloadResult { Code = -1, Message = ex.Message };
        }
    }

    public async Task<IReadOnlyList<DownloadResult>> DownloadManyAsync(string token, IEnumerable<string> filePaths, int maxConcurrency = 4, CancellationToken cancellationToken = default)
    {
        var list = filePaths?.ToList() ?? new List<string>();
        Logger.Debug("DownloadMany count={count} concurrency={cc}", list.Count, maxConcurrency);
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
        Logger.Debug("DownloadToFile path={path} -> {local}", filePath, localPath);
        FsGetResponse meta = new() { Code = -1, Message = "init" };
        const int maxAttempts = 10;
        var attempt = 0;
        while (attempt++ < maxAttempts)
        {
            meta = await GetAsync(token, filePath, cancellationToken: cancellationToken);
            if (meta.Code == 200 && meta.Data is not null && !meta.Data.IsDir) break;

            var msg = meta.Message ?? string.Empty;
            var retryable = meta.Code == -1 || meta.Code == 404 || meta.Code >= 500 || msg.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!retryable) break;

            var delayMs = Math.Min(1000, 100 * (1 << Math.Min(6, attempt)));
            try { await Task.Delay(delayMs, cancellationToken); }
            catch { }
        }
        if (meta.Code != 200 || meta.Data is null || meta.Data.IsDir)
        {
            Logger.Warn("DownloadToFile meta fail code={code} msg={msg}", meta.Code, meta.Message);
            return new DownloadResult { Code = meta.Code, Message = meta.Message };
        }

        var url = meta.Data.RawUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            Logger.Warn("DownloadToFile: raw_url not available for {path}", filePath);
            return new DownloadResult { Code = -1, Message = "raw_url not available" };
        }

        // 优先使用 DownloadAsync（强制非压缩 + 可带鉴权），写入文件
        var res = await DownloadAsync(token, filePath, cancellationToken);
        if (res.Code == 200 && res.Content is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            await File.WriteAllBytesAsync(localPath, res.Content, cancellationToken);
            Logger.Info("DownloadToFile ok -> {local}", localPath);
            return new DownloadResult { Code = 200 };
        }

        // 回退到下载服务（也携带鉴权与非压缩）
        try
        {
            var cfg = BuildDownloadConfig(config, true, token);
            cfg.RequestConfiguration.Headers["Accept-Encoding"] = "identity";
            var service = new DownloadService(cfg);
            await service.DownloadFileTaskAsync(url, localPath);
            Logger.Info("DownloadToFile(ok via fallback) -> {local}", localPath);
            return new DownloadResult { Code = 200 };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "DownloadToFile exception(fallback)");
            return new DownloadResult { Code = -1, Message = ex.Message };
        }
    }

    public async Task<IReadOnlyList<DownloadResult>> DownloadManyToFilesAsync(string token, IEnumerable<(string remotePath, string localPath)> items, int maxConcurrency = 4, DownloadConfiguration? config = null, CancellationToken cancellationToken = default)
    {
        var list = items?.ToList() ?? new List<(string remotePath, string localPath)>();
        Logger.Debug("DownloadManyToFiles count={count} concurrency={cc}", list.Count, maxConcurrency);
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
        Logger.Debug("SafeUpload mode={mode}", request.Mode);
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
                var norm = items.Select(i => new FileUploadItem { FilePath = NormalizeRemotePath(i.FilePath), Content = i.Content }).ToList();
                return await PutManyAsync(token, norm, maxConcurrency, asTask, cancellationToken);
            }
            case UploadMode.Directory:
            {
                if (string.IsNullOrWhiteSpace(request.LocalDirectory)) return Array.Empty<FsPutResponse>();
                var localDir = Path.GetFullPath(request.LocalDirectory!);
                if (!Directory.Exists(localDir)) return Array.Empty<FsPutResponse>();
                var remoteBase = NormalizeRemotePath(request.RemoteBasePath ?? "/");

                var files = Directory.EnumerateFiles(localDir, "*", SearchOption.AllDirectories).ToList();
                var relItems = new List<(string remotePath, string localPath)>();
                foreach (var f in files)
                {
                    var rel = Path.GetRelativePath(localDir, f).Replace('\\', '/');
                    var remotePath = CombinePath(remoteBase, rel);
                    relItems.Add((remotePath, f));
                }

                var results = new FsPutResponse[relItems.Count];
                using var sem = new SemaphoreSlim(Math.Max(1, maxConcurrency));
                var tasks = relItems.Select(async (pair, idx) =>
                {
                    await sem.WaitAsync(cancellationToken);
                    try
                    {
                        await EnsureRemoteDirectoriesAsync(token, GetDirectoryOfRemote(pair.remotePath), cancellationToken);
                        var bytes = await File.ReadAllBytesAsync(pair.localPath, cancellationToken);
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

    private static string EncodePathForHeader(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return "/";
        var p = filePath.Replace('\\', '/');
        if (!p.StartsWith('/')) p = "/" + p;
        while (p.Contains("//")) p = p.Replace("//", "/");
        var parts = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var encoded = string.Join('/', parts.Select(Uri.EscapeDataString));
        return "/" + encoded;
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
                EnableLiveStreaming = false
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

    // Raw upload without safety checks
    private async Task<FsPutResponse> PutRawAsync(string token, string filePath, byte[] content, bool asTask, CancellationToken cancellationToken)
    {
        var safePath = EncodePathForHeader(filePath);
        var request = new RestRequest("/api/fs/put", Method.Put)
            .AddHeader("Authorization", token)
            .AddHeader("File-Path", safePath)
            .AddHeader("As-Task", asTask ? "true" : "false")
            .AddHeader("Content-Type", "application/octet-stream")
            .AddParameter("application/octet-stream", content, ParameterType.RequestBody);
        Logger.Debug("PutRaw path={path} asTask={asTask} size={size}", safePath, asTask, content?.Length);
        var response = await _client.ExecuteAsync(request, cancellationToken);
        var code = (int)response.StatusCode;
        if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
        {
            Logger.Warn("PutRaw fail {code} {msg}", code, response.ErrorMessage ?? response.StatusDescription);
            return new FsPutResponse { Code = code, Message = response.ErrorMessage ?? response.StatusDescription ?? "Request failed" };
        }
        var parsed = JsonConvert.DeserializeObject<FsPutResponse>(response.Content);
        Logger.Info("PutRaw ok code={code}", parsed?.Code);
        return parsed ?? new FsPutResponse { Code = -1, Message = "Deserialize failed" };
    }

    public async Task<IReadOnlyList<FsPutResponse>> PutManyAsync(string token, IEnumerable<FileUploadItem> items, IProgress<UploadProgress>? progress, int maxConcurrency = 20, bool asTask = false, CancellationToken cancellationToken = default)
    {
        var list = items?.ToList() ?? new List<FileUploadItem>();
        Logger.Debug("PutMany count={count} concurrency={cc}", list.Count, maxConcurrency);
        var results = new FsPutResponse[list.Count];
        using var sem = new SemaphoreSlim(Math.Max(1, maxConcurrency));
        var done = 0;
        var tasks = list.Select(async (item, idx) =>
        {
            await sem.WaitAsync(cancellationToken);
            try
            {
                var res = await PutAsync(token, item.FilePath, item.Content, asTask, cancellationToken);
                results[idx] = res;
                var d = Interlocked.Increment(ref done);
                progress?.Report(new UploadProgress { Completed = d, Total = list.Count, CurrentRemotePath = item.FilePath });
            }
            finally { sem.Release(); }
        });
        await Task.WhenAll(tasks);
        return results;
    }

    private async Task EnsureRemoteDirectoriesAsync(string token, string? dir, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dir) || dir == "/") return;
        var parts = dir.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "/";
        foreach (var p in parts)
        {
            current = CombinePath(current, p);
            try
            {
                var res = await MkdirAsync(token, current, ct);
                if (res.Code == 200 || res.Code == 201 || res.Code == 409) { Logger.Debug("EnsureDir ok: {dir}", current); }
                else { Logger.Warn("EnsureDir fail {code}: {dir}", res.Code, current); }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "EnsureDir exception: {dir}", current);
            }
        }
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
        while (p.Contains("//")) p = p.Replace("//", "/");
        return p;
    }

    private static string? GetDirectoryOfRemote(string remotePath)
    {
        var (d, _) = SplitPath(remotePath);
        return d;
    }
}
