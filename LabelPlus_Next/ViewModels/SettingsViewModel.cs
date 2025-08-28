using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Services;
using LabelPlus_Next.Serialization;
using LabelPlus_Next.Models;
using NLog;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using LabelPlus_Next.Services.Api;
using Downloader;

namespace LabelPlus_Next.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly ISettingsService _settingsService;

    [ObservableProperty] private string? baseUrl;
    [ObservableProperty] private string? manifestPath = "manifest.json";
    [ObservableProperty] private string? username;
    [ObservableProperty] private string? password;

    // Old single-field display (kept for compatibility)
    [ObservableProperty] private string? currentVersion;
    [ObservableProperty] private string? latestVersion;

    // New detailed fields
    [ObservableProperty] private string? currentClientVersion;
    [ObservableProperty] private string? currentUpdaterVersion;
    [ObservableProperty] private string? latestClientVersion;
    [ObservableProperty] private string? latestUpdaterVersion;

    [ObservableProperty] private string? updateNotes;
    [ObservableProperty] private string? status;

    // Progress UI
    [ObservableProperty] private string? updateTask;
    [ObservableProperty] private double updateProgress; // 0-100
    [ObservableProperty] private bool isProgressIndeterminate;

    public IAsyncRelayCommand VerifyHttpCommand { get; }
    public IAsyncRelayCommand CheckUpdateCommand { get; }

    public SettingsViewModel() : this(new JsonSettingsService()) { }

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        VerifyHttpCommand = new AsyncRelayCommand(VerifyHttpAsync);
        CheckUpdateCommand = new AsyncRelayCommand(CheckAndUpdateOnStartupAsync);
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        try
        {
            Logger.Info("Loading update settings...");
            var s = await _settingsService.LoadAsync();
            BaseUrl = s.Update.BaseUrl;
            ManifestPath = s.Update.ManifestPath ?? "manifest.json";
            Username = s.Update.Username;
            Password = s.Update.Password;
            Status = "设置已加载";
            Logger.Info("Settings loaded: baseUrl={baseUrl}, manifestPath={manifest}", BaseUrl, ManifestPath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Load settings failed");
            Status = $"加载失败: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        try
        {
            Logger.Info("Saving update settings... baseUrl={baseUrl}, manifestPath={manifest}", BaseUrl, ManifestPath);
            var s = new AppSettings
            {
                Update = new UpdateSettings
                {
                    BaseUrl = BaseUrl,
                    ManifestPath = ManifestPath,
                    Username = Username,
                    Password = Password
                }
            };
            await _settingsService.SaveAsync(s);
            Status = "保存成功";
            Logger.Info("Settings saved");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Save settings failed");
            Status = $"保存失败: {ex.Message}";
        }
    }

    public async Task VerifyHttpAsync()
    {
        try
        {
            Logger.Info("Verify via API download... baseUrl={baseUrl}, manifestPath={manifest}", BaseUrl, ManifestPath);
            if (string.IsNullOrWhiteSpace(BaseUrl)) { Status = "请填写 BaseUrl"; Logger.Warn("BaseUrl is empty"); return; }
            if (string.IsNullOrWhiteSpace(ManifestPath)) { Status = "请填写 Manifest 路径"; Logger.Warn("ManifestPath is empty"); return; }

            // 1) 使用用户名/密码获取 token（如有配置）
            string? token = null;
            if (!string.IsNullOrWhiteSpace(Username))
            {
                Logger.Info("Attempt login via AuthApi with username={user}", Username);
                var auth = new AuthApi(BaseUrl!);
                var login = await auth.LoginAsync(Username!, Password ?? string.Empty);
                if (login.Code == 200 && login.Data is not null && !string.IsNullOrWhiteSpace(login.Data.Token))
                {
                    token = login.Data.Token;
                    Logger.Info("Login success");
                }
                else
                {
                    Logger.Warn("Login failed: code={code}, message={msg}", login.Code, login.Message);
                    Status = $"登录失败: {login.Code} {login.Message}";
                    return;
                }
            }
            else
            {
                Status = "未配置用户名/密码，将无法通过 API 下载";
                Logger.Warn("Username/Password not configured");
                return;
            }

            // 2) 解析 manifest 远端路径
            string filePath;
            if (Uri.TryCreate(ManifestPath, UriKind.Absolute, out var abs))
                filePath = abs.AbsolutePath;
            else
            {
                var p = ManifestPath!.Replace('\\', '/');
                filePath = p.StartsWith('/') ? p : "/" + p;
            }
            Logger.Debug("Resolved manifest api filePath={path}", filePath);

            // 3) 使用 FileSystemApi.DownloadAsync 获取 raw_url 并下载
            Logger.Info("Begin API download of manifest...");
            var fs = new FileSystemApi(BaseUrl!);
            var result = await fs.DownloadAsync(token!, filePath);
            if (result.Code != 200 || result.Content is null)
            {
                Logger.Warn("Download failed: code={code}, message={msg}", result.Code, result.Message);
                Status = $"下载失败: {result.Code} {result.Message}";
                return;
            }
            Logger.Info("Download success: bytes={len}", result.Content.Length);

            // 4) 验证清单 JSON 结构（移除 UTF-8 BOM 并裁剪前后空白）
            var bytes = result.Content;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                Logger.Debug("Detected UTF-8 BOM in manifest, stripping");
                bytes = bytes[3..];
            }
            var json = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Trim();

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("projects", out _))
                {
                    Status = "API 下载验证成功";
                    Logger.Info("Manifest schema valid (projects found)");
                }
                else
                {
                    Status = "API 下载验证失败：清单缺少 projects";
                    Logger.Warn("Manifest schema invalid: projects missing");
                }
            }
            catch (JsonException jx)
            {
                // 内容可能不是 JSON（例如 HTML 或压缩包），记录前 64 字节十六进制以便诊断
                var previewLen = Math.Min(64, bytes.Length);
                var hex = BitConverter.ToString(bytes, 0, previewLen).Replace("-", string.Empty);
                Logger.Error(jx, "Manifest JSON parse failed. FirstBytesHex={hex}", hex);
                Status = $"验证失败：清单不是有效 JSON（{jx.Message}）";
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Verify via API failed");
            Status = $"验证异常: {ex.Message}";
        }
    }

    // 开机检查：优先检查并更新 Updater，自身更新完成后，若 Client 有更新，直接启动 updater 处理（不在主程序下载 Client）
    public async Task CheckAndUpdateOnStartupAsync()
    {
        try
        {
            Logger.Info("Startup update check...");
            var manifestJson = await DownloadManifestViaApiAsync();
            if (manifestJson is null)
            {
                Logger.Warn("Manifest download failed");
                return;
            }

            var manifest = JsonSerializer.Deserialize(manifestJson, AppJsonContext.Default.ManifestV1);
            if (manifest?.Projects is null)
            {
                Logger.Warn("Manifest parse failed or projects missing");
                return;
            }

            var appDir = AppContext.BaseDirectory;
            var updateDir = Path.Combine(appDir, "update");
            Directory.CreateDirectory(updateDir);

            string? clientLocal = ReadLocalVersion(Path.Combine(appDir, "Client.version.json"));
            string? updaterLocal = ReadLocalVersion(Path.Combine(updateDir, "Update.version.json"))
                                   ?? ReadLocalVersion(Path.Combine(appDir, "Update.version.json"));

            manifest.Projects.TryGetValue("LabelPlus_Next.Desktop", out var cproj);
            manifest.Projects.TryGetValue("LabelPlus_Next.Update", out var uproj);
            var clientLatest = cproj?.Latest ?? (cproj?.Releases.Count > 0 ? cproj.Releases[0].Version : null);
            var updaterLatest = uproj?.Latest ?? (uproj?.Releases.Count > 0 ? uproj.Releases[0].Version : null);

            bool needUpdater = IsGreater(updaterLatest, updaterLocal);
            bool needClient = IsGreater(clientLatest, clientLocal);

            // 1) 优先更新 Updater
            if (needUpdater && uproj is not null)
            {
                var (upUrl, upSha) = GetReleaseFileInfo(uproj, updaterLatest!);
                if (!string.IsNullOrWhiteSpace(upUrl))
                {
                    UpdateTask = $"下载更新程序 {updaterLatest}..."; IsProgressIndeterminate = false; UpdateProgress = 0;
                    var zipPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                    var ok = await DownloadZipAutoAsync(upUrl!, zipPath);
                    if (!ok)
                    {
                        Logger.Warn("Updater download failed");
                        return;
                    }
                    if (!await EnsureFileReadyAsync(zipPath)) { Logger.Warn("Updater file not present after download"); return; }

                    if (!string.IsNullOrWhiteSpace(upSha))
                    {
                        var actual = await ComputeSha256Async(zipPath);
                        if (!string.Equals(actual, upSha, StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Warn("Updater SHA256 mismatch. expected={exp} actual={act}", upSha, actual);
                            SafeDelete(zipPath);
                            return;
                        }
                    }
                    UpdateTask = "解压更新程序..."; IsProgressIndeterminate = true; UpdateProgress = 0;
                    ZipFile.ExtractToDirectory(zipPath, updateDir, overwriteFiles: true);
                    SafeDelete(zipPath);
                    UpdateTask = null; IsProgressIndeterminate = false; UpdateProgress = 0;
                    Logger.Info("Updater updated to {ver}", updaterLatest);
                }
            }

            // 2) 如 Client 有更新：直接启动 Updater 处理（不在主程序下载 Client）
            if (needClient)
            {
                var updaterExe = Path.Combine(updateDir, "LabelPlus_Next.Update.exe");
                if (!File.Exists(updaterExe))
                {
                    var fallback = Path.Combine(appDir, "LabelPlus_Next.Update.exe");
                    if (File.Exists(fallback)) updaterExe = fallback;
                }
                if (File.Exists(updaterExe))
                {
                    try
                    {
                        Status = "启动更新程序...";
                        var pid = Process.GetCurrentProcess().Id;
                        var psi = new ProcessStartInfo
                        {
                            FileName = updaterExe,
                            UseShellExecute = false,
                            WorkingDirectory = Path.GetDirectoryName(updaterExe) ?? updateDir
                        };
                        psi.ArgumentList.Add("--waitpid");
                        psi.ArgumentList.Add(pid.ToString());
                        psi.ArgumentList.Add("--targetpath");
                        psi.ArgumentList.Add(appDir);
                        Process.Start(psi);
                        var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                        lifetime?.Shutdown();
                        return;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Failed to start updater for client update");
                    }
                }
                else
                {
                    Logger.Warn("Updater exe not found to start client update");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Startup update check failed");
        }
    }

    private static (string? url, string? sha256) GetReleaseFileInfo(ProjectReleases prj, string version)
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

    private async Task<bool> DownloadZipAutoAsync(string url, string outPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(url) && url.Contains("/dav", StringComparison.OrdinalIgnoreCase))
            {
                if (TryExtractDavPath(url, out var davPath) && !string.IsNullOrEmpty(BaseUrl))
                {
                    // Login and resolve raw url via API
                    var auth = new AuthApi(BaseUrl!);
                    var login = await auth.LoginAsync(Username ?? string.Empty, Password ?? string.Empty);
                    if (login.Code == 200 && !string.IsNullOrWhiteSpace(login.Data?.Token))
                    {
                        var token = login.Data!.Token!;
                        var fs = new FileSystemApi(BaseUrl!);
                        var meta = await fs.GetAsync(token, davPath);
                        if (meta.Code == 200 && meta.Data is not null && !meta.Data.IsDir)
                        {
                            var raw = !string.IsNullOrWhiteSpace(meta.Data.RawUrl)
                                ? meta.Data.RawUrl!
                                : (string.IsNullOrWhiteSpace(meta.Data.Sign) ? null : BaseUrl!.TrimEnd('/') + "/d/" + Uri.EscapeDataString(meta.Data.Sign!));
                            if (!string.IsNullOrWhiteSpace(raw))
                            {
                                return await DownloadWithDownloaderAsync(raw!, outPath);
                            }
                        }
                    }
                }
                // Fallback to direct download
                return await DownloadWithDownloaderAsync(url, outPath);
            }
            else
            {
                return await DownloadWithDownloaderAsync(url, outPath);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "DownloadZipAutoAsync failed for url={url}");
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
            // Naive fallback
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

    private async Task<bool> DownloadWithDownloaderAsync(string url, string outPath)
    {
        try
        {
            var cfg = new DownloadConfiguration
            {
                BufferBlockSize = 10240,
                ChunkCount = 8,
                MaximumBytesPerSecond = 0,
                MaxTryAgainOnFailure = 5,
                MaximumMemoryBufferBytes = 50 * 1024 * 1024,
                ParallelDownload = true,
                ParallelCount = 4,
                Timeout = 10000,
                RangeDownload = false,
                MinimumSizeOfChunking = 102400,
                MinimumChunkSize = 10240,
                ReserveStorageSpaceBeforeStartingDownload = true,
                EnableLiveStreaming = false
            };
            var service = new DownloadService(cfg);
            service.DownloadProgressChanged += (s, e) =>
            {
                UpdateProgress = e.ProgressPercentage;
                IsProgressIndeterminate = false;
            };
            service.DownloadFileCompleted += (s, e) =>
            {
                UpdateProgress = 100;
                IsProgressIndeterminate = false;
            };
            await service.DownloadFileTaskAsync(url, outPath);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Downloader failed: {url}", url);
            IsProgressIndeterminate = false;
            return false;
        }
    }

    private async Task<string?> DownloadManifestViaApiAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(ManifestPath)) return null;
            var auth = new AuthApi(BaseUrl!);
            var login = await auth.LoginAsync(Username ?? string.Empty, Password ?? string.Empty);
            if (login.Code != 200 || string.IsNullOrWhiteSpace(login.Data?.Token)) return null;
            var token = login.Data!.Token!;
            string filePath;
            if (Uri.TryCreate(ManifestPath, UriKind.Absolute, out var abs)) filePath = abs.AbsolutePath; else filePath = ManifestPath!.StartsWith('/') ? ManifestPath! : "/" + ManifestPath!;
            var fs = new FileSystemApi(BaseUrl!);
            var result = await fs.DownloadAsync(token, filePath);
            if (result.Code != 200 || result.Content is null) return null;
            var bytes = result.Content;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) bytes = bytes[3..];
            return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Trim();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "DownloadManifestViaApiAsync failed");
            return null;
        }
    }

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

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var fs = File.OpenRead(path);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = await sha.ComputeHashAsync(fs);
        return Convert.ToHexString(bytes).ToLowerInvariant();
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

        try
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
        catch { }
        return null;
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
}
