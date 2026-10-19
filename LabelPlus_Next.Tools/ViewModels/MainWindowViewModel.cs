using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Services.Api;
using LabelPlus_Next.Tools.Models;
using LabelPlus_Next.Tools.Services;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WebDav;

namespace LabelPlus_Next.Tools.ViewModels;

public class MainWindowViewModel : ObservableObject
{

    private const int MaxDepth = 10; // 可调整：树最大深度
    private const int SettingsSaveDelayMs = 400;
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static IWebDavClient? _client;

    private readonly SemaphoreSlim settingsSaveLock = new(1, 1);
    private CancellationTokenSource? settingsSaveDebounce;
    private bool isLoadingSettings;
    private bool settingsDirty;
    private int settingsVersion;

    private bool hasTreeError;
    private bool isTreeMessageVisible;
    private string? treeMessage;

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

    private string? apiBaseUrl;

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

    public MainWindowViewModel() : this(ToolsSettingsStore.DefaultPath)
    {
    }

    /// <summary>Allows tests (and alternative hosts) to point the tool at another settings file.</summary>
    public MainWindowViewModel(string settingsFilePath)
    {
        SettingsFilePath = settingsFilePath;
        ConnectCommand = new AsyncRelayCommand(ConnectAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ApiUploadCommand = new AsyncRelayCommand<IReadOnlyList<string>?>(ApiUploadAsync);

        // Load synchronously, before the window binds, so the saved configuration is
        // always visible on first paint and cannot be overwritten by a late async load.
        LoadSettings();
    }

    public string SettingsFilePath { get; }

    public string? BaseUrl { get => baseUrl; set => SetProperty(ref baseUrl, value); }
    public string? ApiBaseUrl { get => apiBaseUrl; set => SetProperty(ref apiBaseUrl, value); }
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

    public string? TreeMessage { get => treeMessage; private set => SetProperty(ref treeMessage, value); }
    public bool IsTreeMessageVisible { get => isTreeMessageVisible; private set => SetProperty(ref isTreeMessageVisible, value); }
    public bool HasTreeError { get => hasTreeError; private set => SetProperty(ref hasTreeError, value); }

    public int UploadConcurrency { get => uploadConcurrency; set => SetProperty(ref uploadConcurrency, Math.Max(1, value)); }

    public ObservableCollection<DavNode> Nodes { get; } = new();

    public IAsyncRelayCommand ConnectCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand<IReadOnlyList<string>?> ApiUploadCommand { get; }

    public bool IsLoading { get => isLoading; set => SetProperty(ref isLoading, value); }
    public bool IsUploading { get => isUploading; set => SetProperty(ref isUploading, value); }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is nameof(BaseUrl) or nameof(ApiBaseUrl) or nameof(Username)
            or nameof(Password) or nameof(TargetPath))
        {
            QueueSettingsSave();
        }
    }

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
            Timeout = Timeout.InfiniteTimeSpan
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
            BeginTreeLoad();

            var result = await LoadTreeAsync();
            // Persist the (possibly auto-corrected) endpoint and credentials even when the
            // listing failed, so a fixed BaseUrl is not lost on the next launch.
            await SaveSettingsNowAsync();

            if (!result.Success)
            {
                Status = result.Error ?? "列目录失败";
                ShowTreeMessage(Status, isError: true);
                Logger.Warn("ConnectAsync failed: {error}", Status);
                return;
            }

            Status = result.EffectiveUrl is null
                ? "连接成功"
                : $"连接成功（已自动改用 WebDAV 地址 {result.EffectiveUrl}）";
            if (Nodes.Count == 0)
            {
                ShowTreeMessage("目录为空。", isError: false);
            }
            else
            {
                HideTreeMessage();
            }
            Logger.Info("ConnectAsync completed");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "ConnectAsync failed");
            Status = $"连接异常: {ex.Message}";
            ShowTreeMessage(Status, isError: true);
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
            BeginTreeLoad();

            var result = await LoadTreeAsync();
            if (!result.Success)
            {
                Status = result.Error ?? "刷新失败";
                ShowTreeMessage(Status, isError: true);
                return;
            }

            Status = result.EffectiveUrl is null
                ? "刷新完成"
                : $"刷新完成（已自动改用 WebDAV 地址 {result.EffectiveUrl}）";
            if (Nodes.Count == 0)
            {
                ShowTreeMessage("目录为空。", isError: false);
            }
            else
            {
                HideTreeMessage();
            }
            Logger.Info("RefreshAsync completed");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "RefreshAsync failed");
            Status = $"刷新失败: {ex.Message}";
            ShowTreeMessage(Status, isError: true);
        }
        finally { IsLoading = false; }
    }

    public async Task ApiUploadAsync(IReadOnlyList<string>? files)
    {
        try
        {
            if (files is null || files.Count == 0) return;
            var apiBase = ToolsEndpoints.ResolveApiBaseUrl(ApiBaseUrl, BaseUrl);
            if (apiBase is null) { Status = "未配置 API BaseUrl"; return; }
            var auth = new AuthApi(apiBase);
            var login = await auth.LoginAsync(Username ?? string.Empty, Password ?? string.Empty);
            if (login.Code != 200 || string.IsNullOrWhiteSpace(login.Data?.Token)) { Status = $"登录失败: {login.Code} {login.Message}（API: {apiBase}）"; return; }
            var token = login.Data!.Token!;

            var fs = new FileSystemApi(apiBase);
            var items = files.Select(p => new FileUploadItem
            {
                FilePath = CombineDavPath(TargetPath ?? "/", Path.GetFileName(p)),
                LocalPath = p
            }).ToList();
            var results = await fs.PutManyAsync(token, items, progress: null, maxConcurrency: UploadConcurrency, asTask: false);
            if (results.All(r => r.Code == 200))
                Status = $"API 上传完成，共 {files.Count} 个";
            else
                Status = $"API 上传部分失败：{results.Count(r => r.Code == 200)}/{results.Count} 成功";
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "ApiUploadAsync failed");
            Status = $"API 上传异常: {ex.Message}";
        }
    }

    private static string CombineDavPath(string dir, string name)
    {
        var d = string.IsNullOrWhiteSpace(dir) ? "/" : dir.Replace('\\', '/');
        if (!d.StartsWith('/')) d = "/" + d;
        if (!d.EndsWith('/')) d += "/";
        return d + name;
    }

    private static void EnsurePlaceholderForFolder(DavNode node)
    {
        // If the folder has no children yet, add a dummy to make TreeView show expander
        if (node.IsCollection && node.Children.Count == 0)
        {
            node.Children.Add(new DavNode { Name = "…", IsCollection = false });
        }
    }

    private sealed record TreeLoadResult(bool Success, string? EffectiveUrl, string? Error);

    private void BeginTreeLoad()
    {
        TreeMessage = "正在读取目录…";
        HasTreeError = false;
        IsTreeMessageVisible = true;
    }

    private void ShowTreeMessage(string message, bool isError)
    {
        TreeMessage = message;
        HasTreeError = isError;
        IsTreeMessageVisible = true;
    }

    private void HideTreeMessage()
    {
        HasTreeError = false;
        IsTreeMessageVisible = false;
    }

    private async Task<TreeLoadResult> LoadTreeAsync()
    {
        Logger.Info("LoadTreeAsync...");
        Nodes.Clear();

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new TreeLoadResult(false, null, "BaseUrl 未配置");
        }

        var requestedUrl = NormalizeBaseUrl(BaseUrl!);
        if (!Uri.TryCreate(requestedUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return new TreeLoadResult(false, null, $"BaseUrl 格式不正确: {BaseUrl}");
        }

        var (res, effectiveUrl) = await PropfindRootAsync(requestedUrl);
        if (!res.IsSuccessful)
        {
            var error = BuildListingError(res.StatusCode, res.Description);
            Logger.Warn("Propfind root failed: {code} {desc} ({url})", res.StatusCode, res.Description, requestedUrl);
            return new TreeLoadResult(false, null, error);
        }

        if (effectiveUrl is not null)
        {
            // Continue with the endpoint that actually answered, so relative hrefs in the
            // response resolve correctly. The setter also triggers an auto-save.
            BaseUrl = effectiveUrl;
        }

        var baseNorm = NormalizeUrl(effectiveUrl ?? requestedUrl);
        DavNode? root = null;

        // Create single root node (self)
        foreach (var r in res.Resources)
        {
            if (r.Uri is null) continue;
            var resolved = ResolveToAbsolute(r.Uri);
            if (baseNorm is not null && NormalizeUrl(resolved) == baseNorm)
            {
                root = new DavNode { Name = r.DisplayName ?? r.Uri, Uri = EnsureSlash(resolved), IsCollection = true };
                break;
            }
        }
        if (root is null)
        {
            root = new DavNode { Name = BaseUrl, Uri = EnsureSlash(BaseUrl!), IsCollection = true };
        }

        // Show the directory contents immediately after connecting instead of a single
        // collapsed row. This is also what makes the fix visible in the UI.
        root.IsExpanded = true;
        Nodes.Add(root);

        // Add children of root, skip self to avoid duplicates
        foreach (var r in res.Resources)
        {
            if (r.Uri is null) continue;
            var absChildUri = ResolveToAbsolute(r.Uri);
            if (baseNorm is not null && NormalizeUrl(absChildUri) == baseNorm) continue; // skip self
            if (r.IsCollection) absChildUri = EnsureSlash(absChildUri);
            var child = new DavNode { Name = r.DisplayName ?? r.Uri, Uri = absChildUri, IsCollection = r.IsCollection };
            EnsurePlaceholderForFolder(child);
            root.Children.Add(child);
        }
        Logger.Info("Tree loaded: root={root} children={count}", root.Uri, root.Children.Count);
        return new TreeLoadResult(true, effectiveUrl, null);
    }

    /// <summary>
    /// AList exposes WebDAV on <c>/dav/</c> while its web/API root answers PROPFIND with
    /// 404/405. When the configured URL looks like a site root, transparently try the
    /// conventional endpoint once instead of leaving the user with an empty tree.
    /// </summary>
    private async Task<(PropfindResponse Response, string? EffectiveUrl)> PropfindRootAsync(string baseUrl)
    {
        var response = await _client!.Propfind(new Uri(baseUrl, UriKind.Absolute), ListParams);
        if (response.IsSuccessful) return (response, null);

        if (!ShouldTryDavSuffix(baseUrl, response.StatusCode)) return (response, null);

        var davUrl = new Uri(new Uri(baseUrl, UriKind.Absolute), "dav/").ToString();
        Logger.Info("PROPFIND {url} returned {code}; retrying AList WebDAV endpoint {davUrl}",
            baseUrl, response.StatusCode, davUrl);

        var davResponse = await _client.Propfind(new Uri(davUrl, UriKind.Absolute), ListParams);
        if (davResponse.IsSuccessful) return (davResponse, davUrl);

        // An auth error on /dav/ is more actionable than 405 on the site root: the
        // endpoint exists, only the credentials are wrong.
        if (davResponse.StatusCode is 401 or 403) return (davResponse, null);

        return (response, null);
    }

    private static bool ShouldTryDavSuffix(string baseUrl, int statusCode)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return false;

        // Already a /dav endpoint (or a subpath of one): don't append another segment.
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/dav", StringComparison.OrdinalIgnoreCase)
            || path.Equals("dav", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return statusCode is 400 or 403 or 404 or 405 or 501;
    }

    private static string BuildListingError(int statusCode, string? description)
    {
        var detail = string.IsNullOrWhiteSpace(description)
            ? statusCode.ToString()
            : $"{statusCode} {description}";

        var hint = statusCode switch
        {
            400 => "服务器拒绝了请求，请检查 BaseUrl 是否正确。",
            401 or 403 => "认证失败或无权访问，请检查用户名和密码。",
            404 or 405 or 501 => "该地址不是 WebDAV 端点。AList 的 WebDAV 地址通常为 https://主机/dav/。",
            _ => "请检查 BaseUrl、网络连接和服务器状态。"
        };

        return $"列目录失败: {detail}。{hint}";
    }

    private static string NormalizeBaseUrl(string url)
    {
        var trimmed = url.Trim();
        return EnsureSlash(trimmed);
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
        catch (UriFormatException ex)
        {
            Logger.Debug(ex, "Invalid URL format detected, returning trimmed string");
            return url?.TrimEnd('/');
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unexpected error normalizing URL");
            throw;
        }
    }

    private string ResolveToAbsolute(string possiblyRelative)
    {
        if (string.IsNullOrWhiteSpace(possiblyRelative)) return BaseUrl ?? string.Empty;

        var baseUri = new Uri(EnsureSlash(BaseUrl!), UriKind.Absolute);
        var origin = new Uri(baseUri.GetLeftPart(UriPartial.Authority));

        if (Uri.TryCreate(possiblyRelative, UriKind.Absolute, out var abs))
        {
            // WebDav.Client parses root-relative hrefs ("/dav/file") with no base
            // address and yields "file:///dav/file". Re-anchor those at the WebDAV
            // origin, otherwise every node below the root becomes unusable.
            if (abs.IsFile && string.IsNullOrEmpty(abs.Host))
            {
                return new Uri(origin, abs.PathAndQuery).ToString();
            }
            return abs.ToString();
        }

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
        catch (UriFormatException ex)
        {
            Logger.Debug(ex, "Failed to compute relative URL, returning null");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unexpected error computing relative URL");
            throw;
        }
    }

    private void LoadSettings()
    {
        try
        {
            isLoadingSettings = true;

            var settings = ToolsSettingsStore.Load(SettingsFilePath);
            if (settings is null)
            {
                Logger.Info("No settings file at {path}; using defaults", SettingsFilePath);
                return;
            }

            BaseUrl = settings.BaseUrl;
            ApiBaseUrl = settings.ApiBaseUrl;
            Username = settings.Username;
            Password = settings.Password;
            TargetPath = settings.TargetPath;
            Logger.Info("Settings loaded from {path}: base={base} api={api}", SettingsFilePath, BaseUrl, ApiBaseUrl);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "LoadSettings failed ({path})", SettingsFilePath);
        }
        finally
        {
            isLoadingSettings = false;
            settingsDirty = false;
        }
    }

    /// <summary>
    /// Debounces disk writes while the user is typing. Any change to a connection
    /// setting is persisted automatically, no "Connect" click required.
    /// </summary>
    private void QueueSettingsSave()
    {
        if (isLoadingSettings) return;

        settingsDirty = true;
        settingsVersion++;

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref settingsSaveDebounce, cts);
        previous?.Cancel();
        previous?.Dispose();

        _ = DebouncedSaveAsync(cts.Token);
    }

    private async Task DebouncedSaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SettingsSaveDelayMs, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested) return;
        await SaveSettingsNowAsync();
    }

    /// <summary>Writes the current configuration immediately (also used by the settings dialog).</summary>
    public async Task<bool> SaveSettingsNowAsync()
    {
        // An explicit save supersedes a debounce that has not fired yet.
        var pending = Interlocked.Exchange(ref settingsSaveDebounce, null);
        pending?.Cancel();
        pending?.Dispose();

        var version = settingsVersion;
        var snapshot = CreateSettingsSnapshot();

        await settingsSaveLock.WaitAsync();
        try
        {
            await ToolsSettingsStore.SaveAsync(SettingsFilePath, snapshot);
            if (version == settingsVersion) settingsDirty = false;
            Logger.Info("Settings saved to {path}", SettingsFilePath);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "SaveSettings failed ({path})", SettingsFilePath);
            Status = $"设置保存失败: {ex.Message}";
            return false;
        }
        finally
        {
            settingsSaveLock.Release();
        }
    }

    /// <summary>Last-chance synchronous save used when the application shuts down.</summary>
    public void FlushSettings()
    {
        if (!settingsDirty) return;

        var pending = Interlocked.Exchange(ref settingsSaveDebounce, null);
        pending?.Cancel();
        pending?.Dispose();

        try
        {
            ToolsSettingsStore.Save(SettingsFilePath, CreateSettingsSnapshot());
            settingsDirty = false;
            Logger.Info("Settings flushed on shutdown");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "FlushSettings failed ({path})", SettingsFilePath);
        }
    }

    private ToolsSettings CreateSettingsSnapshot() => new()
    {
        BaseUrl = BaseUrl,
        ApiBaseUrl = ApiBaseUrl,
        Username = Username,
        Password = Password,
        TargetPath = TargetPath
    };

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
