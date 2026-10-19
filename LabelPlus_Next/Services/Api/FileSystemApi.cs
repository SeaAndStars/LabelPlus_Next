using Downloader;
using Newtonsoft.Json;
using NLog;
using RestSharp;
using System.Net;
using System.Net.Http;
using System.IO;

namespace LabelPlus_Next.Services.Api;

public sealed class FileSystemApi : IFileSystemApi
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly string _baseUrl;

    private readonly RestClient _client;
    private readonly HttpClient _http;

    private const string DefaultUserAgent = "pan.baidu.com";

    public FileSystemApi(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        // Increase default timeout for large uploads/downloads.
        // RestSharp defaults to ~100s which can cancel long PUT requests with code=0 "The operation was canceled".
        _client = new RestClient(new RestClientOptions(_baseUrl)
        {
            // 使用无限超时，避免后续 REST 接口在慢网络或大数据时被中断
            Timeout = Timeout.InfiniteTimeSpan
        });
        _client.AddDefaultHeader("User-Agent", DefaultUserAgent);
        // 复用连接的 HttpClient，禁用 100-continue，提升吞吐
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 100
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = Timeout.InfiniteTimeSpan
        };
        _http.DefaultRequestHeaders.ExpectContinue = false;
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
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
                    using (var req1 = new HttpRequestMessage(HttpMethod.Get, raw))
                    {
                        using var resp1 = await _http.SendAsync(req1, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        if (resp1.IsSuccessStatusCode)
                        {
                            oldBytes = await resp1.Content.ReadAsByteArrayAsync(cancellationToken);
                        }
                        else
                        {
                            using var req2 = new HttpRequestMessage(HttpMethod.Get, raw);
                            req2.Headers.TryAddWithoutValidation("Authorization", token);
                            using var resp2 = await _http.SendAsync(req2, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                            resp2.EnsureSuccessStatusCode();
                            oldBytes = await resp2.Content.ReadAsByteArrayAsync(cancellationToken);
                        }
                    }

                    var (dir, name) = SplitPath(filePath);
                    var ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                    var backupName = AppendTimestamp(name, ts);
                    var backupPath = CombinePath(dir, backupName);

                    Logger.Info("SafePut backup -> {backup}", backupPath);
                    await PutRawAsync(token, backupPath, oldBytes, asTask, null, 0, 0, cancellationToken, null);
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

        var res = await PutRawAsync(token, filePath, content, asTask, null, 0, 0, cancellationToken, null);
        Logger.Info("SafePut completed {file} -> code={code}", filePath, res.Code);
        return res;
    }

    // Overload for internal progress wiring
    private async Task<FsPutResponse> SafePutAsync(string token, string filePath, byte[] content, bool asTask, IProgress<UploadProgress>? progress, int completedHint, int totalHint, CancellationToken cancellationToken)
    {
        try
        {
            Logger.Debug("SafePut+Progress start file={file}", filePath);
            var existed = await GetAsync(token, filePath, cancellationToken: cancellationToken);
            if (existed.Code == 200 && existed.Data is not null && !existed.Data.IsDir)
            {
                var raw = existed.Data.RawUrl;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, raw);
                        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        if (resp.IsSuccessStatusCode)
                        {
                            var oldBytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
                            var (dir, name) = SplitPath(filePath);
                            var ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                            var backupName = AppendTimestamp(name, ts);
                            var backupPath = CombinePath(dir, backupName);
                            Logger.Info("SafePut backup -> {backup}", backupPath);
                            // Do not report progress for backup to keep UI clean
                            await PutRawAsync(token, backupPath, oldBytes, asTask, null, 0, 0, cancellationToken, null);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "SafePut+Progress pre-backup error for {file}", filePath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "SafePut+Progress pre-backup failed for {file}", filePath);
        }

        var res = await PutRawAsync(token, filePath, content, asTask, progress, completedHint, totalHint, cancellationToken, null);
        Logger.Info("SafePut+Progress completed {file} -> code={code}", filePath, res.Code);
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
            catch (OperationCanceledException)
            {
                throw;
            }
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

        // Prefer multi-thread downloader with UA
        try
        {
            var bytesNoAuth = await DownloadToBytesWithDownloaderAsync(url, BuildDownloadConfig(null, false, token), cancellationToken);
            if (bytesNoAuth is not null)
            {
                Logger.Info("DownloadAsync ok(size={size}) no-auth via MT", bytesNoAuth.Length);
                return new DownloadResult { Code = 200, Content = bytesNoAuth };
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Downloader(no-auth) failed, will try auth");
        }

        try
        {
            var cfgAuth = BuildDownloadConfig(null, true, token);
            var bytesAuth = await DownloadToBytesWithDownloaderAsync(url, cfgAuth, cancellationToken);
            if (bytesAuth is not null)
            {
                Logger.Info("DownloadAsync ok(size={size}) with-auth via MT", bytesAuth.Length);
                return new DownloadResult { Code = 200, Content = bytesAuth };
            }
            Logger.Warn("DownloadAsync fail via Downloader with-auth");
            return new DownloadResult { Code = -1, Message = "Downloader failed" };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "DownloadAsync exception via Downloader");
            return new DownloadResult { Code = -1, Message = ex.Message };
        }
    }

    // Parallel wrapper over DownloadAsync
    public async Task<IReadOnlyList<DownloadResult>> DownloadManyAsync(string token, IEnumerable<string> filePaths, int maxConcurrency = 4, CancellationToken cancellationToken = default)
    {
        var list = filePaths?.ToList() ?? new List<string>();
        Logger.Debug("DownloadMany count={count} concurrency={cc}", list.Count, maxConcurrency);
        var results = new DownloadResult[list.Count];
        using var sem = new SemaphoreSlim(Math.Max(1, maxConcurrency));
        var tasks = list.Select(async (file, idx) =>
        {
            await sem.WaitAsync(cancellationToken);
            try { results[idx] = await DownloadAsync(token, file, cancellationToken); }
            finally { sem.Release(); }
        });
        await Task.WhenAll(tasks);
        return results;
    }

    private static async Task<byte[]?> DownloadToBytesWithDownloaderAsync(string url, DownloadConfiguration cfg, CancellationToken ct)
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            cfg.RequestConfiguration.Headers["Accept-Encoding"] = "identity";
            var service = new DownloadService(cfg);
            await service.DownloadFileTaskAsync(url, tmp, ct);
            if (!File.Exists(tmp)) return null;
            var bytes = await File.ReadAllBytesAsync(tmp, ct);
            return bytes.Length == 0 ? Array.Empty<byte>() : bytes;
        }
        finally
        {
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch (IOException ex)
            {
                Logger.Warn(ex, "Failed to delete temporary download file {file}", tmp);
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Warn(ex, "Access denied when deleting temporary download file {file}", tmp);
            }
        }
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
            catch (OperationCanceledException)
            {
                throw;
            }
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

        try
        {
            var cfg = BuildDownloadConfig(config, true, token);
            cfg.RequestConfiguration.Headers["Accept-Encoding"] = "identity";
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            var service = new DownloadService(cfg);
            await service.DownloadFileTaskAsync(url, localPath, cancellationToken);
            Logger.Info("DownloadToFile(ok via MT) -> {local}", localPath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "DownloadToFile exception(MT)");
            // 先不直接返回，转入回退逻辑
        }

        try
        {
            // 校验文件是否真实存在且非空
            if (!File.Exists(localPath) || new FileInfo(localPath).Length == 0)
            {
                Logger.Warn("DownloadToFile verify fail or empty -> fallback HTTP client: {local}", localPath);
                // 回退：直接以 HttpClient 下载
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Authorization", token);
                req.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
                req.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!resp.IsSuccessStatusCode)
                {
                    var code = (int)resp.StatusCode;
                    var reason = resp.ReasonPhrase ?? "Request failed";
                    Logger.Warn("Fallback HTTP get fail {code} {reason}", code, reason);
                    return new DownloadResult { Code = code, Message = reason };
                }
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                await using (var fsLocal = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, useAsync: true))
                {
                    await resp.Content.CopyToAsync(fsLocal, cancellationToken);
                }
            }

            // 再次校验
            if (!File.Exists(localPath))
            {
                Logger.Warn("DownloadToFile final verify: file not found {local}", localPath);
                return new DownloadResult { Code = -1, Message = "file not created" };
            }
            var len = new FileInfo(localPath).Length;
            if (len <= 0)
            {
                Logger.Warn("DownloadToFile final verify: empty file {local}", localPath);
                return new DownloadResult { Code = -1, Message = "empty file" };
            }

            Logger.Info("DownloadToFile success size={size} -> {local}", len, localPath);
            return new DownloadResult { Code = 200 };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "DownloadToFile fallback exception");
            return new DownloadResult { Code = -1, Message = ex.Message };
        }
    }

    // Parallel wrapper over DownloadToFileAsync
    public async Task<IReadOnlyList<DownloadResult>> DownloadManyToFilesAsync(string token, IEnumerable<(string remotePath, string localPath)> items, int maxConcurrency = 4, DownloadConfiguration? config = null, CancellationToken cancellationToken = default)
    {
        var list = items?.ToList() ?? new List<(string remotePath, string localPath)>();
        Logger.Debug("DownloadManyToFiles count={count} concurrency={cc}", list.Count, maxConcurrency);
        var results = new DownloadResult[list.Count];
        using var sem = new SemaphoreSlim(Math.Max(1, maxConcurrency));
        var tasks = list.Select(async (pair, i) =>
        {
            await sem.WaitAsync(cancellationToken);
            try { results[i] = await DownloadToFileAsync(token, pair.remotePath, pair.localPath, config, cancellationToken); }
            finally { sem.Release(); }
        });
        await Task.WhenAll(tasks);
        return results;
    }

    private static DownloadConfiguration BuildDownloadConfig(DownloadConfiguration? baseCfg, bool needAuth, string token)
    {
        if (baseCfg is null)
        {
            baseCfg = new DownloadConfiguration
            {
                BufferBlockSize = 10240,
                ChunkCount = 16,
                ParallelDownload = true,

            };
        }
        baseCfg.RequestConfiguration ??= new RequestConfiguration();
        baseCfg.RequestConfiguration.Headers ??= new WebHeaderCollection();
        if (string.IsNullOrWhiteSpace(baseCfg.RequestConfiguration.Headers["User-Agent"]))
            baseCfg.RequestConfiguration.Headers["User-Agent"] = DefaultUserAgent;
        if (needAuth)
        {
            if (string.IsNullOrWhiteSpace(baseCfg.RequestConfiguration.Headers["Authorization"]))
                baseCfg.RequestConfiguration.Headers["Authorization"] = token;
        }
        else
        {
            baseCfg.RequestConfiguration.Headers.Remove("Authorization");
        }
        return baseCfg;
    }

    // Ensure helper exists for upload header encoding
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

    // Provide SafeUpload entry required by interface
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
                    var norm = items.Select(i => new FileUploadItem { FilePath = NormalizeRemotePath(i.FilePath), Content = i.Content, LocalPath = i.LocalPath }).ToList();
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
                            results[idx] = await SafePutAsync(token, pair.remotePath, /*localPath*/ pair.localPath, /*content*/ null, asTask: asTask, progress: null, completedHint: 0, totalHint: 0, cancellationToken: cancellationToken);
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

    // Raw upload without safety checks, with optional progress. Prefer localPath when provided for zero-copy streaming.
    private async Task<FsPutResponse> PutRawAsync(string token, string filePath, byte[]? content, bool asTask, IProgress<UploadProgress>? progress, int completedHint, int totalHint, CancellationToken cancellationToken, string? localPath)
    {
        var safePath = EncodePathForHeader(filePath);
        long? total = null;
        Stream dataStream;
        if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
        {
            var fi = new FileInfo(localPath);
            total = fi.Length;
            dataStream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 256 * 1024, useAsync: true);
        }
        else
        {
            var bytes = content ?? Array.Empty<byte>();
            total = bytes.LongLength;
            dataStream = new MemoryStream(bytes, writable: false);
        }
        await using var ds = dataStream;
        Logger.Debug("PutRaw path={path} asTask={asTask} size={size} via={via}", safePath, asTask, total, string.IsNullOrWhiteSpace(localPath) ? "memory" : "file");
        using var pcs = new ProgressStreamContent(ds, 256 * 1024, (sent, tot, elapsed) =>
        {
            if (progress is null) return;
            var seconds = Math.Max(0.001, elapsed.TotalSeconds);
            var speedMBps = sent / 1024d / 1024d / seconds;
            progress.Report(new UploadProgress
            {
                Completed = completedHint,
                Total = totalHint,
                CurrentRemotePath = filePath,
                BytesSent = sent,
                BytesTotal = tot ?? total,
                SpeedMBps = speedMBps
            });
        }, total);
        using var req = new HttpRequestMessage(HttpMethod.Put, "/api/fs/put");
        req.Headers.TryAddWithoutValidation("Authorization", token);
        req.Headers.TryAddWithoutValidation("File-Path", safePath);
        req.Headers.TryAddWithoutValidation("As-Task", asTask ? "true" : "false");
        req.Content = pcs;
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var code = (int)resp.StatusCode;
        var text = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(text))
        {
            Logger.Warn("PutRaw fail {code} {msg}", code, resp.ReasonPhrase);
            return new FsPutResponse { Code = code, Message = resp.ReasonPhrase ?? "Request failed" };
        }
        var parsed = JsonConvert.DeserializeObject<FsPutResponse>(text);
        Logger.Info("PutRaw ok code={code}", parsed?.Code);
        return parsed ?? new FsPutResponse { Code = -1, Message = "Deserialize failed" };
    }

    // Overload SafePut that accepts localPath for zero-copy streaming; internal use only
    private async Task<FsPutResponse> SafePutAsync(string token, string filePath, string? localPath, byte[]? content, bool asTask, IProgress<UploadProgress>? progress, int completedHint, int totalHint, CancellationToken cancellationToken)
    {
        try
        {
            Logger.Debug("SafePut(Local) start file={file}", filePath);
            var existed = await GetAsync(token, filePath, cancellationToken: cancellationToken);
            if (existed.Code == 200 && existed.Data is not null && !existed.Data.IsDir)
            {
                var raw = existed.Data.RawUrl;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, raw);
                        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        if (resp.IsSuccessStatusCode)
                        {
                            var oldBytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
                            var (dir, name) = SplitPath(filePath);
                            var ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                            var backupName = AppendTimestamp(name, ts);
                            var backupPath = CombinePath(dir, backupName);
                            Logger.Info("SafePut backup -> {backup}", backupPath);
                            await PutRawAsync(token, backupPath, oldBytes, asTask, null, 0, 0, cancellationToken, null);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "SafePut(Local) pre-backup error for {file}", filePath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "SafePut(Local) pre-backup failed for {file}", filePath);
        }

        var res = await PutRawAsync(token, filePath, content, asTask, progress, completedHint, totalHint, cancellationToken, localPath);
        Logger.Info("SafePut(Local) completed {file} -> code={code}", filePath, res.Code);
        return res;
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

    // Overload that supports progress reporting (used by UploadViewModel)
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
                // Provide a hint so progress can show Completed/Total and MB/s for the current file
                var completedHint = Volatile.Read(ref done);
                var totalHint = list.Count;
                if (!string.IsNullOrWhiteSpace(item.LocalPath) && File.Exists(item.LocalPath))
                {
                    var res = await SafePutAsync(token, item.FilePath, item.LocalPath, null, asTask, progress, completedHint, totalHint, cancellationToken);
                    results[idx] = res;
                }
                else
                {
                    var res = await SafePutAsync(token, item.FilePath, item.Content ?? Array.Empty<byte>(), asTask, progress, completedHint, totalHint, cancellationToken);
                    results[idx] = res;
                }
                var d = Interlocked.Increment(ref done);
                progress?.Report(new UploadProgress { Completed = d, Total = list.Count, CurrentRemotePath = item.FilePath });
            }
            finally { sem.Release(); }
        });
        await Task.WhenAll(tasks);
        return results;
    }
}
