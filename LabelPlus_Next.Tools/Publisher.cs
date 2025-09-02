using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LabelPlus_Next.Tools.Models;
using LabelPlus_Next.Services.Api;

namespace LabelPlus_Next.Tools;

public static class Publisher
{
    private const string DefaultUserAgent = "pan.baidu.com";

    public static async Task<int> RunAsync(string version, string artifactsDir, PublishSettings settings)
    {
        // 1) Zip artifacts per platform if needed and compute sha256
        var artifacts = DiscoverArtifacts(artifactsDir);
        var uploaded = new List<(string filename, string url, long size, string sha256, Platform platform)>();
        var baseUrl = settings.BaseUrl.TrimEnd('/') + "/";
        var auth = new AuthApi(baseUrl);
        var login = await auth.LoginAsync(settings.Username, settings.Password);
        if (login.Code != 200 || string.IsNullOrWhiteSpace(login.Data?.Token))
            throw new InvalidOperationException($"登录失败: {login.Code} {login.Message}");
        var token = login.Data!.Token!;
        var fs = new FileSystemApi(baseUrl);

        foreach (var a in artifacts)
        {
            var (zipPath, size, sha) = await EnsureZipAndHashAsync(a.path);
            var remotePath = CombineDavPath(settings.UploadRoot, Path.GetFileName(zipPath));
            await EnsureRemoteDirAsync(fs, token, GetDir(remotePath));
            var res = await fs.SafePutAsync(token, remotePath, await File.ReadAllBytesAsync(zipPath));
            if (res.Code != 200) throw new InvalidOperationException($"上传失败: {remotePath} {res.Code} {res.Message}");
            // 获取公开直链：优先 raw_url，其次 /d/sign，最后兜底 dav 路径
            var meta = await fs.GetAsync(token, remotePath);
            string publicUrl = settings.BaseUrl.TrimEnd('/') + remotePath; // fallback
            if (meta.Code == 200 && meta.Data is not null && !meta.Data.IsDir)
            {
                if (!string.IsNullOrWhiteSpace(meta.Data.RawUrl)) publicUrl = meta.Data.RawUrl!;
                else if (!string.IsNullOrWhiteSpace(meta.Data.Sign)) publicUrl = settings.BaseUrl.TrimEnd('/') + "/d/" + Uri.EscapeDataString(meta.Data.Sign!);
            }
            uploaded.Add((Path.GetFileName(zipPath), publicUrl, size, sha, a.platform));
        }

        // 2) Download current manifest (if exists), update projects
    var manifest = await DownloadManifestAsync(settings, fs, token) ?? new Manifest();
        manifest.GeneratedAt = DateTimeOffset.UtcNow;

    // Separate Desktop and Update packages by name
    var desktopFiles = uploaded.FindAll(f => !f.filename.Contains("update", StringComparison.OrdinalIgnoreCase));
    var updateFiles = uploaded.FindAll(f => f.filename.Contains("update", StringComparison.OrdinalIgnoreCase));

    UpdateProject(manifest, "LabelPlus_Next.Desktop", version, desktopFiles, defaultEntry: new EntryMeta
        {
            Windows = "LabelPlus_Next.Desktop.exe",
            Linux = "LabelPlus_Next",
            Macos = "LabelPlus_Next.app"
        });
    UpdateProject(manifest, "LabelPlus_Next.Update", version, updateFiles, defaultEntry: new EntryMeta
        {
            Windows = "LabelPlus_Next.Update.exe",
            Linux = "LabelPlus_Next.Update",
            Macos = "LabelPlus_Next.Update.app"
        });

        // 3) Upload manifest
    await UploadManifestAsync(settings, manifest, fs, token);
        return 0;
    }

