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
using LabelPlus_Next.Services.Api;

namespace LabelPlus_Next.Tools.ViewModels;

public partial class PackWindowViewModel : ObservableObject
{
    private const string ManifestSchema = "labelplus-manifest/v1";
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

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
                var pubBase = new Uri(new Uri(baseDir), "d/" + targetDir);
                var zipUrl = new Uri(pubBase, fileName).ToString();
                var manifestDownloadUrlFromProject = ManifestUrl;

                // 使用文件 API 上传
                var auth = new AuthApi(baseDir);
                var login = await auth.LoginAsync(uploadSettings.Username ?? string.Empty, uploadSettings.Password ?? string.Empty);
                if (login.Code != 200 || string.IsNullOrWhiteSpace(login.Data?.Token))
                {
                    Status = $"登录失败: {login.Code} {login.Message}";
                    return;
                }
                var token = login.Data!.Token!;
                var fs = new FileSystemApi(baseDir);

                // 远端路径（以 / 开头）
                var remoteBase = "/" + targetDir;
                var remoteZipPath = remoteBase + fileName;
                await EnsureRemoteDirAsync(fs, token, remoteBase);

                CurrentTask = "上传包...";
                CurrentProgress = 0;
                var progress = new Progress<UploadProgress>(p =>
                {
                    var percent = (p.BytesTotal.HasValue && p.BytesTotal.Value > 0)
                        ? (p.BytesSent * 100.0) / p.BytesTotal.Value
                        : 0;
                    CurrentProgress = Math.Round(percent, 1);
                });
                var putRes = await fs.PutManyAsync(token,
                    new[] { new FileUploadItem { FilePath = remoteZipPath, LocalPath = outZip, Content = Array.Empty<byte>() } },
                    progress: progress, maxConcurrency: 1, asTask: false);
                if (putRes.Count == 0 || putRes[0].Code != 200)
                {
                    var code = putRes.Count > 0 ? putRes[0].Code : -1;
                    Status = $"上传失败: {code}";
                    return;
                }

                var fileEntry = new ReleaseFile
                {
                    Name = Path.GetFileName(outZip),
                    Url = zipUrl,
                    Size = zipInfo.Length,
                    Sha256 = sha256
                };

                CurrentTask = "合并清单...";
                var remoteManifestPath = remoteBase + "manifest.json";
                var mergedManifestJson = await BuildMergedManifestV1JsonAsync(fs, token, uploadSettings, manifestDownloadUrlFromProject, versionEntries, zipUrl, remoteManifestPath, fileEntry);
                var manifestFile = Path.Combine(outDir, "manifest.json");
                await File.WriteAllTextAsync(manifestFile, mergedManifestJson, Utf8NoBom);
                ManifestPath = manifestFile;
                ManifestUrl = new Uri(pubBase, "manifest.json").ToString();

                CurrentTask = "上传清单...";
                CurrentProgress = 0;
                var putManifest = await fs.SafePutAsync(token, remoteManifestPath, await File.ReadAllBytesAsync(manifestFile));
                if (putManifest.Code != 200)
                {
                    Status = $"上传清单失败: {putManifest.Code} {putManifest.Message}";
                    return;
                }

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
            var pubRoot = new Uri(new Uri(baseUrl), "d/" + target);
            var auth2 = new AuthApi(baseUrl);
            var login2 = await auth2.LoginAsync(uploadSettings.Username ?? string.Empty, uploadSettings.Password ?? string.Empty);
            if (login2.Code != 200 || string.IsNullOrWhiteSpace(login2.Data?.Token)) { Status = $"登录失败: {login2.Code} {login2.Message}"; return; }
            var token2 = login2.Data!.Token!;
            var fs2 = new FileSystemApi(baseUrl);
            var remoteBase2 = "/" + target;
            await EnsureRemoteDirAsync(fs2, token2, remoteBase2);

            // Upload all artifacts
            var uploaded = new List<(string Project, string Version, ReleaseFile File)>();
            foreach (var a in artifacts)
            {
                var name = Path.GetFileName(a.ZipPath);
                var url = new Uri(pubRoot, name).ToString();
                var remoteZip = remoteBase2 + name;
                CurrentTask = $"上传 {name}...";
                CurrentProgress = 0;
                var progress2 = new Progress<UploadProgress>(p =>
                {
                    var percent = (p.BytesTotal.HasValue && p.BytesTotal.Value > 0)
                        ? (p.BytesSent * 100.0) / p.BytesTotal.Value
                        : 0;
                    CurrentProgress = Math.Round(percent, 1);
                });
                var put = await fs2.PutManyAsync(token2,
                    new[] { new FileUploadItem { FilePath = remoteZip, LocalPath = a.ZipPath, Content = Array.Empty<byte>() } },
                    progress: progress2, maxConcurrency: 1, asTask: false);
                if (put.Count == 0 || put[0].Code != 200) { Status = $"上传失败: {name}"; return; }
                uploaded.Add((a.Project, a.Version, new ReleaseFile { Name = name, Url = url, Size = a.Size, Sha256 = a.Sha256 }));
            }

