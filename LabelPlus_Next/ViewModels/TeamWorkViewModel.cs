using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Models;
using LabelPlus_Next.Serialization;
using LabelPlus_Next.Services.Api;
using NLog;
using System.Collections.ObjectModel;
using System.IO.Compression;
using SC = SharpCompress.Archives;
using SCRar = SharpCompress.Archives.Rar;
using SC7z = SharpCompress.Archives.SevenZip;
using SCZip = SharpCompress.Archives.Zip;
using SharpCompress.Common;
using System.Text;
using System.Text.Json;
using Avalonia.Controls.Notifications;
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

    private CancellationTokenSource? _cts;
    private Func<Task>? _lastOp;

    public TeamWorkViewModel()
    {
    RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    StartTranslateCommand = new AsyncRelayCommand<EpisodeItem>(StartTranslateAsync);
    StartProofCommand = new AsyncRelayCommand<EpisodeItem>(StartProofAsync);
    StartTypesetCommand = new AsyncRelayCommand<EpisodeItem>(StartTypesetAsync);
    OpenSettingsCommand = new AsyncRelayCommand(() => { OpenSettingsRequested?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; });
    CancelCommand = new RelayCommand(() => _cts?.Cancel());
    RetryCommand = new RelayCommand(async () => { var op = _lastOp; if (op is not null) await op(); });
        _ = RefreshAsync();
    }

    private readonly Dictionary<string, string> _projectMap = new(StringComparer.Ordinal);

    private static string UploadSettingsPath => Path.Combine(AppContext.BaseDirectory, "upload.json");

    private async Task<UploadSettings?> LoadUploadSettingsAsync()
    {
        try
        {
            if (!File.Exists(UploadSettingsPath)) return null;
            await using var fs = File.OpenRead(UploadSettingsPath);
            var s = await JsonSerializer.DeserializeAsync(fs, AppJsonContext.Default.UploadSettings);
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
            Status = "未配置服务器地址";
            return null;
        }
        var baseUrl = us.BaseUrl!.TrimEnd('/');
        var auth = new AuthApi(baseUrl);
        var login = await auth.LoginAsync(us.Username ?? string.Empty, us.Password ?? string.Empty);
        if (login.Code != 200 || string.IsNullOrWhiteSpace(login.Data?.Token))
        {
            Status = $"登录失败: {login.Code} {login.Message}";
            return null;
        }
        return (baseUrl, login.Data!.Token!);
    }

    public async Task RefreshAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _lastOp = () => RefreshAsync();
        try
        {
            Logger.Info("TeamWork Refresh: start loading aggregate.");
            Status = "正在加载项目...";
            IsBusy = true; Progress = 10; ProgressText = "登录中";
            var login = await LoginAsync();
            if (login is null) return;
            var (baseUrl, token) = login.Value;
            var fs = new FileSystemApi(baseUrl);
            Progress = 30; ProgressText = "下载聚合清单";
            var agg = await fs.DownloadAsync(token, "/project.json", ct);
            if (agg.Code != 200 || agg.Content is null)
            {
                Status = $"下载失败: {agg.Code} {agg.Message}";
                Logger.Warn("TeamWork Refresh: download project.json failed {code} {msg}", agg.Code, agg.Message);
                ShowNotify("加载失败", Status, NotificationType.Warning);
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
            Status = $"已加载 {Suggestions.Count} 个项目";
            Progress = 100; ProgressText = "完成";
            Status = $"已加载 {Suggestions.Count} 个项目";
            Logger.Info("TeamWork Refresh: loaded {count} projects", Suggestions.Count);
            ShowNotify("加载完成", Status, NotificationType.Success);
        }
        catch (Exception ex)
        {
            Status = $"加载失败: {ex.Message}";
            Logger.Error(ex, "Refresh TeamWork failed.");
            ShowNotify("加载失败", ex.Message, NotificationType.Error);
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
                Status = "未在聚合清单中找到项目路径";
                Logger.Warn("TeamWork: project path not found for {name}", name);
                ShowNotify("错误", Status, NotificationType.Warning);
                return;
            }
            var fs = new FileSystemApi(baseUrl);
            var dl = await fs.DownloadAsync(token, projectJson, ct);
            if (dl.Code != 200 || dl.Content is null)
            {
                Status = $"项目JSON下载失败: {dl.Code} {dl.Message}";
                Logger.Warn("TeamWork LoadProject: download failed {code} {msg}", dl.Code, dl.Message);
                ShowNotify("下载失败", Status, NotificationType.Warning);
                return;
            }
            var txt = Encoding.UTF8.GetString(TrimBom(dl.Content)).TrimStart('\uFEFF');
            ProjectCn cn;
            try { cn = JsonSerializer.Deserialize(txt, AppJsonContext.Default.ProjectCn) ?? new ProjectCn(); }
            catch { cn = new ProjectCn(); }
            foreach (var kv in cn.Items)
            {
                var isSpecial = kv.Key.StartsWith("番外", StringComparison.OrdinalIgnoreCase);
                var isVolume = kv.Key.StartsWith("卷", StringComparison.OrdinalIgnoreCase) || kv.Key.StartsWith("V", StringComparison.OrdinalIgnoreCase) || kv.Key.StartsWith("Vol", StringComparison.OrdinalIgnoreCase);
                var ep = new EpisodeItem
                {
                    Number = ToInt(kv.Key),
                    Key = kv.Key,
                    DisplayNumber = isSpecial ? kv.Key : (isVolume ? kv.Key : ToInt(kv.Key).ToString("00")),
                    Status = kv.Value.Status ?? string.Empty,
                    SourcePath = kv.Value.SourcePath,
                    TranslatePath = kv.Value.TranslatePath,
                    TypesetPath = kv.Value.TypesetPath,
                    ProjectJsonPath = projectJson
                };
                Episodes.Add(ep);
            }
            // Order by number desc
            var ordered = Episodes.OrderByDescending(e => e.Number).ToList();
            Episodes.Clear();
            foreach (var e in ordered) Episodes.Add(e);
            Status = $"已加载 {Episodes.Count} 话";
            Logger.Info("TeamWork LoadProject: loaded {count} episodes", Episodes.Count);
            ShowNotify("项目已载入", Status, NotificationType.Success);
        }
        catch (Exception ex)
        {
            Status = $"加载项目失败: {ex.Message}";
            Logger.Error(ex, "LoadProjectAsync failed.");
            ShowNotify("加载项目失败", ex.Message, NotificationType.Error);
        }
    }

    private static int ToInt(string key) => int.TryParse(key, out var n) ? n : 0;
    private static byte[] TrimBom(byte[] bytes) => (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ? bytes[3..] : bytes;

    private static string WorkRoot => Path.Combine(AppContext.BaseDirectory, "work");

    private async Task StartTranslateAsync(EpisodeItem? ep)
    {
        if (ep is null) return;
        try
        {
            _lastOp = () => StartTranslateAsync(ep);
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            Logger.Info("StartTranslate: ep={ep}", ep.Number);
            var login = await LoginAsync();
            if (login is null) return;
            var (baseUrl, token) = login.Value;
            var fs = new FileSystemApi(baseUrl);
            IsBusy = true; Progress = 0; ProgressText = "准备本地工作区";

            // Local workspace
            var projectName = SelectedProject ?? "project";
            var localDir = Path.Combine(WorkRoot, Sanitize(projectName), KeyToSubDir(ep.Key, ep.Number));
            Directory.CreateDirectory(localDir);

            // Download source (dir or archive) — 若本地已有图片则跳过下载
            if (!string.IsNullOrWhiteSpace(ep.SourcePath))
            {
                Progress = 10; ProgressText = "检查图源";
                // 若目录下已有任意图像文件，跳过远端下载
                var hasImages = Directory.EnumerateFiles(localDir, "*", SearchOption.TopDirectoryOnly)
                    .Any(f => IsImageExt(Path.GetExtension(f)));
                if (!hasImages)
                {
                    var meta = await fs.GetAsync(token, ep.SourcePath!, cancellationToken: ct);
                if (meta.Code == 200 && meta.Data is not null)
                {
                    if (meta.Data.IsDir)
                    {
                            var list = await fs.ListAsync(token, ep.SourcePath!, cancellationToken: ct);
                        var items = new List<(string remotePath, string localPath)>();
                        foreach (var it in list.Data?.Content ?? Array.Empty<FsItem>())
                        {
                            if (it.IsDir) continue;
                            var ext = Path.GetExtension(it.Name ?? string.Empty);
                            if (!IsImageExt(ext)) continue;
                            var r = ep.SourcePath!.TrimEnd('/') + "/" + it.Name;
                            var l = Path.Combine(localDir, it.Name!);
                            items.Add((r, l));
                        }
                        ProgressText = $"下载图源（{items.Count}）";
                        await fs.DownloadManyToFilesAsync(token, items.Select(p => (p.remotePath, p.localPath)), maxConcurrency: 6, cancellationToken: ct);
                        Progress = 40;
                    }
                    else
                    {
                        var name = Path.GetFileName(ep.SourcePath!);
                        var outPath = Path.Combine(localDir, name);
                            // 若压缩包文件已存在且目录已有图片，也跳过
                            var hasArchive = File.Exists(outPath);
                            if (!hasArchive || !hasImages)
                            {
                                ProgressText = "下载图源压缩包";
                                await fs.DownloadToFileAsync(token, ep.SourcePath!, outPath, cancellationToken: ct);
                                ProgressText = "解压图源"; Progress = 55;
                                var bytes = await File.ReadAllBytesAsync(outPath);
                                await TryExtractImagesAsync(bytes, name, localDir);
                            }
                    }
                }
                }
            }

            // Create translate file if absent
            var translateFileName = IsSpecialKey(ep.Key)
                ? $"{Sanitize(projectName)}_{KeyToLabel(ep.Key, ep.Number)}_translate.txt"
                : $"{Sanitize(projectName)}_{ep.Number:00}_translate.txt";
            var localTxt = Path.Combine(localDir, translateFileName);
            if (!File.Exists(localTxt))
            {
                var images = Directory.EnumerateFiles(localDir, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => IsImageExt(Path.GetExtension(f)))
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Cast<string>()
                    .ToList();
                var content = Services.TranslationFileUtils.BuildInitialContent(images);
                await File.WriteAllTextAsync(localTxt, content, Encoding.UTF8);
                Logger.Info("StartTranslate: created local translate file {file}", localTxt);
            }

            // Upload if not on remote
            string remoteTxt;
            if (string.IsNullOrWhiteSpace(ep.TranslatePath))
            {
                var remoteDir = RemoteDirFromProjectJson(ep.ProjectJsonPath);
                remoteTxt = CombineRemote(remoteDir, KeyToSubDir(ep.Key, ep.Number), translateFileName);
                var bytes = await File.ReadAllBytesAsync(localTxt);
                var put = await fs.SafePutAsync(token, remoteTxt, bytes, cancellationToken: ct);
                if (put.Code == 200)
                {
                    await UpdateEpisodeAsync(fs, token, ep, update => update.TranslatePath = remoteTxt, newStatus: "翻译");
                    ep.TranslatePath = remoteTxt; ep.Status = "翻译";
                    Logger.Info("StartTranslate: uploaded translate and updated JSON: {remote}", remoteTxt);
                    ShowNotify("已开始翻译", $"话数 {EpisodeLabel(ep)} 已初始化翻译文件", NotificationType.Success);
                }
            }
            else remoteTxt = ep.TranslatePath!;

            // Open in Translate view
            var session = new Services.CollaborationSession { BaseUrl = baseUrl, Token = token, RemoteTranslatePath = remoteTxt, Username = null };
            Views.MainWindow.Instance?.OpenTranslateWithFile(localTxt, session);
            Progress = 100; ProgressText = "完成";
            Logger.Info("StartTranslate: opened translate view for {file}", localTxt);
        }
        catch (Exception ex)
        {
            Status = $"开始翻译失败: {ex.Message}";
            Logger.Error(ex, "StartTranslate failed.");
            ShowNotify("开始翻译失败", ex.Message, NotificationType.Error);
        }
        finally { IsBusy = false; }
    }

    private async Task StartProofAsync(EpisodeItem? ep)
    {
        if (ep is null) return;
        try
        {
            _lastOp = () => StartProofAsync(ep);
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            Logger.Info("StartProof: ep={ep}", ep.Number);
            var login = await LoginAsync();
            if (login is null) return;
            var (baseUrl, token) = login.Value;
            var fs = new FileSystemApi(baseUrl);
            IsBusy = true; Progress = 0; ProgressText = "准备校对";
            if (string.IsNullOrWhiteSpace(ep.TranslatePath)) { Status = "无翻译文件"; return; }
            var projectName = SelectedProject ?? "project";
            var localDir = Path.Combine(WorkRoot, Sanitize(projectName), KeyToSubDir(ep.Key, ep.Number));
            Directory.CreateDirectory(localDir);

            // 若本地缺少图源，则按 SourcePath 下载一次
            await EnsureLocalSourcesAsync(fs, token, ep.SourcePath, localDir, ct);
            var fileName = Path.GetFileName(ep.TranslatePath!);
            // 如果远端已是 checked 文件，直接视为最终版
            var remoteIsChecked = IsCheckedFileName(fileName);
            var checkedName = remoteIsChecked ? fileName : MakeCheckedFileName(fileName);
            var localTxt = Path.Combine(localDir, fileName);
            var checkedLocal = Path.Combine(localDir, checkedName);

            // 计算远端 checked 路径
            var remoteDir = Path.GetDirectoryName(ep.TranslatePath!)?.Replace('\\', '/');
            var remoteChecked = string.IsNullOrEmpty(remoteDir) ? checkedName : $"{remoteDir}/{checkedName}";

            // 1) 如果远端已是 checked：优先使用远端checked；若本地已存在相同文件，直接打开；否则下载远端checked到本地
            if (remoteIsChecked)
            {
                if (File.Exists(checkedLocal))
                {
                    Logger.Info("StartProof: remote already checked, open local checked {file}", checkedLocal);
                }
                else
                {
                    ProgressText = "下载校对";
                    await fs.DownloadToFileAsync(token, ep.TranslatePath!, checkedLocal, cancellationToken: ct);
                }
                var sessionChecked = new Services.CollaborationSession { BaseUrl = baseUrl, Token = token, RemoteTranslatePath = ep.TranslatePath!, Username = null };
                Views.MainWindow.Instance?.OpenTranslateWithFile(checkedLocal, sessionChecked);
                Progress = 100; ProgressText = "完成";
                return;
            }

            // 2) 如果本地已有 checked 文件，直接打开本地，跳过下载与上传
            if (File.Exists(checkedLocal))
            {
                Logger.Info("StartProof: local checked exists, skip download & upload. {file}", checkedLocal);
                var session1 = new Services.CollaborationSession { BaseUrl = baseUrl, Token = token, RemoteTranslatePath = remoteChecked, Username = null };
                Views.MainWindow.Instance?.OpenTranslateWithFile(checkedLocal, session1);
                Progress = 100; ProgressText = "完成";
                return;
            }

            // 3) 若本地已有原翻译，则复制为 checked；否则从远端下载原翻译，再生成 checked
            if (File.Exists(localTxt))
            {
                Logger.Info("StartProof: local translate exists, make checked copy. {file}", localTxt);
                File.Copy(localTxt, checkedLocal, overwrite: true);
            }
            else
            {
                ProgressText = "下载翻译";
                await fs.DownloadToFileAsync(token, ep.TranslatePath!, localTxt, cancellationToken: ct);
                File.Copy(localTxt, checkedLocal, overwrite: true);
            }

            // 4) 上传 checked 并更新 JSON
            var put = await fs.SafePutAsync(token, remoteChecked, await File.ReadAllBytesAsync(checkedLocal), cancellationToken: ct);
            if (put.Code == 200)
            {
                await UpdateEpisodeAsync(fs, token, ep, update => update.TranslatePath = remoteChecked, newStatus: "校对");
                ep.TranslatePath = remoteChecked; ep.Status = "校对";
                Logger.Info("StartProof: uploaded checked file and updated JSON {remote}", remoteChecked);
                ShowNotify("校对完成", $"话数 {EpisodeLabel(ep)} 已上传校对文件", NotificationType.Success);
            }
            var session = new Services.CollaborationSession { BaseUrl = baseUrl, Token = token, RemoteTranslatePath = remoteChecked, Username = null };
            Views.MainWindow.Instance?.OpenTranslateWithFile(checkedLocal, session);
            Progress = 100; ProgressText = "完成";
        }
        catch (Exception ex)
        {
            Status = $"开始校对失败: {ex.Message}";
            Logger.Error(ex, "StartProof failed.");
            ShowNotify("开始校对失败", ex.Message, NotificationType.Error);
        }
        finally { IsBusy = false; }
    }

    private async Task EnsureLocalSourcesAsync(FileSystemApi fs, string token, string? sourcePath, string localDir, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return;
            var hasImages = Directory.EnumerateFiles(localDir, "*", SearchOption.TopDirectoryOnly)
                .Any(f => IsImageExt(Path.GetExtension(f)));
            if (hasImages) return;
            ProgressText = "下载图源";
            var meta = await fs.GetAsync(token, sourcePath!, cancellationToken: ct);
            if (meta.Code == 200 && meta.Data is not null)
            {
                if (meta.Data.IsDir)
                {
                    var list = await fs.ListAsync(token, sourcePath!, cancellationToken: ct);
                    var items = new List<(string remotePath, string localPath)>();
                    foreach (var it in list.Data?.Content ?? Array.Empty<FsItem>())
                    {
                        if (it.IsDir) continue;
                        var ext = Path.GetExtension(it.Name ?? string.Empty);
                        if (!IsImageExt(ext)) continue;
                        var r = sourcePath!.TrimEnd('/') + "/" + it.Name;
                        var l = Path.Combine(localDir, it.Name!);
                        items.Add((r, l));
                    }
                    await fs.DownloadManyToFilesAsync(token, items.Select(p => (p.remotePath, p.localPath)), maxConcurrency: 6, cancellationToken: ct);
                }
                else
                {
                    var name = Path.GetFileName(sourcePath!);
                    var outPath = Path.Combine(localDir, name);
                    await fs.DownloadToFileAsync(token, sourcePath!, outPath, cancellationToken: ct);
                    var bytes = await File.ReadAllBytesAsync(outPath, ct);
                    await TryExtractImagesAsync(bytes, name, localDir);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "EnsureLocalSources failed");
        }
    }

    private async Task StartTypesetAsync(EpisodeItem? ep)
    {
        if (ep is null) return;
        try
        {
            _lastOp = () => StartTypesetAsync(ep);
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            Logger.Info("StartTypeset: ep={ep}", ep.Number);
            var login = await LoginAsync();
            if (login is null) return;
            var (baseUrl, token) = login.Value;
            var fs = new FileSystemApi(baseUrl);
            IsBusy = true; Progress = 0; ProgressText = "准备本地目录";
            var projectName = SelectedProject ?? "project";
            var localDir = Path.Combine(WorkRoot, Sanitize(projectName), KeyToSubDir(ep.Key, ep.Number));
            Directory.CreateDirectory(localDir);
            // Download source
            if (!string.IsNullOrWhiteSpace(ep.SourcePath))
            {
        var meta = await fs.GetAsync(token, ep.SourcePath!, cancellationToken: ct);
                if (meta.Code == 200 && meta.Data is not null)
                {
                    if (meta.Data.IsDir)
                    {
            var list = await fs.ListAsync(token, ep.SourcePath!, cancellationToken: ct);
                        var items = new List<(string remotePath, string localPath)>();
                        foreach (var it in list.Data?.Content ?? Array.Empty<FsItem>())
                        {
                            if (it.IsDir) continue;
                            var r = ep.SourcePath!.TrimEnd('/') + "/" + it.Name;
                            var l = Path.Combine(localDir, it.Name!);
                            items.Add((r, l));
                        }
                        ProgressText = $"下载资源（{items.Count}）";
                        await fs.DownloadManyToFilesAsync(token, items.Select(p => (p.remotePath, p.localPath)), maxConcurrency: 6, cancellationToken: ct);
                    }
                    else
                    {
                        var name = Path.GetFileName(ep.SourcePath!);
                        var outPath = Path.Combine(localDir, name);
                        ProgressText = "下载图源压缩包";
                        await fs.DownloadToFileAsync(token, ep.SourcePath!, outPath, cancellationToken: ct);
                        var bytes = await File.ReadAllBytesAsync(outPath);
                        ProgressText = "解压资源";
                        await TryExtractAllAsync(bytes, name, localDir);
                    }
                }
            }
            // Download translate
            if (!string.IsNullOrWhiteSpace(ep.TranslatePath))
            {
                var name = Path.GetFileName(ep.TranslatePath!);
                var outPath = Path.Combine(localDir, name);
                ProgressText = "下载翻译";
                // 冲突保护：若本地已存在且与远端不同，则提示合并
                if (File.Exists(outPath))
                {
                    try
                    {
                        var localTxt = await File.ReadAllTextAsync(outPath, Encoding.UTF8);
                        var remoteRes = await fs.DownloadAsync(token, ep.TranslatePath!, ct);
                        if (remoteRes.Code == 200 && remoteRes.Content is not null)
                        {
                            var remoteTxt = Encoding.UTF8.GetString(remoteRes.Content);
                            if (!string.Equals(localTxt, remoteTxt, StringComparison.Ordinal))
                            {
                                // 打开合并助手供用户选择，返回后再写入
                                var win = Views.MainWindow.Instance;
                                string? merged = null;
                                if (win is not null)
                                {
                                    var dlg = new Views.Windows.MergeConflictWindow();
                                    merged = await dlg.ShowAsync(win, remoteTxt, localTxt, name);
                                }
                                if (merged is not null)
                                {
                                    await File.WriteAllTextAsync(outPath, merged, Encoding.UTF8);
                                }
                                else
                                {
                                    // 用户取消合并：保留本地文件，不覆盖
                                    Logger.Warn("Skip overwrite local translate due to unresolved conflict: {file}", outPath);
                                }
                            }
                            else
                            {
                                // 内容一致，无需下载
                            }
                        }
                        else
                        {
                            // 无法读取远端，按原逻辑尝试下载覆盖
                            await fs.DownloadToFileAsync(token, ep.TranslatePath!, outPath, cancellationToken: ct);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "Conflict check on download failed, fallback to overwrite.");
                        await fs.DownloadToFileAsync(token, ep.TranslatePath!, outPath, cancellationToken: ct);
                    }
                }
                else
                {
                    await fs.DownloadToFileAsync(token, ep.TranslatePath!, outPath, cancellationToken: ct);
                }
            }
            // Open folder
            try
            {
                var dirToOpen = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = localDir,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(dirToOpen);
                ShowNotify("已准备嵌字", $"话数 {EpisodeLabel(ep)} 本地目录已打开", NotificationType.Information);
            }
            catch { }
        }
        catch (Exception ex)
        {
            Status = $"开始嵌字失败: {ex.Message}";
            Logger.Error(ex, "StartTypeset failed.");
            ShowNotify("开始嵌字失败", ex.Message, NotificationType.Error);
        }
        finally { IsBusy = false; }
    }

    private static bool IsImageExt(string ext)
        => new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" }.Contains(ext, StringComparer.OrdinalIgnoreCase);

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(s) ? "project" : s;
    }

    private void ShowNotify(string title, string message, NotificationType type)
    {
    try { NotificationManager?.Show(new Notification(title, message), showIcon: true, showClose: true, type: type, classes: ["Light"]); }
    catch { }
    }

    private static string RemoteDirFromProjectJson(string projectJsonPath)
    {
        var dir = Path.GetDirectoryName(projectJsonPath)?.Replace('\\', '/').TrimEnd('/') ?? "/";
        return dir.EndsWith('/') ? dir : dir + "/";
    }
    private static string CombineRemote(string dir, string sub, string file)
    {
        dir = dir.Replace('\\', '/');
        if (!dir.EndsWith('/')) dir += "/";
        return $"{dir}{sub}/{file}";
    }

    private static bool IsSpecialKey(string key) => !string.IsNullOrEmpty(key) && (key.StartsWith("番外", StringComparison.OrdinalIgnoreCase) || key.StartsWith("SP", StringComparison.OrdinalIgnoreCase) || key.StartsWith("Special", StringComparison.OrdinalIgnoreCase) || key.StartsWith("Extra", StringComparison.OrdinalIgnoreCase));
    private static bool IsVolumeKey(string key) => !string.IsNullOrEmpty(key) && (key.StartsWith("卷", StringComparison.OrdinalIgnoreCase) || key.StartsWith("V", StringComparison.OrdinalIgnoreCase) || key.StartsWith("Vol", StringComparison.OrdinalIgnoreCase));
    private static string KeyToLabel(string key, int number)
    {
        if (IsSpecialKey(key))
        {
            var digits = new string(key.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var n) && n > 0) return $"番外{n:00}";
            return "番外";
        }
        if (IsVolumeKey(key))
        {
            var digits = new string(key.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var n) && n > 0) return $"卷{n:00}";
            return $"卷{number:00}";
        }
        return number.ToString("00");
    }
    private static string KeyToSubDir(string key, int number)
    {
        if (IsSpecialKey(key))
        {
            var digits = new string(key.Where(char.IsDigit).ToArray());
            return string.IsNullOrEmpty(digits) ? "番外" : $"番外{int.Parse(digits):00}";
        }
        if (IsVolumeKey(key))
        {
            var digits = new string(key.Where(char.IsDigit).ToArray());
            return string.IsNullOrEmpty(digits) ? $"卷{number:00}" : $"卷{int.Parse(digits):00}";
        }
        return number.ToString("00");
    }
    private static string EpisodeLabel(EpisodeItem ep) => KeyToLabel(ep.Key, ep.Number);

    private async Task UpdateEpisodeAsync(FileSystemApi fs, string token, EpisodeItem ep, Action<EpisodeCn> edit, string? newStatus = null)
    {
        var dl = await fs.DownloadAsync(token, ep.ProjectJsonPath!);
        if (dl.Code != 200 || dl.Content is null) return;
        var txt = Encoding.UTF8.GetString(TrimBom(dl.Content)).TrimStart('\uFEFF');
        ProjectCn cn;
        try { cn = JsonSerializer.Deserialize(txt, AppJsonContext.Default.ProjectCn) ?? new ProjectCn(); }
        catch { cn = new ProjectCn(); }
        if (!cn.Items.TryGetValue(ep.Key, out var ecn))
        {
            ecn = new EpisodeCn();
            cn.Items[ep.Key] = ecn;
        }
        edit(ecn);
        if (!string.IsNullOrWhiteSpace(newStatus)) ecn.Status = newStatus;
        var jsonOut = JsonSerializer.Serialize(cn, AppJsonContext.Default.ProjectCn);
        await fs.SafePutAsync(token, ep.ProjectJsonPath!, Encoding.UTF8.GetBytes(jsonOut));
    }

    private static bool IsCheckedFileName(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;
        return baseName.EndsWith("_checked", StringComparison.OrdinalIgnoreCase) || baseName.EndsWith("checked", StringComparison.OrdinalIgnoreCase);
    }

    private static string MakeCheckedFileName(string originalFileName)
    {
        var dir = Path.GetDirectoryName(originalFileName);
        var nameNoExt = Path.GetFileNameWithoutExtension(originalFileName) ?? string.Empty;
        var ext = Path.GetExtension(originalFileName);
        // 避免重复追加
        if (!nameNoExt.EndsWith("_checked", StringComparison.OrdinalIgnoreCase))
            nameNoExt += "_checked";
        var finalName = nameNoExt + (string.IsNullOrEmpty(ext) ? ".txt" : ext);
        return string.IsNullOrEmpty(dir) ? finalName : Path.Combine(dir, finalName);
    }

    // Extraction helpers using SharpCompress — run heavy work on background threads to avoid UI stalls
    private static async Task TryExtractImagesAsync(byte[] archiveBytes, string fileName, string extractDir)
    {
        await Task.Run(() => ExtractArchiveCore(archiveBytes, fileName, extractDir, imagesOnly: true));
    }

    private static async Task TryExtractAllAsync(byte[] archiveBytes, string fileName, string extractDir)
    {
        await Task.Run(() => ExtractArchiveCore(archiveBytes, fileName, extractDir, imagesOnly: false));
    }

    private static void ExtractArchiveCore(byte[] archiveBytes, string fileName, string extractDir, bool imagesOnly)
    {
        try
        {
            Directory.CreateDirectory(extractDir);
            using var ms = new MemoryStream(archiveBytes);
            SC.IArchive? archive = null;
            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) archive = SCZip.ZipArchive.Open(ms);
            else if (fileName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase)) archive = SCRar.RarArchive.Open(ms);
            else if (fileName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".7zip", StringComparison.OrdinalIgnoreCase)) archive = SC7z.SevenZipArchive.Open(ms);
            if (archive is null) return;
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory) continue;
                var key = entry.Key;
                if (string.IsNullOrEmpty(key)) continue;
                var outPath = imagesOnly
                    ? Path.Combine(extractDir, Path.GetFileName(key))
                    : Path.Combine(extractDir, key.Replace('/', Path.DirectorySeparatorChar));

                if (imagesOnly)
                {
                    var ext = Path.GetExtension(key);
                    if (ext is null || !IsImageExt(ext)) continue;
                    // Avoid collisions by appending index
                    if (File.Exists(outPath))
                    {
                        var baseName = Path.GetFileNameWithoutExtension(outPath);
                        var ext2 = Path.GetExtension(outPath);
                        var i = 1;
                        string tryPath;
                        do { tryPath = Path.Combine(extractDir, $"{baseName}_{i}{ext2}"); i++; }
                        while (File.Exists(tryPath));
                        outPath = tryPath;
                    }
                }
                else
                {
                    var dir = Path.GetDirectoryName(outPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                }

                using var outStream = File.Create(outPath);
                using var inStream = entry.OpenEntryStream();
                inStream?.CopyTo(outStream);
            }
        }
        catch (Exception ex)
        {
            if (imagesOnly) Logger.Warn(ex, "Extract images failed for {file}", fileName);
            else Logger.Warn(ex, "Extract all failed for {file}", fileName);
        }
    }

    public sealed partial class EpisodeItem : ObservableObject
    {
        public int Number { get; set; }
        public string Key { get; set; } = "";
    public string DisplayNumber { get; set; } = "";
        [ObservableProperty] private string? status;
        public string? SourcePath { get; set; }
        [ObservableProperty] private string? translatePath;
        public string? TypesetPath { get; set; }
        public string ProjectJsonPath { get; set; } = "";
    }
}
