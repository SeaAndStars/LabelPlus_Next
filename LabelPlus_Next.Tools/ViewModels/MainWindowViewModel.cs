using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Tools.Models;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WebDav;

namespace LabelPlus_Next.Tools.ViewModels;

public class MainWindowViewModel : ObservableObject
{

    private const int MaxDepth = 10; // 可调整：树最大深度
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static IWebDavClient? _client;

    private static readonly PropfindParameters ListParams = new()
    {
        Headers = new List<KeyValuePair<string, string>>
        {
            new("Depth", "1"),
            new("Cache-Control", "no-cache"),
            new("Pragma", "no-cache")
        }
    };

    private string? baseUrl;

    // Track current client configuration to decide recreation
    private string? currentClientBaseUrl;
    private string? currentClientPassword;
    private string? currentClientUser;

    // 拆分忙碌状态：加载目录与上传互不阻塞
    private bool isLoading;

    private bool isUploading;

    private string? password;

    private string? status;

    private string? targetPath;

    private int uploadConcurrency = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

    private string? username;

    public MainWindowViewModel()
    {
        ConnectCommand = new AsyncRelayCommand(ConnectAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        _ = LoadSettingsAsync();
    }
    public string? BaseUrl { get => baseUrl; set => SetProperty(ref baseUrl, value); }
    public string? Username { get => username; set => SetProperty(ref username, value); }
    public string? Password { get => password; set => SetProperty(ref password, value); }
    public string? TargetPath { get => targetPath; set => SetProperty(ref targetPath, value); }

    public string? Status
    {
        get => status;
        set
        {
            SetProperty(ref status, value);
            Logger.Info("Status: {status}", value);
        }
    }

    public int UploadConcurrency { get => uploadConcurrency; set => SetProperty(ref uploadConcurrency, Math.Max(1, value)); }

    public ObservableCollection<DavNode> Nodes { get; } = new();

    public IAsyncRelayCommand ConnectCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    private string SettingsPath
    {
        get => Path.Combine(AppContext.BaseDirectory, "tools.settings.json");
    }

    public bool IsLoading { get => isLoading; set => SetProperty(ref isLoading, value); }
    public bool IsUploading { get => isUploading; set => SetProperty(ref isUploading, value); }

    private void EnsureClient(bool forceRecreate = false)
    {
        if (string.IsNullOrWhiteSpace(BaseUrl)) throw new InvalidOperationException("BaseUrl 未配置");
        var needRecreate = forceRecreate
                           || _client == null
                           || !string.Equals(currentClientBaseUrl, BaseUrl, StringComparison.Ordinal)
                           || !string.Equals(currentClientUser, Username, StringComparison.Ordinal)
                           || !string.Equals(currentClientPassword, Password, StringComparison.Ordinal);

        if (!needRecreate) return;

        var handler = new HttpClientHandler
        {
            PreAuthenticate = true,
            UseCookies = true,
            AllowAutoRedirect = true
        };
        if (!string.IsNullOrEmpty(Username))
        {
            handler.Credentials = new NetworkCredential(Username, Password ?? string.Empty);
        }
        var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(100)
        };
        _client = new WebDavClient(httpClient);
        currentClientBaseUrl = BaseUrl;
        currentClientUser = Username;
        currentClientPassword = Password;
        Logger.Info("Client configured for {baseUrl}", BaseUrl);
    }