            // Merge manifest with all uploaded files
            CurrentTask = "合并清单...";
            string? json = null;
            try
            {
                var get = await fs2.GetAsync(token2, remoteBase2 + "manifest.json");
                if (get.Code == 200 && get.Data is not null && !get.Data.IsDir)
                {
                    var dl = await fs2.DownloadAsync(token2, remoteBase2 + "manifest.json");
                    if (dl.Code == 200 && dl.Content is not null)
                        json = StripUtf8Bom(Encoding.UTF8.GetString(dl.Content));
                }
            }
            catch (HttpRequestException ex)
            {
                Logger.Warn(ex, "网络异常，无法获取远端 manifest");
            }
            catch (TaskCanceledException ex)
            {
                Logger.Warn(ex, "获取远端 manifest 超时");
            }
            catch (IOException ex)
            {
                Logger.Warn(ex, "读取远端 manifest 时发生 IO 错误");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "获取远端 manifest 时发生未预期异常");
                throw;
            }

            ManifestV1? manifest = null;
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    json = StripUtf8Bom(json);
                    manifest = JsonSerializer.Deserialize<ManifestV1>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
                    if (manifest is not null && !string.Equals(manifest.Schema, ManifestSchema, StringComparison.Ordinal))
                        manifest = null;
                }
                catch (JsonException ex)
                {
                    Logger.Warn(ex, "远端 manifest JSON 解析失败，忽略并重新生成");
                    manifest = null;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "解析远端 manifest 时发生未预期异常");
                    throw;
                }
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
            await File.WriteAllTextAsync(manifestOut, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), Utf8NoBom);
            ManifestPath = manifestOut;
            ManifestUrl = new Uri(pubRoot, "manifest.json").ToString();

            CurrentTask = "上传清单...";
            CurrentProgress = 0;
            var putMan2 = await fs2.SafePutAsync(token2, remoteBase2 + "manifest.json", await File.ReadAllBytesAsync(manifestOut));
            if (putMan2.Code != 200) { Status = $"上传清单失败: {putMan2.Code} {putMan2.Message}"; return; }

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

    // WebDAV 已移除，上传走 FileSystemApi

    private static string AppendSlash(string url) => url.EndsWith('/') ? url : url + '/';
    private static string NormalizePath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return string.Empty;
        var s = p.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s + '/';
    }

    private static string StripUtf8Bom(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text[0] == '\uFEFF' ? text[1..] : text;
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
        catch (JsonException ex)
        {
            Logger.Warn(ex, "旧 manifest JSON 结构无效，将返回空项目列表");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "迁移旧 manifest 时发生未预期异常");
            throw;
        }
        prj.Releases.Sort((x, y) =>
        {
            if (x?.Version is null || y?.Version is null) return 0;
            return -CompareSemVer(x.Version, y.Version); // desc
        });
        prj.Latest = prj.Releases.Count > 0 ? prj.Releases[0].Version : null;
        return manifest;
    }

    // 旧的 WebDAV 合并方法已移除，改用文件 API 的 BuildMergedManifestV1JsonAsync

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
        catch (JsonException ex)
        {
            Logger.Debug(ex, "检测 manifest JSON 格式失败");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "检查 manifest JSON 时发生未预期异常");
            throw;
        }
        return false;
    }

    private static void LogFetchedJson(string source, string url, string json)
    {
        const int maxLen = 10000;
        var preview = json.Length > maxLen ? json[..maxLen] + $"\n...（截断 {json.Length - maxLen} 字符）" : json;
        Logger.Info("Fetched manifest ({source}) from {url} length={len} content=\n{json}", source, url, json.Length, preview);
    }

    // 使用文件 API 合并 manifest（不依赖 WebDAV）
    private async Task<string> BuildMergedManifestV1JsonAsync(FileSystemApi fs, string token, ToolsSettings s, string? downloadUrl,
        IReadOnlyList<(string Project, string Version)> entries, string zipUrl, string remoteManifestPath, ReleaseFile fileEntry)
    {
        string? json = null;
        // 1) 从文件 API 获取 manifest.json
        try
        {
            var get = await fs.GetAsync(token, remoteManifestPath);
            if (get.Code == 200 && get.Data is not null && !get.Data.IsDir)
            {
                var dl = await fs.DownloadAsync(token, remoteManifestPath);
                if (dl.Code == 200 && dl.Content is not null)
                {
                    var candidate = StripUtf8Bom(Encoding.UTF8.GetString(dl.Content));
                    Logger.Info("已通过 FileAPI 获取 manifest.json: {path}", remoteManifestPath);
                    LogFetchedJson("FileAPI", remoteManifestPath, candidate);
                    // 仅当内容看起来像 manifest 再采用，避免后续反序列化触发第一次机会异常
                    if (LooksLikeManifestJson(candidate))
                        json = candidate;
                    else
                        Logger.Warn("FileAPI 返回的 JSON 不像 manifest（可能是错误页/登录页），已忽略");
                }
            }
            else
            {
                Logger.Info("FileAPI 获取 manifest.json 为空或失败: {code} {msg}", get.Code, get.Message);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "通过 FileAPI 获取 manifest.json 异常");
        }

        // 2) 回退到 HTTP 下载地址（若像 manifest）
        if (json is null && !string.IsNullOrWhiteSpace(downloadUrl))
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                if (!string.IsNullOrEmpty(s.Username))
                {
                    var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{s.Username}:{s.Password}"));
                    http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basic);
                }
                var httpJson = StripUtf8Bom(await http.GetStringAsync(downloadUrl));
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
                json = StripUtf8Bom(json);
                manifest = JsonSerializer.Deserialize<ManifestV1>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
                if (manifest is not null && !string.Equals(manifest.Schema, ManifestSchema, StringComparison.Ordinal))
                    manifest = null;
            }
            catch (JsonException ex)
            {
                Logger.Warn(ex, "通过下载地址获取的 manifest JSON 无法解析，忽略");
                manifest = null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "处理下载 manifest JSON 时发生未预期异常");
                throw;
            }
        }
        manifest ??= MigrateOldToV1(entries[0].Project, json);

        foreach (var (proj, ver) in entries)
            UpsertV1(manifest, proj, ver, zipUrl, fileEntry);

        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        return JsonSerializer.Serialize(manifest, opts);
    }

    private static async Task EnsureRemoteDirAsync(FileSystemApi fs, string token, string baseDir)
    {
        // baseDir like "/OneDrive2/Update/"
        if (string.IsNullOrWhiteSpace(baseDir)) return;
        var p = baseDir.Replace("\\", "/");
        if (!p.StartsWith('/')) p = "/" + p;
        p = p.TrimEnd('/');
        if (p == "/") return;
        var parts = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "/";
        foreach (var part in parts)
        {
            current = current == "/" ? "/" + part : current + "/" + part;
            try { await fs.MkdirAsync(token, current); }
            catch (HttpRequestException ex)
            {
                Logger.Warn(ex, "创建远端目录 {current} 时网络异常", current);
            }
            catch (TaskCanceledException ex)
            {
                Logger.Warn(ex, "创建远端目录 {current} 超时", current);
            }
            catch (IOException ex)
            {
                Logger.Warn(ex, "创建远端目录 {current} 时 IO 错误", current);
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Warn(ex, "创建远端目录 {current} 权限不足", current);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "创建远端目录 {current} 时发生未预期异常", current);
                throw;
            }
        }
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
        // Use the actual csproj location to derive the publish folder to avoid
        // accidentally pointing to the root project (e.g. LabelPlus_Next) when
        // we really want LabelPlus_Next.Desktop or LabelPlus_Next.Update.
        var csproj = GetProjectPath(repoRoot, projectName);
        var projDir = Path.GetDirectoryName(csproj) ?? Path.Combine(repoRoot, projectName);
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
        catch (IOException ex)
        {
            Logger.Warn(ex, "读取 {jsonPath} 失败，无法获取版本", jsonPath);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warn(ex, "读取 {jsonPath} 权限不足", jsonPath);
            return null;
        }
        catch (JsonException ex)
        {
            Logger.Warn(ex, "{jsonPath} JSON 格式无效，忽略版本信息", jsonPath);
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "处理 {jsonPath} 时发生未预期异常", jsonPath);
            throw;
        }
    }
}
