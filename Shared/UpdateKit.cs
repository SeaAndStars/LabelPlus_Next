using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Downloader;
using NLog;

// 客户端与更新程序共用的更新基础设施（以链接文件方式编译进两个项目）。
// 只依赖 BCL + Downloader + NLog，且不使用反射 JSON，保证 AOT 兼容。
namespace LabelPlus_Next.Shared;

/// <summary>宽松的语义化版本比较：支持 "v" 前缀、任意段数、预发布(-beta.1)与构建元数据(+abc)。</summary>
public static class UpdateVersion
{
    public static bool IsNewer(string? remote, string? local)
    {
        if (string.IsNullOrWhiteSpace(remote)) return false;
        if (string.IsNullOrWhiteSpace(local)) return true;
        return Compare(remote, local) > 0;
    }

    public static int Compare(string? a, string? b)
    {
        var pa = Parse(a);
        var pb = Parse(b);
        if (pa is null || pb is null)
            return string.Compare(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        var len = Math.Max(pa.Value.Core.Length, pb.Value.Core.Length);
        for (var i = 0; i < len; i++)
        {
            var x = i < pa.Value.Core.Length ? pa.Value.Core[i] : 0;
            var y = i < pb.Value.Core.Length ? pb.Value.Core[i] : 0;
            if (x != y) return x.CompareTo(y);
        }

        // 正式版 > 预发布版
        var preA = pa.Value.Pre;
        var preB = pb.Value.Pre;
        if (preA.Length == 0 && preB.Length == 0) return 0;
        if (preA.Length == 0) return 1;
        if (preB.Length == 0) return -1;

        var n = Math.Max(preA.Length, preB.Length);
        for (var i = 0; i < n; i++)
        {
            if (i >= preA.Length) return -1;
            if (i >= preB.Length) return 1;
            var aNum = long.TryParse(preA[i], out var an);
            var bNum = long.TryParse(preB[i], out var bn);
            int c;
            if (aNum && bNum) c = an.CompareTo(bn);
            else if (aNum) c = -1;
            else if (bNum) c = 1;
            else c = string.Compare(preA[i], preB[i], StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return 0;
    }

    private static (long[] Core, string[] Pre)? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];
        var plus = t.IndexOf('+');
        if (plus >= 0) t = t[..plus];
        var dash = t.IndexOf('-');
        var core = dash >= 0 ? t[..dash] : t;
        var pre = dash >= 0 ? t[(dash + 1)..] : string.Empty;

        var parts = core.Split('.');
        var nums = new long[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!long.TryParse(parts[i], out nums[i]) || nums[i] < 0) return null;
        }
        var preParts = pre.Length == 0 ? Array.Empty<string>() : pre.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return (nums, preParts);
    }
}

public static class UpdatePlatform
{
    public static readonly string[] KnownRids = ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"];

    public static string CurrentRid
    {
        get
        {
            var arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            if (OperatingSystem.IsWindows()) return arm ? "win-arm64" : "win-x64";
            if (OperatingSystem.IsMacOS()) return arm ? "osx-arm64" : "osx-x64";
            return arm ? "linux-arm64" : "linux-x64";
        }
    }

    /// <summary>从候选文件中挑选当前平台的包：先匹配 RID，再退回第一个带 URL 的文件。</summary>
    public static T? PickForCurrentPlatform<T>(IEnumerable<T>? files, Func<T, string?> name, Func<T, string?> url) where T : class
    {
        if (files is null) return null;
        var list = files.Where(f => !string.IsNullOrWhiteSpace(url(f))).ToList();
        var rid = CurrentRid;
        return list.FirstOrDefault(f => (name(f)?.Contains(rid, StringComparison.OrdinalIgnoreCase) ?? false)
                                        || (url(f)?.Contains(rid, StringComparison.OrdinalIgnoreCase) ?? false))
               ?? list.FirstOrDefault();
    }