    private async Task ConnectAsync()
    {
        if (IsLoading)
        {
            Status = "正在加载目录，请稍后";
            return;
        }
        IsLoading = true;
        try
        {
            Logger.Info("ConnectAsync started");
            EnsureClient(forceRecreate: true);
            Status = "刷新中...";
            await LoadTreeAsync();
            Status = "连接成功";
            await SaveSettingsAsync();
            Logger.Info("ConnectAsync completed");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "ConnectAsync failed");
            Status = $"连接异常: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    private async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }
        IsLoading = true;
        try
        {
            Logger.Info("RefreshAsync started");
            EnsureClient(forceRecreate: false);
            Status = "刷新中...";
            await LoadTreeAsync();
            Status = "刷新完成";
            Logger.Info("RefreshAsync completed");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "RefreshAsync failed");
            Status = $"刷新失败: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    private static void EnsurePlaceholderForFolder(DavNode node)
    {
        // If the folder has no children yet, add a dummy to make TreeView show expander
        if (node.IsCollection && node.Children.Count == 0)
        {
            node.Children.Add(new DavNode { Name = "…", IsCollection = false });
        }
    }

    private async Task LoadTreeAsync()
    {
        Logger.Info("LoadTreeAsync...");
        Nodes.Clear();
        var res = await _client!.Propfind(new Uri(BaseUrl!, UriKind.Absolute), ListParams);
        if (!res.IsSuccessful)
        {
            Logger.Warn("Propfind root failed: {code} {desc}", res.StatusCode, res.Description);
            Status = $"列目录失败: {res.StatusCode} {res.Description}";
            return;
        }

        var baseNorm = NormalizeUrl(BaseUrl);
        DavNode? root = null;

        // Create single root node (self)
        foreach (var r in res.Resources)
        {
            if (r.Uri is null) continue;
            if (baseNorm is not null && NormalizeUrl(r.Uri) == baseNorm)
            {
                var selfAbs = ResolveToAbsolute(r.Uri);
                selfAbs = EnsureSlash(selfAbs); // 根目录确保斜杠
                root = new DavNode { Name = r.DisplayName ?? r.Uri, Uri = selfAbs, IsCollection = true };
                break;
            }
        }
        if (root is null)
        {
            root = new DavNode { Name = BaseUrl, Uri = EnsureSlash(BaseUrl!), IsCollection = true };
        }
        Nodes.Add(root);

        // Add children of root, skip self to avoid duplicates
        foreach (var r in res.Resources)
        {
            if (r.Uri is null) continue;
            if (baseNorm is not null && NormalizeUrl(r.Uri) == baseNorm) continue; // skip self
            var absChildUri = ResolveToAbsolute(r.Uri);
            if (r.IsCollection) absChildUri = EnsureSlash(absChildUri);
            var child = new DavNode { Name = r.DisplayName ?? r.Uri, Uri = absChildUri, IsCollection = r.IsCollection };
            EnsurePlaceholderForFolder(child);
            root.Children.Add(child);
        }
        Logger.Info("Tree loaded: root={root} children={count}", root.Uri, root.Children.Count);
    }

    private async Task LoadChildrenAsync(DavNode parent, string uri, int depth)
    {
        if (depth >= MaxDepth) return; // 控制层级，避免过深
        var abs = ResolveToAbsolute(uri);
        abs = EnsureSlash(abs); // 目录请求需以斜杠结尾
        Logger.Debug("LoadChildren depth={depth} uri={uri}", depth, abs);
        var res = await _client!.Propfind(new Uri(abs, UriKind.Absolute), ListParams);
        if (!res.IsSuccessful)
        {
            Logger.Warn("Propfind failed: {code} {desc}", res.StatusCode, res.Description);
            return;
        }
        foreach (var r in res.Resources)
        {
            var resUri = r.Uri ?? string.Empty;
            var childAbs = ResolveToAbsolute(resUri);
            if (r.IsCollection) childAbs = EnsureSlash(childAbs);
            if (NormalizeUrl(childAbs) == NormalizeUrl(abs)) continue;
            var child = new DavNode { Name = r.DisplayName ?? resUri, Uri = childAbs, IsCollection = r.IsCollection };
            parent.Children.Add(child);
            if (r.IsCollection)
            {
                await Task.Delay(25); // 轻节流，避免服务端限流
                await LoadChildrenAsync(child, childAbs, depth + 1);
            }
        }
    }

    public async Task UploadFilesAsync(IReadOnlyList<string> paths, string? destFolderUri = null)
    {
        if (IsUploading)
        {
            Status = "正在上传，请稍后";
            return;
        }
        IsUploading = true;
        try
        {
            EnsureClient();
            var sem = new SemaphoreSlim(UploadConcurrency);
            var failures = new ConcurrentBag<string>();
            var tasks = paths.Select(async path =>
            {
                await sem.WaitAsync();
                try
                {
                    var ok = await UploadSingleWithRetryAsync(path, destFolderUri);
                    if (!ok) failures.Add(Path.GetFileName(path));
                }
                finally { sem.Release(); }
            }).ToArray();

            await Task.WhenAll(tasks);

            if (failures.IsEmpty)
                Status = $"上传完成，共 {paths.Count} 个文件";
            else
                Status = $"部分失败：{paths.Count - failures.Count}/{paths.Count} 成功，失败：{string.Join(", ", failures.Take(5))}{(failures.Count > 5 ? " 等" : "")}";

            Logger.Info("UploadFilesAsync completed: {count} total, {success} success, {failures} failures",
                paths.Count, paths.Count - failures.Count, failures.Count);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "UploadFilesAsync failed");
            Status = $"上传异常: {ex.Message}";
        }
        finally
        {
            IsUploading = false;
            // 自动刷新
            _ = RefreshAsync();
        }
    }

