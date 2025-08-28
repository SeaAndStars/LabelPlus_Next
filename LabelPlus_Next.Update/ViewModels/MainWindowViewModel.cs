using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Downloader;
using LabelPlus_Next.Update.Models;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Ursa.Controls;
using System.Diagnostics;
using System.Net.Http;
using NLog;

namespace LabelPlus_Next.Update.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        [ObservableProperty] private DownloadStatus status = DownloadStatus.Idle;
        [ObservableProperty] private double progressPercentage;
        [ObservableProperty] private double speed; // MB/s
        [ObservableProperty] private double bytesReceivedInMB;
        [ObservableProperty] private double totalBytesToReceiveInMB;
        [ObservableProperty] private TimeSpan remaining;
        [ObservableProperty] private string? version;

        private string? _overrideAppDir;

        public void OverrideAppDir(string dir) => _overrideAppDir = dir;

        public IRelayCommand StartCommand { get; }
        public IRelayCommand StopCommand { get; }
        public IRelayCommand RestartCommand { get; }

        public MainWindowViewModel()
        {
            StartCommand = new RelayCommand(async () => await RunUpdateAsync());
            StopCommand = new RelayCommand(() => { /* no-op */ });
            RestartCommand = new RelayCommand(async () => await RunUpdateAsync());
        }

        public Task RunUpdateAsyncPublic() => RunUpdateAsync();

        private async Task RunUpdateAsync()
        {
            try
            {
                Status = DownloadStatus.Checking;
                var appDir = _overrideAppDir ?? AppContext.BaseDirectory;
                var settingsPath = Path.Combine(appDir, "settings.json");
                UpdateSettings upd;
                if (!File.Exists(settingsPath))
                {
                    // Use hard-coded defaults when settings.json is missing
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

                    // Ensure defaults are applied if values are blank
                    if (string.IsNullOrWhiteSpace(upd.BaseUrl)) upd.BaseUrl = UpdateSettings.DefaultBaseUrl;
                    if (string.IsNullOrWhiteSpace(upd.ManifestPath)) upd.ManifestPath = UpdateSettings.DefaultManifestPath;
                }

                var manifestJson = await FetchManifestJsonAsync(upd, appDir);
                if (manifestJson is null)
                {
                    Logger.Warn("Fetch manifest failed");
                    await MessageBox.ShowAsync("获取清单失败", "错误");
                    return;
                }
                var manifest = JsonSerializer.Deserialize<ManifestV1>(manifestJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (manifest?.Projects is null)
                {
                    Logger.Warn("Manifest parse failed or projects missing");
                    await MessageBox.ShowAsync("清单格式不正确", "错误");
                    return;
                }

                // Local versions
                var clientLocal = ReadLocalVersion(Path.Combine(appDir, "Client.version.json"));
                var updateDir = Path.Combine(appDir, "update");
                var updaterLocal = ReadLocalVersion(Path.Combine(updateDir, "update.json"))
                                   ?? ReadLocalVersion(Path.Combine(updateDir, "Update.version.json"))
                                   ?? ReadLocalVersion(Path.Combine(appDir, "Update.version.json"));

                var clientProjKey = "LabelPlus_Next.Desktop";
                var updaterProjKey = "LabelPlus_Next.Update";
                manifest.Projects.TryGetValue(clientProjKey, out var cproj);
                manifest.Projects.TryGetValue(updaterProjKey, out var uproj);
                var clientLatest = cproj?.Latest ?? (cproj?.Releases.Count > 0 ? cproj.Releases[0].Version : null);
                var updaterLatest = uproj?.Latest ?? (uproj?.Releases.Count > 0 ? uproj.Releases[0].Version : null);

                Logger.Info("Local: client={clientLocal}, updater={updaterLocal}; Remote: client={clientLatest}, updater={updaterLatest}", clientLocal, updaterLocal, clientLatest, updaterLatest);

                // If updater itself is outdated, attempt to self-update with retry and DAV handling
                if (IsGreater(updaterLatest, updaterLocal) && uproj is not null)
                {
                    var (fileUrl, fileSha) = GetReleaseFileInfo(uproj, updaterLatest!);
                    Logger.Info("Updater self-update required -> {ver}, url={url}", updaterLatest, fileUrl);
                    if (!string.IsNullOrWhiteSpace(fileUrl))
                    {
                        var tmpZip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                        var ok = await DownloadWithRetryAndSha256Async(fileUrl!, tmpZip, upd, fileSha, attempts: 3);
                        if (!ok)
                        {
                            Status = DownloadStatus.Error;
                            Logger.Warn("Updater self-update download/verify failed");
                            await MessageBox.ShowAsync("更新程序自更新失败（下载/校验）", "错误");
                            return;
                        }
                        Logger.Info("Updater zip downloaded and verified, extracting to {dir}", appDir);
                        // Extract selectively, skip any file under 'update/' folder
                        await ExtractZipSelectiveAsync(tmpZip, appDir, relPath =>
                        {
                            var p = relPath.Replace('\\', '/');
                            return p.StartsWith("update/", StringComparison.OrdinalIgnoreCase);
                        });
                        try { File.Delete(tmpZip); } catch { }
                        await MessageBox.ShowOverlayAsync($"更新程序已更新到 {updaterLatest}，请从主程序重新触发更新", "提示");
                        (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
                        return;
                    }
                }

                if (IsGreater(clientLatest, clientLocal) && cproj is not null)
                {
                    var (fileUrl, fileSha) = GetReleaseFileInfo(cproj, clientLatest!);
                    Logger.Info("Client update -> {ver}, url={url}", clientLatest, fileUrl);
                    if (!string.IsNullOrWhiteSpace(fileUrl))
                    {
                        var ok = await DownloadAndApplyAsync(fileUrl!, appDir, upd, fileSha);
                        if (!ok)
                        {
                            Status = DownloadStatus.Error;
                            await MessageBox.ShowAsync("客户端更新失败", "错误");
                            return;
                        }
                        await MessageBox.ShowOverlayAsync("更新完成，正在重启主程序...", "提示");
                        TryStartMainApp(appDir);
                        (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
                        return;
                    }
                    else
                    {
                        await MessageBox.ShowAsync("清单未提供下载地址", "错误");
                    }
                }
                else
                {
                    await MessageBox.ShowOverlayAsync("已经是最新版本", "提示");
                    TryStartMainApp(appDir);
                    (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "RunUpdateAsync failed");
                await MessageBox.ShowAsync($"更新失败: {ex.Message}", "错误");
            }
        }

        private (string? url, string? sha256) GetReleaseFileInfo(ProjectReleases prj, string version)
        {
            foreach (var r in prj.Releases)
            {
                if (string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase))
                {
                    if (r.Files != null && r.Files.Count > 0)
                    {
                        var f = r.Files.Find(f => !string.IsNullOrEmpty(f.Url)) ?? r.Files[0];
                        return (f.Url, f.Sha256);
                    }
                    return (r.Url, null);
                }
            }
            return (null, null);
        }

        private async Task<bool> DownloadWithDownloaderAsync(string url, string outPath)
        {
            try
            {
                Status = DownloadStatus.Downloading;
                Logger.Info("Downloading: {url}", url);
                var cfg = new DownloadConfiguration
                {
                    ChunkCount = 16, // Number of file parts, default is 1
                    ParallelDownload = true // Download parts in parallel (default is false)
                };
                var service = new DownloadService(cfg);
                service.DownloadProgressChanged += (s, e) =>
                {
                    BytesReceivedInMB = e.ReceivedBytesSize / 1024d / 1024d;
                    TotalBytesToReceiveInMB = e.TotalBytesToReceive / 1024d / 1024d;
                    Speed = e.AverageBytesPerSecondSpeed / 1024d / 1024d;
                    ProgressPercentage = e.ProgressPercentage;
                    Remaining = TimeSpan.FromSeconds(e.TotalBytesToReceive > 0 && e.AverageBytesPerSecondSpeed > 0
                        ? (e.TotalBytesToReceive - e.ReceivedBytesSize) / e.AverageBytesPerSecondSpeed
                        : 0);
                };
                await service.DownloadFileTaskAsync(url, outPath);
                Status = DownloadStatus.Completed;
                Logger.Info("Download finished -> {file}", outPath);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "DownloadWithDownloaderAsync failed");
                Status = DownloadStatus.Error;
                return false;
            }
        }

        // DAV + API helpers ... unchanged below but add logging
        private static bool TryExtractDavPath(string url, out string davPath)
        {
            davPath = string.Empty;
            try
            {
                var u = new Uri(url);
                var segments = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < segments.Length; i++)
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
                var body = JsonSerializer.Serialize(new { username = upd.Username ?? string.Empty, password = upd.Password ?? string.Empty });
                using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                using var resp = await http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return null;
                var json = await resp.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<LoginResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                return result?.Data?.Token;
            }
            catch { return null; }
        }

        private static async Task<string?> ApiFsGetRawUrlAsync(UpdateSettings upd, string token, string davPath)
        {
            try
            {
                using var http = new HttpClient { BaseAddress = new Uri(AppendSlash(upd.BaseUrl!)) };
                var body = JsonSerializer.Serialize(new { path = davPath, password = "", page = 1, per_page = 0, refresh = true });
                using var req = new HttpRequestMessage(HttpMethod.Post, "/api/fs/get")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                req.Headers.TryAddWithoutValidation("Authorization", token);
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
            if (string.IsNullOrWhiteSpace(token)) return url;
            var raw = await ApiFsGetRawUrlAsync(upd, token, davPath);
            return string.IsNullOrWhiteSpace(raw) ? url : raw!;
        }

        private async Task<bool> DownloadWithRetryAndSha256Async(string url, string outPath, UpdateSettings upd, string? expectedSha, int attempts = 3)
        {
            for (int i = 1; i <= attempts; i++)
            {
                var finalUrl = await ResolveRawUrlAsync(upd, url);
                Logger.Info("Attempt {i}/{n}: {url}", i, attempts, finalUrl);
                var ok = await DownloadWithDownloaderAsync(finalUrl, outPath);
                if (!ok) { SafeDelete(outPath); continue; }
                if (!await EnsureFileReadyAsync(outPath)) { SafeDelete(outPath); continue; }

                if (!string.IsNullOrWhiteSpace(expectedSha))
                {
                    var actual = await ComputeSha256Async(outPath);
                    if (!string.Equals(actual, expectedSha, StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Warn("SHA256 mismatch. expected={exp} actual={act}", expectedSha, actual);
                        SafeDelete(outPath);
                        if (i == attempts) return false; // give up
                        continue; // retry
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
            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        return true;
                    }
                }
                catch { }
                try { await Task.Delay(delayMs); } catch { }
            }
            return false;
        }

        private async Task<bool> DownloadAndApplyAsync(string url, string appDir, UpdateSettings upd, string? expectedSha256)
        {
            var tmpZip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
            var ok = await DownloadWithRetryAndSha256Async(url, tmpZip, upd, expectedSha256, attempts: 3);
            if (!ok) return false;

            Logger.Info("Extracting client zip to {dir}", appDir);
            // Extract selectively, skip any file under 'update/' folder
            await ExtractZipSelectiveAsync(tmpZip, appDir, relPath =>
            {
                var p = relPath.Replace('\\', '/');
                return p.StartsWith("update/", StringComparison.OrdinalIgnoreCase);
            });
            try { File.Delete(tmpZip); } catch { }
            Status = DownloadStatus.Completed;
            ProgressPercentage = 100;
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
                if (name.EndsWith("/") || name.EndsWith("\\")) continue; // directory entry
                if (skipPredicate(name)) continue;

                var relative = name.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                var outPath = Path.GetFullPath(Path.Combine(destRoot, relative));
                if (!outPath.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase)) continue; // prevent path traversal
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

        private static void TryStartMainApp(string appDir)
        {
            try
            {
                var exe = ResolveMainExe(appDir);
                if (exe is null) return;
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                    WorkingDirectory = appDir
                };
                Process.Start(psi);
            }
            catch { }
        }

        private static string? ResolveMainExe(string appDir)
        {
            // Prefer using Client.version.json's project field
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
                            var exe = Path.Combine(appDir, proj + ".exe");
                            if (File.Exists(exe)) return exe;
                        }
                    }
                }
            }
            catch { }

            // Fallback search: *.Desktop*.exe in root
            try
            {
                var candidates = Directory.GetFiles(appDir, "*.exe", SearchOption.TopDirectoryOnly);
                foreach (var f in candidates)
                {
                    var name = Path.GetFileName(f);
                    if (name.Contains(".Desktop", StringComparison.OrdinalIgnoreCase) && !name.Contains("Update", StringComparison.OrdinalIgnoreCase))
                        return f;
                }
                // fallback to any exe except updater
                foreach (var f in candidates)
                {
                    if (!Path.GetFileName(f).Contains("Update", StringComparison.OrdinalIgnoreCase)) return f;
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
            if (System.Version.TryParse(remote, out var r) && System.Version.TryParse(local, out var l)) return r > l;
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

            // 1) Try HTTP fetch (with DAV to raw resolution)
            try
            {
                var url = manifestUri.ToString();
                var resolved = await ResolveRawUrlAsync(upd, url);
                Logger.Info("Fetch manifest: url={url} => resolved={resolved}", url, resolved);
                var text = await DownloadStringWithAuthFallbackAsync(resolved, token: null);
                if (!string.IsNullOrEmpty(text))
                {
                    var trimmed = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
                    if (!trimmed.StartsWith("<")) return trimmed; // likely JSON
                    Logger.Warn("Manifest HTTP returned HTML, will try API fallback");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "HTTP fetch manifest failed, will try API fallback");
            }

            // 2) API fallback via /api/fs/get
            try
            {
                var token = await ApiLoginAsync(upd);
                if (string.IsNullOrWhiteSpace(token)) { Logger.Warn("API login failed for manifest fallback"); return null; }

                string remotePath;
                if (manifestUri.IsAbsoluteUri && string.Equals(manifestUri.Scheme, "http", StringComparison.OrdinalIgnoreCase) || string.Equals(manifestUri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                {
                    // If absolute to the same server, use its AbsolutePath; otherwise try candidate text
                    remotePath = manifestUri.AbsolutePath;
                    if (string.IsNullOrWhiteSpace(remotePath) && !string.IsNullOrWhiteSpace(candidate))
                        remotePath = candidate.StartsWith('/') ? candidate : "/" + candidate;
                }
                else
                {
                    remotePath = !string.IsNullOrWhiteSpace(candidate) ? (candidate.StartsWith('/') ? candidate : "/" + candidate) : "/manifest.json";
                }

                // If it's a DAV path we can still use davPath; otherwise use the path as-is
                string apiPath = remotePath;
                if (remotePath.Contains("/dav/", StringComparison.OrdinalIgnoreCase) && TryExtractDavPath(remotePath, out var dav)) apiPath = dav;

                Logger.Info("API fallback get manifest: path={path}", apiPath);
                var rawUrl = await ApiFsGetRawUrlAsync(upd, token!, apiPath);
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
            try
            {
                using var req1 = new HttpRequestMessage(HttpMethod.Get, url);
                using var resp1 = await http.SendAsync(req1);
                if (resp1.IsSuccessStatusCode)
                {
                    var t = await resp1.Content.ReadAsStringAsync();
                    return t;
                }
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(token))
            {
                try
                {
                    using var req2 = new HttpRequestMessage(HttpMethod.Get, url);
                    req2.Headers.TryAddWithoutValidation("Authorization", token);
                    using var resp2 = await http.SendAsync(req2);
                    if (resp2.IsSuccessStatusCode)
                    {
                        var t = await resp2.Content.ReadAsStringAsync();
                        return t;
                    }
                }
                catch { }
            }
            return null;
        }

        // Minimal DTOs for API responses
        private sealed class LoginResponse
        {
            [JsonPropertyName("code")] public int Code { get; set; }
            [JsonPropertyName("message")] public string? Message { get; set; }
            [JsonPropertyName("data")] public LoginData? Data { get; set; }
        }
        private sealed class LoginData { [JsonPropertyName("token")] public string? Token { get; set; } }

        private sealed class FsGetResponse
        {
            [JsonPropertyName("code")] public int Code { get; set; }
            [JsonPropertyName("message")] public string? Message { get; set; }
            [JsonPropertyName("data")] public FsGetData? Data { get; set; }
        }
        private sealed class FsGetData
        {
            [JsonPropertyName("raw_url")] public string? RawUrl { get; set; }
            [JsonPropertyName("sign")] public string? Sign { get; set; }
            [JsonPropertyName("is_dir")] public bool IsDir { get; set; }
        }
    }
}
