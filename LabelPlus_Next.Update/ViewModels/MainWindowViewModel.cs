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
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Ursa.Controls;
using System.Diagnostics;

namespace LabelPlus_Next.Update.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
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
                    await MessageBox.ShowOverlayAsync("未找到 settings.json，无法更新", "提示");
                    return;
                }
                await using (var fs = File.OpenRead(settingsPath))
                {
                    var s = await JsonSerializer.DeserializeAsync<AppSettings>(fs, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    upd = s?.Update ?? new UpdateSettings();
                }

                var manifestJson = await FetchManifestJsonAsync(upd, appDir);
                if (manifestJson is null)
                {
                    await MessageBox.ShowAsync("获取清单失败", "错误");
                    return;
                }
                var manifest = JsonSerializer.Deserialize<ManifestV1>(manifestJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (manifest?.Projects is null)
                {
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

                if (IsGreater(updaterLatest, updaterLocal) && uproj is not null)
                {
                    var (fileUrl, fileSha) = GetReleaseFileInfo(uproj, updaterLatest!);
                    if (!string.IsNullOrWhiteSpace(fileUrl))
                    {
                        // Multi-thread download with Downloader
                        var tmpZip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                        await DownloadWithDownloaderAsync(fileUrl!, tmpZip);

                        if (!string.IsNullOrWhiteSpace(fileSha))
                        {
                            var actual = await ComputeSha256Async(tmpZip);
                            if (!string.Equals(actual, fileSha, StringComparison.OrdinalIgnoreCase))
                            {
                                try { File.Delete(tmpZip); } catch { }
                                Status = DownloadStatus.Error;
                                await MessageBox.ShowAsync("SHA256 校验失败", "错误");
                                return;
                            }
                        }

                        // Extract selectively, skip any file under 'update/' folder
                        await ExtractZipSelectiveAsync(tmpZip, appDir, relPath =>
                        {
                            var p = relPath.Replace('\\', '/');
                            return p.StartsWith("update/", StringComparison.OrdinalIgnoreCase);
                        });

                        try { File.Delete(tmpZip); } catch { }
                    }
                }

                if (IsGreater(clientLatest, clientLocal) && cproj is not null)
                {
                    var (fileUrl, fileSha) = GetReleaseFileInfo(cproj, clientLatest!);
                    if (!string.IsNullOrWhiteSpace(fileUrl))
                    {
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
                    // Still try to start main app if not running and updater launched standalone with target path
                    TryStartMainApp(appDir);
                    (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
                }
            }
            catch (Exception ex)
            {
                await MessageBox.ShowAsync($"更新失败: {ex.Message}", "错误");
            }
        }

        private async Task DownloadWithDownloaderAsync(string url, string outPath)
        {
            Status = DownloadStatus.Downloading;
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
            // HTTP-only simple fetch: directly GET the manifest URL (since Update project must be independent)
            Uri? manifestUri = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(upd.BaseUrl) && !string.IsNullOrWhiteSpace(upd.ManifestPath))
                {
                    var baseUrl = AppendSlash(upd.BaseUrl!);
                    var candidate = upd.ManifestPath!.Trim();
                    manifestUri = Uri.TryCreate(candidate, UriKind.Absolute, out var abs) ? abs : new Uri(new Uri(baseUrl), candidate.TrimStart('/'));
                }
            }
            catch { }
            if (manifestUri is null) return null;

            try
            {
                using var http = new System.Net.Http.HttpClient();
                var json = await http.GetStringAsync(manifestUri);
                return json;
            }
            catch { return null; }
        }
    }
}
