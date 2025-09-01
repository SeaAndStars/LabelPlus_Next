using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Tools.Models;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using WebDav;

namespace LabelPlus_Next.Tools.ViewModels;

public partial class PackWindowViewModel : ObservableObject
{
    private const string ManifestSchema = "labelplus-manifest/v1";
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    // Keep all project-version pairs detected from version files
    private readonly List<(string Project, string Version)> versionEntries = new();

    [ObservableProperty] private double currentProgress;
    [ObservableProperty] private string? currentTask;
    [ObservableProperty] private string? manifestPath;
    // Note: during LoadClientVersionAsync this holds the download URL from project json; later in build it will hold DAV url
    [ObservableProperty] private string? manifestUrl;
    [ObservableProperty] private string? project;
    [ObservableProperty] private string? selectedFolder;
    [ObservableProperty] private string? solutionPath;
    [ObservableProperty] private string? status;
    [ObservableProperty] private string? targetPath;
    [ObservableProperty] private string? version;
    [ObservableProperty] private string? zipPath;

    // Publish options (UI configurable)
    [ObservableProperty] private bool winX64 = true;
    [ObservableProperty] private bool winArm64;
    [ObservableProperty] private bool linuxX64 = true;
    [ObservableProperty] private bool linuxArm64;
    [ObservableProperty] private bool osxX64 = true;
    [ObservableProperty] private bool osxArm64;

    [ObservableProperty] private bool selfContained = true;
    [ObservableProperty] private bool singleFile; // default off for easier debugging
    [ObservableProperty] private bool publishTrimmed; // risky for Avalonia; default off

    public PackWindowViewModel()
    {
        BrowseCommand = new AsyncRelayCommand(BrowseAsync);
        BuildAndUploadCommand = new AsyncRelayCommand(BuildAndUploadAsync);
        BuildSolutionCommand = new AsyncRelayCommand(BuildSolutionAsync);
    }

    public IAsyncRelayCommand BrowseCommand { get; }
    public IAsyncRelayCommand BuildAndUploadCommand { get; }
    public IAsyncRelayCommand BuildSolutionCommand { get; }

    private async Task BrowseAsync()
    {
        // no StorageProvider here; handled by View's code-behind
        await Task.CompletedTask;
    }

    public async Task SetFolderAsync(string folder)
    {
        SelectedFolder = folder;
        await LoadClientVersionAsync();
    }

