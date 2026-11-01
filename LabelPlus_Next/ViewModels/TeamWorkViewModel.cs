using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Models;
using LabelPlus_Next.Serialization;
using LabelPlus_Next.Services;
using LabelPlus_Next.Services.Api;
using NLog;
using Notification = Ursa.Controls.Notification;
using WindowNotificationManager = Ursa.Controls.WindowNotificationManager;

namespace LabelPlus_Next.ViewModels;

public partial class TeamWorkViewModel : ObservableObject
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    [ObservableProperty] private string? status;
    [ObservableProperty] private string? selectedProject;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private double progress;
    [ObservableProperty] private string? progressText;
    public WindowNotificationManager? NotificationManager { get; set; }

    public ObservableCollection<string> Suggestions { get; } = new();
    public ObservableCollection<EpisodeItem> Episodes { get; } = new();

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand OpenSettingsCommand { get; }
    public event EventHandler? OpenSettingsRequested;
    public IAsyncRelayCommand<EpisodeItem> StartTranslateCommand { get; }
    public IAsyncRelayCommand<EpisodeItem> StartProofCommand { get; }
    public IAsyncRelayCommand<EpisodeItem> StartTypesetCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand RetryCommand { get; }
    // 保留：查看文件命令（不再在列中使用）
    public IAsyncRelayCommand<EpisodeItem> ShowFilesCommand { get; }
    // 新增：下载文件命令（作用于文件子项）
    public IAsyncRelayCommand<NodeItem> DownloadFileCommand { get; } // 新增

    private CancellationTokenSource? _cts;
    private Func<Task>? _lastOp;
    private string? _lastNotifyKey;
    private DateTime _lastNotifyAt;

    public TeamWorkViewModel() : this(true) { }

    public TeamWorkViewModel(bool autoRefresh)
    {
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        StartTranslateCommand = new AsyncRelayCommand<EpisodeItem>(StartTranslateAsync);
        StartProofCommand = new AsyncRelayCommand<EpisodeItem>(StartProofAsync);
        StartTypesetCommand = new AsyncRelayCommand<EpisodeItem>(StartTypesetAsync);
        OpenSettingsCommand = new AsyncRelayCommand(() => { OpenSettingsRequested?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; });
        CancelCommand = new RelayCommand(() => _cts?.Cancel());
        RetryCommand = new RelayCommand(async () => { var op = _lastOp; if (op is not null) await op(); });
        ShowFilesCommand = new AsyncRelayCommand<EpisodeItem>(ShowFilesAsync);
        DownloadFileCommand = new AsyncRelayCommand<NodeItem>(DownloadFileAsync); // 新增
        if (autoRefresh) _ = RefreshAsync();
    }

    private readonly Dictionary<string, string> _projectMap = new(StringComparer.Ordinal);

    private async Task<UploadSettings?> LoadUploadSettingsAsync()
    {
        try
        {
            var s = await UploadSettingsStore.LoadAsync();
            // Force baseUrl to default as requested (username/password from settings)
            if (s != null) s.BaseUrl = "https://alist1.seastarss.cn";
            return s;
        }
        catch (Exception ex) { Logger.Error(ex, "Load upload settings failed."); return null; }
    }

    private async Task<(string baseUrl, string token)?> LoginAsync()
    {
        var us = await LoadUploadSettingsAsync();
        if (us is null || string.IsNullOrWhiteSpace(us.BaseUrl))
        {
            Status = UiText.Tr("未配置服务器地址");
            return null;
        }
        var baseUrl = us.BaseUrl!.TrimEnd('/');
        var auth = new AuthApi(baseUrl);
        var login = await auth.LoginAsync(us.Username ?? string.Empty, us.Password ?? string.Empty);
        if (login.Code != 200 || string.IsNullOrWhiteSpace(login.Data?.Token))
        {
            Status = UiText.Tr("登录失败: {0} {1}", login.Code, login.Message);
            return null;
        }
        return (baseUrl, login.Data!.Token!);
    }

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _lastOp = () => RefreshAsync();
        try
        {
            Logger.Info("TeamWork Refresh: start loading aggregate.");
            Status = UiText.Tr("正在加载项目...");
            Progress = 10; ProgressText = UiText.Tr("登录中");
            var login = await LoginAsync();
            if (login is null) return;
            var (baseUrl, token) = login.Value;
            var fs = new FileSystemApi(baseUrl);
            Progress = 30; ProgressText = UiText.Tr("下载聚合清单");
            var agg = await fs.DownloadAsync(token, "/project.json", ct);
            if (agg.Code != 200 || agg.Content is null)
            {
                Status = UiText.Tr("下载失败: {0} {1}", agg.Code, agg.Message);
                Logger.Warn("TeamWork Refresh: download project.json failed {code} {msg}", agg.Code, agg.Message);
                ShowNotify(UiText.Tr("加载失败"), Status, NotificationType.Warning);
                return;
            }
            var txt = Encoding.UTF8.GetString(TrimBom(agg.Content)).Trim();
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var doc = JsonDocument.Parse(txt))
            {
                if (doc.RootElement.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in projects.EnumerateObject())
                    {
                        map[p.Name] = p.Value.GetString() ?? string.Empty;
                    }
                }
            }
            _projectMap.Clear();
            Suggestions.Clear();
            foreach (var kv in map)
            {
                _projectMap[kv.Key] = kv.Value;
                Suggestions.Add(kv.Key);
            }
            Status = UiText.Tr("已加载 {0} 个项目", Suggestions.Count);
            Progress = 100; ProgressText = UiText.Tr("完成");
            Logger.Info("TeamWork Refresh: loaded {count} projects", Suggestions.Count);
            ShowNotify(UiText.Tr("加载完成"), Status, NotificationType.Success);
        }
        catch (Exception ex)
        {
            Status = UiText.Tr("加载失败: {0}", ex.Message);
            Logger.Error(ex, "Refresh TeamWork failed.");
            ShowNotify(UiText.Tr("加载失败"), ex.Message, NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedProjectChanged(string? value)
    {
        // When value is empty, we still want dropdown to show all suggestions (MinimumPrefixLength=0 handles UI side).
        // Trigger project load only when a concrete name is set.
        if (!string.IsNullOrWhiteSpace(value))
            _ = LoadProjectAsync(value);
    }

    private async Task LoadProjectAsync(string? name)
    {
        Logger.Info("TeamWork LoadProject: name={name}", name);
        Episodes.Clear();
        TreeRows.Clear();
        _treeRoots.Clear();
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            _lastOp = () => LoadProjectAsync(name);
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var login = await LoginAsync();
            if (login is null) return;
            var (baseUrl, token) = login.Value;
            if (!_projectMap.TryGetValue(name, out var projectJson) || string.IsNullOrWhiteSpace(projectJson))
            {
                Status = UiText.Tr("未在聚合清单中找到项目路径");
                Logger.Warn("TeamWork: project path not found for {name}", name);
                ShowNotify(UiText.Tr("错误"), Status, NotificationType.Warning);
                return;
            }
            var fs = new FileSystemApi(baseUrl);
            var dl = await fs.DownloadAsync(token, projectJson, ct);
            if (dl.Code != 200 || dl.Content is null)
            {
                Status = UiText.Tr("项目JSON下载失败: {0} {1}", dl.Code, dl.Message);
                Logger.Warn("TeamWork LoadProject: download failed {code} {msg}", dl.Code, dl.Message);
                ShowNotify(UiText.Tr("下载失败"), Status, NotificationType.Warning);
                return;
            }
            var txt = Encoding.UTF8.GetString(TrimBom(dl.Content)).TrimStart('\uFEFF');
            ProjectCn cn;
            try { cn = JsonSerializer.Deserialize(txt, AppJsonContext.Default.ProjectCn) ?? new ProjectCn(); }
            catch (JsonException ex)
            {
                Logger.Warn(ex, "TeamWork: project metadata JSON invalid at {path}", projectJson);
                cn = new ProjectCn();
            }
            catch (DecoderFallbackException ex)
            {
                Logger.Warn(ex, "TeamWork: failed to decode project metadata at {path}", projectJson);
                cn = new ProjectCn();
            }
            foreach (var kv in cn.Items)
            {
                var key = kv.Key;
                var val = kv.Value ?? new EpisodeCn();
                // 计算显示名：优先 Display；其次根据 Kind/Number；Kind=杂项 时显示“杂项”；否则按键名或数字兜底
                string disp;
                if (!string.IsNullOrWhiteSpace(val.Display)) disp = val.Display!;
                else if (!string.IsNullOrWhiteSpace(val.Kind) && val.Kind!.StartsWith("杂项")) disp = "杂项";
                else if (!string.IsNullOrWhiteSpace(val.Kind) && val.Number.HasValue && val.Number > 0)
                {
                    disp = val.Kind!.StartsWith("番外") ? $"番外{val.Number:00}" : (val.Kind!.StartsWith("卷") ? $"卷{val.Number:00}" : val.Number!.Value.ToString("00"));
                }
                else
                {
                    disp = key.StartsWith("番外", StringComparison.OrdinalIgnoreCase) ? key : (key.StartsWith("卷", StringComparison.OrdinalIgnoreCase) ? key : ToInt(key).ToString("00"));
                }

                var ep = new EpisodeItem
                {
                    Number = ToInt(key),
                    Key = key,
                    DisplayNumber = disp,
                    Status = val.Status ?? string.Empty,
                    SourcePath = val.SourcePath,
                    TranslatePath = val.TranslatePath,
                    ProofPath = val.ProofPath,
                    TypesetPath = val.TypesetPath,
                    ProjectJsonPath = projectJson,
                    FilePaths = val.FilePaths,

                    SourceOwner = val.SourceOwner,
                    SourceCreatedAt = val.SourceCreatedAt,
                    SourceUpdatedAt = val.SourceUpdatedAt,

                    TranslateOwner = val.TranslateOwner,
                    TranslateCreatedAt = val.TranslateCreatedAt,
                    TranslateUpdatedAt = val.TranslateUpdatedAt,

                    ProofOwner = val.ProofOwner,
                    ProofCreatedAt = val.ProofCreatedAt,
                    ProofUpdatedAt = val.ProofUpdatedAt,

                    PublishOwner = val.PublishOwner,
                    PublishCreatedAt = val.PublishCreatedAt,
                    PublishUpdatedAt = val.PublishUpdatedAt
                };
                Episodes.Add(ep);
            }
            // Order by number desc
            var ordered = Episodes.OrderByDescending(e => e.Number).ToList();
            Episodes.Clear();
            foreach (var e in ordered) Episodes.Add(e);

            BuildTreeSource(ordered);

            Status = UiText.Tr("已加载 {0} 话", Episodes.Count);
            Logger.Info("TeamWork LoadProject: loaded {count} episodes", Episodes.Count);
            ShowNotify(UiText.Tr("项目已载入"), Status, NotificationType.Success);
        }
        catch (Exception ex)
        {
            Status = UiText.Tr("加载项目失败: {0}", ex.Message);
            Logger.Error(ex, "LoadProjectAsync failed.");
            ShowNotify(UiText.Tr("加载项目失败"), ex.Message, NotificationType.Error);
        }
    }

    private readonly List<NodeItem> _treeRoots = new();
    public ObservableCollection<NodeItem> TreeRows { get; } = new();
    public bool IsTreeEmpty => TreeRows.Count == 0;

    private void BuildTreeSource(List<EpisodeItem> episodes)
    {
        _treeRoots.Clear();
        foreach (var episode in episodes)
        {
            var node = CreateNode(episode);
            node.IsExpanded = true;
            WireTreeCommands(node);
            _treeRoots.Add(node);
        }
        RebuildTreeRows();
    }

    private void WireTreeCommands(NodeItem node)
    {
        node.ToggleCommand = ToggleTreeNodeCommand;
        node.TranslateCommand = StartTranslateCommand;
        node.ProofCommand = StartProofCommand;
        node.TypesetCommand = StartTypesetCommand;
        node.DownloadCommand = DownloadFileCommand;
        foreach (var child in node.Children) WireTreeCommands(child);
    }

    private void RebuildTreeRows()
    {
        TreeRows.Clear();
        foreach (var root in _treeRoots) AddTreeRow(root, 0);
        OnPropertyChanged(nameof(IsTreeEmpty));
    }

    private void AddTreeRow(NodeItem node, int depth)
    {
        node.Depth = depth;
        TreeRows.Add(node);
        if (!node.IsExpanded) return;
        foreach (var child in node.Children) AddTreeRow(child, depth + 1);
    }

    [RelayCommand]
    private void ToggleTreeNode(NodeItem? node)
    {
        if (node is null || node.Children.Count == 0) return;
        node.IsExpanded = !node.IsExpanded;
        RebuildTreeRows();
    }

    private static string Fmt(DateTimeOffset? t) => t.HasValue ? t.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "-";

    private static NodeItem CreateNode(EpisodeItem e)
    {
        var node = new NodeItem
        {
            Episode = e,
            Title = e.DisplayNumber,
            Status = e.Status,
            Path = null,
            Owner = null,
            CreatedAt = null,
            UpdatedAt = null,
            IsEpisodeRoot = true
        };
        var stages = new List<NodeItem>
        {
            new() { Title = UiText.Tr("图源"), Episode = e, Status = string.IsNullOrWhiteSpace(e.SourcePath) ? "-" : UiText.Tr("已上传"), Path = e.SourcePath, Owner = e.SourceOwner, CreatedAt = Fmt(e.SourceCreatedAt), UpdatedAt = Fmt(e.SourceUpdatedAt) },
            new() { Title = UiText.Tr("翻译"), Episode = e, Status = string.IsNullOrWhiteSpace(e.TranslatePath) ? "-" : UiText.Tr("已上传"), Path = e.TranslatePath, Owner = e.TranslateOwner, CreatedAt = Fmt(e.TranslateCreatedAt), UpdatedAt = Fmt(e.TranslateUpdatedAt) },
            new() { Title = UiText.Tr("校对"), Episode = e, Status = string.IsNullOrWhiteSpace(e.ProofPath) ? "-" : UiText.Tr("已上传"), Path = e.ProofPath, Owner = e.ProofOwner, CreatedAt = Fmt(e.ProofCreatedAt), UpdatedAt = Fmt(e.ProofUpdatedAt) },
            new() { Title = UiText.Tr("嵌字"), Episode = e, Status = string.IsNullOrWhiteSpace(e.TypesetPath) ? "-" : UiText.Tr("已上传"), Path = e.TypesetPath, Owner = e.PublishOwner, CreatedAt = Fmt(e.PublishCreatedAt), UpdatedAt = Fmt(e.PublishUpdatedAt) }
        };
        node.Children.AddRange(stages);

        // 新增：文件容器 + 文件子项
        if (e.FilePaths is { Count: > 0 })
        {
            var container = new NodeItem { Title = UiText.Tr("文件"), Episode = e, IsGroupContainer = true };
            foreach (var rp in e.FilePaths)
            {
                if (string.IsNullOrWhiteSpace(rp)) continue;
                var fileName = GetFileNameFromRemote(rp);
                container.Children.Add(new NodeItem
                {
                    Episode = e,
                    Title = string.IsNullOrWhiteSpace(fileName) ? rp : fileName,
                    Status = "-",
                    Path = rp,
                    IsFile = true
                });
            }
            node.Children.Add(container);
        }

        return node;
    }

    private static string GetFileNameFromRemote(string remotePath)
    {
        var p = (remotePath ?? string.Empty).Replace('\\', '/');
        if (string.IsNullOrEmpty(p)) return string.Empty;
        if (p.EndsWith('/')) p = p.TrimEnd('/');
        var idx = p.LastIndexOf('/');
        return idx >= 0 ? p[(idx + 1)..] : p;
    }

    // 新增：显示文件列表（用于兼容旧的命令绑定，当前 UI 未使用）
    private async Task ShowFilesAsync(EpisodeItem? ep)
    {
        if (ep is null)
        {
            ShowNotify(UiText.Tr("文件列表"), UiText.Tr("无记录"), NotificationType.Information);
            return;
        }
        var list = ep.FilePaths;
        if (list is null || list.Count == 0)
        {
            ShowNotify(UiText.Tr("文件列表"), UiText.Tr("无记录"), NotificationType.Information);
            return;
        }
        var take = Math.Min(50, list.Count);
        var sb = new StringBuilder();
        for (int i = 0; i < take; i++) sb.AppendLine(list[i]);
        if (list.Count > take) sb.AppendLine(UiText.Tr("... 其余 {0} 条省略", list.Count - take));
        ShowNotify(UiText.Tr("文件列表"), sb.ToString(), NotificationType.Information);
        await Task.CompletedTask;
    }

    // 新增：下载文件实现
    private async Task DownloadFileAsync(NodeItem? node)
    {
        try
        {
            if (node is null)
            {
                ShowNotify(UiText.Tr("下载"), UiText.Tr("无效的项目"), NotificationType.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(node.Path))
            {
                ShowNotify(UiText.Tr("下载"), UiText.Tr("该项没有可下载的路径"), NotificationType.Information);
                return;
            }

            var login = await LoginAsync();
            if (login is null)
            {
                ShowNotify(UiText.Tr("下载失败"), UiText.Tr("未能登录服务器"), NotificationType.Error);
                return;
            }
            var (baseUrl, token) = login.Value;
            var fs = new FileSystemApi(baseUrl);

            // 若为目录则直接提示暂不支持打包下载
            var meta = await fs.GetAsync(token, node.Path);
            if (meta.Code != 200 || meta.Data is null)
            {
                ShowNotify(UiText.Tr("下载失败"), meta.Message ?? UiText.Tr("未知错误"), NotificationType.Error);
                return;
            }
            if (meta.Data.IsDir)
            {
                ShowNotify(UiText.Tr("下载"), UiText.Tr("该路径是文件夹，暂不支持打包下载"), NotificationType.Information);
                return;
            }

            // 目标路径：桌面/LabelPlus_Downloads/<项目名>/<话数显示名>/<文件名>
            var fileName = GetFileNameFromRemote(node.Path);
            var projectName = string.IsNullOrWhiteSpace(SelectedProject) ? "Project" : SelectedProject!;
            var episodeName = string.IsNullOrWhiteSpace(node.Episode?.DisplayNumber) ? (node.Episode?.Key ?? "Episode") : node.Episode!.DisplayNumber!;
            var destDir = BuildDownloadDirectory(projectName, episodeName);
            Directory.CreateDirectory(destDir);
            var localPath = Path.Combine(destDir, SanitizeFileName(fileName));

            var res = await fs.DownloadToFileAsync(token, node.Path, localPath);
            if (res.Code == 200)
            {
                ShowNotify(UiText.Tr("下载完成"), localPath, NotificationType.Success);
            }
            else
            {
                var msg = string.IsNullOrWhiteSpace(res.Message) ? UiText.Tr("错误码 {0}", res.Code) : res.Message!;
                ShowNotify(UiText.Tr("下载失败"), msg, NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "DownloadFile failed");
            ShowNotify(UiText.Tr("下载异常"), ex.Message, NotificationType.Error);
        }
    }

    [ExcludeFromCodeCoverage]
    private static string BuildDownloadDirectory(string project, string episode)
    {
        string baseDir;
        try { baseDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
        catch (PlatformNotSupportedException)
        {
            baseDir = Path.GetTempPath();
        }
        catch (SecurityException)
        {
            baseDir = Path.GetTempPath();
        }
        catch (UnauthorizedAccessException)
        {
            baseDir = Path.GetTempPath();
        }
        return Path.Combine(baseDir, "LabelPlus_Downloads", SanitizeFileName(project), SanitizeFileName(episode));
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "file";
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    // 还原缺失的帮助方法
    private static byte[] TrimBom(byte[] bytes) => (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ? bytes[3..] : bytes;

    [ExcludeFromCodeCoverage]
    private void ShowNotify(string title, string message, NotificationType type)
    {
        // Suppress an identical toast shown twice by overlapping refresh calls.
        var key = title + "\u0001" + message + "\u0001" + (int)type;
        var now = DateTime.UtcNow;
        if (string.Equals(key, _lastNotifyKey, StringComparison.Ordinal) && now - _lastNotifyAt < TimeSpan.FromSeconds(2))
            return;
        _lastNotifyKey = key;
        _lastNotifyAt = now;

        try { NotificationManager?.Show(new Notification(title, message), showIcon: true, showClose: true, type: type, classes: ["Light"]); }
        catch (InvalidOperationException ex)
        {
            Logger.Warn(ex, "Failed to show notification {title}", title);
        }
    }

    private static int ToInt(string key) => int.TryParse(new string((key ?? string.Empty).Where(char.IsDigit).ToArray()), out var n) ? n : 0;

    // 占位实现，后续可接入实际业务流程
    private async Task StartTranslateAsync(EpisodeItem? ep) { await Task.CompletedTask; }
    private async Task StartProofAsync(EpisodeItem? ep) { await Task.CompletedTask; }
    private async Task StartTypesetAsync(EpisodeItem? ep) { await Task.CompletedTask; }

    public sealed partial class EpisodeItem : ObservableObject
    {
        public int Number { get; set; }
        public string Key { get; set; } = "";
        public string DisplayNumber { get; set; } = "";
        [ObservableProperty] private string? status;
        public string? SourcePath { get; set; }
        [ObservableProperty] private string? translatePath;
        public string? ProofPath { get; set; }
        public string? TypesetPath { get; set; }
        public string ProjectJsonPath { get; set; } = "";
        // 新增：文件路径列表（解析支持）
        public List<string>? FilePaths { get; set; }

        public string? SourceOwner { get; set; }
        public DateTimeOffset? SourceCreatedAt { get; set; }
        public DateTimeOffset? SourceUpdatedAt { get; set; }
        public string? TranslateOwner { get; set; }
        public DateTimeOffset? TranslateCreatedAt { get; set; }
        public DateTimeOffset? TranslateUpdatedAt { get; set; }
        public string? ProofOwner { get; set; }
        public DateTimeOffset? ProofCreatedAt { get; set; }
        public DateTimeOffset? ProofUpdatedAt { get; set; }
        public string? PublishOwner { get; set; }
        public DateTimeOffset? PublishCreatedAt { get; set; }
        public DateTimeOffset? PublishUpdatedAt { get; set; }

        public string SourceInfo => UiText.Tr("负责人: {0}  创建: {1}  更新: {2}", SourceOwner ?? "-", FormatTime(SourceCreatedAt), FormatTime(SourceUpdatedAt));
        public string TranslateInfo => UiText.Tr("负责人: {0}  创建: {1}  更新: {2}", TranslateOwner ?? "-", FormatTime(TranslateCreatedAt), FormatTime(TranslateUpdatedAt));
        public string ProofInfo => UiText.Tr("负责人: {0}  创建: {1}  更新: {2}", ProofOwner ?? "-", FormatTime(ProofCreatedAt), FormatTime(ProofUpdatedAt));
        public string PublishInfo => UiText.Tr("负责人: {0}  创建: {1}  更新: {2}", PublishOwner ?? "-", FormatTime(PublishCreatedAt), FormatTime(PublishUpdatedAt));
        private static string FormatTime(DateTimeOffset? t) => t.HasValue ? t.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "-";
    }
}

public class NodeItem : ObservableObject
    {
        public TeamWorkViewModel.EpisodeItem Episode { get; set; } = null!;
        public string? Title { get; set; }
        public string? Status { get; set; }
        public string? Path { get; set; }
        public string? Owner { get; set; }
        public string? CreatedAt { get; set; }
        public string? UpdatedAt { get; set; }
        public List<NodeItem> Children { get; } = new();
        // 新增：标记是否为文件子项
        public bool IsFile { get; set; } // 新增
        // 新增：话数根节点标记（用于仅在根节点显示开始按钮）
        public bool IsEpisodeRoot { get; set; } // 新增
        // 新增：分组容器（例如“文件”）标记
        public bool IsGroupContainer { get; set; } // 新增
        // 新增：为表达式树提供安全访问的文件数文本
        public string FileCountText => (Episode?.FilePaths != null) ? Episode.FilePaths.Count.ToString() : "0";

        private int _depth;
        public int Depth
        {
            get => _depth;
            set
            {
                if (SetProperty(ref _depth, value)) OnPropertyChanged(nameof(IndentMargin));
            }
        }

        public Thickness IndentMargin => new(Depth * 18, 0, 0, 0);

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value)) OnPropertyChanged(nameof(ChevronGlyph));
            }
        }

        public bool HasChildren => Children.Count > 0;
        public string ChevronGlyph => IsExpanded ? "⌄" : "›";
        public System.Windows.Input.ICommand? ToggleCommand { get; set; }
        public System.Windows.Input.ICommand? TranslateCommand { get; set; }
        public System.Windows.Input.ICommand? ProofCommand { get; set; }
        public System.Windows.Input.ICommand? TypesetCommand { get; set; }
        public System.Windows.Input.ICommand? DownloadCommand { get; set; }
    }
