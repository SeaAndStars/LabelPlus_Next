using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Models;
using LabelPlus_Next.Serialization;
using LabelPlus_Next.Services;
using LabelPlus_Next.Services.Api;
using NLog;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ursa.Controls;

namespace LabelPlus_Next.ViewModels;

public partial class UploadViewModel : ObservableObject
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly ISettingsService _settingsService = new JsonSettingsService();
    private IFileDialogService? _dialogs;

    // Track whether current flow is uploading to existing project (must merge existing JSON)
    private bool _uploadToExistingProject;
    [ObservableProperty] private string? currentUploadingPath;

    [ObservableProperty] private string? searchText;

    [ObservableProperty] private string? selectedProject;

    [ObservableProperty] private string? status;

    [ObservableProperty] private int uploadCompleted;
    [ObservableProperty] private int uploadTotal;

    public UploadViewModel() : this(true) { }

    public UploadViewModel(bool autoRefresh)
    {
        AddProjectCommand = new AsyncRelayCommand(AddProjectAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        // MVVM commands
        PickUploadFolderCommand = new AsyncRelayCommand(PickUploadFolderAsync);
        PickUploadFilesCommand = new AsyncRelayCommand(PickUploadFilesAsync);
        OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
        if (autoRefresh)
        {
            _ = RefreshAsync(); // auto refresh when page opens
        }
        Logger.Info("UploadViewModel created.");
    }

    public ObservableCollection<string> Suggestions { get; } = new();
    public ObservableCollection<string> Projects { get; } = new();

    public IAsyncRelayCommand AddProjectCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    // New: dialog-backed commands for MVVM
    public IAsyncRelayCommand PickUploadFolderCommand { get; }
    public IAsyncRelayCommand PickUploadFilesCommand { get; }
    public IAsyncRelayCommand OpenSettingsCommand { get; }

    // Keep last loaded mapping (name -> remote project json path)
    public Dictionary<string, string> ProjectMap { get; } = new();

    // For Add Project flow
    public string? LastSelectedFolderPath { get; set; }
    public ObservableCollection<EpisodeEntry> PendingEpisodes { get; } = new();
    private string? _pendingProjectName;
    public string? PendingProjectName
    {
        get => _pendingProjectName;
        set => SetProperty(ref _pendingProjectName, value);
    }
    public bool HasDuplicates { get; private set; }

    public static string UploadSettingsPath
    {
        get => Path.Combine(AppContext.BaseDirectory, "upload.json");
    }

    public event EventHandler? MetadataReady; // Raised after PendingEpisodes prepared
    public event EventHandler? OpenSettingsRequested; // Raised when settings should be shown
    public event EventHandler<IReadOnlyList<UploadViewModel>>? MultiMetadataReady; // New event for multiple VMs

    public void InitializeServices(IFileDialogService dialogs)
    {
        _dialogs ??= dialogs;
        Logger.Debug("Dialogs service injected.");
    }

    private async Task<UploadSettings?> LoadUploadSettingsAsync()
    {
        try
        {
            if (!File.Exists(UploadSettingsPath))
            {
                var def = new UploadSettings { BaseUrl = "https://alist1.seastarss.cn" };
                await using var fw = File.Create(UploadSettingsPath);
                await JsonSerializer.SerializeAsync(fw, def, AppJsonContext.Default.UploadSettings);
                Logger.Info("Created default upload settings at {path}", UploadSettingsPath);
                return def;
            }
            await using var fs = File.OpenRead(UploadSettingsPath);
            var s = await JsonSerializer.DeserializeAsync(fs, AppJsonContext.Default.UploadSettings);
            Logger.Debug("Upload settings loaded: base={base}", s?.BaseUrl);
            return s;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "LoadUploadSettingsAsync failed.");
            return null;
        }
    }

    private async Task PickUploadFolderAsync()
    {
        if (_dialogs is null) return;
        var folder = await _dialogs.PickFolderAsync("选择要上传的文件夹");
        if (string.IsNullOrEmpty(folder)) return;
        _uploadToExistingProject = true;
        LastSelectedFolderPath = folder;
        var folderName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(SelectedProject)) SelectedProject = folderName;
        if (!string.Equals(SelectedProject, folderName, StringComparison.Ordinal))
        {
            await _dialogs.ShowMessageAsync($"所选文件夹名“{folderName}”与目标项目名“{SelectedProject}”不一致，请确认。");
            Status = "项目名不一致";
            Logger.Warn("Folder name mismatch: folder={folderName} selectedProject={selected}", folderName, SelectedProject);
            return;
        }
        PendingProjectName = SelectedProject;
        Status = $"已选择文件夹：{folderName}";
        Logger.Info("Picked upload folder: {folder}", folder);

        try
        {
            // Prepare episodes from local folder and open metadata window
            var episodes = ScanEpisodes(folder);
            PendingEpisodes.Clear();
            foreach (var e in episodes.OrderByDescending(e => e.Number)) PendingEpisodes.Add(e);
            HasDuplicates = false; // duplicates will be handled inside UploadPendingAsync merge stage
            MetadataReady?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Status = "未能读取文件";
            Logger.Error(ex, "PickUploadFolderAsync prepare failed for {folder}", folder);
        }
    }

    private async Task PickUploadFilesAsync()
    {
        if (_dialogs is null) return;
        var files = await _dialogs.PickFilesAsync("选择要上传的文件（可多选）");
        if (files is null || files.Count == 0) return;
        _uploadToExistingProject = true;

        // Infer project from SelectedProject or parent folder name of first file
        var first = files[0];
        var parentFolder = Path.GetFileName(Path.GetDirectoryName(first) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(SelectedProject)) SelectedProject = parentFolder;
        if (!string.IsNullOrWhiteSpace(SelectedProject) && !string.Equals(SelectedProject, parentFolder, StringComparison.Ordinal))
        {
            await _dialogs.ShowMessageAsync($"所选文件所在文件夹名“{parentFolder}”与目标项目名“{SelectedProject}”不一致，请确认。");
            Status = "项目名不一致";
            Logger.Warn("PickUploadFilesAsync: parent folder mismatch. parent={parentFolder} selected={selected}", parentFolder, SelectedProject);
            return;
        }
        PendingProjectName = SelectedProject;

        // Group files by episode number inferred from file/folder name
        var byEpisode = new Dictionary<int, EpisodeEntry>();
        foreach (var path in files)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!TryParseEpisodeNumber(name, out var num))
            {
                // fallback: try parent folder
                var folder = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
                if (!TryParseEpisodeNumber(folder, out num)) continue;
            }
            if (!byEpisode.TryGetValue(num, out var ep))
            {
                ep = new EpisodeEntry { Number = num, Status = "立项" };
                byEpisode[num] = ep;
            }
            ep.LocalFiles.Add(path);
            // upgrade status depending on ext
            var ext = Path.GetExtension(path);
            if (ext.Equals(".psd", StringComparison.OrdinalIgnoreCase)) ep.Status = PickHigherStatus(ep.Status, "嵌字");
            else if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                var lower = Path.GetFileName(path).ToLowerInvariant();
                ep.Status = PickHigherStatus(ep.Status, lower.Contains("校对") || lower.Contains("校隊") || lower.Contains("check") ? "校对" : "翻译");
            }
        }

        PendingEpisodes.Clear();
        foreach (var ep in byEpisode.Values.OrderByDescending(e => e.Number)) PendingEpisodes.Add(ep);
        LastSelectedFolderPath = Path.GetDirectoryName(first); // remember base folder
        Status = $"已选择文件：{files.Count} 个";
        HasDuplicates = false;
        MetadataReady?.Invoke(this, EventArgs.Empty);
    }

    private Task OpenSettingsAsync()
    {
        Logger.Debug("OpenSettings requested.");
        OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private async Task AddProjectAsync()
    {
        try
        {
            _uploadToExistingProject = false;

            List<string> candidateFolders = new();
            if (!string.IsNullOrWhiteSpace(LastSelectedFolderPath) && Directory.Exists(LastSelectedFolderPath))
            {
                candidateFolders.Add(LastSelectedFolderPath!);
            }
            else if (_dialogs is not null)
            {
                // let user pick multiple episode folders
                var pickedMany = await _dialogs.PickFoldersAsync("选择要上传的话数文件夹（可多选）");
                if (pickedMany is { Count: > 0 }) candidateFolders.AddRange(pickedMany);
                else
                {
                    var pickedSingle = await _dialogs.PickFolderAsync("选择要上传的文件夹");
                    if (!string.IsNullOrEmpty(pickedSingle)) candidateFolders.Add(pickedSingle);
                }
            }

            if (candidateFolders.Count == 0)
            {
                Status = "请先选择上传文件夹";
                Logger.Warn("AddProject aborted: no valid folder.");
                return;
            }

            // Login once and refresh aggregate for map
            var us = await LoadUploadSettingsAsync();
            if (us is null || string.IsNullOrWhiteSpace(us.BaseUrl))
            {
                Status = "未配置服务器地址";
                Logger.Warn("No server baseUrl.");
                return;
            }
            var baseUrl = us.BaseUrl!.TrimEnd('/');
            var auth = new AuthApi(baseUrl);
            var login = await auth.LoginAsync(us.Username ?? string.Empty, us.Password ?? string.Empty);
            if (login.Code != 200 || string.IsNullOrWhiteSpace(login.Data?.Token))
            {
                Status = $"登录失败: {login.Code} {login.Message}";
                Logger.Warn("Login failed: {code} {msg}", login.Code, login.Message);
                return;
            }
            var token = login.Data.Token!;
            await RefreshAsync();
            var fsApi = new FileSystemApi(baseUrl);

            if (candidateFolders.Count == 1)
            {
                // Keep original single-folder flow
                var folder = candidateFolders[0];
                var projectName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                PendingProjectName = projectName;
                Logger.Info("AddProject(single) for {project} at {dir}", projectName, folder);

                if (ProjectMap.ContainsKey(projectName))
                {
                    Logger.Warn("Aggregate already contains project {name}", projectName);
                    await MessageBox.ShowAsync($"聚合清单中已存在项目“{projectName}”。建议使用‘上传到项目’以避免冲突。", "提示", MessageBoxIcon.Information);
                    return;
                }

                // Episode duplicate check (optional)
                var projectJsonPath = ProjectMap.TryGetValue(projectName, out var path) && !string.IsNullOrWhiteSpace(path)
                    ? path
                    : $"/{projectName}/{projectName}_project.json";
                var existingEpisodeNums = await GetExistingEpisodeNumbersAsync(fsApi, token, projectJsonPath);
                var episodes = ScanEpisodes(folder);
                HasDuplicates = episodes.Any(e => existingEpisodeNums.Contains(e.Number));
                if (HasDuplicates)
                {
                    await MessageBox.ShowAsync("检测到与远端项目存在相同话数。建议使用‘上传到项目’功能以避免覆盖。", "提示", MessageBoxIcon.Warning);
                    return;
                }
                PendingEpisodes.Clear();
                foreach (var e in episodes.OrderByDescending(e => e.Number)) PendingEpisodes.Add(e);
                MetadataReady?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Multiple folders: create a VM per folder and raise multi event
            var list = new List<UploadViewModel>();
            foreach (var folder in candidateFolders)
            {
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    var projectName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (ProjectMap.ContainsKey(projectName))
                    {
                        await _dialogs!.ShowMessageAsync($"聚合清单中已存在项目“{projectName}”，已跳过。");
                        continue;
                    }
                    var projectJsonPath = ProjectMap.TryGetValue(projectName, out var path) && !string.IsNullOrWhiteSpace(path)
                        ? path
                        : $"/{projectName}/{projectName}_project.json";
                    var existingEpisodeNums = await GetExistingEpisodeNumbersAsync(fsApi, token, projectJsonPath);
                    var episodes = ScanEpisodes(folder).OrderByDescending(e => e.Number).ToList();
                    var dup = episodes.Any(e => existingEpisodeNums.Contains(e.Number));
                    if (dup)
                    {
                        await _dialogs!.ShowMessageAsync($"项目“{projectName}”存在与远端相同话数，已跳过。请使用‘上传到项目’。");
                        continue;
                    }

                    var vm = new UploadViewModel();
                    vm.InitializeServices(_dialogs!);
                    // copy project map for correct path resolution
                    foreach (var kv in ProjectMap) vm.ProjectMap[kv.Key] = kv.Value;
                    vm.PendingProjectName = projectName;
                    vm.LastSelectedFolderPath = folder;
                    vm.PendingEpisodes.Clear();
                    foreach (var e in episodes) vm.PendingEpisodes.Add(e);
                    vm.HasDuplicates = false;
                    list.Add(vm);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Prepare child vm failed for {folder}", folder);
                }
            }

            if (list.Count == 0)
            {
                Status = "没有可上传的项目";
                return;
            }

            MultiMetadataReady?.Invoke(this, list);
        }
        catch (Exception ex)
        {
            Status = $"新增项目失败: {ex.Message}";
            Logger.Error(ex, "AddProjectAsync failed.");
        }
    }

    private static async Task<HashSet<int>> GetExistingEpisodeNumbersAsync(FileSystemApi fsApi, string token, string projectJsonPath)
    {
        var set = new HashSet<int>();
        try
        {
            var get = await fsApi.GetAsync(token, projectJsonPath);
            if (get.Code == 200 && get.Data is { IsDir: false })
            {
                var dl = await fsApi.DownloadAsync(token, projectJsonPath);
                if (dl.Code == 200 && dl.Content is { Length: > 0 })
                {
                    var bytes = dl.Content;
                    if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) bytes = bytes[3..];
                    var json = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Trim();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("episodes", out var eps) && eps.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in eps.EnumerateObject())
                        {
                            if (TryParseEpisodeNumber(prop.Name, out var n))
                                set.Add(n);
                        }
                    }
                    else if (doc.RootElement.TryGetProperty("项目", out var items) && items.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in items.EnumerateObject())
                        {
                            if (TryParseEpisodeNumber(prop.Name, out var n))
                                set.Add(n);
                        }
                    }
                }
            }
        }
        catch { }
        return set;
    }

    private static int StatusRank(string status)
    {
        return status switch
        {
            "发布" => 4,
            "嵌字" => 3,
            "校对" => 2,
            "翻译" => 1,
            _ => 0
        };
    }

    private static string PickHigherStatus(string a, string b) => StatusRank(b) > StatusRank(a) ? b : a;

    private static List<EpisodeEntry> ScanEpisodes(string localDir)
    {
        var archives = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip", ".7z", ".rar" };
        var allEntries = Directory.EnumerateFileSystemEntries(localDir, "*", SearchOption.TopDirectoryOnly).ToList();

        var map = new Dictionary<int, EpisodeEntry>();

        foreach (var entry in allEntries)
        {
            if (Directory.Exists(entry))
            {
                var name = Path.GetFileName(entry);
                if (!TryParseEpisodeNumber(name, out var num)) continue;
                var folderEp = BuildEpisodeFromFolder(entry, num);
                if (!map.TryGetValue(num, out var existing))
                {
                    map[num] = folderEp;
                }
                else
                {
                    var set = new HashSet<string>(existing.LocalFiles, StringComparer.OrdinalIgnoreCase);
                    foreach (var f in folderEp.LocalFiles)
                    {
                        if (set.Add(f))
                            existing.LocalFiles.Add(f);
                    }
                    existing.Status = PickHigherStatus(existing.Status, folderEp.Status);
                }
            }
            else if (File.Exists(entry))
            {
                var ext = Path.GetExtension(entry);
                if (archives.Contains(ext))
                {
                    var name = Path.GetFileNameWithoutExtension(entry);
                    if (!TryParseEpisodeNumber(name, out var num)) continue;
                    if (!map.TryGetValue(num, out var existing))
                    {
                        var ep = new EpisodeEntry { Number = num, Status = "立项" };
                        ep.LocalFiles.Add(entry);
                        map[num] = ep;
                    }
                    else
                    {
                        if (!existing.LocalFiles.Contains(entry, StringComparer.OrdinalIgnoreCase))
                            existing.LocalFiles.Add(entry);
                        existing.Status = PickHigherStatus(existing.Status, "立项");
                    }
                }
            }
        }

        // Robustness: any txt file with episode number in its file name marks that episode as 翻译 (lower priority than 校对)
        try
        {
            var txtFiles = Directory.EnumerateFiles(localDir, "*.txt", SearchOption.AllDirectories).ToList();
            foreach (var txt in txtFiles)
            {
                var nameWithoutExt = Path.GetFileNameWithoutExtension(txt);
                int num;
                if (!TryParseEpisodeNumber(nameWithoutExt, out num))
                {
                    var parent = Path.GetFileName(Path.GetDirectoryName(txt) ?? string.Empty);
                    if (!TryParseEpisodeNumber(parent, out num)) continue;
                }
                if (!map.TryGetValue(num, out var epEntry))
                {
                    epEntry = new EpisodeEntry { Number = num, Status = "立项" };
                    map[num] = epEntry;
                }
                if (!epEntry.LocalFiles.Contains(txt, StringComparer.OrdinalIgnoreCase))
                    epEntry.LocalFiles.Add(txt);

                // Determine candidate status: prefer 校对 if keywords, else 翻译
                var lower = nameWithoutExt.ToLowerInvariant();
                var candidate = lower.Contains("校对") || lower.Contains("校隊") || lower.Contains("check") ? "校对" : "翻译";
                epEntry.Status = PickHigherStatus(epEntry.Status, candidate);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "ScanEpisodes: txt pass failed");
        }

        Logger.Debug("ScanEpisodes: merged into {count} episodes", map.Count);
        return map.Values.ToList();
    }

    private static EpisodeEntry BuildEpisodeFromFolder(string folder, int number)
    {
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList();
        var hasArchive = files.Any(f => new[] { ".zip", ".7z", ".rar" }.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        var hasTxt = files.Any(f => string.Equals(Path.GetExtension(f), ".txt", StringComparison.OrdinalIgnoreCase));
        var hasPsd = files.Any(f => string.Equals(Path.GetExtension(f), ".psd", StringComparison.OrdinalIgnoreCase));
        var hasCheckTxt = files.Any(f => string.Equals(Path.GetExtension(f), ".txt", StringComparison.OrdinalIgnoreCase) &&
                                         ContainsAny(Path.GetFileNameWithoutExtension(f), new[] { "check", "校队", "校对" }));
        var status = hasPsd ? "嵌字" : hasCheckTxt ? "校对" : hasTxt ? "翻译" : hasArchive ? "立项" : "立项";
        var ep = new EpisodeEntry { Number = number, Status = status };
        ep.LocalFiles.AddRange(files);
        return ep;
    }

    private static bool ContainsAny(string s, IEnumerable<string> needles)
    {
        foreach (var n in needles)
        {
            if (s?.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static bool TryParseEpisodeNumber(string name, out int number)
    {
        var digits = new string(name.Where(char.IsDigit).ToArray());
        if (!string.IsNullOrEmpty(digits) && int.TryParse(digits, out number)) return true;
        number = ParseChineseNumeral(name);
        return number > 0;
    }

    private static int ParseChineseNumeral(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var map = new Dictionary<char, int>
        {
            ['零'] = 0, ['一'] = 1, ['二'] = 2, ['两'] = 2, ['三'] = 3, ['四'] = 4, ['五'] = 5, ['六'] = 6, ['七'] = 7, ['八'] = 8, ['九'] = 9,
            ['十'] = 10, ['百'] = 100, ['千'] = 1000, ['〇'] = 0
        };
        var total = 0;
        var section = 0;
        var number = 0;
        foreach (var ch in s)
        {
            if (!map.TryGetValue(ch, out var val)) continue;
            if (val == 10 || val == 100 || val == 1000)
            {
                if (number == 0) number = 1;
                section += number * val;
                number = 0;
            }
            else
            {
                number = val;
            }
        }
        total += section + number;
        return total;
    }

    private async Task RefreshAsync()
    {
        try
        {
            Status = "正在刷新项目列表...";
            var us = await LoadUploadSettingsAsync();
            if (us is null || string.IsNullOrWhiteSpace(us.BaseUrl))
            {
                Status = "未配置服务器地址";
                Logger.Warn("Refresh aborted: missing baseUrl.");
                return;
            }

            var baseUrl = us.BaseUrl!.TrimEnd('/');
            var filePath = "/project.json";

            var auth = new AuthApi(baseUrl);
            var login = await auth.LoginAsync(us.Username ?? string.Empty, us.Password ?? string.Empty);
            if (login.Code != 200 || string.IsNullOrWhiteSpace(login.Data?.Token))
            {
                Status = $"登录失败: {login.Code} {login.Message}";
                Logger.Warn("Refresh login failed: {code} {msg}", login.Code, login.Message);
                return;
            }
            var token = login.Data!.Token!;

            var fsApi = new FileSystemApi(baseUrl);
            var result = await fsApi.DownloadAsync(token, filePath);
            if (result.Code != 200 || result.Content is null)
            {
                Status = $"下载失败: {result.Code} {result.Message}";
                Logger.Warn("Download project.json failed: {code} {msg}", result.Code, result.Message);
                return;
            }

            var bytes = result.Content;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) bytes = bytes[3..];
            var json = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Trim();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Object)
            {
                Status = "清单格式错误（缺少 projects）";
                Logger.Warn("Invalid manifest: projects missing.");
                return;
            }

            ProjectMap.Clear();
            Suggestions.Clear();
            Projects.Clear();

            foreach (var prop in projects.EnumerateObject())
            {
                var name = prop.Name;
                var path = prop.Value.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;
                ProjectMap[name] = path;
                Suggestions.Add(name);
                Projects.Add(name);
            }

            Status = $"已加载 {Projects.Count} 个项目";
            Logger.Info("Projects loaded: {count}", Projects.Count);
        }
        catch (Exception ex)
        {
            Status = $"刷新失败: {ex.Message}";
            Logger.Error(ex, "RefreshAsync failed.");
        }
    }

    public async Task<bool> UploadPendingAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(PendingProjectName))
            {
                Status = "无项目";
                Logger.Warn("Upload aborted: no project name.");
                return false;
            }
            var us = await LoadUploadSettingsAsync();
            if (us is null || string.IsNullOrWhiteSpace(us.BaseUrl))
            {
                Status = "未配置服务器地址";
                Logger.Warn("Upload aborted: no baseUrl.");
                return false;
            }

            var toUpload = PendingEpisodes.Where(e => e.Include).ToList();
            var episodesCount = toUpload.Count;
            var plannedFilesCount = toUpload.Sum(e => e.LocalFiles.Count);
            var includeAggregate = !_uploadToExistingProject; // only for new project
            var preSteps = 0
                           + 1 // 登录
                           + 1 // 创建项目目录
                           + episodesCount // 创建各话目录
                           + plannedFilesCount // 读取与规划文件
                           + (includeAggregate ? 2 : 0) // 下载/上传根清单
                           + 1 // 下载项目JSON
                           + 1 // 合并JSON
                           + 1; // 上传项目JSON
            var uploadSteps = plannedFilesCount;
            var totalSteps = preSteps + uploadSteps;

            UploadCompleted = 0;
            UploadTotal = totalSteps;
            CurrentUploadingPath = null;
            void Step(string message, string? path = null)
            {
                Status = message;
                CurrentUploadingPath = path;
                UploadCompleted = Math.Min(UploadCompleted + 1, UploadTotal);
            }

            Status = "登录中...";
            var baseUrl = us.BaseUrl!.TrimEnd('/');
            var auth = new AuthApi(baseUrl);
            var login = await auth.LoginAsync(us.Username ?? string.Empty, us.Password ?? string.Empty);
            if (login.Code != 200 || string.IsNullOrWhiteSpace(login.Data?.Token))
            {
                Status = $"登录失败: {login.Code} {login.Message}";
                Logger.Warn("Upload login failed: {code} {msg}", login.Code, login.Message);
                return false;
            }
            Step("登录完成");
            var token = login.Data!.Token!;
            var fsApi = new FileSystemApi(baseUrl);

            var projectDir = "/" + PendingProjectName!.Trim('/') + "/";
            var mkProj = await fsApi.MkdirAsync(token, projectDir);
            if (mkProj.Code is >= 400 and < 500)
            {
                // Fallback: treat as ok if directory already exists
                var chk = await fsApi.GetAsync(token, projectDir.TrimEnd('/'));
                if (chk.Code != 200 || chk.Data is null || !chk.Data.IsDir)
                {
                    Status = $"创建目录失败: {mkProj.Message}";
                    return false;
                }
                Logger.Info("Project directory already exists: {dir}", projectDir);
            }
            Step("已创建项目目录", projectDir);

            foreach (var ep in toUpload)
            {
                var epDir = projectDir + ep.Number + "/";
                var mk = await fsApi.MkdirAsync(token, epDir);
                if (mk.Code is >= 400 and < 500)
                {
                    var chk = await fsApi.GetAsync(token, epDir.TrimEnd('/'));
                    if (chk.Code != 200 || chk.Data is null || !chk.Data.IsDir)
                    {
                        Status = $"创建话数目录失败: {mk.Message}";
                        return false;
                    }
                    Logger.Info("Episode directory already exists: {dir}", epDir);
                }
                Step($"目录就绪: {ep.Number}", epDir);
            }

            // Build items and map paths; count planning steps
            var items = new List<FileUploadItem>();
            var resultMap = new Dictionary<int, (string? source, string? translate, string? typeset)>();
            foreach (var ep in toUpload)
            {
                var epDir = projectDir + ep.Number + "/";
                string? sourcePath = null;
                string? translatePath = null;
                string? typesetPath = null;
                foreach (var file in ep.LocalFiles)
                {
                    if (!File.Exists(file))
                    {
                        Step("跳过缺失文件", file);
                        continue;
                    }
                    var name = Path.GetFileName(file);
                    var remote = epDir + name;
                    var content = await File.ReadAllBytesAsync(file);
                    items.Add(new FileUploadItem { FilePath = remote, Content = content });
                    var ext = Path.GetExtension(name);
                    if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) || ext.Equals(".7z", StringComparison.OrdinalIgnoreCase) || ext.Equals(".rar", StringComparison.OrdinalIgnoreCase))
                        sourcePath ??= remote;
                    else if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        if (translatePath is null) translatePath = remote;
                        var lower = name.ToLowerInvariant();
                        if (lower.Contains("校对") || lower.Contains("校隊") || lower.Contains("check")) translatePath = remote;
                    }
                    else if (ext.Equals(".psd", StringComparison.OrdinalIgnoreCase))
                        typesetPath ??= remote;
                    Step("已规划文件", remote);
                }
                resultMap[ep.Number] = (sourcePath, translatePath, typesetPath);
            }

            // Update aggregate /project.json if creating a new project
            // Resolve project JSON path: if the (possibly edited) name exists in aggregate, use it to merge; otherwise default under projectDir
            var projectJsonPath = ProjectMap.TryGetValue(PendingProjectName, out var pj) && !string.IsNullOrWhiteSpace(pj)
                ? pj
                : projectDir + PendingProjectName + "_project.json";

            if (includeAggregate)
            {
                // If user edited project name to an existing one, prevent accidental conflict when creating a new project
                if (ProjectMap.ContainsKey(PendingProjectName))
                {
                    Status = $"聚合清单中已存在项目“{PendingProjectName}”，请更换名称或改用‘上传到项目’。";
                    return false;
                }
                const string aggregatePath = "/project.json";
                var agg = await fsApi.DownloadAsync(token, aggregatePath);
                if (agg.Code != 200 || agg.Content is null)
                {
                    Status = $"下载聚合清单失败: {agg.Code} {agg.Message}";
                    return false;
                }
                Step("已下载聚合清单", aggregatePath);

                Dictionary<string, string> map = new();
                try
                {
                    using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(agg.Content).TrimStart('\uFEFF'));
                    if (!doc.RootElement.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Object)
                    {
                        Status = "聚合清单格式错误";
                        return false;
                    }
                    foreach (var prop in projects.EnumerateObject())
                    {
                        map[prop.Name] = prop.Value.GetString() ?? string.Empty;
                    }
                }
                catch
                {
                    Status = "解析聚合清单失败";
                    return false;
                }

                // merge
                map[PendingProjectName] = projectJsonPath;
                var aggObj = new AggregateProjects { Projects = map };
                var aggOut = JsonSerializer.Serialize(aggObj, AppJsonContext.Default.AggregateProjects);
                var putAgg = await fsApi.SafePutAsync(token, aggregatePath, Encoding.UTF8.GetBytes(aggOut));
                if (putAgg.Code != 200)
                {
                    Status = $"更新聚合清单失败: {putAgg.Message}";
                    return false;
                }
                Step("聚合清单已更新", aggregatePath);
            }

            // Download and merge per-project JSON
            ProjectCn cn;
            var requireMergeExisting = _uploadToExistingProject;
            var getMeta = await fsApi.GetAsync(token, projectJsonPath);
            if (getMeta.Code == 200 && getMeta.Data is { IsDir: false })
            {
                var dl = await fsApi.DownloadAsync(token, projectJsonPath);
                if (dl.Code == 200 && dl.Content is { Length: > 0 })
                {
                    try
                    {
                        var txt = Encoding.UTF8.GetString(dl.Content).TrimStart('\uFEFF');
                        cn = JsonSerializer.Deserialize(txt, AppJsonContext.Default.ProjectCn) ?? new ProjectCn();
                    }
                    catch
                    {
                        if (requireMergeExisting)
                        {
                            Logger.Warn("Deserialize project JSON failed, continue with empty model.");
                            Status = "项目JSON读取失败，已以空模板继续";
                            cn = new ProjectCn();
                        }
                        else
                        {
                            cn = new ProjectCn();
                        }
                    }
                }
                else
                {
                    if (requireMergeExisting)
                    {
                        Logger.Warn("Download project JSON failed (code={code}), continue with empty model.", dl.Code);
                        Status = "未能下载项目JSON，已以空模板继续";
                        cn = new ProjectCn();
                    }
                    else
                    {
                        cn = new ProjectCn();
                    }
                }
            }
            else
            {
                if (requireMergeExisting)
                {
                    Logger.Warn("Project JSON not found at {path}, continue with empty model.", projectJsonPath);
                    Status = "未找到项目JSON，已以空模板继续";
                    cn = new ProjectCn();
                }
                else
                {
                    cn = new ProjectCn();
                }
            }
            Step("已获取项目JSON", projectJsonPath);

            foreach (var ep in toUpload)
            {
                resultMap.TryGetValue(ep.Number, out var pmap);
                var key = ep.Number.ToString("00");
                cn.Items[key] = new EpisodeCn { Status = ep.Status, SourcePath = pmap.source, TranslatePath = pmap.translate, TypesetPath = pmap.typeset };
            }
            cn.Items = cn.Items.OrderByDescending(kv => int.TryParse(kv.Key, out var n) ? n : 0).ToDictionary(k => k.Key, v => v.Value);
            Step("JSON 合并完成", projectJsonPath);

            var ctx = new AppJsonContext(new JsonSerializerOptions(AppJsonContext.Default.Options)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                WriteIndented = true
            });
            var jsonOut = JsonSerializer.Serialize(cn, ctx.ProjectCn);
            var bytesOut = Encoding.UTF8.GetBytes(jsonOut);
            var putJson = await fsApi.SafePutAsync(token, projectJsonPath, bytesOut);
            if (putJson.Code != 200)
            {
                Status = $"更新项目JSON失败: {putJson.Message}";
                return false;
            }
            Step("JSON 已上传", projectJsonPath);

            // Offset for upload progress
            var offset = UploadCompleted;
            var progress = new Progress<UploadProgress>(p =>
            {
                UploadTotal = totalSteps; // keep total constant
                UploadCompleted = Math.Min(offset + p.Completed, UploadTotal);
                CurrentUploadingPath = p.CurrentRemotePath;
                Status = $"上传中 {UploadCompleted}/{UploadTotal}: {CurrentUploadingPath}";
            });

            var res = await fsApi.PutManyAsync(token, items, progress, 6);
            if (res.Any(r => r.Code != 200))
            {
                var first = res.FirstOrDefault(r => r.Code != 200);
                Status = $"上传失败: {first?.Message}";
                return false;
            }

            Status = "上传完成";
            Logger.Info("Upload completed successfully.");
            await RefreshAsync();
            return true;
        }
        catch (Exception ex)
        {
            Status = $"上传异常: {ex.Message}";
            Logger.Error(ex, "UploadPendingAsync failed.");
            return false;
        }
    }
}