    /// <summary>删除明显属于其他平台的残留压缩包（例如 Windows 上的 linux-x64.zip）。</summary>
    public static void CleanupForeignArchives(string dir, Logger logger)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            var others = KnownRids.Where(r => !string.Equals(r, CurrentRid, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
            {
                var ext = Path.GetExtension(file);
                if (!ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".7z", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetFileName(file);
                if (!others.Any(r => name.Contains(r, StringComparison.OrdinalIgnoreCase))) continue;
                try { File.Delete(file); logger.Info("Removed foreign archive {file}", file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { logger.Warn(ex, "Failed to delete foreign archive {file}", file); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Warn(ex, "Failed to clean foreign archives in {dir}", dir);
        }
    }

    /// <summary>非 Windows 平台确保可执行位存在。</summary>
    public static void EnsureExecutable(string path, Logger logger)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path)) return;
        try
        {
            const UnixFileMode exec = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            var mode = File.GetUnixFileMode(path);
            if ((mode & exec) == 0) File.SetUnixFileMode(path, mode | exec);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            logger.Warn(ex, "Failed to set execute bit on {path}", path);
        }
    }
}

public static class UpdateFiles
{
    public static async Task<string> ComputeSha256Async(string path, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>未提供期望值时视为通过；否则必须一致（忽略大小写）。</summary>
    public static async Task<bool> MatchesSha256Async(string path, string? expected, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expected)) return true;
        var actual = await ComputeSha256Async(path, ct).ConfigureAwait(false);
        return string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValidZip(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            _ = zip.Entries.Count;
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static string NewTempFile(string ext = ".zip") => Path.Combine(Path.GetTempPath(), "lpn-" + Guid.NewGuid().ToString("N") + ext);
}

/// <summary>
/// 分阶段安装：先完整解压到目标目录下的临时目录，再逐个替换文件（旧文件移入备份目录）。
/// 任何一步失败都会回滚，保证安装目录不会停留在“半新半旧”的状态。
/// </summary>
public static class StagedInstaller
{
    private const string StagingPrefix = ".lpn-staging-";
    private const string BackupPrefix = ".lpn-backup-";

    /// <param name="skip">接收以 '/' 分隔的相对路径，返回 true 则跳过该条目。</param>
    /// <returns>实际替换/新增的文件数。</returns>
    public static Task<int> ApplyZipAsync(string zipPath, string targetDir, Func<string, bool>? skip = null,
        IProgress<double>? progress = null, CancellationToken ct = default)
        => Task.Run(() => Apply(zipPath, targetDir, skip, progress, ct), ct);

    private static int Apply(string zipPath, string targetDir, Func<string, bool>? skip, IProgress<double>? progress, CancellationToken ct)
    {
        var root = Path.GetFullPath(targetDir);
        Directory.CreateDirectory(root);
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var id = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(root, StagingPrefix + id);
        var backup = Path.Combine(root, BackupPrefix + id);

        foreach (var stale in Directory.EnumerateDirectories(root, StagingPrefix + "*"))
            UpdateFiles.TryDeleteDirectory(stale);

        var staged = new List<string>();
        try
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
                for (var i = 0; i < entries.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var rel = entries[i].FullName.Replace('\\', '/').TrimStart('/');
                    if (rel.StartsWith(StagingPrefix, StringComparison.Ordinal) || rel.StartsWith(BackupPrefix, StringComparison.Ordinal)) continue;
                    if (skip?.Invoke(rel) == true) continue;

                    var dest = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
                    if (!dest.StartsWith(rootWithSep, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new InvalidDataException($"压缩包条目越界: {entries[i].FullName}");

                    var relNative = Path.GetRelativePath(root, dest);
                    var stagedPath = Path.Combine(staging, relNative);
                    Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                    // ExtractToFile 会保留 Unix 权限位（可执行位）
                    entries[i].ExtractToFile(stagedPath, overwrite: true);
                    staged.Add(relNative);
                    progress?.Report((i + 1) * 80.0 / entries.Count);
                }
            }

            var swapped = new List<(string Rel, bool HadOriginal)>(staged.Count);
            try
            {
                for (var i = 0; i < staged.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var rel = staged[i];
                    var dest = Path.Combine(root, rel);
                    var had = File.Exists(dest);
                    if (had)
                    {
                        var bak = Path.Combine(backup, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(bak)!);
                        File.Move(dest, bak);
                    }
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        File.Move(Path.Combine(staging, rel), dest);
                    }
                    catch
                    {
                        if (had) File.Move(Path.Combine(backup, rel), dest);
                        throw;
                    }
                    swapped.Add((rel, had));
                    progress?.Report(80 + (i + 1) * 20.0 / staged.Count);
                }
            }
            catch
            {
                Rollback(root, backup, swapped);
                throw;
            }

            UpdateFiles.TryDeleteDirectory(backup);
            return staged.Count;
        }
        finally
        {
            UpdateFiles.TryDeleteDirectory(staging);
        }
    }

    private static void Rollback(string root, string backup, List<(string Rel, bool HadOriginal)> swapped)
    {
        for (var i = swapped.Count - 1; i >= 0; i--)
        {
            var (rel, had) = swapped[i];
            var dest = Path.Combine(root, rel);
            try
            {
                if (File.Exists(dest)) File.Delete(dest);
                if (had) File.Move(Path.Combine(backup, rel), dest);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogManager.GetCurrentClassLogger().Error(ex, "Rollback failed for {file}; original kept in {backup}", dest, backup);
                return; // 保留备份目录以便手动恢复
            }
        }
        UpdateFiles.TryDeleteDirectory(backup);
    }
}

public static class AlistUrl
{
    /// <summary>从 AList 的 /d/&lt;path&gt; 或 /dav/&lt;path&gt; 链接中提取存储路径（以 '/' 开头，已反转义）。</summary>
    public static bool TryGetStoragePath(string? url, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        var segments = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (!segments[i].Equals("d", StringComparison.OrdinalIgnoreCase) && !segments[i].Equals("dav", StringComparison.OrdinalIgnoreCase)) continue;
            path = "/" + string.Join('/', segments.Skip(i + 1).Select(Uri.UnescapeDataString));
            return true;
        }
        return false;
    }

    /// <summary>AList 公链 /d/ 需要 download=1 才直接返回文件流。</summary>
    public static string WithDownloadFlag(string url)
    {
        if (!url.Contains("/d/", StringComparison.OrdinalIgnoreCase) || url.Contains("download=", StringComparison.OrdinalIgnoreCase)) return url;
        return url + (url.Contains('?') ? "&download=1" : "?download=1");
    }

    public static bool IsBaiduHost(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u)
           && (u.Host.EndsWith("baidupcs.com", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith("baidu.com", StringComparison.OrdinalIgnoreCase));
}

/// <summary>AList API 访问：一次更新流程内复用同一个登录 token。</summary>
public sealed class AlistResolver
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly string _baseUrl;
    private readonly string _username;
    private readonly string _password;
    private Task<string?>? _token;

    public AlistResolver(string baseUrl, string? username, string? password)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _username = username ?? string.Empty;
        _password = password ?? string.Empty;
    }

