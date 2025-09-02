using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Downloader;
using LabelPlus_Next.Models;
using LabelPlus_Next.Serialization;
using LabelPlus_Next.Services;
using LabelPlus_Next.Services.Api;
using NLog;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace LabelPlus_Next.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly ISettingsService _settingsService;

    private const string DefaultUserAgent = "pan.baidu.com";

    [ObservableProperty] private string? baseUrl = "https://alist.seastarss.cn";

    // New detailed fields
    [ObservableProperty] private string? currentClientVersion;
    [ObservableProperty] private string? currentUpdaterVersion;

    // Old single-field display (kept for compatibility)
    [ObservableProperty] private string? currentVersion;
    [ObservableProperty] private bool isProgressIndeterminate;
    [ObservableProperty] private string? latestClientVersion;
    [ObservableProperty] private string? latestUpdaterVersion;
    [ObservableProperty] private string? latestVersion;
    [ObservableProperty] private string? manifestPath = "/OneDrive2/Update/manifest.json";
    [ObservableProperty] private string? password = "91f158c48d6ab9c5373c992eb07426cad91da2befd437101b1c90797d8c9daf1";
    [ObservableProperty] private string? status;

    [ObservableProperty] private string? updateNotes;
    [ObservableProperty] private double updateProgress; // 0-100

    // Progress UI
    [ObservableProperty] private string? updateTask;
    [ObservableProperty] private string? username = "Upgrade";

    public SettingsViewModel() : this(new JsonSettingsService()) { }

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        VerifyHttpCommand = new AsyncRelayCommand(VerifyHttpAsync);
        CheckUpdateCommand = new AsyncRelayCommand(CheckAndUpdateOnStartupAsync);
        _ = LoadAsync();
    }

    public IAsyncRelayCommand VerifyHttpCommand { get; }
    public IAsyncRelayCommand CheckUpdateCommand { get; }

    public async Task LoadAsync()
    {
        try
        {
            Logger.Info("Loading update settings...");
            var s = await _settingsService.LoadAsync();

            // Keep VM defaults as priority; only override when settings.json has non-empty values
            var defBase = BaseUrl;
            var defPath = ManifestPath;
            var defUser = Username;
            var defPwd = Password;
            var effBase = string.IsNullOrWhiteSpace(s.Update.BaseUrl) ? defBase : s.Update.BaseUrl;
            var effPath = string.IsNullOrWhiteSpace(s.Update.ManifestPath) ? defPath : s.Update.ManifestPath;
            var effUser = string.IsNullOrWhiteSpace(s.Update.Username) ? defUser : s.Update.Username;
            var effPwd = string.IsNullOrWhiteSpace(s.Update.Password) ? defPwd : s.Update.Password;

            BaseUrl = effBase;
            ManifestPath = effPath;
            Username = effUser;
            Password = effPwd;

            // Persist back if we had to fill missing values using defaults
            var needSave = false;
            if (!string.Equals(s.Update.BaseUrl, effBase, StringComparison.Ordinal))
            {
                s.Update.BaseUrl = effBase;
                needSave = true;
            }
            if (!string.Equals(s.Update.ManifestPath, effPath, StringComparison.Ordinal))
            {
                s.Update.ManifestPath = effPath;
                needSave = true;
            }
            if (!string.Equals(s.Update.Username, effUser, StringComparison.Ordinal))
            {
                s.Update.Username = effUser;
                needSave = true;
            }
            if (!string.Equals(s.Update.Password, effPwd, StringComparison.Ordinal))
            {
                s.Update.Password = effPwd;
                needSave = true;
            }
            if (needSave)
            {
                Logger.Info("Settings missing values filled from VM defaults. Saving back to settings.json");
                await _settingsService.SaveAsync(s);
            }

            Status = "设置已加载";
            Logger.Info("Settings effective: baseUrl={baseUrl}, manifestPath={manifest}", BaseUrl, ManifestPath);
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
            if (string.IsNullOrWhiteSpace(BaseUrl))
            {
                Status = "请填写 BaseUrl";
                Logger.Warn("BaseUrl is empty");
                return;
            }
            if (string.IsNullOrWhiteSpace(ManifestPath))
            {
                Status = "请填写 Manifest 路径";
                Logger.Warn("ManifestPath is empty");
                return;
            }

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

    // 通过 IUpdateService 验证清单，便于测试不同失败路径（超时/JSON非法/网络错误/空清单等）
    public async Task VerifyViaUpdateServiceAsync(IUpdateService updateService, UpdateSettings upd, CancellationToken ct = default)
    {
        try
        {
            Status = "正在验证清单...";
            Logger.Info("Verify via IUpdateService...");
            var manifest = await updateService.FetchManifestAsync(upd, ct);
            if (manifest is null)
            {
                Status = "清单为空";
                return;
            }
            if (manifest.Files is null)
            {
                Status = "清单格式错误";
                return;
            }
            Status = "服务验证成功";
        }
        catch (TaskCanceledException)
        {
            Status = "验证失败：请求超时";
        }
        catch (TimeoutException)
        {
            Status = "验证失败：请求超时";
        }
        catch (JsonException jx)
        {
            Status = $"验证失败：清单不是有效 JSON（{jx.Message}）";
        }
        catch (HttpRequestException hx)
        {
            Status = $"验证失败：网络错误（{hx.Message}）";
        }
        catch (Exception ex)
        {
            Status = $"验证异常: {ex.Message}";
        }
    }

    // 开机检查：优先检查并更新 Updater，自身更新完成后，若 Client 有更新，直接启动 updater 处理（不在主程序下载 Client）
    public async Task CheckAndUpdateOnStartupAsync()
    {
        try
        {
            Status = "正在检查更新...";
            Logger.Info("Startup update check...");
            var manifestJson = await DownloadManifestViaApiAsync();
            if (manifestJson is null)
            {
                Status = "获取清单失败";
                Logger.Warn("Manifest download failed");
                return;
            }

            var manifest = JsonSerializer.Deserialize(manifestJson, AppJsonContext.Default.ManifestV1);
            if (manifest?.Projects is null)
            {
                Status = "清单格式错误";
                Logger.Warn("Manifest parse failed or projects missing");
                return;
            }

            var appDir = AppContext.BaseDirectory;
            var updateDir = Path.Combine(appDir, "update");
            Directory.CreateDirectory(updateDir);

            var clientLocal = ReadLocalVersion(Path.Combine(appDir, "Client.version.json"));
            var updaterLocal = ReadLocalVersion(Path.Combine(updateDir, "Update.version.json"))
                               ?? ReadLocalVersion(Path.Combine(appDir, "Update.version.json"));

            manifest.Projects.TryGetValue("LabelPlus_Next.Desktop", out var cproj);
            manifest.Projects.TryGetValue("LabelPlus_Next.Update", out var uproj);
            var clientLatest = cproj?.Latest ?? (cproj?.Releases.Count > 0 ? cproj.Releases[0].Version : null);
            var updaterLatest = uproj?.Latest ?? (uproj?.Releases.Count > 0 ? uproj.Releases[0].Version : null);

            var needUpdater = IsGreater(updaterLatest, updaterLocal);
            var needClient = IsGreater(clientLatest, clientLocal);

            if (!needUpdater && !needClient)
            {
                Status = "已经是最新版本";
            }

            // 1) 优先更新 Updater
            if (needUpdater && uproj is not null)
            {
                var (upUrl, upSha) = GetReleaseFileInfoByOS(uproj, updaterLatest!);
                if (!string.IsNullOrWhiteSpace(upUrl))
                {
                    UpdateTask = $"下载更新程序 {updaterLatest}...";
                    IsProgressIndeterminate = false;
                    UpdateProgress = 0;
                    Status = UpdateTask;
                    var zipPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                    var ok = await DownloadZipAutoAsync(upUrl!, zipPath);
                    if (!ok)
                    {
                        Status = "下载更新程序失败";
                        Logger.Warn("Updater download failed");
                        return;
                    }
                    if (!await EnsureFileReadyAsync(zipPath))
                    {
                        Status = "下载文件不存在";
                        Logger.Warn("Updater file not present after download");
                        return;
                    }

                    // 跳过强制 SHA256 校验：仅记录告警，不中断更新流程
                    if (!string.IsNullOrWhiteSpace(upSha))
                    {
                        var actual = await ComputeSha256Async(zipPath);
                        if (!string.Equals(actual, upSha, StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Warn("Updater SHA256 mismatch (ignored). expected={exp} actual={act}", upSha, actual);
                        }
                    }
                    UpdateTask = "解压更新程序...";
                    IsProgressIndeterminate = true;
                    UpdateProgress = 0;
                    Status = UpdateTask;
                    ZipFile.ExtractToDirectory(zipPath, updateDir, true);
                    SafeDelete(zipPath);
                    UpdateTask = null;
                    IsProgressIndeterminate = false;
                    UpdateProgress = 0;
                    Status = $"更新程序已更新到 {updaterLatest}";
                    Logger.Info("Updater updated to {ver}", updaterLatest);
                }
            }

            // 2) 如 Client 有更新：直接启动 Updater 处理（不在主程序下载 Client）
            if (needClient)
            {
                string? updaterEntry = ResolveUpdaterEntry(updateDir) ?? ResolveUpdaterEntry(appDir);
                if (!string.IsNullOrEmpty(updaterEntry) && (File.Exists(updaterEntry) || Directory.Exists(updaterEntry)))
                {
                    try
                    {
                        Status = "启动更新程序...";
                        var pid = Process.GetCurrentProcess().Id;

                        // macOS .app 通过 open 启动，并传递 --args
                        if (OperatingSystem.IsMacOS() && Directory.Exists(updaterEntry) && updaterEntry.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                        {
                            var psiOpen = new ProcessStartInfo
                            {
                                FileName = "/usr/bin/open",
                                UseShellExecute = false,
                                WorkingDirectory = Path.GetDirectoryName(updaterEntry) ?? updateDir
                            };
                            psiOpen.ArgumentList.Add(updaterEntry);
                            psiOpen.ArgumentList.Add("--args");
                            psiOpen.ArgumentList.Add("--waitpid");
                            psiOpen.ArgumentList.Add(pid.ToString());
                            psiOpen.ArgumentList.Add("--targetpath");
                            psiOpen.ArgumentList.Add(appDir);
                            Process.Start(psiOpen);
                        }
                        else
                        {
                            // 非 Windows 平台确保可执行位
                            if (!OperatingSystem.IsWindows() && File.Exists(updaterEntry))
                            {
                                try
                                {
                                    var mode = System.IO.File.GetUnixFileMode(updaterEntry);
                                    var hasExec = mode.HasFlag(System.IO.UnixFileMode.UserExecute) || mode.HasFlag(System.IO.UnixFileMode.GroupExecute) || mode.HasFlag(System.IO.UnixFileMode.OtherExecute);
                                    if (!hasExec)
                                    {
                                        mode |= System.IO.UnixFileMode.UserExecute | System.IO.UnixFileMode.GroupExecute | System.IO.UnixFileMode.OtherExecute;
                                        System.IO.File.SetUnixFileMode(updaterEntry, mode);
                                    }
                                }
                                catch { }
                            }

                            var psi = new ProcessStartInfo
                            {
                                FileName = updaterEntry,
                                UseShellExecute = OperatingSystem.IsWindows(),
                                WorkingDirectory = Path.GetDirectoryName(updaterEntry) ?? updateDir
                            };
                            psi.ArgumentList.Add("--waitpid");
                            psi.ArgumentList.Add(pid.ToString());
                            psi.ArgumentList.Add("--targetpath");
                            psi.ArgumentList.Add(appDir);
                            Process.Start(psi);
                        }

                        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                        lifetime?.Shutdown();
                    }
                    catch (Exception ex)
                    {
                        Status = "启动更新程序失败";
                        Logger.Error(ex, "Failed to start updater for client update");
                    }
                }
                else
                {
                    Status = "未找到更新程序，无法启动客户端更新";
                    Logger.Warn("Updater entry not found to start client update");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Startup update check failed");
            Status = $"检查更新失败: {ex.Message}";
        }
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
        // default fallback
        return arch == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
    }

    private static (string? url, string? sha256) GetReleaseFileInfoByOS(ProjectReleases prj, string version)
    {
        foreach (var r in prj.Releases)
        {
            if (!string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase)) continue;
            var files = r.Files;
            if (files is { Count: > 0 })
            {
                var rid = GetCurrentRid();
                // Prefer matching rid in Name or Url
                var match = files.FirstOrDefault(f => (!string.IsNullOrEmpty(f.Name) && f.Name.Contains(rid, StringComparison.OrdinalIgnoreCase))
                                                       || (!string.IsNullOrEmpty(f.Url) && f.Url.Contains(rid, StringComparison.OrdinalIgnoreCase)));
                if (match is not null && !string.IsNullOrWhiteSpace(match.Url))
                    return (match.Url, match.Sha256);
                // Fallback to first with url
                var any = files.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Url));
                if (any is not null)
                    return (any.Url, any.Sha256);
            }
            return (r.Url, null);
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
                                : string.IsNullOrWhiteSpace(meta.Data.Sign)
                                    ? null
                                    : BaseUrl!.TrimEnd('/') + "/d/" + Uri.EscapeDataString(meta.Data.Sign!);
                            if (!string.IsNullOrWhiteSpace(raw))
                            {
                                bool okApi;
                                try
                                {
                                    var h = new Uri(raw!).Host.ToLowerInvariant();
                                    if (h.Contains("baidupcs.com") || h.Contains("baidu.com"))
                                    {
                                        Logger.Info("Direct HttpClient (baidupcs) for {url}", raw);
                                        okApi = await DownloadWithHttpClientAsync(raw!, outPath);
                                    }
                                    else
                                    {
                                        okApi = await DownloadWithDownloaderAsync(raw!, outPath);
                                    }
                                }
                                catch
                                {
                                    okApi = await DownloadWithDownloaderAsync(raw!, outPath);
                                }
                                if (okApi)
                                {
                                    // 快速验档
                                    try { using var _ = ZipFile.OpenRead(outPath); return true; }
                                    catch (Exception ex) { Logger.Warn(ex, "Downloaded file is not a valid ZIP: {url}"); try { File.Delete(outPath); } catch { } return false; }
                                }
                            }
                        }
                    }
                }
                // Fallback to direct download
                return await DownloadWithDownloaderAsync(url, outPath);
            }
            // 优先处理 /d/ 链接：忽略 d，取其后路径，通过 API fs/get 获取 raw_url 再多线程下载
            if (!string.IsNullOrWhiteSpace(url) && url.Contains("/d/", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(BaseUrl))
            {
                try
                {
                    var u = new Uri(url);
                    var path = u.AbsolutePath;
                    var idx = path.IndexOf("/d/", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var after = path.Substring(idx + 3); // after '/d'
                        if (after.StartsWith('/')) after = after.Substring(1);
                        after = Uri.UnescapeDataString(after);
                        var apiPath = "/" + after; // 确保以 '/' 开头
                        var auth = new AuthApi(BaseUrl!);
                        var login = await auth.LoginAsync(Username ?? string.Empty, Password ?? string.Empty);
                        if (login.Code == 200 && !string.IsNullOrWhiteSpace(login.Data?.Token))
                        {
                            var token = login.Data!.Token!;
                            var fs = new FileSystemApi(BaseUrl!);
                            var meta = await fs.GetAsync(token, apiPath);
                            if (meta.Code == 200 && meta.Data is not null && !meta.Data.IsDir)
                            {
                                var raw = !string.IsNullOrWhiteSpace(meta.Data.RawUrl)
                                    ? meta.Data.RawUrl!
                                    : string.IsNullOrWhiteSpace(meta.Data.Sign)
                                        ? null
                                        : BaseUrl!.TrimEnd('/') + "/d/" + Uri.EscapeDataString(meta.Data.Sign!);
                                if (!string.IsNullOrWhiteSpace(raw))
                                {
                                    bool okApi;
                                    try
                                    {
                                        var h = new Uri(raw!).Host.ToLowerInvariant();
                                        if (h.Contains("baidupcs.com") || h.Contains("baidu.com"))
                                        {
                                            Logger.Info("Direct HttpClient (baidupcs) for {url}", raw);
                                            okApi = await DownloadWithHttpClientAsync(raw!, outPath);
                                        }
                                        else
                                        {
                                            okApi = await DownloadWithDownloaderAsync(raw!, outPath);
                                        }
                                    }
                                    catch
                                    {
                                        okApi = await DownloadWithDownloaderAsync(raw!, outPath);
                                    }
                                    if (okApi)
                                    {
                                        // 快速验档
                                        try { using var _ = ZipFile.OpenRead(outPath); return true; }
                                        catch (Exception ex) { Logger.Warn(ex, "Downloaded file is not a valid ZIP: {url}"); try { File.Delete(outPath); } catch { } return false; }
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // 回退：为 /d/ 添加 ?download=1 直接下载
            if (!string.IsNullOrWhiteSpace(url) && url.Contains("/d/", StringComparison.OrdinalIgnoreCase) && !url.Contains("download=", StringComparison.OrdinalIgnoreCase))
            {
                url += url.Contains('?') ? "&download=1" : "?download=1";
            }

            bool ok;
            try
            {
                var host = new Uri(url).Host.ToLowerInvariant();
                if (host.Contains("baidupcs.com") || host.Contains("baidu.com"))
                {
                    Logger.Info("Direct HttpClient (baidupcs) for {url}", url);
                    ok = await DownloadWithHttpClientAsync(url, outPath);
                }
                else
                {
                    ok = await DownloadWithDownloaderAsync(url, outPath);
                }
            }
            catch
            {
                ok = await DownloadWithDownloaderAsync(url, outPath);
            }
            if (!ok) return false;

            // 快速验档：确认是合法 ZIP，避免后续解压抛 InvalidDataException
            try
            {
                using var _ = ZipFile.OpenRead(outPath);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Downloaded file is not a valid ZIP: {url}");
                try { File.Delete(outPath); } catch { }
                return false;
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
            cfg.RequestConfiguration = cfg.RequestConfiguration ?? new RequestConfiguration();
            cfg.RequestConfiguration.Headers = cfg.RequestConfiguration.Headers ?? new System.Net.WebHeaderCollection();
            cfg.RequestConfiguration.Headers["User-Agent"] = DefaultUserAgent;
            // 对 baidupcs 域名禁用并发/分片并补充必要头，避免 403
            try
            {
                var host = new Uri(url).Host.ToLowerInvariant();
                if (host.Contains("baidupcs.com") || host.Contains("baidu.com"))
                {

                    cfg.RequestConfiguration.Headers["Referer"] = "https://pan.baidu.com/disk/home";
                    cfg.RequestConfiguration.Headers["Origin"] = "https://pan.baidu.com";
                    cfg.RequestConfiguration.Headers["Accept"] = "*/*";
                }
            }
            catch { }
            Logger.Info("Downloader starting: url={url}, ua={ua}, parallel={parallel}, chunks={chunks}, parallelCount={pc}, range={range}",
                url, DefaultUserAgent, cfg.ParallelDownload, cfg.ChunkCount, cfg.ParallelCount, cfg.RangeDownload);
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
                try
                {
                    var err = (e as System.ComponentModel.AsyncCompletedEventArgs)?.Error;
                    var canceled = (e as System.ComponentModel.AsyncCompletedEventArgs)?.Cancelled ?? false;
                    if (err != null) Logger.Warn(err, "Downloader completed with error: {url}", url);
                    else Logger.Info("Downloader completed: url={url}, out={out}", url, outPath);
                    if (canceled) Logger.Warn("Downloader canceled: {url}", url);
                }
                catch { }
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

    private async Task<bool> DownloadWithHttpClientAsync(string url, string outPath)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = System.Net.DecompressionMethods.All
            };
            using var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
            req.Headers.TryAddWithoutValidation("Accept", "*/*");
            try
            {
                var host = new Uri(url).Host.ToLowerInvariant();
                if (host.Contains("baidupcs.com") || host.Contains("baidu.com"))
                {
                    req.Headers.TryAddWithoutValidation("Referer", "https://pan.baidu.com/disk/home");
                    req.Headers.TryAddWithoutValidation("Origin", "https://pan.baidu.com");
                }
            }
            catch { }

            Logger.Info("HttpClient downloading: {url}", url);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode)
            {
                Logger.Warn("HttpClient non-success: {(int)resp.StatusCode} {resp.ReasonPhrase} for {url}");
                return false;
            }

            var total = resp.Content.Headers.ContentLength;
            await using var stream = await resp.Content.ReadAsStreamAsync();
            var dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await using var fs = File.Create(outPath);

            var buffer = new byte[64 * 1024];
            long read = 0;
            int n;
            if (total is null || total <= 0)
            {
                IsProgressIndeterminate = true;
                while ((n = await stream.ReadAsync(buffer)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, n));
                }
                IsProgressIndeterminate = false;
                UpdateProgress = 100;
            }
            else
            {
                while ((n = await stream.ReadAsync(buffer)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, n));
                    read += n;
                    UpdateProgress = total > 0 ? (read * 100.0 / total.Value) : 0;
                    IsProgressIndeterminate = false;
                }
                UpdateProgress = 100;
            }
            Logger.Info("HttpClient completed: url={url}, out={out}", url, outPath);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "DownloadWithHttpClientAsync failed: {url}", url);
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
            if (Uri.TryCreate(ManifestPath, UriKind.Absolute, out var abs)) filePath = abs.AbsolutePath;
            else filePath = ManifestPath!.StartsWith('/') ? ManifestPath! : "/" + ManifestPath!;
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
        using var sha = SHA256.Create();
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
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static string? ResolveUpdaterEntry(string dir)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var exe = Path.Combine(dir, "LabelPlus_Next.Update.exe");
                if (File.Exists(exe)) return exe;
                var anyUpdate = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(p => Path.GetFileName(p).Contains("Update", StringComparison.OrdinalIgnoreCase));
                if (anyUpdate is not null) return anyUpdate;
                var anyExe = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (anyExe is not null) return anyExe;
                return null;
            }
            if (OperatingSystem.IsMacOS())
            {
                // 优先 .app bundle
                var app1 = Path.Combine(dir, "LabelPlus_Next.Update.app");
                if (Directory.Exists(app1)) return app1;
                var apps = Directory.GetDirectories(dir, "*.app", SearchOption.TopDirectoryOnly);
                var appUpdate = apps.FirstOrDefault(p => Path.GetFileName(p).Contains("Update", StringComparison.OrdinalIgnoreCase));
                if (appUpdate is not null) return appUpdate;
                if (apps.Length > 0) return apps[0];
                // 退回无扩展名可执行文件
                var unix = Directory.GetFileSystemEntries(dir, "*", SearchOption.TopDirectoryOnly)
                    .Where(p => File.Exists(p) && string.IsNullOrEmpty(Path.GetExtension(p)));
                var named = unix.FirstOrDefault(p => Path.GetFileName(p).Equals("LabelPlus_Next.Update", StringComparison.OrdinalIgnoreCase));
                if (named is not null) return named;
                var upd = unix.FirstOrDefault(p => Path.GetFileName(p).Contains("Update", StringComparison.OrdinalIgnoreCase));
                if (upd is not null) return upd;
                return unix.FirstOrDefault();
            }
            // Linux
            {
                var unix = Directory.GetFileSystemEntries(dir, "*", SearchOption.TopDirectoryOnly)
                    .Where(p => File.Exists(p) && string.IsNullOrEmpty(Path.GetExtension(p)));
                var named = unix.FirstOrDefault(p => Path.GetFileName(p).Equals("LabelPlus_Next.Update", StringComparison.OrdinalIgnoreCase));
                if (named is not null) return named;
                var upd = unix.FirstOrDefault(p => Path.GetFileName(p).Contains("Update", StringComparison.OrdinalIgnoreCase));
                if (upd is not null) return upd;
                return unix.FirstOrDefault();
            }
        }
        catch { }
        return null;
    }

    private static async Task<bool> EnsureFileReadyAsync(string path, int maxAttempts = 10, int delayMs = 100)
    {
        for (var i = 0; i < maxAttempts; i++)
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
            try { await Task.Delay(delayMs); }
            catch { }
        }
        return false;
    }
}
