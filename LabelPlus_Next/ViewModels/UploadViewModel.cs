using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Models;
using LabelPlus_Next.Serialization;
using LabelPlus_Next.Services;
using LabelPlus_Next.Services.Api;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;

namespace LabelPlus_Next.ViewModels;

public partial class UploadViewModel : ObservableObject
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    [ObservableProperty] private string? status;
    [ObservableProperty] private string? searchText;

    public ObservableCollection<string> Suggestions { get; } = new();
    public ObservableCollection<string> Projects { get; } = new();

    [ObservableProperty] private string? selectedProject;

    public IAsyncRelayCommand AddProjectCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    // New: dialog-backed commands for MVVM
    public IAsyncRelayCommand PickUploadFolderCommand { get; }
    public IAsyncRelayCommand PickUploadFilesCommand { get; }
    public IAsyncRelayCommand OpenSettingsCommand { get; }

    private readonly ISettingsService _settingsService = new JsonSettingsService();
    private IFileDialogService? _dialogs;

    // Keep last loaded mapping (name -> remote project json path)
    public Dictionary<string, string> ProjectMap { get; } = new();

    // For Add Project flow
    public string? LastSelectedFolderPath { get; set; }
    public ObservableCollection<EpisodeEntry> PendingEpisodes { get; } = new();
    public string? PendingProjectName { get; private set; }
    public bool HasDuplicates { get; private set; }

    public event EventHandler? MetadataReady; // Raised after PendingEpisodes prepared
    public event EventHandler? OpenSettingsRequested; // Raised when settings should be shown

    public UploadViewModel()
    {
        AddProjectCommand = new AsyncRelayCommand(AddProjectAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        // MVVM commands
        PickUploadFolderCommand = new AsyncRelayCommand(PickUploadFolderAsync);
        PickUploadFilesCommand = new AsyncRelayCommand(PickUploadFilesAsync);
        OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
        _ = RefreshAsync(); // auto refresh when page opens
        Logger.Info("UploadViewModel created.");
    }

    public void InitializeServices(IFileDialogService dialogs) { _dialogs ??= dialogs; Logger.Debug("Dialogs service injected."); }

    public static string UploadSettingsPath => Path.Combine(AppContext.BaseDirectory, "upload.json");

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
        LastSelectedFolderPath = folder;
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Status = $"已选择文件夹：{name}";
        Logger.Info("Picked upload folder: {folder}", folder);
    }

    private async Task PickUploadFilesAsync()
    {
        if (_dialogs is null) return;
        var folder = await _dialogs.PickFolderAsync("选择要上传的文件夹");
        if (string.IsNullOrEmpty(folder)) return;
        try
        {
            var exts = new HashSet<string>(new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".txt", ".zip", ".7z", ".rar" }, StringComparer.OrdinalIgnoreCase);
            var count = Directory.EnumerateFiles(folder).Count(f => exts.Contains(Path.GetExtension(f)));
            Status = $"已选择文件：{count} 个";
            Logger.Info("Picked files in folder {folder}: {count}", folder, count);
        }
        catch (Exception ex)
        {
            Status = "未能读取文件";
            Logger.Error(ex, "PickUploadFilesAsync failed for {folder}", folder);
        }
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
            if ((string.IsNullOrWhiteSpace(LastSelectedFolderPath) || !Directory.Exists(LastSelectedFolderPath)) && _dialogs is not null)
            {
                var picked = await _dialogs.PickFolderAsync("选择要上传的文件夹");
                if (!string.IsNullOrEmpty(picked))
                {
                    LastSelectedFolderPath = picked;
                    Logger.Info("AddProject: folder selected interactively: {folder}", picked);
                }
            }

            if (string.IsNullOrWhiteSpace(LastSelectedFolderPath) || !Directory.Exists(LastSelectedFolderPath))
            {
                Status = "请先选择上传文件夹";
                Logger.Warn("AddProject aborted: no valid folder.");
                return;
            }

            var localDir = LastSelectedFolderPath!;
            var projectName = Path.GetFileName(localDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            PendingProjectName = projectName;
            Logger.Info("AddProject for {project} at {dir}", projectName, localDir);

            var us = await LoadUploadSettingsAsync();
            if (us is null || string.IsNullOrWhiteSpace(us.BaseUrl)) { Status = "未配置服务器地址"; Logger.Warn("No server baseUrl."); return; }
            var baseUrl = us.BaseUrl!.TrimEnd('/');
            var auth = new AuthApi(baseUrl);
            var login = await auth.LoginAsync(us.Username ?? string.Empty, us.Password ?? string.Empty);
            if (login.Code != 200 || string.IsNullOrWhiteSpace(login.Data?.Token)) { Status = $"登录失败: {login.Code} {login.Message}"; Logger.Warn("Login failed: {code} {msg}", login.Code, login.Message); return; }
            var token = login.Data.Token!;

            await RefreshAsync();

            string projectJsonPath = ProjectMap.TryGetValue(projectName, out var path) && !string.IsNullOrWhiteSpace(path)
                ? path
                : $"/{projectName}/{projectName}_project.json";

            var fsApi = new FileSystemApi(baseUrl);
            var existingEpisodeNums = new HashSet<int>();
            var get = await fsApi.GetAsync(token, projectJsonPath);
            if (get.Code == 200 && get.Data is { IsDir: false })
            {
                var dl = await fsApi.DownloadAsync(token, projectJsonPath);
                if (dl.Code == 200 && dl.Content is { Length: > 0 })
                {
                    var bytes = dl.Content;
                    if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) bytes = bytes[3..];
                    var json = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Trim();
                    try
                    {
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("episodes", out var eps) && eps.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var prop in eps.EnumerateObject())
                            {
                                if (TryParseEpisodeNumber(prop.Name, out var n)) existingEpisodeNums.Add(n);
                            }
                        }
                        else
                        {
                            foreach (var prop in doc.RootElement.EnumerateObject())
                            {
                                if (TryParseEpisodeNumber(prop.Name, out var n)) existingEpisodeNums.Add(n);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "Parse remote project json failed: {path}", projectJsonPath);
                    }
                }
            }

            var episodes = ScanEpisodes(localDir);
            HasDuplicates = episodes.Any(e => existingEpisodeNums.Contains(e.Number));
            Logger.Info("Local episodes found: {count}, duplicates with remote: {dup}", episodes.Count, HasDuplicates);

            var sorted = episodes.OrderByDescending(e => e.Number).ToList();

            PendingEpisodes.Clear();
            foreach (var e in sorted) PendingEpisodes.Add(e);

            MetadataReady?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Status = $"新增项目失败: {ex.Message}";
            Logger.Error(ex, "AddProjectAsync failed.");
        }
    }

    private static int StatusRank(string status)
    {
        return status switch
        {
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
                        if (set.Add(f)) existing.LocalFiles.Add(f);
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
        Logger.Debug("ScanEpisodes: merged into {count} episodes", map.Count);
        return map.Values.ToList();
    }

    private static EpisodeEntry BuildEpisodeFromFolder(string folder, int number)
    {
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList();
        bool hasArchive = files.Any(f => new[] { ".zip", ".7z", ".rar" }.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        bool hasTxt = files.Any(f => string.Equals(Path.GetExtension(f), ".txt", StringComparison.OrdinalIgnoreCase));
        bool hasPsd = files.Any(f => string.Equals(Path.GetExtension(f), ".psd", StringComparison.OrdinalIgnoreCase));
        bool hasCheckTxt = files.Any(f => string.Equals(Path.GetExtension(f), ".txt", StringComparison.OrdinalIgnoreCase) &&
                                           ContainsAny(Path.GetFileNameWithoutExtension(f), new[] { "check", "校队", "校对" }));
        string status = hasPsd ? "嵌字" : (hasCheckTxt ? "校对" : (hasTxt ? "翻译" : (hasArchive ? "立项" : "立项")));
        var ep = new EpisodeEntry { Number = number, Status = status };
        ep.LocalFiles.AddRange(files);
        return ep;
    }

    private static bool ContainsAny(string s, IEnumerable<string> needles)
    {
        foreach (var n in needles)
            if (s?.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
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
        var map = new Dictionary<char, int> {
            ['零']=0,['一']=1,['二']=2,['两']=2,['三']=3,['四']=4,['五']=5,['六']=6,['七']=7,['八']=8,['九']=9,
            ['十']=10,['百']=100,['千']=1000,['〇']=0
        };
        int total = 0; int section = 0; int number = 0;
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
            if (string.IsNullOrWhiteSpace(PendingProjectName)) { Status = "无项目"; Logger.Warn("Upload aborted: no project name."); return false; }
            var us = await LoadUploadSettingsAsync();
            if (us is null || string.IsNullOrWhiteSpace(us.BaseUrl)) { Status = "未配置服务器地址"; Logger.Warn("Upload aborted: no baseUrl."); return false; }
            var baseUrl = us.BaseUrl!.TrimEnd('/');
            var auth = new AuthApi(baseUrl);
            var login = await auth.LoginAsync(us.Username ?? string.Empty, us.Password ?? string.Empty);
            if (login.Code != 200 || string.IsNullOrWhiteSpace(login.Data?.Token)) { Status = $"登录失败: {login.Code} {login.Message}"; Logger.Warn("Upload login failed: {code} {msg}", login.Code, login.Message); return false; }
            var token = login.Data!.Token!;
            var fsApi = new FileSystemApi(baseUrl);

            var projectDir = "/" + PendingProjectName!.Trim('/') + "/";
            await fsApi.MkdirAsync(token, projectDir);

            var toUpload = PendingEpisodes.Where(e => e.Include).ToList();
            Logger.Info("Uploading {count} episodes to {dir}", toUpload.Count, projectDir);
            foreach (var ep in toUpload)
            {
                var epDir = projectDir + ep.Number + "/";
                await fsApi.MkdirAsync(token, epDir);
                foreach (var file in ep.LocalFiles)
                {
                    try
                    {
                        if (!File.Exists(file)) { Logger.Warn("File missing: {file}", file); continue; }
                        var name = Path.GetFileName(file);
                        var remote = epDir + name;
                        var bytes = await File.ReadAllBytesAsync(file);
                        var res = await fsApi.SafePutAsync(token, remote, bytes);
                        if (res.Code != 200) { Status = $"上传失败: {remote} -> {res.Message}"; Logger.Warn("Upload failed: {remote} {code} {msg}", remote, res.Code, res.Message); return false; }
                        Logger.Debug("Uploaded: {remote}", remote);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Upload single file failed: {file}", file);
                        Status = $"上传失败: {file}";
                        return false;
                    }
                }
            }

            string projectJsonPath = ProjectMap.TryGetValue(PendingProjectName, out var path) && !string.IsNullOrWhiteSpace(path)
                ? path
                : projectDir + PendingProjectName + "_project.json";

            // Load existing project json strongly-typed
            ProjectJson root;
            var get = await fsApi.GetAsync(token, projectJsonPath);
            if (get.Code == 200 && get.Data is { IsDir: false })
            {
                var dl = await fsApi.DownloadAsync(token, projectJsonPath);
                if (dl.Code == 200 && dl.Content is { Length: > 0 })
                {
                    var text = Encoding.UTF8.GetString(dl.Content).TrimStart('\uFEFF');
                    try
                    {
                        root = JsonSerializer.Deserialize(text, AppJsonContext.Default.ProjectJson) ?? new ProjectJson();
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "Deserialize existing project json failed");
                        root = new ProjectJson();
                    }
                }
                else root = new ProjectJson();
            }
            else root = new ProjectJson();

            // Merge episodes
            foreach (var ep in toUpload)
            {
                root.Episodes[ep.Number.ToString()] = new EpisodeInfo { Status = ep.Status };
            }

            // Order desc by numeric key
            root.Episodes = root.Episodes
                .OrderByDescending(kv => int.TryParse(kv.Key, out var n) ? n : 0)
                .ToDictionary(k => k.Key, v => v.Value);

            var jsonOut = JsonSerializer.Serialize(root, AppJsonContext.Default.ProjectJson);
            var bytesOut = Encoding.UTF8.GetBytes(jsonOut);
            var put = await fsApi.SafePutAsync(token, projectJsonPath, bytesOut);
            if (put.Code != 200) { Status = $"更新项目JSON失败: {put.Message}"; Logger.Warn("Update project json failed: {code} {msg}", put.Code, put.Message); return false; }

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