    public string BaseUrl => _baseUrl;

    public Task<string?> GetTokenAsync(CancellationToken ct = default) => _token ??= LoginAsync(ct);

    /// <summary>
    /// 将清单里的下载地址转换为可直接下载的地址：/d/ 与 /dav/ 链接通过 fs/get 解析 raw_url，
    /// 解析失败时退回原地址（/d/ 自动补 download=1）。
    /// </summary>
    public async Task<string> ResolveDownloadUrlAsync(string url, CancellationToken ct = default)
    {
        if (AlistUrl.TryGetStoragePath(url, out var storagePath))
        {
            var raw = await GetRawUrlAsync(storagePath, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(raw)) return raw!;
        }
        return AlistUrl.WithDownloadFlag(url);
    }

    public async Task<string?> GetRawUrlAsync(string storagePath, CancellationToken ct = default)
    {
        try
        {
            var token = await GetTokenAsync(ct).ConfigureAwait(false);
            using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/api/fs/get")
            {
                Content = JsonBody(w =>
                {
                    w.WriteString("path", storagePath);
                    w.WriteString("password", string.Empty);
                    w.WriteBoolean("refresh", false);
                })
            };
            if (!string.IsNullOrWhiteSpace(token)) req.Headers.TryAddWithoutValidation("Authorization", token);
            using var doc = await SendJsonAsync(req, ct).ConfigureAwait(false);
            if (doc is null || !TryGetData(doc, out var data)) return null;
            if (data.TryGetProperty("is_dir", out var isDir) && isDir.ValueKind == JsonValueKind.True) return null;
            if (data.TryGetProperty("raw_url", out var raw) && raw.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(raw.GetString()))
                return raw.GetString();
            if (data.TryGetProperty("sign", out var sign) && sign.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(sign.GetString()))
                return _baseUrl + "/d" + EscapePath(storagePath) + "?sign=" + Uri.EscapeDataString(sign.GetString()!);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            Logger.Warn(ex, "fs/get failed for {path}", storagePath);
            return null;
        }
    }