    private async Task LoadClientVersionAsync()
    {
        versionEntries.Clear();
        if (string.IsNullOrEmpty(SelectedFolder)) return;
        try
        {
            // support Update.version.json or Client.version.json (and misspelled variant)
            var pUpdate = Path.Combine(SelectedFolder!, "Update.version.json");
            var pClient = Path.Combine(SelectedFolder!, "Client.version.json");
            var pClientTypo = Path.Combine(SelectedFolder!, "Clietn.version.json");

            var found = new List<(string path, ClientVersion cv)>();
            if (File.Exists(pUpdate))
            {
                await using var fs = File.OpenRead(pUpdate);
                var cv = await JsonSerializer.DeserializeAsync<ClientVersion>(fs, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
                if (cv is not null) found.Add((pUpdate, cv));
            }
            if (File.Exists(pClient))
            {
                await using var fs = File.OpenRead(pClient);
                var cv = await JsonSerializer.DeserializeAsync<ClientVersion>(fs, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
                if (cv is not null) found.Add((pClient, cv));
            }
            if (!File.Exists(pClient) && File.Exists(pClientTypo))
            {
                await using var fs = File.OpenRead(pClientTypo);
                var cv = await JsonSerializer.DeserializeAsync<ClientVersion>(fs, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
                if (cv is not null) found.Add((pClientTypo, cv));
            }

            if (found.Count == 0)
            {
                Status = "未找到 Update.version.json 或 Client.version.json";
                return;
            }

            // Deduplicate by project name, keep order preferring Update.version.json first
            foreach (var item in found)
            {
                if (!string.IsNullOrWhiteSpace(item.cv.Project) && !string.IsNullOrWhiteSpace(item.cv.Version))
                {
                    if (!versionEntries.Exists(e => string.Equals(e.Project, item.cv.Project, StringComparison.OrdinalIgnoreCase)))
                        versionEntries.Add((item.cv.Project!, item.cv.Version!));
                }
                // Take first non-empty manifest url as hint
                if (string.IsNullOrEmpty(ManifestUrl) && !string.IsNullOrEmpty(item.cv.Manifest))
                    ManifestUrl = item.cv.Manifest;
            }

            // Primary display uses the first entry
            Project = versionEntries[0].Project;
            Version = versionEntries[0].Version;

            var filesUsed = string.Join(", ", found.Select(f => Path.GetFileName(f.path)));
            Status = $"版本: {Version}, 项目: {Project}（{filesUsed}）";
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "读取版本文件失败");
            Status = $"读取版本失败: {ex.Message}";
        }
    }

    private async Task BuildAndUploadAsync()
    {
        try
        {
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "tools.settings.json");
            if (!File.Exists(settingsPath))
            {
                Status = "未在程序根目录找到 tools.settings.json";
                return;
            }
            var uploadSettings = await LoadSettingsAsync(settingsPath);
            if (uploadSettings is null)
            {
                Status = "读取 tools.settings.json 失败";
                return;
            }
            TargetPath ??= uploadSettings.TargetPath;

            var outDir = Path.Combine(AppContext.BaseDirectory, "packages");
            Directory.CreateDirectory(outDir);

            // If user selected a folder -> old behavior: zip that folder as a package
            if (!string.IsNullOrEmpty(SelectedFolder))
            {
                if (versionEntries.Count == 0) await LoadClientVersionAsync();
                if (versionEntries.Count == 0)
                {
                    Status = "版本或项目为空";
                    return;
                }

                var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var fileName = $"{Project}({Version})_{ts}.zip";
                var outZip = Path.Combine(outDir, fileName);
                if (File.Exists(outZip)) File.Delete(outZip);

                ZipFile.CreateFromDirectory(SelectedFolder!, outZip, CompressionLevel.Optimal, false, Encoding.UTF8);
                ZipPath = outZip;

                var zipInfo = new FileInfo(outZip);
                var sha256 = await ComputeSha256Async(outZip);

                var baseDir = AppendSlash(uploadSettings.BaseUrl ?? throw new InvalidOperationException("BaseUrl 为空"));
                var targetDir = NormalizePath(TargetPath);
                var destBase = new Uri(new Uri(baseDir), targetDir);
                var zipUrl = new Uri(destBase, fileName).ToString();
                var davManifestUrl = new Uri(destBase, "manifest.json");
                var manifestDownloadUrlFromProject = ManifestUrl;

                var client = CreateWebDavClient(uploadSettings);

                CurrentTask = "上传包...";
                CurrentProgress = 0;
                await UploadFileWithRetryAsync(client, outZip, zipUrl, "包", (sent, total) =>
                {
                    if (total > 0) CurrentProgress = Math.Round(sent * 100.0 / total, 1);
                });

                var fileEntry = new ReleaseFile
                {
                    Name = Path.GetFileName(outZip),
                    Url = zipUrl,
                    Size = zipInfo.Length,
                    Sha256 = sha256
                };

                CurrentTask = "合并清单...";
                var mergedManifestJson = await BuildMergedManifestV1JsonAsync(client, uploadSettings, manifestDownloadUrlFromProject, versionEntries, zipUrl, davManifestUrl, fileEntry);
                var manifestFile = Path.Combine(outDir, "manifest.json");
                await File.WriteAllTextAsync(manifestFile, mergedManifestJson, Encoding.UTF8);
                ManifestPath = manifestFile;
                ManifestUrl = davManifestUrl.ToString();

                CurrentTask = "上传清单...";
                CurrentProgress = 0;
                await UploadFileWithRetryAsync(client, manifestFile, ManifestUrl, "manifest", (sent, total) =>
                {
                    if (total > 0) CurrentProgress = Math.Round(sent * 100.0 / total, 1);
                });

                CurrentTask = null;
                CurrentProgress = 0;
                Status = "生成并上传完成";
                return;
            }

            // Auto build selected platforms for Desktop and Update projects
            var repoRoot = FindRepoRoot();
            if (repoRoot is null)
            {
                Status = "未能定位仓库根目录";
                return;
            }

            // Read versions from project roots
            versionEntries.Clear();
            var desktopVer = TryReadVersion(Path.Combine(repoRoot, "LabelPlus_Next", "Client.version.json"));
            if (desktopVer is not null) versionEntries.Add(("LabelPlus_Next.Desktop", desktopVer));
            var updateVer = TryReadVersion(Path.Combine(repoRoot, "LabelPlus_Next.Update", "Update.version.json"));
            if (updateVer is not null) versionEntries.Add(("LabelPlus_Next.Update", updateVer));
            if (versionEntries.Count == 0)
            {
                Status = "未能读取版本信息";
                return;
            }

            var ridList = new List<string>();
            if (WinX64) ridList.Add("win-x64");
            if (WinArm64) ridList.Add("win-arm64");
            if (LinuxX64) ridList.Add("linux-x64");
            if (LinuxArm64) ridList.Add("linux-arm64");
            if (OsxX64) ridList.Add("osx-x64");
            if (OsxArm64) ridList.Add("osx-arm64");
            if (ridList.Count == 0)
            {
                Status = "请至少选择一个目标平台（RID）";
                return;
            }

            var projects = new[] { "LabelPlus_Next.Desktop", "LabelPlus_Next.Update" };
            var artifacts = new List<(string Project, string Version, string Rid, string ZipPath, long Size, string Sha256)>();

            foreach (var proj in projects)
            {
                var ver = versionEntries.FirstOrDefault(v => string.Equals(v.Project, proj, StringComparison.OrdinalIgnoreCase)).Version;
                if (string.IsNullOrEmpty(ver)) continue;
                foreach (var rid in ridList)
                {
                    CurrentTask = $"发布 {proj} ({rid})...";
                    await PublishProjectAsync(repoRoot, proj, rid, SelfContained, SingleFile, PublishTrimmed);
                    var publishDir = GetPublishDir(repoRoot, proj, rid);
                    if (!Directory.Exists(publishDir))
                    {
                        Logger.Warn("未找到发布目录: {dir}", publishDir);
                        continue;
                    }
                    var ts2 = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var fileName2 = $"{proj}({ver})_{rid}_{ts2}.zip";
                    var outZip2 = Path.Combine(outDir, fileName2);
                    if (File.Exists(outZip2)) File.Delete(outZip2);
                    CurrentTask = $"打包 {proj} ({rid})...";
                    ZipFile.CreateFromDirectory(publishDir, outZip2, CompressionLevel.Optimal, false, Encoding.UTF8);
                    var fi = new FileInfo(outZip2);
                    var sha = await ComputeSha256Async(outZip2);
                    artifacts.Add((proj, ver, rid, outZip2, fi.Length, sha));
                }
            }

            if (artifacts.Count == 0)
            {
                Status = "没有生成任何产物";
                return;
            }

            var baseUrl = AppendSlash(uploadSettings.BaseUrl ?? throw new InvalidOperationException("BaseUrl 为空"));
            var target = NormalizePath(TargetPath);
            var destRoot = new Uri(new Uri(baseUrl), target);
            var davManifestUri = new Uri(destRoot, "manifest.json");
            var clientDav = CreateWebDavClient(uploadSettings);

            // Upload all artifacts
            var uploaded = new List<(string Project, string Version, ReleaseFile File)>();
            foreach (var a in artifacts)
            {
                var name = Path.GetFileName(a.ZipPath);
                var url = new Uri(destRoot, name).ToString();
                CurrentTask = $"上传 {name}...";
                CurrentProgress = 0;
                await UploadFileWithRetryAsync(clientDav, a.ZipPath, url, "包", (sent, total) =>
                {
                    if (total > 0) CurrentProgress = Math.Round(sent * 100.0 / total, 1);
                });
                uploaded.Add((a.Project, a.Version, new ReleaseFile { Name = name, Url = url, Size = a.Size, Sha256 = a.Sha256 }));
            }

            // Merge manifest with all uploaded files
            CurrentTask = "合并清单...";
            string? json = null;
            try
            {
                var resp = await clientDav.GetRawFile(davManifestUri);
                if (resp.IsSuccessful && resp.Stream is not null)
                {
                    using var sr = new StreamReader(resp.Stream, Encoding.UTF8, true);
                    json = await sr.ReadToEndAsync();
                }
            }
            catch { }

            ManifestV1? manifest = null;
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    manifest = JsonSerializer.Deserialize<ManifestV1>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
                    if (manifest is not null && !string.Equals(manifest.Schema, ManifestSchema, StringComparison.Ordinal))
                        manifest = null;
                }
                catch { manifest = null; }
            }
            manifest ??= new ManifestV1();

            foreach (var (proj, ver) in versionEntries)
            {
                foreach (var f in uploaded.Where(u => string.Equals(u.Project, proj, StringComparison.OrdinalIgnoreCase) && string.Equals(u.Version, ver, StringComparison.OrdinalIgnoreCase)))
                {
                    UpsertV1(manifest, proj, ver, f.File.Url!, f.File);
                }
            }

            var manifestOut = Path.Combine(outDir, "manifest.json");
            await File.WriteAllTextAsync(manifestOut, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), Encoding.UTF8);
            ManifestPath = manifestOut;
            ManifestUrl = davManifestUri.ToString();