    private static (string path, Platform platform)[] DiscoverArtifacts(string dir)
    {
        // Discover recursively: publish directories and zip files containing RID keywords
        var list = new List<(string, Platform)>();
        foreach (var path in Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories))
        {
            Platform? p = null;
            var lower = path.Replace('\\', '/').ToLowerInvariant();
            if (lower.Contains("win-x64") || lower.Contains("win-arm64")) p = Platform.Windows;
            else if (lower.Contains("linux-x64") || lower.Contains("linux-arm64")) p = Platform.Linux;
            else if (lower.Contains("osx-x64") || lower.Contains("osx-arm64") || lower.Contains("macos")) p = Platform.MacOS;
            if (p is null) continue;

            if (Directory.Exists(path))
            {
                // Only pick publish folders or obvious platform folders
                var name = Path.GetFileName(path).ToLowerInvariant();
                if (name == "publish" || name.Contains("win-") || name.Contains("linux-") || name.Contains("osx-") || name.Contains("macos"))
                {
                    list.Add((path, p.Value));
                }
            }
            else if (File.Exists(path))
            {
                // Prefer zip files if already prepared
                if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) list.Add((path, p.Value));
            }
        }
        return list.ToArray();
    }

    private static async Task<(string zipPath, long size, string sha256)> EnsureZipAndHashAsync(string path)
    {
        string zipPath;
        if (Directory.Exists(path))
        {
            var rid = PathInference.InferRidFromPath(path) ?? "any";
            var proj = PathInference.FindProjectNameUpwards(path) ?? new DirectoryInfo(path).Name;
            var name = $"{proj}-{rid}.zip";
            zipPath = Path.Combine(Path.GetTempPath(), name);
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(path, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }
        else if (File.Exists(path))
        {
            zipPath = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? path : path + ".zip";
            if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                archive.CreateEntryFromFile(path, Path.GetFileName(path));
            }
        }
        else throw new FileNotFoundException(path);

        var fi = new FileInfo(zipPath);
        await using var fs = File.OpenRead(zipPath);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs);
        return (zipPath, fi.Length, Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static string GetDir(string path)
    {
        var idx = path.LastIndexOf('/');
        return idx <= 0 ? "/" : path.Substring(0, idx);
    }

    private static async Task<string?> ApiLoginAsync(PublishSettings s)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(s.BaseUrl.TrimEnd('/') + "/") };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
            var body = JsonSerializer.Serialize(new { username = s.Username, password = s.Password });
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("token", out var token)) return token.GetString();
        }
        catch { }
        return null;
    }

    private static bool LooksLikeManifest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (root.TryGetProperty("schema", out var schema)
                && schema.ValueKind == JsonValueKind.String
                && root.TryGetProperty("projects", out var proj)
                && proj.ValueKind == JsonValueKind.Object) return true;
        }
        catch { }
        return false;
    }

    private static async Task<Manifest?> DownloadManifestAsync(PublishSettings s, FileSystemApi fs, string token)
    {
        try
        {
            var get = await fs.GetAsync(token, s.ManifestPath);
            if (get.Code != 200 || get.Data is null || get.Data.IsDir) return null;
            string? json = null;
            if (!string.IsNullOrWhiteSpace(get.Data.RawUrl))
            {
                // 直接 HTTP 获取 raw_url 内容
                using var http = new HttpClient();
                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
                var text = await http.GetStringAsync(get.Data.RawUrl);
                if (LooksLikeManifest(text)) json = text; // 只接受像 manifest 的内容
            }
            else if (!string.IsNullOrWhiteSpace(get.Data.Sign))
            {
                var raw = s.BaseUrl.TrimEnd('/') + "/d/" + Uri.EscapeDataString(get.Data.Sign!);
                using var http = new HttpClient();
                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
                var text = await http.GetStringAsync(raw + (raw.Contains('?') ? "&download=1" : "?download=1"));
                if (LooksLikeManifest(text)) json = text;
            }
            else
            {
                var dl = await fs.DownloadAsync(token, s.ManifestPath);
                if (dl.Code != 200 || dl.Content is null) return null;
                var text = Encoding.UTF8.GetString(dl.Content);
                if (LooksLikeManifest(text)) json = text;
            }
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<Manifest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch { return null; }
    }

    private static async Task UploadManifestAsync(PublishSettings s, Manifest m, FileSystemApi fs, string token)
    {
        await EnsureRemoteDirAsync(fs, token, GetDir(s.ManifestPath));
        var json = JsonSerializer.Serialize(m, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        var res = await fs.SafePutAsync(token, s.ManifestPath, Encoding.UTF8.GetBytes(json));
        if (res.Code != 200) throw new InvalidOperationException($"上传 manifest 失败: {res.Code} {res.Message}");
    }

    private static void UpdateProject(Manifest m, string projectName, string version, List<(string filename, string url, long size, string sha256, Platform platform)> files, EntryMeta defaultEntry)
    {
        if (!m.Projects.TryGetValue(projectName, out var proj))
        {
            proj = new Project();
            m.Projects[projectName] = proj;
        }
        proj.Latest = version;
        var release = new ProjectRelease { Version = version };
        foreach (var f in files)
        {
            var file = new ProjectReleaseFile
            {
                Name = f.filename,
                Url = f.url,
                Size = f.size,
                Sha256 = f.sha256,
            };
            // attach entry metadata to each file so Updater can read based on platform
            file.EntryWindows = defaultEntry.Windows;
            file.EntryLinux = defaultEntry.Linux;
            file.EntryMacos = defaultEntry.Macos;
            release.Files.Add(file);
        }
        // 合并：移除同版本，追加后按版本倒序去重保留历史
        proj.Releases.RemoveAll(r => string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase));
        proj.Releases.Add(release);
        // 去重（按 Version）
        var map = new Dictionary<string, ProjectRelease>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in proj.Releases)
        {
            if (!map.ContainsKey(r.Version)) map[r.Version] = r;
        }
        // 排序：语义版本优先，否则按字符串降序
        static int Cmp(string a, string b)
        {
            if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb)) return -va.CompareTo(vb);
            return -string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }
        var sorted = new List<ProjectRelease>(map.Values);
        sorted.Sort((x, y) => Cmp(x.Version, y.Version));
        proj.Releases = sorted;
    }

    private static string CombineDavPath(string root, string fileName)
    {
        if (!root.StartsWith('/')) root = "/" + root;
        if (root.EndsWith('/')) return root + fileName;
        return root + "/" + fileName;
    }

    private static async Task EnsureRemoteDirAsync(FileSystemApi fs, string token, string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || dir == "/") return;
        var parts = dir.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "/";
        foreach (var p in parts)
        {
            current = current == "/" ? "/" + p : current + "/" + p;
            try { await fs.MkdirAsync(token, current); } catch { }
        }
    }
}

public enum Platform { Windows, Linux, MacOS }

public sealed class EntryMeta
{
    public string? Windows { get; set; }
    public string? Linux { get; set; }
    public string? Macos { get; set; }
}

// helpers
file static class PathInference
{
    public static string? InferRidFromPath(string path)
    {
        var lower = path.Replace('\\', '/').ToLowerInvariant();
        foreach (var rid in new[] { "win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64" })
        {
            if (lower.Contains(rid)) return rid;
        }
        return null;
    }

    public static string? FindProjectNameUpwards(string start)
    {
        try
        {
            var dir = new DirectoryInfo(start);
            for (int i = 0; i < 8 && dir is not null; i++)
            {
                var csprojs = dir.GetFiles("*.csproj", SearchOption.TopDirectoryOnly);
                if (csprojs.Length > 0) return Path.GetFileNameWithoutExtension(csprojs[0].Name);
                dir = dir.Parent;
            }
        }
        catch { }
        return null;
    }
}