    private async Task<string?> LoginAsync(CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/api/auth/login")
            {
                Content = JsonBody(w =>
                {
                    w.WriteString("username", _username);
                    w.WriteString("password", _password);
                })
            };
            using var doc = await SendJsonAsync(req, ct).ConfigureAwait(false);
            if (doc is not null && TryGetData(doc, out var data) && data.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String)
                return t.GetString();
            Logger.Warn("AList login returned no token for {baseUrl}", _baseUrl);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            Logger.Warn(ex, "AList login failed for {baseUrl}", _baseUrl);
            _token = null; // 允许下次重试
            return null;
        }
    }

    private static string EscapePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static bool TryGetData(JsonDocument doc, out JsonElement data)
    {
        data = default;
        var root = doc.RootElement;
        if (root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.GetInt32() != 200) return false;
        return root.TryGetProperty("data", out data) && data.ValueKind == JsonValueKind.Object;
    }

    private static async Task<JsonDocument?> SendJsonAsync(HttpRequestMessage req, CancellationToken ct)
    {
        req.Headers.TryAddWithoutValidation("User-Agent", UpdateHttp.UserAgent);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(UpdateHttp.ApiTimeout);
        using var resp = await UpdateHttp.Client.SendAsync(req, cts.Token).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        await using var s = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(s, cancellationToken: cts.Token).ConfigureAwait(false);
    }

    private static ByteArrayContent JsonBody(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            write(w);
            w.WriteEndObject();
        }
        var content = new ByteArrayContent(buffer.WrittenSpan.ToArray());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return content;
    }
}

public static class UpdateHttp
{
    public const string UserAgent = "pan.baidu.com";
    public static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(30);

    // 进程内共享连接池，避免每次请求新建 HttpClient 造成的握手开销与套接字耗尽
    public static HttpClient Client { get; } = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(15)
    })
    { Timeout = Timeout.InfiniteTimeSpan };

    public static void ApplyHostHeaders(HttpRequestMessage req)
    {
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        if (req.RequestUri is not null && AlistUrl.IsBaiduHost(req.RequestUri.ToString()))
        {
            req.Headers.TryAddWithoutValidation("Referer", "https://pan.baidu.com/disk/home");
            req.Headers.TryAddWithoutValidation("Origin", "https://pan.baidu.com");
        }
    }

    public static async Task<string?> GetStringAsync(string url, string? token = null, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHostHeaders(req);
        if (!string.IsNullOrWhiteSpace(token)) req.Headers.TryAddWithoutValidation("Authorization", token);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ApiTimeout);
        using var resp = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
    }
}

public readonly record struct TransferProgress(long Received, long Total, double BytesPerSecond)
{
    public double Percentage => Total > 0 ? Math.Min(100, Received * 100.0 / Total) : 0;
    public bool IsIndeterminate => Total <= 0;
    public TimeSpan Remaining => Total > 0 && BytesPerSecond > 0 ? TimeSpan.FromSeconds(Math.Max(0, (Total - Received) / BytesPerSecond)) : TimeSpan.Zero;
}