            CurrentTask = "上传清单...";
            CurrentProgress = 0;
            await UploadFileWithRetryAsync(clientDav, manifestOut, ManifestUrl, "manifest", (sent, total) =>
            {
                if (total > 0) CurrentProgress = Math.Round(sent * 100.0 / total, 1);
            });

            CurrentTask = null;
            CurrentProgress = 0;
            Status = "多平台发布并上传完成";
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "生成或上传失败");
            Status = $"生成或上传失败: {ex.Message}";
        }
    }

    private async Task BuildSolutionAsync()
    {
        if (string.IsNullOrWhiteSpace(SolutionPath))
        {
            Status = "请先选择解决方案 .sln";
            return;
        }
        try
        {
            CurrentTask = "dotnet restore";
            await RunDotnetAsync($"restore \"{SolutionPath}\"");
            CurrentTask = "dotnet build -c Release";
            await RunDotnetAsync($"build \"{SolutionPath}\" -c Release");
            Status = "解决方案构建完成";
        }
        catch (Exception ex)
        {
            Status = $"解决方案构建失败: {ex.Message}";
        }
        finally
        {
            CurrentTask = null;
        }
    }

    private static async Task RunDotnetAsync(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var tcs = new TaskCompletionSource<int>();
        proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Logger.Info(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Logger.Warn(e.Data); };
        proc.Exited += (_, __) => tcs.TrySetResult(proc.ExitCode);
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        var code = await tcs.Task.ConfigureAwait(false);
        if (code != 0) throw new InvalidOperationException($"dotnet 命令失败: exit={code}, args={args}");
    }

    private static WebDavClient CreateWebDavClient(ToolsSettings s)
    {
        var handler = new SocketsHttpHandler
        {
            PreAuthenticate = true,
            UseCookies = true,
            AllowAutoRedirect = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 2,
            AutomaticDecompression = DecompressionMethods.All
        };
        if (!string.IsNullOrEmpty(s.Username)) handler.Credentials = new NetworkCredential(s.Username, s.Password ?? string.Empty);
        var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan, DefaultRequestVersion = new Version(1, 1) };
        httpClient.DefaultRequestHeaders.ExpectContinue = false;
        return new WebDavClient(httpClient);
    }

    private static bool IsTransient(Exception ex) => ex is TaskCanceledException ||
                                                     ex is IOException ||
                                                     ex is HttpRequestException ||
                                                     ex is SocketException se && (se.SocketErrorCode == SocketError.ConnectionReset || se.SocketErrorCode == SocketError.TimedOut);

    private static async Task UploadFileWithRetryAsync(WebDavClient client, string localPath, string absoluteUrl, string kind, Action<long, long>? onProgress)
    {
        const int maxRetries = 3;
        const int baseDelayMs = 800;
        const int bufferSize = 128 * 1024; // 128KB
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await using var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
                // Wrap with progress stream and use StreamContent (lets HttpClient handle Content-Length correctly)
                using var ps = new ProgressReadStream(fs, fs.Length, onProgress);
                using var content = new StreamContent(ps, bufferSize);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Headers.ContentLength = fs.Length;
                var result = await client.PutFile(new Uri(absoluteUrl), content);
                if (!result.IsSuccessful)
                {
                    if (attempt >= maxRetries) throw new InvalidOperationException($"上传失败: {absoluteUrl} {result.StatusCode} {result.Description}");
                }
                else return;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < maxRetries)
            {
                Logger.Warn(ex, "临时性错误，第 {attempt}/{max} 次重试 {kind}: {url}", attempt, maxRetries, kind, absoluteUrl);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 300)));
        }
    }

    private static string AppendSlash(string url) => url.EndsWith('/') ? url : url + '/';
    private static string NormalizePath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return string.Empty;
        var s = p.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s + '/';
    }

    private static async Task<ToolsSettings?> LoadSettingsAsync(string settingsPath)
    {
        await using var fs = File.OpenRead(settingsPath);
        return await JsonSerializer.DeserializeAsync<ToolsSettings>(fs, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
    }

    private static int CompareSemVer(string a, string b)
    {
        if (System.Version.TryParse(a, out var va) && System.Version.TryParse(b, out var vb))
        {
            return va.CompareTo(vb);
        }
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static ReleaseItem? FindByVersion(List<ReleaseItem> list, string version)
    {
        return list.Find(r => string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase));
    }

    private static void UpsertV1(ManifestV1 m, string project, string version, string url, ReleaseFile? file)
    {
        if (!m.Projects.TryGetValue(project, out var prj))
        {
            prj = new ProjectReleases();
            m.Projects[project] = prj;
        }
        var existing = FindByVersion(prj.Releases, version);
        if (existing is null)
        {
            existing = new ReleaseItem { Version = version, Url = url, Time = DateTimeOffset.UtcNow };
            if (file is not null) existing.Files = new List<ReleaseFile> { file };
            prj.Releases.Insert(0, existing);
        }
        else
        {
            // Only set Url if missing, keep the first one (typically Windows) as default
            if (string.IsNullOrWhiteSpace(existing.Url) && !string.IsNullOrWhiteSpace(url))
                existing.Url = url;
            existing.Time = DateTimeOffset.UtcNow;
            if (file is not null)
            {
                existing.Files ??= new List<ReleaseFile>();
                var idx = existing.Files.FindIndex(f => string.Equals(f.Url, file.Url, StringComparison.OrdinalIgnoreCase) || string.Equals(f.Name, file.Name, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) existing.Files[idx] = file;
                else existing.Files.Add(file);
            }
        }
        // update latest
        if (string.IsNullOrWhiteSpace(prj.Latest) || CompareSemVer(version, prj.Latest!) > 0)
        {
            prj.Latest = version;
        }
        m.GeneratedAt = DateTimeOffset.UtcNow;
    }

    private static ManifestV1 MigrateOldToV1(string project, string? json)
    {
        var manifest = new ManifestV1();
        var prj = new ProjectReleases();
        manifest.Projects[project] = prj;
        if (string.IsNullOrWhiteSpace(json)) return manifest;
        try
        {
            var node = JsonNode.Parse(json);
            if (node is JsonArray arr)
            {
                foreach (var n in arr)
                {
                    if (n is JsonObject o)
                    {
                        var ver = o["version"]?.GetValue<string>();
                        var url = o["url"]?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(ver) && !string.IsNullOrWhiteSpace(url))
                        {
                            prj.Releases.Add(new ReleaseItem { Version = ver, Url = url });
                        }
                    }
                }
            }
            else if (node is JsonObject obj)
            {
                if (obj["items"] is JsonArray items)
                {
                    foreach (var n in items)
                    {
                        if (n is JsonObject o)
                        {
                            var ver = o["version"]?.GetValue<string>();
                            var url = o["url"]?.GetValue<string>();
                            if (!string.IsNullOrWhiteSpace(ver) && !string.IsNullOrWhiteSpace(url))
                            {
                                prj.Releases.Add(new ReleaseItem { Version = ver, Url = url });
                            }
                        }
                    }
                }
                else
                {
                    var ver = obj["version"]?.GetValue<string>();
                    var url = obj["url"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(ver) && !string.IsNullOrWhiteSpace(url))
                    {
                        prj.Releases.Add(new ReleaseItem { Version = ver, Url = url });
                    }
                }
            }
        }
        catch
        {
            // ignore malformed; return empty prj
        }
        prj.Releases.Sort((x, y) =>
        {
            if (x?.Version is null || y?.Version is null) return 0;
            return -CompareSemVer(x.Version, y.Version); // desc
        });
        prj.Latest = prj.Releases.Count > 0 ? prj.Releases[0].Version : null;
        return manifest;
    }

    private async Task<string> BuildMergedManifestV1JsonAsync(WebDavClient davClient, ToolsSettings s, string? downloadUrl, IReadOnlyList<(string Project, string Version)> entries, string zipUrl, Uri davManifestUrl, ReleaseFile fileEntry)
    {
        string? json = null;

        // 1) Prefer WebDAV manifest
        try
        {
            var resp = await davClient.GetRawFile(davManifestUrl);
            if (resp.IsSuccessful && resp.Stream is not null)
            {
                using var sr = new StreamReader(resp.Stream, Encoding.UTF8, true);
                json = await sr.ReadToEndAsync();
                Logger.Info("已通过 WebDAV 获取 manifest.json: {url}", davManifestUrl);
                LogFetchedJson("WebDAV", davManifestUrl.ToString(), json);
            }
            else
            {
                Logger.Info("WebDAV 获取 manifest.json 为空或失败: {code} {desc}", resp.StatusCode, resp.Description);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "通过 WebDAV 获取 manifest.json 异常");
        }

        // 2) Fallback to HTTP download URL only if needed and content looks like a valid manifest
        if (json is null && !string.IsNullOrWhiteSpace(downloadUrl))
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                if (!string.IsNullOrEmpty(s.Username))
                {
                    var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{s.Username}:{s.Password}"));
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
                }
                var httpJson = await http.GetStringAsync(downloadUrl);
                Logger.Info("已从下载地址获取 manifest.json: {url}", downloadUrl);
                LogFetchedJson("HTTP", downloadUrl!, httpJson);
                if (LooksLikeManifestJson(httpJson)) json = httpJson;
                else Logger.Warn("HTTP 返回的 JSON 不像 manifest（可能为错误响应），已忽略");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "HTTP 下载 manifest.json 失败");
            }
        }

        ManifestV1? manifest = null;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                manifest = JsonSerializer.Deserialize<ManifestV1>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
                if (manifest is not null && !string.Equals(manifest.Schema, ManifestSchema, StringComparison.Ordinal))
                {
                    // not our schema -> treat as null to migrate
                    manifest = null;
                }
            }
            catch { manifest = null; }
        }
        manifest ??= MigrateOldToV1(entries[0].Project, json);

        foreach (var (proj, ver) in entries)
            UpsertV1(manifest, proj, ver, zipUrl, fileEntry);

        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        return JsonSerializer.Serialize(manifest, opts);
    }

    private static bool LooksLikeManifestJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("schema", out var s) && s.ValueKind == JsonValueKind.String) return true;
                if (root.TryGetProperty("projects", out var proj) && proj.ValueKind == JsonValueKind.Object) return true;
                if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array) return true;
                if (root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String && root.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String) return true;
                if (root.TryGetProperty("code", out _) && root.TryGetProperty("message", out _)) return false;
                return false;
            }
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String)
                        return true;
                }
            }
        }
        catch { return false; }
        return false;
    }

    private static void LogFetchedJson(string source, string url, string json)
    {
        const int maxLen = 10000;
        var preview = json.Length > maxLen ? json[..maxLen] + $"\n...（截断 {json.Length - maxLen} 字符）" : json;
        Logger.Info("Fetched manifest ({source}) from {url} length={len} content=\n{json}", source, url, json.Length, preview);
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class ProgressReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly Action<long, long>? _onProgress;
        private readonly long _size;
        private long _total;

        public ProgressReadStream(Stream inner, long size, Action<long, long>? onProgress)
        {
            _inner = inner;
            _size = size;
            _onProgress = onProgress;
            _total = 0;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            if (read > 0)
            {
                _total += read;
                _onProgress?.Invoke(_total, _size);
            }
            return read;
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
            if (read > 0)
            {
                _total += read;
                _onProgress?.Invoke(_total, _size);
            }
            return read;
        }
#if NET8_0_OR_GREATER
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            if (read > 0)
            {
                _total += read;
                _onProgress?.Invoke(_total, _size);
            }
            return read;
        }
