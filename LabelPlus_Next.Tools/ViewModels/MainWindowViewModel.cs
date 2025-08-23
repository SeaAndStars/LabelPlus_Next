using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Tools.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using WebDav;

namespace LabelPlus_Next.Tools.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private static IWebDavClient? _client;

    private const int MaxDepth = 20; // 可调整：树最大深度

    private string? baseUrl;
    public string? BaseUrl { get => baseUrl; set => SetProperty(ref baseUrl, value); }

    private string? username;
    public string? Username { get => username; set => SetProperty(ref username, value); }

    private string? password;
    public string? Password { get => password; set => SetProperty(ref password, value); }

    private string? status;
    public string? Status { get => status; set => SetProperty(ref status, value); }

    public ObservableCollection<DavNode> Nodes { get; } = new();

    public IAsyncRelayCommand ConnectCommand { get; }

    private string SettingsPath => Path.Combine(AppContext.BaseDirectory, "tools.settings.json");

    public MainWindowViewModel()
    {
        ConnectCommand = new AsyncRelayCommand(ConnectAsync);
        _ = LoadSettingsAsync();
    }

    private void EnsureClient()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl)) throw new InvalidOperationException("BaseUrl 未配置");
        var p = new WebDavClientParams { BaseAddress = new Uri(BaseUrl!) };
        if (!string.IsNullOrEmpty(Username)) p.Credentials = new NetworkCredential(Username, Password ?? string.Empty);
        if (_client is IDisposable d) d.Dispose();
        _client = new WebDavClient(p);
    }

    private async Task ConnectAsync()
    {
        try
        {
            EnsureClient();
            await LoadTreeAsync();
            Status = "连接成功";
            await SaveSettingsAsync();
        }
        catch (Exception ex)
        {
            Status = $"连接异常: {ex.Message}";
        }
    }

    private async Task LoadTreeAsync()
    {
        Nodes.Clear();
        var res = await _client!.Propfind("");
        if (!res.IsSuccessful)
        {
            Status = $"列目录失败: {(int)res.StatusCode} {res.Description}";
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
                root = new DavNode { Name = r.DisplayName ?? r.Uri, Uri = r.Uri, IsCollection = true };
                break;
            }
        }
        if (root is null)
        {
            root = new DavNode { Name = BaseUrl, Uri = BaseUrl, IsCollection = true };
        }
        Nodes.Add(root);

        // Add children of root, skip self to avoid duplicates
        foreach (var r in res.Resources)
        {
            if (r.Uri is null) continue;
            if (baseNorm is not null && NormalizeUrl(r.Uri) == baseNorm) continue; // skip self
            var child = new DavNode { Name = r.DisplayName ?? r.Uri, Uri = r.Uri, IsCollection = r.IsCollection };
            root.Children.Add(child);
            if (r.IsCollection)
            {
                await LoadChildrenAsync(child, r.Uri, depth: 1);
            }
        }
    }

    private async Task LoadChildrenAsync(DavNode parent, string uri, int depth)
    {
        if (depth >= MaxDepth) return; // 控制层级，避免过深
        var res = await _client!.Propfind(uri);
        if (!res.IsSuccessful) return;
        foreach (var r in res.Resources)
        {
            if (r.Uri == uri) continue; // 跳过 self
            var child = new DavNode { Name = r.DisplayName ?? r.Uri, Uri = r.Uri, IsCollection = r.IsCollection };
            parent.Children.Add(child);
            if (r.IsCollection)
                await LoadChildrenAsync(child, r.Uri!, depth + 1);
        }
    }

    public async Task UploadFilesAsync(IReadOnlyList<string> paths, string? destFolderUri = null)
    {
        try
        {
            EnsureClient();
            string? rel = null;
            if (!string.IsNullOrEmpty(destFolderUri))
            {
                rel = ToRelativePath(destFolderUri!);
            }
            foreach (var path in paths)
            {
                var name = Path.GetFileName(path);
                await using var fs = File.OpenRead(path);
                var target = BuildRemotePath(rel, name);
                var result = await _client!.PutFile(target, fs);
                if (!result.IsSuccessful)
                {
                    Status = $"上传失败: {target} {(int)result.StatusCode} {result.Description}";
                    return;
                }
            }
            Status = $"上传完成，共 {paths.Count} 个文件";
        }
        catch (Exception ex)
        {
            Status = $"上传异常: {ex.Message}";
        }
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
            }
        }
        catch { }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var s = new ToolsSettings { BaseUrl = BaseUrl, Username = Username, Password = Password };
            await using var fs = File.Create(SettingsPath);
            await JsonSerializer.SerializeAsync(fs, s, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        }
        catch { }
    }
}