/// <summary>
/// 统一下载入口：普通主机使用 Downloader 分片下载，失败自动退回 HttpClient 流式下载；
/// 百度网盘直链强制单流以避免 403。进度回调最多 10Hz。
/// </summary>
public static class UpdateDownloader
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(100);

    public static async Task<bool> DownloadAsync(string url, string outPath, int chunks, IProgress<TransferProgress>? progress, CancellationToken ct = default)
    {
        UpdateFiles.TryDelete(outPath);
        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (!AlistUrl.IsBaiduHost(url) && chunks > 1)
        {
            if (await TryDownloaderAsync(url, outPath, chunks, progress, ct).ConfigureAwait(false)) return true;
            ct.ThrowIfCancellationRequested();
            Logger.Info("Chunked download failed, falling back to single stream: {url}", url);
        }
        return await TryHttpClientAsync(url, outPath, progress, ct).ConfigureAwait(false);
    }

    private static async Task<bool> TryDownloaderAsync(string url, string outPath, int chunks, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var cfg = new DownloadConfiguration
        {
            ChunkCount = chunks,
            ParallelDownload = true,
            ParallelCount = Math.Min(chunks, 4),
            MaxTryAgainOnFailure = 3,
            Timeout = 15000,
            MinimumSizeOfChunking = 1024 * 1024,
            ReserveStorageSpaceBeforeStartingDownload = true,
            RequestConfiguration = new RequestConfiguration { Headers = new WebHeaderCollection { ["User-Agent"] = UpdateHttp.UserAgent } }
        };
        var service = new DownloadService(cfg);
        var last = Stopwatch.StartNew();
        Exception? error = null;
        var cancelled = false;
        service.DownloadProgressChanged += (_, e) =>
        {
            if (last.Elapsed < ReportInterval) return;
            last.Restart();
            progress?.Report(new TransferProgress(e.ReceivedBytesSize, e.TotalBytesToReceive, e.AverageBytesPerSecondSpeed));
        };
        service.DownloadFileCompleted += (_, e) =>
        {
            if (e is AsyncCompletedEventArgs a) { error = a.Error; cancelled = a.Cancelled; }
        };
        try
        {
            await service.DownloadFileTaskAsync(url, outPath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error ??= ex;
        }
        finally
        {
            service.Dispose();
        }
        ct.ThrowIfCancellationRequested();
        if (error is not null || cancelled || !File.Exists(outPath) || new FileInfo(outPath).Length == 0)
        {
            if (error is not null) Logger.Warn(error, "Downloader failed: {url}", url);
            UpdateFiles.TryDelete(outPath);
            return false;
        }
        var len = new FileInfo(outPath).Length;
        progress?.Report(new TransferProgress(len, len, 0));
        return true;
    }

    private static async Task<bool> TryHttpClientAsync(string url, string outPath, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            UpdateHttp.ApplyHostHeaders(req);
            using var resp = await UpdateHttp.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Logger.Warn("HTTP {code} downloading {url}", (int)resp.StatusCode, url);
                return false;
            }
            var total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous);
            var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
            try
            {
                var sw = Stopwatch.StartNew();
                var lastReport = TimeSpan.Zero;
                long received = 0;
                int n;
                while ((n = await src.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    received += n;
                    if (sw.Elapsed - lastReport < ReportInterval) continue;
                    lastReport = sw.Elapsed;
                    progress?.Report(new TransferProgress(received, total, received / Math.Max(sw.Elapsed.TotalSeconds, 0.001)));
                }
                progress?.Report(new TransferProgress(received, total > 0 ? total : received, 0));
                if (received == 0) { Logger.Warn("Empty download from {url}", url); return false; }
                if (total > 0 && received != total) { Logger.Warn("Truncated download {got}/{total} from {url}", received, total, url); return false; }
                return true;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Logger.Warn(ex, "HttpClient download failed: {url}", url);
            return false;
        }
        finally
        {
            if (!ct.IsCancellationRequested && File.Exists(outPath) && new FileInfo(outPath).Length == 0) UpdateFiles.TryDelete(outPath);
        }
    }

    /// <summary>下载 + ZIP 校验 + SHA256 校验，失败按次数重试。校验不通过不会返回 true。</summary>
    public static async Task<bool> DownloadVerifiedAsync(AlistResolver resolver, string url, string outPath, string? expectedSha256,
        int chunks, IProgress<TransferProgress>? progress, int attempts = 3, CancellationToken ct = default)
    {
        for (var i = 1; i <= attempts; i++)
        {
            ct.ThrowIfCancellationRequested();
            var finalUrl = await resolver.ResolveDownloadUrlAsync(url, ct).ConfigureAwait(false);
            Logger.Info("Download attempt {i}/{n}: {url}", i, attempts, finalUrl);
            if (await DownloadAsync(finalUrl, outPath, chunks, progress, ct).ConfigureAwait(false))
            {
                if (!UpdateFiles.IsValidZip(outPath))
                    Logger.Warn("Downloaded file is not a valid ZIP: {url}", finalUrl);
                else if (!await UpdateFiles.MatchesSha256Async(outPath, expectedSha256, ct).ConfigureAwait(false))
                    Logger.Warn("SHA256 mismatch for {url}, expected {sha}", finalUrl, expectedSha256);
                else
                    return true;
            }
            UpdateFiles.TryDelete(outPath);
            if (i < attempts) await Task.Delay(TimeSpan.FromSeconds(i), ct).ConfigureAwait(false);
        }
        return false;
    }
}