#endif
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // Manifest v1 models
    private class ManifestV1
    {
        [JsonPropertyName("schema")] public string Schema { get; set; } = ManifestSchema;
        [JsonPropertyName("generatedAt")] public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
        [JsonPropertyName("projects")] public Dictionary<string, ProjectReleases> Projects { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private class ProjectReleases
    {
        [JsonPropertyName("latest")] public string? Latest { get; set; }
        [JsonPropertyName("releases")] public List<ReleaseItem> Releases { get; } = new();
    }

    private class ReleaseItem
    {
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("time")] public DateTimeOffset? Time { get; set; }
        [JsonPropertyName("notes")] public string? Notes { get; set; }
        [JsonPropertyName("files")] public List<ReleaseFile>? Files { get; set; }
    }

    private class ReleaseFile
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }

    private class ClientVersion
    {
        public string? Project { get; set; }
        public string? Version { get; set; }
        [JsonPropertyName("manifest")] public string? Manifest { get; set; }
    }

    // Helper: find repo root by walking up until we see project directories
    private static string? FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        // climb up to 8 levels
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            var d = new DirectoryInfo(dir);
            if (d.GetDirectories("LabelPlus_Next", SearchOption.TopDirectoryOnly).Any() &&
                d.GetDirectories("LabelPlus_Next.Update", SearchOption.TopDirectoryOnly).Any())
            {
                return d.FullName;
            }
            dir = d.Parent?.FullName;
        }
        return null;
    }

    private static string GetProjectPath(string repoRoot, string projectName)
    {
        // projectName is like LabelPlus_Next.Desktop / LabelPlus_Next.Update
        var parts = projectName.Split('.', 2);
        var dir = parts[0];
        var file = projectName + ".csproj";
        var combined = Path.Combine(repoRoot, dir, file);
        if (!File.Exists(combined))
        {
            // try in projectName folder directly
            combined = Path.Combine(repoRoot, projectName, file);
        }
        return combined;
    }

    private static string GetPublishDir(string repoRoot, string projectName, string rid)
    {
        var parts = projectName.Split('.', 2);
        var dir = parts[0];
        var projDir = Path.Combine(repoRoot, dir);
        // Assume net9.0 and Release
        return Path.Combine(projDir, "bin", "Release", "net9.0", rid, "publish");
    }

    private static async Task PublishProjectAsync(string repoRoot, string projectName, string rid, bool selfContained, bool singleFile, bool trimmed)
    {
        var csproj = GetProjectPath(repoRoot, projectName);
        var args = $"publish \"{csproj}\" -c Release -r {rid} --self-contained {selfContained.ToString().ToLowerInvariant()} /p:PublishSingleFile={(singleFile ? "true" : "false")} /p:PublishTrimmed={(trimmed ? "true" : "false")}";
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var tcs = new TaskCompletionSource<int>();
        proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Logger.Info(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Logger.Warn(e.Data); };
        proc.Exited += (_, __) => tcs.TrySetResult(proc.ExitCode);
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        var code = await tcs.Task.ConfigureAwait(false);
        if (code != 0) throw new InvalidOperationException($"dotenv publish 失败（{projectName} {rid}）：exit={code}");
    }

    private static string? TryReadVersion(string jsonPath)
    {
        try
        {
            if (!File.Exists(jsonPath)) return null;
            using var fs = File.OpenRead(jsonPath);
            using var doc = JsonDocument.Parse(fs);
            if (doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
            return null;
        }
        catch { return null; }
    }
}
