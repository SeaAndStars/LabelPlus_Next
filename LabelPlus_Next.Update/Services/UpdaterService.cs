using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LabelPlus_Next.Shared;
using LabelPlus_Next.Update.Models;
using DStatus = LabelPlus_Next.Update.ViewModels.DownloadStatus;
using NLog;

namespace LabelPlus_Next.Update.Services;

public sealed class UpdaterService : IUpdaterService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private const string ClientProjKey = "LabelPlus_Next.Desktop";
    private const string UpdaterProjKey = "LabelPlus_Next.Update";
    private const string LegacyManifestPath = "/OneDrive/Update/manifest.json";

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
    public string? StatusText { get; private set; }
    public event Action? ProgressChanged;

    public async Task RunUpdateAsync(string? overrideAppDir = null, CancellationToken ct = default)
    {
        var targetDir = overrideAppDir ?? AppContext.BaseDirectory; // 客户端安装目录（更新解压目标）
        try
        {
            SetStatus(DStatus.Checking, "正在检查更新...");
            var upd = await LoadSettingsAsync(overrideAppDir, ct);
            var resolver = new AlistResolver(upd.BaseUrl!, upd.Username, upd.Password);

            var updateDir = Path.Combine(targetDir, "update");
            var clientLocal = ReadLocalVersion(Path.Combine(targetDir, "Client.version.json"));
            var updaterLocal = ReadLocalVersion(Path.Combine(updateDir, "update.json"))
                               ?? ReadLocalVersion(Path.Combine(updateDir, "Update.version.json"))
                               ?? ReadLocalVersion(Path.Combine(targetDir, "Update.version.json"));

            var manifestJson = await FetchManifestJsonAsync(resolver, upd, targetDir, ct);
            ManifestV1? manifest = null;
            if (!string.IsNullOrWhiteSpace(manifestJson))
            {
                try
                {
                    manifest = JsonSerializer.Deserialize<ManifestV1>(manifestJson, WebJson);
                }
                catch (JsonException ex)
                {
                    Logger.Warn(ex, "Manifest JSON invalid; trying GitHub fallback");
                }
            }
            if (manifest?.Projects is not { Count: > 0 })
            {
                // AList/manifest failure: try the public GitHub release as a last resort.
                if (await TryGitHubUpdateAsync(resolver, targetDir, clientLocal, ct)) return;
                SetStatus(DStatus.Error, "获取清单失败");
                await _messages.ShowAsync(manifestJson is null ? "获取清单失败" : "清单格式不正确", "错误");
                return;
            }
            manifest.Projects.TryGetValue(ClientProjKey, out var cproj);
            manifest.Projects.TryGetValue(UpdaterProjKey, out var uproj);
            var clientLatest = LatestOf(cproj);
            var updaterLatest = LatestOf(uproj);
            Logger.Info("Local: client={clientLocal}, updater={updaterLocal}; Remote: client={clientLatest}, updater={updaterLatest}", clientLocal, updaterLocal, clientLatest, updaterLatest);
            LatestVersion = clientLatest ?? clientLocal;

            // 更新程序无法安全覆盖正在运行的自身；主程序会在下次检查时先刷新更新程序，这里只记录并继续更新客户端
            if (UpdateVersion.IsNewer(updaterLatest, updaterLocal))
                Logger.Info("Updater {local} is older than {remote}; the client will refresh it on its next update check", updaterLocal, updaterLatest);

            if (!UpdateVersion.IsNewer(clientLatest, clientLocal) || cproj is null)
            {
                SetStatus(DStatus.Completed, "已经是最新版本");
                await _messages.ShowOverlayAsync("已经是最新版本", "提示");
                TryStartMainApp(targetDir);
                return;
            }

            var release = cproj.Releases.FirstOrDefault(r => string.Equals(r.Version, clientLatest, StringComparison.OrdinalIgnoreCase));
            var file = UpdatePlatform.PickForCurrentPlatform(release?.Files, f => f.Name, f => f.Url);
            var url = file?.Url ?? release?.Url;
            if (string.IsNullOrWhiteSpace(url))
            {
                if (await TryGitHubDownloadForVersionAsync(resolver, targetDir, clientLatest, ct)) return;
                SetStatus(DStatus.Error, "清单未提供下载地址");
                await _messages.ShowAsync("清单未提供下载地址", "错误");
                return;
            }
            Logger.Info("Client update -> {ver}, url={url}", clientLatest, url);

            if (!await DownloadAndApplyAsync(resolver, url!, targetDir, file?.Sha256, ct))
            {
                if (await TryGitHubDownloadForVersionAsync(resolver, targetDir, clientLatest, ct)) return;
                await _messages.ShowAsync("客户端更新失败，已保留原有版本", "错误");
                return;
            }
            UpdatePlatform.CleanupForeignArchives(targetDir, Logger);
            UpdatePlatform.CleanupForeignArchives(updateDir, Logger);
            await _messages.ShowOverlayAsync("更新完成，正在重启主程序...", "提示");
            TryStartMainApp(targetDir, file);
        }
        catch (OperationCanceledException)
        {
            Logger.Info("Update cancelled by user");
            SetStatus(DStatus.Paused, "已取消更新，原有版本未改动");
            TryStartMainApp(targetDir);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "RunUpdateAsync failed");
            SetStatus(DStatus.Error, "更新失败");
            await _messages.ShowAsync($"更新失败: {ex.Message}", "错误");
        }
    }

    private async Task<bool> DownloadAndApplyAsync(AlistResolver resolver, string url, string appDir, string? expectedSha256, CancellationToken ct)
    {
        var tmpZip = UpdateFiles.NewTempFile();
        try
        {
            SetStatus(DStatus.Downloading, "正在下载更新...");
            var progress = new Progress<TransferProgress>(OnTransferProgress);
            // 单流下载：降低对远端网盘的压力，提升稳定性
            if (!await UpdateDownloader.DownloadVerifiedAsync(resolver, url, tmpZip, expectedSha256, chunks: 1, progress, ct: ct))
            {
                SetStatus(DStatus.Error, "下载或校验失败");
                return false;
            }

            SetStatus(DStatus.Installing, "正在安装更新...");
            ProgressPercentage = 0;
            var installProgress = new Progress<double>(v => { ProgressPercentage = v; ProgressChanged?.Invoke(); });
            var count = await StagedInstaller.ApplyZipAsync(tmpZip, appDir,
                rel => rel.StartsWith("update/", StringComparison.OrdinalIgnoreCase), installProgress, ct);
            Logger.Info("Installed {count} files into {dir}", count, appDir);
            ProgressPercentage = 100;
            SetStatus(DStatus.Completed, "更新完成");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Logger.Error(ex, "Applying client update failed; install rolled back");
            SetStatus(DStatus.Error, "安装失败，已回滚");
            return false;
        }
        finally
        {
            UpdateFiles.TryDelete(tmpZip);
        }
    }

    /// <summary>
    /// Manifest is unreachable: use the newest GitHub release when it is newer
    /// than the local client. Handles "already up to date" as a successful outcome.
    /// </summary>
    private async Task<bool> TryGitHubUpdateAsync(AlistResolver resolver, string appDir, string? clientLocal, CancellationToken ct)
    {
        var release = await GitHubUpdateSource.FetchLatestAsync(ct);
        if (release is null) return false;

        LatestVersion = release.Version;
        if (!UpdateVersion.IsNewer(release.Version, clientLocal))
        {
            SetStatus(DStatus.Completed, "已经是最新版本");
            await _messages.ShowOverlayAsync("已经是最新版本", "提示");
            TryStartMainApp(appDir);
            return true;
        }

        var asset = GitHubUpdateSource.FindAsset(release, "LabelPlus_Next.Desktop", UpdatePlatform.CurrentRid);
        if (asset is null)
        {
            Logger.Warn("GitHub fallback has no Desktop asset for {rid}", UpdatePlatform.CurrentRid);
            return false;
        }

        Logger.Info("GitHub fallback update -> {version}, {url}", release.Version, asset.Url);
        if (!await DownloadAndApplyAsync(resolver, asset.Url, appDir, asset.Sha256, ct)) return false;

        var updateDir = Path.Combine(appDir, "update");
        UpdatePlatform.CleanupForeignArchives(appDir, Logger);
        UpdatePlatform.CleanupForeignArchives(updateDir, Logger);
        await _messages.ShowOverlayAsync("更新完成，正在重启主程序...", "提示");
        TryStartMainApp(appDir);
        return true;
    }

    /// <summary>
    /// Manifest existed but its download failed (broken AList/asset). Prefer the
    /// GitHub asset with the same version and its API digest; fall back to the
    /// deterministic release URL without a digest so the download can still run.
    /// </summary>
    private async Task<bool> TryGitHubDownloadForVersionAsync(AlistResolver resolver, string appDir, string? version, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;

        var release = await GitHubUpdateSource.FetchLatestAsync(ct);
        var asset = release is not null
                    && string.Equals(release.Version, version, StringComparison.OrdinalIgnoreCase)
            ? GitHubUpdateSource.FindAsset(release, "LabelPlus_Next.Desktop", UpdatePlatform.CurrentRid)
            : null;
        var url = asset?.Url ?? GitHubUpdateSource.DownloadUrl(version, "LabelPlus_Next.Desktop", UpdatePlatform.CurrentRid);
        Logger.Info("Trying GitHub download fallback: {url}", url);

        if (!await DownloadAndApplyAsync(resolver, url, appDir, asset?.Sha256, ct)) return false;

        var updateDir = Path.Combine(appDir, "update");
        UpdatePlatform.CleanupForeignArchives(appDir, Logger);
        UpdatePlatform.CleanupForeignArchives(updateDir, Logger);
        await _messages.ShowOverlayAsync("更新完成，正在重启主程序...", "提示");
        TryStartMainApp(appDir);
        return true;
    }

    private void OnTransferProgress(TransferProgress p)
    {
        BytesReceivedInMB = p.Received / 1048576d;
        TotalBytesToReceiveInMB = p.Total > 0 ? p.Total / 1048576d : 0;
        Speed = p.BytesPerSecond / 1048576d;
        ProgressPercentage = p.Percentage;
        Remaining = p.Remaining;
        ProgressChanged?.Invoke();
    }

    private void SetStatus(DStatus status, string text)
    {
        Status = status;
        StatusText = text;
        ProgressChanged?.Invoke();
    }

    private static async Task<UpdateSettings> LoadSettingsAsync(string? overrideAppDir, CancellationToken ct)
    {
        var fallbacks = new List<string>();
        if (!string.IsNullOrWhiteSpace(overrideAppDir))
        {
            fallbacks.Add(Path.Combine(overrideAppDir, "settings.json"));
            fallbacks.Add(Path.Combine(overrideAppDir, "update", "settings.json"));
        }
        fallbacks.Add(AppConfigPaths.LegacySettings);
        fallbacks.Add(AppConfigPaths.LegacyUpdateSettings);
        var settingsPath = AppConfigPaths.ResolveExisting(AppConfigPaths.Settings, fallbacks.ToArray());

        UpdateSettings upd = new();
        if (File.Exists(settingsPath))
        {
            try
            {
                await using var fs = File.OpenRead(settingsPath);
                upd = (await JsonSerializer.DeserializeAsync<AppSettings>(fs, WebJson, ct))?.Update ?? new UpdateSettings();
            }
            catch (JsonException ex)
            {
                Logger.Warn(ex, "settings.json is invalid at {path}, using defaults", settingsPath);
            }

            if (!string.Equals(settingsPath, AppConfigPaths.Settings, StringComparison.Ordinal)
                && !File.Exists(AppConfigPaths.Settings))
            {
                try
                {
                    AppConfigPaths.EnsureParent(AppConfigPaths.Settings);
                    File.Copy(settingsPath, AppConfigPaths.Settings);
                    AppConfigPaths.SecureFile(AppConfigPaths.Settings);
                    Logger.Info("Migrated settings.json from {source} to AppData", settingsPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Logger.Warn(ex, "Failed to migrate settings.json to AppData");
                }
            }
        }
        else
        {
            Logger.Warn("settings.json not found at AppData or legacy locations, using defaults");
        }
        if (string.IsNullOrWhiteSpace(upd.BaseUrl)) upd.BaseUrl = UpdateSettings.DefaultBaseUrl;
        if (string.IsNullOrWhiteSpace(upd.ManifestPath) || string.Equals(upd.ManifestPath.Trim(), LegacyManifestPath, StringComparison.OrdinalIgnoreCase))
            upd.ManifestPath = UpdateSettings.DefaultManifestPath;
        return upd;
    }

    /// <summary>
    /// 获取清单：ManifestPath 为存储路径时经 fs/get 取 raw_url；为绝对 URL 时，AList 链接同样解析，
    /// 其他地址直接 GET；最后退回 Client.version.json 中的 manifest 地址。
    /// </summary>
    private static async Task<string?> FetchManifestJsonAsync(AlistResolver resolver, UpdateSettings upd, string appDir, CancellationToken ct)
    {
        var candidates = new[] { upd.ManifestPath, ReadManifestFromClientVersion(appDir) }
            .Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var c = candidate!.Trim().Replace(LegacyManifestPath, UpdateSettings.DefaultManifestPath, StringComparison.OrdinalIgnoreCase);
                string? url;
                if (!Uri.TryCreate(c, UriKind.Absolute, out var abs) || abs.IsFile)
                    url = await resolver.GetRawUrlAsync("/" + c.Replace('\\', '/').TrimStart('/'), ct);
                else if (AlistUrl.TryGetStoragePath(c, out var storagePath))
                    url = await resolver.GetRawUrlAsync(storagePath, ct) ?? AlistUrl.WithDownloadFlag(c);
                else
                    url = c;
                if (string.IsNullOrWhiteSpace(url)) continue;

                Logger.Info("Fetch manifest: {candidate} => {url}", c, url);
                var token = await resolver.GetTokenAsync(ct);
                var text = (await UpdateHttp.GetStringAsync(url!, null, ct) ?? await UpdateHttp.GetStringAsync(url!, token, ct))?.TrimStart('﻿', ' ', '\t', '\r', '\n');
                if (!string.IsNullOrEmpty(text) && !text.StartsWith('<')) return text;
                Logger.Warn("Manifest at {url} was empty or HTML", url);
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                Logger.Warn(ex, "Fetching manifest from {candidate} failed", candidate);
            }
        }
        return null;
    }

    private static string? ReadManifestFromClientVersion(string appDir)
    {
        try
        {
            var verPath = Path.Combine(appDir, "Client.version.json");
            if (!File.Exists(verPath)) return null;
            using var fs = File.OpenRead(verPath);
            using var doc = JsonDocument.Parse(fs);
            return doc.RootElement.TryGetProperty("manifest", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.Warn(ex, "Failed to read manifest from Client.version.json in {dir}", appDir);
            return null;
        }
    }

    private static string? LatestOf(ProjectReleases? p)
        => p is null ? null : p.Latest ?? p.Releases.Select(r => r.Version).Where(v => !string.IsNullOrWhiteSpace(v)).MaxBy(v => v, System.Collections.Generic.Comparer<string?>.Create(UpdateVersion.Compare));

    private static string? ReadLocalVersion(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            using var doc = JsonDocument.Parse(fs);
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.Warn(ex, "Failed to read local version file {path}", path);
            return null;
        }
    }

    private static void TryStartMainApp(string appDir, ReleaseFile? meta = null)
    {
        try
        {
            var entry = ResolveMainEntry(appDir, meta);
            if (entry is null) return;

            ProcessStartInfo psi;
            if (OperatingSystem.IsMacOS() && Directory.Exists(entry) && entry.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                psi = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
                psi.ArgumentList.Add(entry);
            }
            else
            {
                UpdatePlatform.EnsureExecutable(entry, Logger);
                psi = new ProcessStartInfo(entry) { UseShellExecute = OperatingSystem.IsWindows() };
            }
            psi.WorkingDirectory = appDir;
            Process.Start(psi);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or UnauthorizedAccessException or PlatformNotSupportedException or InvalidOperationException)
        {
            Logger.Warn(ex, "Failed to start main app process in {dir}", appDir);
        }
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
        catch (IOException ex)
        {
            Logger.Warn(ex, "Failed to read Client.version.json at {path}", clientVerPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warn(ex, "Access denied reading Client.version.json at {path}", clientVerPath);
        }
        catch (JsonException ex)
        {
            Logger.Warn(ex, "Client.version.json contains invalid JSON at {path}", clientVerPath);
        }

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
        catch (IOException ex)
        {
            Logger.Warn(ex, "Failed to enumerate application directory {dir} when resolving main entry", appDir);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warn(ex, "Access denied enumerating application directory {dir}", appDir);
        }
        return null;
    }
}
