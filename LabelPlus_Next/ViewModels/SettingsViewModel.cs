using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Services;
using LabelPlus_Next.Serialization;
using LabelPlus_Next.Models;
using NLog;
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WebDav;
using System.Diagnostics;
using System.Linq;

namespace LabelPlus_Next.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    // Shared WebDAV client for the app lifetime
    private static IWebDavClient? _client;

    private readonly ISettingsService _settingsService;
    private readonly IUpdateService _updateService; // kept for compat, not used for v1

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

    public IAsyncRelayCommand VerifyWebDavCommand { get; }

    public SettingsViewModel() : this(new JsonSettingsService(), new WebDavUpdateService()) { }

    public SettingsViewModel(ISettingsService settingsService, IUpdateService updateService)
    {
        _settingsService = settingsService;
        _updateService = updateService;
        VerifyWebDavCommand = new AsyncRelayCommand(VerifyWebDavAsync);
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        try
        {
            var s = await _settingsService.LoadAsync();
            BaseUrl = s.Update.BaseUrl;
            ManifestPath = s.Update.ManifestPath ?? "manifest.json";
            Username = s.Update.Username;
            Password = s.Update.Password;
            Status = "设置已加载";
        }
        catch (Exception ex)
        {
            Status = $"加载失败: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        try
        {
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
        }
        catch (Exception ex)
        {
            Status = $"保存失败: {ex.Message}";
        }
    }

    private bool EnsureWebDavClient()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            Status = "请填写 BaseUrl";
            return false;
        }
        try
        {
            var uri = new Uri(BaseUrl!, UriKind.Absolute);
            var @params = new WebDavClientParams { BaseAddress = uri };
            if (!string.IsNullOrEmpty(Username))
            {
                @params.Credentials = new NetworkCredential(Username, Password ?? string.Empty);
            }
            if (_client is IDisposable d) d.Dispose();
            _client = new WebDavClient(@params);
            return true;
        }
        catch (Exception ex)
        {
            Status = $"WebDAV 配置错误: {ex.Message}";
            return false;
        }
    }

    public async Task VerifyWebDavAsync()
    {
        try
        {
            if (!EnsureWebDavClient()) return;
            var path = string.IsNullOrWhiteSpace(ManifestPath) ? string.Empty : ManifestPath!.TrimStart('/');
            var result = await _client!.Propfind(path);
            if (result.IsSuccessful)
            {
                Status = $"WebDAV 验证成功，资源数: {result.Resources.Count}";
            }
            else
            {
                Status = $"WebDAV 验证失败: {(int)result.StatusCode} {result.Description}";
            }
        }
        catch (Exception ex)
        {
            Status = $"验证异常: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task CheckUpdateAsync()
    {
        try
        {
            Status = "检查更新中...";
            UpdateTask = null; UpdateProgress = 0; IsProgressIndeterminate = false;
            var manifestJson = await FetchManifestJsonAsync();
            if (manifestJson is null)
            {
                Status = "获取清单失败";
                return;
            }
            Logger.Info("Update manifest downloaded (len={len})", manifestJson.Length);

            var manifest = JsonSerializer.Deserialize(manifestJson, AppJsonContext.Default.ManifestV1);
            if (manifest?.Projects is null)
            {
                Status = "清单格式不正确";
                return;
            }

            var appDir = AppContext.BaseDirectory;
            var clientLocal = ReadLocalVersion(Path.Combine(appDir, "Client.version.json"));
            var updateDir = Path.Combine(appDir, "update");
            var updaterLocal = ReadLocalVersion(Path.Combine(updateDir, "Update.version.json"));
            CurrentClientVersion = clientLocal ?? "未知";
            CurrentUpdaterVersion = updaterLocal ?? "未知";
            CurrentVersion = CurrentClientVersion;

            var clientProjKey = "LabelPlus_Next.Desktop";
            var updaterProjKey = "LabelPlus_Next.Update";

            var clientLatest = manifest.Projects.TryGetValue(clientProjKey, out var cproj) ? (cproj.Latest ?? GetTopReleaseVersion(cproj)) : null;
            var updaterLatest = manifest.Projects.TryGetValue(updaterProjKey, out var uproj) ? (uproj.Latest ?? GetTopReleaseVersion(uproj)) : null;
            LatestClientVersion = clientLatest ?? "未知";
            LatestUpdaterVersion = updaterLatest ?? "未知";
            LatestVersion = LatestClientVersion;

            var needClient = IsGreater(clientLatest, clientLocal);
            var needUpdater = IsGreater(updaterLatest, updaterLocal);

            if (needUpdater && uproj is not null)
            {
                var url = GetReleaseUrl(uproj, updaterLatest!);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    UpdateTask = $"正在更新 Update 到 {updaterLatest}..."; IsProgressIndeterminate = true; UpdateProgress = 0;
                    var zipPath = await DownloadToTempAsync(url);
                    UpdateTask = "解压更新程序..."; IsProgressIndeterminate = true; UpdateProgress = 0;
                    Directory.CreateDirectory(updateDir);
                    ZipFile.ExtractToDirectory(zipPath, updateDir, overwriteFiles: true);
                    TryDelete(zipPath);
                    UpdateTask = null; IsProgressIndeterminate = false; UpdateProgress = 0;
                    Status = "更新程序已更新";
                }
            }

            if (needClient)
            {
                Directory.CreateDirectory(updateDir);
                var updaterExe = Path.Combine(updateDir, "LabelPlus_Next.Update.exe");
                if (!File.Exists(updaterExe))
                {
                    var fallbackExe = Path.Combine(appDir, "LabelPlus_Next.Update.exe");
                    if (File.Exists(fallbackExe)) updaterExe = fallbackExe;
                }
                if (File.Exists(updaterExe))
                {
                    Status = "启动更新程序...";
                    try
                    {
                        var pid = Process.GetCurrentProcess().Id;
                        var psi = new ProcessStartInfo
                        {
                            FileName = updaterExe,
                            UseShellExecute = false,
                            WorkingDirectory = Path.GetDirectoryName(updaterExe) ?? appDir
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
                        Status = $"启动更新程序失败: {ex.Message}";
                    }
                }
                else
                {
                    Status = "未找到更新程序 exe";
                }
            }
            else if (!needUpdater)
            {
                Status = "已经是最新版本";
            }
        }
        catch (Exception ex)
        {
            Status = $"检查失败: {ex.Message}";
            Logger.Error(ex, "CheckUpdateAsync failed");
        }
    }

    public async Task<(bool success, bool hasUpdate, string? message)> ProbeUpdateAsync()
    {
        try
        {
            var manifestJson = await FetchManifestJsonAsync();
            if (manifestJson is null) return (false, false, "获取清单失败");
            var manifest = JsonSerializer.Deserialize(manifestJson, AppJsonContext.Default.ManifestV1);
            if (manifest?.Projects is null) return (false, false, "清单格式不正确");

            var appDir = AppContext.BaseDirectory;
            var clientLocal = ReadLocalVersion(Path.Combine(appDir, "Client.version.json"));
            var updateDir = Path.Combine(appDir, "update");
            var updaterLocal = ReadLocalVersion(Path.Combine(updateDir, "update.json"))
                               ?? ReadLocalVersion(Path.Combine(updateDir, "Update.version.json"))
                               ?? ReadLocalVersion(Path.Combine(appDir, "Update.version.json"));

            var clientProjKey = "LabelPlus_Next.Desktop";
            var updaterProjKey = "LabelPlus_Next.Update";
            var clientLatest = manifest.Projects.TryGetValue(clientProjKey, out var cproj) ? (cproj.Latest ?? GetTopReleaseVersion(cproj)) : null;
            var updaterLatest = manifest.Projects.TryGetValue(updaterProjKey, out var uproj) ? (uproj.Latest ?? GetTopReleaseVersion(uproj)) : null;

            var needClient = IsGreater(clientLatest, clientLocal);
            var needUpdater = IsGreater(updaterLatest, updaterLocal);
            return (true, needClient || needUpdater, null);
        }
        catch (Exception ex)
        {
            return (false, false, ex.Message);
        }
    }

    private static bool IsGreater(string? remote, string? local)
    {
        if (string.IsNullOrWhiteSpace(remote)) return false;
        if (string.IsNullOrWhiteSpace(local)) return true;
        if (Version.TryParse(remote, out var r) && Version.TryParse(local, out var l))
            return r > l;
        return string.Compare(remote, local, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private static string? GetTopReleaseVersion(ProjectReleases? prj)
    {
        if (prj?.Releases is null || prj.Releases.Count == 0) return null;
        return prj.Releases[0].Version;
    }

    private static string? GetReleaseUrl(ProjectReleases prj, string version)
    {
        foreach (var r in prj.Releases)
        {
            if (string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase))
                return r.Url;
        }
        return null;
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

    private async Task<string?> FetchManifestJsonAsync()
    {
        if (!EnsureWebDavClient()) return null;
        var path = string.IsNullOrWhiteSpace(ManifestPath) ? string.Empty : ManifestPath!.TrimStart('/');
        try
        {
            var resp = await _client!.GetRawFile(path);
            if (resp.IsSuccessful && resp.Stream is not null)
            {
                using var sr = new StreamReader(resp.Stream, Encoding.UTF8, true);
                return await sr.ReadToEndAsync();
            }
            Logger.Warn("WebDAV GetRawFile manifest failed: {code} {desc}", (int)resp.StatusCode, resp.Description);
            return null;
        }
        catch (Exception ex) when (ex is IOException || ex is SocketException)
        {
            Logger.Warn(ex, "FetchManifestJsonAsync (WebDAV) failed: {path}", path);
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string AppendSlash(string url) => url.EndsWith('/') ? url : url + '/';

    private async Task<string> DownloadToTempAsync(string url)
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");

        long? size = await TryGetContentLengthAsync(url);
        IsProgressIndeterminate = !size.HasValue; UpdateProgress = 0;

        try
        {
            var client = CreateWebDavDownloader();
            var uri = new Uri(url, UriKind.Absolute);
            var resp = await client.GetRawFile(uri);
            if (resp.IsSuccessful && resp.Stream is not null)
            {
                await using var fs = File.Create(tmp);
                await CopyWithProgressAsync(resp.Stream, fs, size);
                Logger.Info("Zip downloaded via WebDAV: {url} -> {tmp}", url, tmp);
                return tmp;
            }
            else
            {
                Logger.Warn("WebDAV GetRawFile failed: {code} {desc} for {url}", (int)resp.StatusCode, resp.Description, url);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is SocketException)
        {
            Logger.Warn(ex, "WebDAV download failed: {url}", url);
        }

        throw new IOException("WebDAV 下载失败");
    }

    private async Task CopyWithProgressAsync(Stream input, Stream output, long? totalLength)
    {
        const int buf = 128 * 1024;
        var buffer = new byte[buf];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await output.WriteAsync(buffer, 0, read);
            total += read;
            if (totalLength.HasValue && totalLength.Value > 0)
            {
                UpdateProgress = Math.Round(total * 100.0 / totalLength.Value, 1);
            }
        }
        if (!totalLength.HasValue) UpdateProgress = 100;
    }

    private async Task<long?> TryGetContentLengthAsync(string url)
    {
        try
        {
            var client = CreateWebDavDownloader();
            var uri = new Uri(url, UriKind.Absolute);
            var prop = await client.Propfind(uri);
            if (prop.IsSuccessful)
            {
                var r = prop.Resources.FirstOrDefault();
                return r?.ContentLength;
            }
        }
        catch { }
        return null;
    }

    private IWebDavClient CreateWebDavDownloader()
    {
        if (!string.IsNullOrEmpty(Username))
        {
            return new WebDavClient(new WebDavClientParams
            {
                Credentials = new NetworkCredential(Username, Password ?? string.Empty)
            });
        }
        return new WebDavClient();
    }
}
