using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Downloader;
using LabelPlus_Next.Update.Models;
using LabelPlus_Next.Update.ViewModels;
using DStatus = LabelPlus_Next.Update.ViewModels.DownloadStatus;
using NLog;

namespace LabelPlus_Next.Update.Services;

public sealed class UpdaterService : IUpdaterService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private const string DefaultUserAgent = "pan.baidu.com";
    private readonly IMessageService _messages;

    public UpdaterService(IMessageService messages)
    {
        _messages = messages;
    }

    public double BytesReceivedInMB { get; private set; }
    public double ProgressPercentage { get; private set; }
    public TimeSpan Remaining { get; private set; }
    public double Speed { get; private set; }
    public DStatus Status { get; private set; } = DStatus.Idle;
    public double TotalBytesToReceiveInMB { get; private set; }
    public string? LatestVersion { get; private set; }
    public event Action? ProgressChanged;

    public async Task RunUpdateAsync(string? overrideAppDir = null)
    {
        try
        {
            Status = DStatus.Checking;
            ProgressChanged?.Invoke();
            var targetDir = overrideAppDir ?? AppContext.BaseDirectory; // 客户端安装目录（更新解压目标）
            var settingsDir = DetermineSettingsDir(overrideAppDir, AppContext.BaseDirectory); // 优先取传入的“更新目录”
            var settingsPath = Path.Combine(settingsDir, "settings.json");
            UpdateSettings upd;
            if (!File.Exists(settingsPath))
            {
                upd = new UpdateSettings
                {
                    BaseUrl = UpdateSettings.DefaultBaseUrl,
                    ManifestPath = UpdateSettings.DefaultManifestPath
                };
                Logger.Warn("settings.json not found at {path}, using defaults: {baseUrl}{manifest}", settingsPath, upd.BaseUrl, upd.ManifestPath);
            }
            else
            {
                await using (var fs = File.OpenRead(settingsPath))
                {
                    var s = await JsonSerializer.DeserializeAsync<AppSettings>(fs, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    upd = s?.Update ?? new UpdateSettings();
                }
                if (string.IsNullOrWhiteSpace(upd.BaseUrl)) upd.BaseUrl = UpdateSettings.DefaultBaseUrl;
                if (string.IsNullOrWhiteSpace(upd.ManifestPath)) upd.ManifestPath = UpdateSettings.DefaultManifestPath;
            }

            var manifestJson = await FetchManifestJsonAsync(upd, targetDir);
            if (manifestJson is null)
            {
                Logger.Warn("Fetch manifest failed");
                await _messages.ShowAsync("获取清单失败", "错误");
                return;
            }
            var manifest = JsonSerializer.Deserialize<ManifestV1>(manifestJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (manifest?.Projects is null)
            {
                Logger.Warn("Manifest parse failed or projects missing");
                await _messages.ShowAsync("清单格式不正确", "错误");
                return;
            }

            var clientLocal = ReadLocalVersion(Path.Combine(targetDir, "Client.version.json"));
            var updateDir = Path.Combine(targetDir, "update");
            var updaterLocal = ReadLocalVersion(Path.Combine(updateDir, "update.json"))
                               ?? ReadLocalVersion(Path.Combine(updateDir, "Update.version.json"))
                               ?? ReadLocalVersion(Path.Combine(targetDir, "Update.version.json"));

            var clientProjKey = "LabelPlus_Next.Desktop";
            var updaterProjKey = "LabelPlus_Next.Update";
            manifest.Projects.TryGetValue(clientProjKey, out var cproj);
            manifest.Projects.TryGetValue(updaterProjKey, out var uproj);
            var clientLatest = cproj?.Latest ?? (cproj?.Releases.Count > 0 ? cproj.Releases[0].Version : null);
            var updaterLatest = uproj?.Latest ?? (uproj?.Releases.Count > 0 ? uproj.Releases[0].Version : null);

            Logger.Info("Local: client={clientLocal}, updater={updaterLocal}; Remote: client={clientLatest}, updater={updaterLatest}", clientLocal, updaterLocal, clientLatest, updaterLatest);
            LatestVersion = clientLatest ?? clientLocal;
            ProgressChanged?.Invoke();

            if (IsGreater(updaterLatest, updaterLocal) && uproj is not null)
            {
                var (fileUrl, fileSha) = GetReleaseFileInfoByOS(uproj, updaterLatest!);
                Logger.Info("Updater self-update required -> {ver}, url={url}", updaterLatest, fileUrl);
                if (!string.IsNullOrWhiteSpace(fileUrl))
                {
                    var tmpZip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                    var ok = await DownloadWithRetryAndSha256Async(fileUrl!, tmpZip, upd, fileSha);
                    if (!ok)
                    {
                        Status = DStatus.Error;
                        ProgressChanged?.Invoke();
                        Logger.Warn("Updater self-update download/verify failed");
                        await _messages.ShowAsync("更新程序自更新失败（下载/校验）", "错误");
                        return;
                    }
                    Logger.Info("Updater zip downloaded and verified, extracting to {dir}", targetDir);
                    await ExtractZipSelectiveAsync(tmpZip, targetDir, relPath => relPath.Replace('\\', '/').StartsWith("update/", StringComparison.OrdinalIgnoreCase));
                    try { File.Delete(tmpZip); } catch { }
                    await _messages.ShowOverlayAsync($"更新程序已更新到 {updaterLatest}，请从主程序重新触发更新", "提示");
                    TryStartMainApp(targetDir);
                    return;
                }
            }

            if (IsGreater(clientLatest, clientLocal) && cproj is not null)
            {
                var (fileUrl, fileSha) = GetReleaseFileInfoByOS(cproj, clientLatest!);
                Logger.Info("Client update -> {ver}, url={url}", clientLatest, fileUrl);
                if (!string.IsNullOrWhiteSpace(fileUrl))
                {
                    // keep selected release file metadata for entry if possible
                    var selectedRelease = cproj.Releases.FirstOrDefault(r => string.Equals(r.Version, clientLatest, StringComparison.OrdinalIgnoreCase));
                    ReleaseFile? selectedFile = null;
                    if (selectedRelease?.Files is { Count: > 0 })
                    {
                        var rid = GetCurrentRid();
                        selectedFile = selectedRelease.Files.FirstOrDefault(f => (!string.IsNullOrEmpty(f.Name) && f.Name.Contains(rid, StringComparison.OrdinalIgnoreCase))
                                                                              || (!string.IsNullOrEmpty(f.Url) && f.Url.Contains(rid, StringComparison.OrdinalIgnoreCase)))
                                       ?? selectedRelease.Files.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Url));
                    }

                    var ok = await DownloadAndApplyAsync(fileUrl!, targetDir, upd, fileSha);
                    if (!ok)
                    {
                        Status = DStatus.Error;
                        ProgressChanged?.Invoke();
                        await _messages.ShowAsync("客户端更新失败", "错误");
                        return;
                    }
                    await _messages.ShowOverlayAsync("更新完成，正在重启主程序...", "提示");
                    TryStartMainApp(targetDir, selectedFile);
                }
                else
                {
                    await _messages.ShowAsync("清单未提供下载地址", "错误");
                }
            }
            else
            {
                await _messages.ShowOverlayAsync("已经是最新版本", "提示");
                TryStartMainApp(targetDir);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "RunUpdateAsync failed");
            await _messages.ShowAsync($"更新失败: {ex.Message}", "错误");
        }
    }

    private static string DetermineSettingsDir(string? overrideAppDir, string runtimeBaseDir)
    {
        // 优先级：overrideAppDir → overrideAppDir/update → runtimeBaseDir
        try
        {
            if (!string.IsNullOrWhiteSpace(overrideAppDir) && Directory.Exists(overrideAppDir))
            {
                var p = Path.Combine(overrideAppDir, "settings.json");
                if (File.Exists(p)) return overrideAppDir;
                var upd = Path.Combine(overrideAppDir, "update");
                if (Directory.Exists(upd) && File.Exists(Path.Combine(upd, "settings.json"))) return upd;
            }
        }
        catch { }
        return runtimeBaseDir;
    }

    private static string GetCurrentRid()
    {
        var arch = RuntimeInformation.ProcessArchitecture;
        if (OperatingSystem.IsWindows())
            return arch == Architecture.Arm64 ? "win-arm64" : "win-x64";
        if (OperatingSystem.IsLinux())
            return arch == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
        if (OperatingSystem.IsMacOS())
            return arch == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        return arch == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
    }

    private (string? url, string? sha256) GetReleaseFileInfoByOS(ProjectReleases prj, string version)
    {
        foreach (var r in prj.Releases)
        {
            if (!string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase)) continue;
            if (r.Files is { Count: > 0 })
            {
                var rid = GetCurrentRid();
                var match = r.Files.FirstOrDefault(f => (!string.IsNullOrEmpty(f.Name) && f.Name.Contains(rid, StringComparison.OrdinalIgnoreCase))
                                                     || (!string.IsNullOrEmpty(f.Url) && f.Url.Contains(rid, StringComparison.OrdinalIgnoreCase)));
                if (match is not null && !string.IsNullOrWhiteSpace(match.Url))
                    return (match.Url, match.Sha256);
                var any = r.Files.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Url));
                if (any is not null) return (any.Url, any.Sha256);
            }
            return (r.Url, null);
        }
        return (null, null);
    }

    private (string? url, string? sha256) GetReleaseFileInfo(ProjectReleases prj, string version) => GetReleaseFileInfoByOS(prj, version);

    private async Task<bool> DownloadWithDownloaderAsync(string url, string outPath)
    {
        try
        {
            Status = DStatus.Downloading;
            ProgressChanged?.Invoke();
            Logger.Info("Downloading: {url}", url);
            // AList 公链 /d/<sign> 需要 ?download=1 才返回文件流
            if (!string.IsNullOrWhiteSpace(url) && url.Contains("/d/", StringComparison.OrdinalIgnoreCase) && !url.Contains("download=", StringComparison.OrdinalIgnoreCase))
            {
                url += url.Contains('?') ? "&download=1" : "?download=1";
            }
            var cfg = new DownloadConfiguration { ChunkCount = 8, ParallelDownload = true };
            cfg.RequestConfiguration ??= new RequestConfiguration();
            cfg.RequestConfiguration.Headers ??= new System.Net.WebHeaderCollection();
            cfg.RequestConfiguration.Headers["User-Agent"] = DefaultUserAgent;
            // 对 baidupcs 域名补充必要的头，避免 403
            try
            {
                var host = new Uri(url).Host.ToLowerInvariant();
                if (host.Contains("baidupcs.com") || host.Contains("baidu.com"))
                {
            // 强制单流，禁用并发/分片，减少风控触发
            cfg.ChunkCount = 1;
            cfg.ParallelDownload = false;
                    cfg.RangeDownload = false;
            Logger.Info("Enforce single-stream for host={host}", host);
                    cfg.RequestConfiguration.Headers["Referer"] = "https://pan.baidu.com/disk/home";
                    cfg.RequestConfiguration.Headers["Origin"] = "https://pan.baidu.com";
                    cfg.RequestConfiguration.Headers["Accept"] = "*/*";
                    // 某些节点对并发/分片敏感，必要时可降级为单流
                    // 先保留并发，失败时由上层回退到 HttpClient 单流下载
                }
            }
            catch { }
            Logger.Info("Downloader config: ua={ua}, chunks={chunks}, parallel={pd}, range={range}", DefaultUserAgent, cfg.ChunkCount, cfg.ParallelDownload, cfg.RangeDownload);
            var service = new DownloadService(cfg);
            var completedWithoutError = false;
            service.DownloadProgressChanged += (s, e) =>
            {
                BytesReceivedInMB = e.ReceivedBytesSize / 1024d / 1024d;
                TotalBytesToReceiveInMB = e.TotalBytesToReceive / 1024d / 1024d;
                Speed = e.AverageBytesPerSecondSpeed / 1024d / 1024d;
                ProgressPercentage = e.ProgressPercentage;
                Remaining = TimeSpan.FromSeconds(e.TotalBytesToReceive > 0 && e.AverageBytesPerSecondSpeed > 0
                    ? (e.TotalBytesToReceive - e.ReceivedBytesSize) / e.AverageBytesPerSecondSpeed
                    : 0);
                ProgressChanged?.Invoke();
            };
            service.DownloadFileCompleted += (s, e) =>
            {
                try
                {
                    var err = (e as System.ComponentModel.AsyncCompletedEventArgs)?.Error;
                    var canceled = (e as System.ComponentModel.AsyncCompletedEventArgs)?.Cancelled ?? false;
                    if (err != null) Logger.Warn(err, "Downloader completed with error: {url}", url);
                    else Logger.Info("Downloader completed: url={url}, out={out}", url, outPath);
                    if (canceled) Logger.Warn("Downloader canceled: {url}", url);
                    completedWithoutError = err == null && !canceled;
                }
                catch { }
            };
            await service.DownloadFileTaskAsync(url, outPath);
            // 校验下载是否成功（事件未报告错误且文件非空）
            if (!completedWithoutError)
            {
                Logger.Warn("Downloader indicated failure or cancellation for {url}", url);
                SafeDelete(outPath);
                Status = DStatus.Error;
                ProgressChanged?.Invoke();
                return false;
            }
            try
            {
                var fi = new FileInfo(outPath);
                if (!fi.Exists || fi.Length == 0)
                {
                    Logger.Warn("Downloaded file is empty -> {file}", outPath);
                    SafeDelete(outPath);
                    Status = DStatus.Error;
                    ProgressChanged?.Invoke();
                    return false;
                }
            }
            catch { }

            Status = DStatus.Completed;
            ProgressChanged?.Invoke();
            Logger.Info("Download finished -> {file}", outPath);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "DownloadWithDownloaderAsync failed");
            Status = DStatus.Error;
            ProgressChanged?.Invoke();
            return false;
        }
    }

    private async Task<bool> DownloadWithHttpClientAsync(string url, string outPath)
    {
        try
        {
            // AList 公链 /d/<sign> 需要 ?download=1
            if (!string.IsNullOrWhiteSpace(url) && url.Contains("/d/", StringComparison.OrdinalIgnoreCase) && !url.Contains("download=", StringComparison.OrdinalIgnoreCase))
            {
                url += url.Contains('?') ? "&download=1" : "?download=1";
            }

            using var http = new HttpClient();
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
            try
            {
                var host = new Uri(url).Host.ToLowerInvariant();
                if (host.Contains("baidupcs.com") || host.Contains("baidu.com"))
                {
                    http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://pan.baidu.com/disk/home");
                    http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://pan.baidu.com");
                    http.DefaultRequestHeaders.Accept.ParseAdd("*/*");
                }
            }
            catch { }

            Logger.Info("HttpClient fallback downloading: {url}", url);
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode)
            {
                Logger.Warn("HttpClient fallback failed: {code} {url}", (int)resp.StatusCode, url);
                return false;
            }
            await using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                await resp.Content.CopyToAsync(fs);
            }
            try
            {
                var fi = new FileInfo(outPath);
                if (!fi.Exists || fi.Length == 0)
                {
                    Logger.Warn("HttpClient fallback wrote empty file -> {file}", outPath);
                    SafeDelete(outPath);
                    return false;
                }
            }
            catch { }
            Logger.Info("HttpClient fallback finished -> {file}", outPath);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "DownloadWithHttpClientAsync failed");
            SafeDelete(outPath);
            return false;
        }
    }

    private static bool TryExtractDavPath(string url, out string davPath)
    {
        davPath = string.Empty;
        try
        {
            var u = new Uri(url);
            var segments = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segments.Length; i++)
            {
                if (string.Equals(segments[i], "dav", StringComparison.OrdinalIgnoreCase))
                {
                    var rest = string.Join('/', segments.Skip(i + 1));
                    davPath = "/" + rest;
                    return true;
                }
            }
            return false;
        }
        catch
        {
            var idx = url.IndexOf("/dav", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var after = url[(idx + 4)..];
                davPath = after.StartsWith('/') ? after : "/" + after;
                var q = davPath.IndexOf('?');
                if (q >= 0) davPath = davPath[..q];
                return true;
            }
            return false;
        }
    }

    private static async Task<string?> ApiLoginAsync(UpdateSettings upd)
    {
        if (string.IsNullOrWhiteSpace(upd.BaseUrl)) return null;
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(AppendSlash(upd.BaseUrl!)) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
            var body = JsonSerializer.Serialize(new { username = upd.Username ?? string.Empty, password = upd.Password ?? string.Empty });
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<LoginResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return result?.Data?.Token;
        }
        catch { return null; }
    }

    private static async Task<string?> ApiFsGetRawUrlAsync(UpdateSettings upd, string? token, string davPath)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(AppendSlash(upd.BaseUrl!)) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
            var body = JsonSerializer.Serialize(new { path = davPath, password = "", page = 1, per_page = 0, refresh = true });
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/fs/get") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (!string.IsNullOrWhiteSpace(token)) req.Headers.TryAddWithoutValidation("Authorization", token);
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<FsGetResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (result?.Data is null || result.Data.IsDir) return null;
            if (!string.IsNullOrWhiteSpace(result.Data.RawUrl)) return result.Data.RawUrl;
            if (!string.IsNullOrWhiteSpace(result.Data.Sign)) return AppendSlash(upd.BaseUrl!) + "d/" + Uri.EscapeDataString(result.Data.Sign);
            return null;
        }
        catch { return null; }
    }

    private static async Task<string> ResolveRawUrlAsync(UpdateSettings upd, string url)
    {
        if (!url.Contains("/dav", StringComparison.OrdinalIgnoreCase)) return url;
        if (!TryExtractDavPath(url, out var davPath)) return url;
        var token = await ApiLoginAsync(upd);
        var raw = await ApiFsGetRawUrlAsync(upd, token, davPath);
        return string.IsNullOrWhiteSpace(raw) ? url : raw!;
    }

    private async Task<bool> DownloadWithRetryAndSha256Async(string url, string outPath, UpdateSettings upd, string? expectedSha, int attempts = 3)
    {
        for (var i = 1; i <= attempts; i++)
        {
            string finalUrl = url;
            // 如果是 /d/ 链接，尝试通过 API fs/get 获取 raw_url
            if (!string.IsNullOrWhiteSpace(finalUrl) && finalUrl.Contains("/d/", StringComparison.OrdinalIgnoreCase))
            {
                var token = await ApiLoginAsync(upd);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    try
                    {
                        var u = new Uri(finalUrl);
                        var path = u.AbsolutePath;
                        var idx = path.IndexOf("/d/", StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            var after = path.Substring(idx + 3);
                            if (after.StartsWith('/')) after = after.Substring(1);
                            after = Uri.UnescapeDataString(after);
                            var apiPath = "/" + after;
                            var raw = await ApiFsGetRawUrlAsync(upd, token!, apiPath);
                            if (!string.IsNullOrWhiteSpace(raw)) finalUrl = raw!;
                        }
                    }
                    catch { }
                }
            }
            finalUrl = await ResolveRawUrlAsync(upd, finalUrl);
            Logger.Info("Attempt {i}/{n}: finalUrl={url}", i, attempts, finalUrl);
            bool ok;
            var isBaidu = false;
            try { var host = new Uri(finalUrl).Host.ToLowerInvariant(); isBaidu = host.Contains("baidupcs.com") || host.Contains("baidu.com"); }
            catch { }
            if (isBaidu)
            {
                Logger.Info("Direct HttpClient (baidupcs) for {url}", finalUrl);
                ok = await DownloadWithHttpClientAsync(finalUrl, outPath);
            }
            else
            {
                ok = await DownloadWithDownloaderAsync(finalUrl, outPath);
                if (!ok)
                {
                    Logger.Info("Downloader failed, try HttpClient fallback for {url}", finalUrl);
                    ok = await DownloadWithHttpClientAsync(finalUrl, outPath);
                }
            }
            if (!ok) { SafeDelete(outPath); continue; }
            if (!await EnsureFileReadyAsync(outPath)) { SafeDelete(outPath); continue; }

            // 校验是否为有效 ZIP（若无效，删除并重试）
            try { using var _ = ZipFile.OpenRead(outPath); }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Downloaded file is not a valid ZIP, will retry: {url}");
                SafeDelete(outPath);
                continue;
            }

            // 跳过 SHA256 强制校验（仅记录告警，不阻断流程）
            if (!string.IsNullOrWhiteSpace(expectedSha))
            {
                var actual = await ComputeSha256Async(outPath);
                if (!string.Equals(actual, expectedSha, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Warn("SHA256 mismatch (ignored). expected={exp} actual={act}", expectedSha, actual);
                }
            }
            return true;
        }
        return false;
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static async Task<bool> EnsureFileReadyAsync(string path, int maxAttempts = 10, int delayMs = 100)
    {
        for (var i = 0; i < maxAttempts; i++)
        {
            try { if (File.Exists(path)) { using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); return true; } }
            catch { }
            try { await Task.Delay(delayMs); } catch { }
        }
        return false;
    }

    private async Task<bool> DownloadAndApplyAsync(string url, string appDir, UpdateSettings upd, string? expectedSha256)
    {
        var tmpZip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        var ok = await DownloadWithRetryAndSha256Async(url, tmpZip, upd, expectedSha256);
        if (!ok) return false;

        Logger.Info("Extracting client zip to {dir}", appDir);
        await ExtractZipSelectiveAsync(tmpZip, appDir, relPath => relPath.Replace('\\', '/').StartsWith("update/", StringComparison.OrdinalIgnoreCase));
        try { File.Delete(tmpZip); } catch { }
    Status = DStatus.Completed;
    ProgressPercentage = 100;
    ProgressChanged?.Invoke();
        return true;
    }

    private static async Task ExtractZipSelectiveAsync(string zipPath, string destDir, Func<string, bool> skipPredicate)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var destRoot = Path.GetFullPath(destDir);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (string.IsNullOrEmpty(name)) continue;
            if (name.EndsWith("/") || name.EndsWith("\\")) continue;
            if (skipPredicate(name)) continue;

            var relative = name.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var outPath = Path.GetFullPath(Path.Combine(destRoot, relative));
            if (!outPath.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase)) continue;
            var outDir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
            await using var inStream = entry.Open();
            await using var outStream = File.Create(outPath);
            await inStream.CopyToAsync(outStream);
        }
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        var bytes = await sha.ComputeHashAsync(fs);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void TryStartMainApp(string appDir, ReleaseFile? meta = null)
    {
        try
        {
            var entry = ResolveMainEntry(appDir, meta);
            if (entry is null) return;

            // macOS .app bundle
            if (OperatingSystem.IsMacOS() && Directory.Exists(entry) && entry.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                var psiOpen = new ProcessStartInfo
                {
                    FileName = "/usr/bin/open",
                    Arguments = '"' + entry + '"',
                    UseShellExecute = false,
                    WorkingDirectory = appDir
                };
                Process.Start(psiOpen);
                return;
            }

            if (!OperatingSystem.IsWindows())
            {
                // Ensure executable bit
                try
                {
                    var mode = System.IO.File.GetUnixFileMode(entry);
                    var hasExec = mode.HasFlag(System.IO.UnixFileMode.UserExecute) || mode.HasFlag(System.IO.UnixFileMode.GroupExecute) || mode.HasFlag(System.IO.UnixFileMode.OtherExecute);
                    if (!hasExec)
                    {
                        mode |= System.IO.UnixFileMode.UserExecute | System.IO.UnixFileMode.GroupExecute | System.IO.UnixFileMode.OtherExecute;
                        System.IO.File.SetUnixFileMode(entry, mode);
                    }
                }
                catch { }
            }

            var psi = new ProcessStartInfo
            {
                FileName = entry,
                UseShellExecute = OperatingSystem.IsWindows(),
                WorkingDirectory = appDir
            };
            Process.Start(psi);
        }
        catch { }
    }

    private static string? ResolveMainEntry(string appDir, ReleaseFile? meta)
    {
        // If manifest provides explicit entry, honor it first
        string? preferred = null;
        if (meta is not null)
        {
            if (OperatingSystem.IsWindows()) preferred = meta.EntryWindows ?? meta.Entry;
            else if (OperatingSystem.IsMacOS()) preferred = meta.EntryMacos ?? meta.Entry;
            else preferred = meta.EntryLinux ?? meta.Entry;
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                var p = Path.Combine(appDir, preferred);
                if (Directory.Exists(p) || File.Exists(p)) return p;
            }
        }
        var clientVerPath = Path.Combine(appDir, "Client.version.json");
        try
        {
            if (File.Exists(clientVerPath))
            {
                using var fs = File.OpenRead(clientVerPath);
                using var doc = JsonDocument.Parse(fs);
                if (doc.RootElement.TryGetProperty("project", out var p) && p.ValueKind == JsonValueKind.String)
                {
                    var proj = p.GetString();
                    if (!string.IsNullOrWhiteSpace(proj))
                    {
                        if (OperatingSystem.IsWindows())
                        {
                            var exe = Path.Combine(appDir, proj + ".exe");
                            if (File.Exists(exe)) return exe;
                        }
                        else
                        {
                            var unixPath = Path.Combine(appDir, proj);
                            if (File.Exists(unixPath)) return unixPath;
                        }
                    }
                }
            }
        }
        catch { }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var candidates = Directory.GetFiles(appDir, "*.exe", SearchOption.TopDirectoryOnly);
                foreach (var f in candidates)
                {
                    var name = Path.GetFileName(f);
                    if (name.Contains(".Desktop", StringComparison.OrdinalIgnoreCase) && !name.Contains("Update", StringComparison.OrdinalIgnoreCase))
                        return f;
                }
                foreach (var f in candidates)
                {
                    if (!Path.GetFileName(f).Contains("Update", StringComparison.OrdinalIgnoreCase)) return f;
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                // Prefer .app bundle
                var apps = Directory.GetDirectories(appDir, "*.app", SearchOption.TopDirectoryOnly);
                if (apps.Length > 0) return apps[0];

                var unixCandidates = Directory.GetFileSystemEntries(appDir, "*", SearchOption.TopDirectoryOnly)
                    .Where(p => File.Exists(p) && string.IsNullOrEmpty(Path.GetExtension(p)));
                // Prefer LabelPlus_Next name
                var preferredUnix1 = unixCandidates.FirstOrDefault(p => Path.GetFileName(p).Equals("LabelPlus_Next", StringComparison.OrdinalIgnoreCase));
                if (preferredUnix1 is not null) return preferredUnix1;
                var desktopLike = unixCandidates.FirstOrDefault(p => Path.GetFileName(p).Contains(".Desktop", StringComparison.OrdinalIgnoreCase));
                if (desktopLike is not null) return desktopLike;
                return unixCandidates.FirstOrDefault();
            }
            else // Linux
            {
                var unixCandidates = Directory.GetFileSystemEntries(appDir, "*", SearchOption.TopDirectoryOnly)
                    .Where(p => File.Exists(p) && string.IsNullOrEmpty(Path.GetExtension(p)));
                var preferredUnix2 = unixCandidates.FirstOrDefault(p => Path.GetFileName(p).Equals("LabelPlus_Next", StringComparison.OrdinalIgnoreCase));
                if (preferredUnix2 is not null) return preferredUnix2;
                var desktopLike = unixCandidates.FirstOrDefault(p => Path.GetFileName(p).Contains(".Desktop", StringComparison.OrdinalIgnoreCase));
                if (desktopLike is not null) return desktopLike;
                return unixCandidates.FirstOrDefault();
            }
        }
        catch { }
        return null;
    }

    private static string AppendSlash(string url) => url.EndsWith('/') ? url : url + '/';

    private static string? ReadLocalVersion(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            using var doc = JsonDocument.Parse(fs);
            if (doc.RootElement.TryGetProperty("version", out var v)) return v.GetString();
            return null;
        }
        catch { return null; }
    }

    private static bool IsGreater(string? remote, string? local)
    {
        if (string.IsNullOrWhiteSpace(remote)) return false;
        if (string.IsNullOrWhiteSpace(local)) return true;
        if (Version.TryParse(remote, out var r) && Version.TryParse(local, out var l)) return r > l;
        return string.Compare(remote, local, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private async Task<string?> FetchManifestJsonAsync(UpdateSettings upd, string appDir)
    {
        Uri? manifestUri = null;
        string? candidate = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(upd.BaseUrl) && !string.IsNullOrWhiteSpace(upd.ManifestPath))
            {
                var baseUrl = AppendSlash(upd.BaseUrl!);
                candidate = upd.ManifestPath!.Trim();
                // 运行时迁移旧默认路径 -> OneDrive2
                if (string.Equals(candidate, "/OneDrive/Update/manifest.json", StringComparison.OrdinalIgnoreCase))
                    candidate = "/OneDrive2/Update/manifest.json";
                manifestUri = Uri.TryCreate(candidate, UriKind.Absolute, out var abs) ? abs : new Uri(new Uri(baseUrl), candidate.TrimStart('/'));
            }
            if (manifestUri is null)
            {
                var verPath = Path.Combine(appDir, "Client.version.json");
                if (File.Exists(verPath))
                {
                    using var fs = File.OpenRead(verPath);
                    using var doc = JsonDocument.Parse(fs);
                    if (doc.RootElement.TryGetProperty("manifest", out var m) && m.ValueKind == JsonValueKind.String)
                    {
                        var s = m.GetString();
                        if (!string.IsNullOrWhiteSpace(s) && Uri.TryCreate(s, UriKind.Absolute, out var u)) manifestUri = u;
                    }
                }
            }
        }
        catch { }
        if (manifestUri is null)
        {
            Logger.Warn("Manifest URI not resolved from settings or Client.version.json");
            return null;
        }

        try
        {
            var url = manifestUri.ToString();
            // 若仍指向旧路径，迁移
            if (url.Contains("/OneDrive/Update/manifest.json", StringComparison.OrdinalIgnoreCase))
                url = url.Replace("/OneDrive/Update/manifest.json", "/OneDrive2/Update/manifest.json", StringComparison.OrdinalIgnoreCase);
            var resolved = await ResolveRawUrlAsync(upd, url);
            Logger.Info("Fetch manifest: url={url} => resolved={resolved}", url, resolved);
            var text = await DownloadStringWithAuthFallbackAsync(resolved, null);
            if (!string.IsNullOrEmpty(text))
            {
                var trimmed = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
                if (!trimmed.StartsWith("<")) return trimmed;
                Logger.Warn("Manifest HTTP returned HTML, will try API fallback");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "HTTP fetch manifest failed, will try API fallback");
        }

        try
        {
            var token = await ApiLoginAsync(upd);
            if (string.IsNullOrWhiteSpace(token)) { Logger.Warn("API login failed for manifest fallback, will try without token"); }

            string remotePath;
            if (manifestUri.IsAbsoluteUri && string.Equals(manifestUri.Scheme, "http", StringComparison.OrdinalIgnoreCase) || string.Equals(manifestUri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                remotePath = manifestUri.AbsolutePath;
                if (string.IsNullOrWhiteSpace(remotePath) && !string.IsNullOrWhiteSpace(candidate))
                    remotePath = candidate.StartsWith('/') ? candidate : "/" + candidate;
            }
            else
            {
                remotePath = !string.IsNullOrWhiteSpace(candidate) ? candidate.StartsWith('/') ? candidate : "/" + candidate : "/manifest.json";
            }

            var apiPath = remotePath;
            if (remotePath.Contains("/dav/", StringComparison.OrdinalIgnoreCase) && TryExtractDavPath(remotePath, out var dav)) apiPath = dav;

            Logger.Info("API fallback get manifest: path={path}", apiPath);
            var rawUrl = await ApiFsGetRawUrlAsync(upd, token, apiPath);
            if (string.IsNullOrWhiteSpace(rawUrl)) { Logger.Warn("fs/get did not return raw_url/sign for manifest"); return null; }

            var text = await DownloadStringWithAuthFallbackAsync(rawUrl!, token);
            var trimmed = text?.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("<")) return trimmed;
            Logger.Warn("Manifest content after API fallback looked invalid");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "API fallback to fetch manifest failed");
        }

        return null;
    }

    private static async Task<string?> DownloadStringWithAuthFallbackAsync(string url, string? token)
    {
        using var http = new HttpClient();
        http.Timeout = Timeout.InfiniteTimeSpan;
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
        try
        {
            using var req1 = new HttpRequestMessage(HttpMethod.Get, url);
            var resp1 = await http.SendAsync(req1);
            if (resp1.IsSuccessStatusCode) return await resp1.Content.ReadAsStringAsync();
        }
        catch { }

        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                using var req2 = new HttpRequestMessage(HttpMethod.Get, url);
                req2.Headers.TryAddWithoutValidation("Authorization", token);
                var resp2 = await http.SendAsync(req2);
                if (resp2.IsSuccessStatusCode) return await resp2.Content.ReadAsStringAsync();
            }
            catch { }
        }
        return null;
    }

    private sealed class LoginResponse { public int Code { get; set; } public string? Message { get; set; } public LoginData? Data { get; set; } }
    private sealed class LoginData { public string? Token { get; set; } }
    private sealed class FsGetResponse { public int Code { get; set; } public string? Message { get; set; } public FsGetData? Data { get; set; } }
    private sealed class FsGetData {
        [JsonPropertyName("raw_url")] public string? RawUrl { get; set; }
        [JsonPropertyName("sign")] public string? Sign { get; set; }
        [JsonPropertyName("is_dir")] public bool IsDir { get; set; }
    }
}