    private async Task<bool> UploadSingleWithRetryAsync(string path, string? destFolderUri)
    {
        const int maxRetries = 3;
        const int baseDelayMs = 600;
        var name = Path.GetFileName(path);

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await using var fs = File.OpenRead(path);
                var targetAbs = BuildAbsoluteTarget(destFolderUri, name);
                Logger.Info("Uploading (try {attempt}/{max}) {file} -> {target}", attempt, maxRetries, name, targetAbs);
                var result = await _client!.PutFile(targetAbs, fs);
                if (result.IsSuccessful)
                {
                    Logger.Info("Upload successful: {file} -> {target}", name, targetAbs);
                    return true;
                }

                var code = result.StatusCode;
                Logger.Warn("Upload attempt {attempt} failed: {code} {desc}", attempt, code, result.Description);
                if (!IsTransientStatus(code))
                    return false;
            }
            catch (Exception ex) when (IsTransientException(ex))
            {
                Logger.Warn(ex, "Transient error on try {attempt} for {file}", attempt, name);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Non-transient error for {file}", name);
                return false;
            }

            var delay = TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 250));
            Logger.Info("Retrying {file} in {delay}ms...", name, delay.TotalMilliseconds);
            await Task.Delay(delay);
        }
        return false;
    }

    private static bool IsTransientStatus(int code) => code == 408 || code == 429 || code >= 500 && code <= 599;

    private static bool IsTransientException(Exception ex) => ex is TaskCanceledException || ex is IOException || ex is HttpRequestException;

    private string BuildAbsoluteTarget(string? destFolderUri, string fileName)
    {
        var baseDir = EnsureSlash(BaseUrl!);
        string? relDir = null;

        if (!string.IsNullOrEmpty(destFolderUri))
        {
            if (Uri.TryCreate(destFolderUri, UriKind.Absolute, out var abs))
            {
                relDir = ToRelativePath(abs.ToString());
            }
            else
            {
                relDir = destFolderUri.Replace('\\', '/').Trim('/');
                var basePath = new Uri(baseDir).AbsolutePath.Trim('/');
                if (!string.IsNullOrEmpty(basePath))
                {
                    if (relDir.Equals(basePath, StringComparison.OrdinalIgnoreCase)) relDir = string.Empty;
                    else if (relDir.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase)) relDir = relDir[(basePath.Length + 1)..];
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(TargetPath))
        {
            relDir = TargetPath!.Replace('\\', '/').Trim('/');
        }

        var remotePath = BuildRemotePath(relDir, fileName);
        var final = new Uri(new Uri(baseDir, UriKind.Absolute), remotePath).ToString();
        Logger.Debug("BuildAbsoluteTarget dest={dest} file={file} -> rel={rel} remote={remote} final={final}", destFolderUri, fileName, relDir, remotePath, final);
        return final;
    }

    private static string BuildRemotePath(string? relativeDir, string fileName)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(relativeDir))
        {
            var cleaned = relativeDir.Replace('\\', '/').Trim('/');
            if (!string.IsNullOrEmpty(cleaned))
            {
                parts.AddRange(cleaned.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
            }
        }
        parts.Add(Uri.EscapeDataString(fileName));
        return string.Join('/', parts);
    }

    private static string EnsureSlash(string url) => url.EndsWith('/') ? url : url + "/";

    private static string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var u = new Uri(url, UriKind.Absolute);
            var left = u.GetLeftPart(UriPartial.Path).TrimEnd('/');
            return left;
        }
        catch { return url?.TrimEnd('/'); }
    }

    private string ResolveToAbsolute(string possiblyRelative)
    {
        if (string.IsNullOrWhiteSpace(possiblyRelative)) return BaseUrl ?? string.Empty;
        if (Uri.TryCreate(possiblyRelative, UriKind.Absolute, out var abs)) return abs.ToString();

        var baseUri = new Uri(EnsureSlash(BaseUrl!), UriKind.Absolute);
        var origin = new Uri(baseUri.GetLeftPart(UriPartial.Authority));
        var basePath = baseUri.AbsolutePath.Trim('/'); // e.g. "dav"

        if (possiblyRelative.StartsWith('/'))
        {
            // Root-relative path: combine with site origin to avoid duplicating base path
            return new Uri(origin, possiblyRelative).ToString();
        }
        var rel = possiblyRelative.Replace('\\', '/');
        // If rel already contains the base path prefix (e.g. "dav/..."), combine with origin
        if (!string.IsNullOrEmpty(basePath) && (rel.Equals(basePath, StringComparison.OrdinalIgnoreCase) || rel.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase)))
        {
            return new Uri(origin, "/" + rel).ToString();
        }
        // Normal relative to BaseUrl
        return new Uri(baseUri, rel).ToString();
    }

    private string? ToRelativePath(string absolute)
    {
        try
        {
            if (string.IsNullOrEmpty(BaseUrl)) return null;
            // Ensure base is treated as directory by ending with '/'
            var baseUrlDir = BaseUrl!.TrimEnd('/') + "/";
            var baseUri = new Uri(baseUrlDir, UriKind.Absolute);
            var absUri = new Uri(absolute, UriKind.Absolute);
            var rel = baseUri.MakeRelativeUri(absUri).ToString();
            return Uri.UnescapeDataString(rel);
        }
        catch { return null; }
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            await using var fs = File.OpenRead(SettingsPath);
            var s = await JsonSerializer.DeserializeAsync<ToolsSettings>(fs, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (s != null)
            {
                BaseUrl = s.BaseUrl;
                Username = s.Username;
                Password = s.Password;
                TargetPath = s.TargetPath;
            }
            Logger.Info("Settings loaded: base={base}", BaseUrl);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "LoadSettingsAsync failed");
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var s = new ToolsSettings { BaseUrl = BaseUrl, Username = Username, Password = Password, TargetPath = TargetPath };
            await using var fs = File.Create(SettingsPath);
            await JsonSerializer.SerializeAsync(fs, s, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            Logger.Info("Settings saved");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "SaveSettingsAsync failed");
        }
    }

    public async Task RefreshNodeAsync(DavNode node)
    {
        try
        {
            EnsureClient();
            if (!node.IsCollection || string.IsNullOrEmpty(node.Uri)) return;
            var abs = ResolveToAbsolute(node.Uri);
            abs = EnsureSlash(abs);
            Logger.Info("RefreshNodeAsync: {uri}", abs);
            var res = await _client!.Propfind(new Uri(abs, UriKind.Absolute), ListParams);
            if (!res.IsSuccessful)
            {
                Logger.Warn("RefreshNodeAsync failed: {code} {desc}", res.StatusCode, res.Description);
                return;
            }
            node.Children.Clear();
            foreach (var r in res.Resources)
            {
                var resUri = r.Uri ?? string.Empty;
                var childAbs = ResolveToAbsolute(resUri);
                if (r.IsCollection) childAbs = EnsureSlash(childAbs);
                if (NormalizeUrl(childAbs) == NormalizeUrl(abs)) continue;
                var child = new DavNode
                {
                    Name = r.DisplayName ?? resUri,
                    Uri = childAbs,
                    IsCollection = r.IsCollection
                };
                EnsurePlaceholderForFolder(child);
                node.Children.Add(child);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "RefreshNodeAsync error");
        }
    }
}
