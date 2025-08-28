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
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using NLog;
using Avalonia.Controls;
using Ursa.Controls;

namespace LabelPlus_Next.ViewModels;

public partial class UploadViewModel : ObservableObject
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    [ObservableProperty] private string? status;

    [ObservableProperty] private int uploadCompleted;
    [ObservableProperty] private int uploadTotal;
    [ObservableProperty] private string? currentUploadingPath;

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

    // Track whether current flow is uploading to existing project (must merge existing JSON)
    private bool _uploadToExistingProject;

    public event EventHandler? MetadataReady; // Raised after PendingEpisodes prepared
    public event EventHandler? OpenSettingsRequested; // Raised when settings should be shown
    public event EventHandler<IReadOnlyList<UploadViewModel>>? MultiMetadataReady; // New event for multiple VMs

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
        var folder = await _dialogs.PickFolderAsync("ѡ��Ҫ�ϴ����ļ���");
        if (string.IsNullOrEmpty(folder)) return;
        _uploadToExistingProject = true;
        LastSelectedFolderPath = folder;
        var folderName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(SelectedProject)) SelectedProject = folderName;
        if (!string.Equals(SelectedProject, folderName, StringComparison.Ordinal))
        {
            await _dialogs.ShowMessageAsync($"��ѡ�ļ�������{folderName}����Ŀ����Ŀ����{SelectedProject}����һ�£���ȷ�ϡ�");
            Status = "��Ŀ����һ��";
            Logger.Warn("Folder name mismatch: folder={folderName} selectedProject={selected}", folderName, SelectedProject);
            return;
        }
        PendingProjectName = SelectedProject;
        Status = $"��ѡ���ļ��У�{folderName}";
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
            Status = "δ�ܶ�ȡ�ļ�";
            Logger.Error(ex, "PickUploadFolderAsync prepare failed for {folder}", folder);
        }
    }

    private async Task PickUploadFilesAsync()
    {
        if (_dialogs is null) return;
        var files = await _dialogs.PickFilesAsync("ѡ��Ҫ�ϴ����ļ����ɶ�ѡ��");
        if (files is null || files.Count == 0) return;
        _uploadToExistingProject = true;

        // Infer project from SelectedProject or parent folder name of first file
        var first = files[0];
        var parentFolder = Path.GetFileName(Path.GetDirectoryName(first) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(SelectedProject)) SelectedProject = parentFolder;
        if (!string.IsNullOrWhiteSpace(SelectedProject) && !string.Equals(SelectedProject, parentFolder, StringComparison.Ordinal))
        {
            await _dialogs.ShowMessageAsync($"��ѡ�ļ������ļ�������{parentFolder}����Ŀ����Ŀ����{SelectedProject}����һ�£���ȷ�ϡ�");
            Status = "��Ŀ����һ��";
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
                ep = new EpisodeEntry { Number = num, Status = "����" };
                byEpisode[num] = ep;
            }
            ep.LocalFiles.Add(path);
            // upgrade status depending on ext
            var ext = Path.GetExtension(path);
            if (ext.Equals(".psd", StringComparison.OrdinalIgnoreCase)) ep.Status = PickHigherStatus(ep.Status, "Ƕ��");
            else if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                var lower = Path.GetFileName(path).ToLowerInvariant();
                ep.Status = PickHigherStatus(ep.Status, lower.Contains("У��") || lower.Contains("У�") || lower.Contains("check") ? "У��" : "����");
            }
        }

        PendingEpisodes.Clear();
        foreach (var ep in byEpisode.Values.OrderByDescending(e => e.Number)) PendingEpisodes.Add(ep);
        LastSelectedFolderPath = Path.GetDirectoryName(first); // remember base folder
        Status = $"��ѡ���ļ���{files.Count} ��";
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
                var pickedMany = await _dialogs.PickFoldersAsync("ѡ��Ҫ�ϴ��Ļ����ļ��У��ɶ�ѡ��");
                if (pickedMany is { Count: > 0 }) candidateFolders.AddRange(pickedMany);
                else
                {
                    var pickedSingle = await _dialogs.PickFolderAsync("ѡ��Ҫ�ϴ����ļ���");
                    if (!string.IsNullOrEmpty(pickedSingle)) candidateFolders.Add(pickedSingle);
                }
            }

            if (candidateFolders.Count == 0)
            {
                Status = "����ѡ���ϴ��ļ���";
                Logger.Warn("AddProject aborted: no valid folder.");
                return;
            }

            // Login once and refresh aggregate for map
            var us = await LoadUploadSettingsAsync();
            if (us is null || string.IsNullOrWhiteSpace(us.BaseUrl)) { Status = "δ���÷�������ַ"; Logger.Warn("No server baseUrl."); return; }
            var baseUrl = us.BaseUrl!.TrimEnd('/');
            var auth = new AuthApi(baseUrl);
            var login = await auth.LoginAsync(us.Username ?? string.Empty, us.Password ?? string.Empty);
            if (login.Code != 200 || string.IsNullOrWhiteSpace(login.Data?.Token)) { Status = $"��¼ʧ��: {login.Code} {login.Message}"; Logger.Warn("Login failed: {code} {msg}", login.Code, login.Message); return; }
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
                    await MessageBox.ShowAsync($"�ۺ��嵥���Ѵ�����Ŀ��{projectName}��������ʹ�á��ϴ�����Ŀ���Ա����ͻ��", "��ʾ", MessageBoxIcon.Information, MessageBoxButton.OK);
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
                    await MessageBox.ShowAsync("��⵽��Զ����Ŀ������ͬ����������ʹ�á��ϴ�����Ŀ�������Ա��⸲�ǡ�", "��ʾ", MessageBoxIcon.Warning, MessageBoxButton.OK);
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
                        await _dialogs!.ShowMessageAsync($"�ۺ��嵥���Ѵ�����Ŀ��{projectName}������������");
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
                        await _dialogs!.ShowMessageAsync($"��Ŀ��{projectName}��������Զ����ͬ����������������ʹ�á��ϴ�����Ŀ����");
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
                Status = "û�п��ϴ�����Ŀ";
                return;
            }

            MultiMetadataReady?.Invoke(this, list);
        }
        catch (Exception ex)
        {
            Status = $"������Ŀʧ��: {ex.Message}";
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
                            if (TryParseEpisodeNumber(prop.Name, out var n)) set.Add(n);
                    }
                    else if (doc.RootElement.TryGetProperty("��Ŀ", out var items) && items.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in items.EnumerateObject())
                            if (TryParseEpisodeNumber(prop.Name, out var n)) set.Add(n);
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
            "����" => 4,
            "Ƕ��" => 3,
            "У��" => 2,
            "����" => 1,
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
                        var ep = new EpisodeEntry { Number = num, Status = "����" };
                        ep.LocalFiles.Add(entry);
                        map[num] = ep;
                    }
                    else
                    {
                        if (!existing.LocalFiles.Contains(entry, StringComparer.OrdinalIgnoreCase))
                            existing.LocalFiles.Add(entry);
                        existing.Status = PickHigherStatus(existing.Status, "����");
                    }
                }
            }
        }

        // Robustness: any txt file with episode number in its file name marks that episode as ���� (lower priority than У��)
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
                    epEntry = new EpisodeEntry { Number = num, Status = "����" };
                    map[num] = epEntry;
                }
                if (!epEntry.LocalFiles.Contains(txt, StringComparer.OrdinalIgnoreCase))
                    epEntry.LocalFiles.Add(txt);

                // Determine candidate status: prefer У�� if keywords, else ����
                var lower = nameWithoutExt.ToLowerInvariant();
                var candidate = (lower.Contains("У��") || lower.Contains("У�") || lower.Contains("check")) ? "У��" : "����";
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
        bool hasArchive = files.Any(f => new[] { ".zip", ".7z", ".rar" }.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        bool hasTxt = files.Any(f => string.Equals(Path.GetExtension(f), ".txt", StringComparison.OrdinalIgnoreCase));
        bool hasPsd = files.Any(f => string.Equals(Path.GetExtension(f), ".psd", StringComparison.OrdinalIgnoreCase));
        bool hasCheckTxt = files.Any(f => string.Equals(Path.GetExtension(f), ".txt", StringComparison.OrdinalIgnoreCase) &&
                                           ContainsAny(Path.GetFileNameWithoutExtension(f), new[] { "check", "У��", "У��" }));
        string status = hasPsd ? "Ƕ��" : (hasCheckTxt ? "У��" : (hasTxt ? "����" : (hasArchive ? "����" : "����")));
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
            ['��']=0,['һ']=1,['��']=2,['��']=2,['��']=3,['��']=4,['��']=5,['��']=6,['��']=7,['��']=8,['��']=9,
            ['ʮ']=10,['��']=100,['ǧ']=1000,['��']=0
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
            Status = "����ˢ����Ŀ�б�...";
            var us = await LoadUploadSettingsAsync();
            if (us is null || string.IsNullOrWhiteSpace(us.BaseUrl))
            {
                Status = "δ���÷�������ַ";
                Logger.Warn("Refresh aborted: missing baseUrl.");
                return;
            }

            var baseUrl = us.BaseUrl!.TrimEnd('/');
            var filePath = "/project.json";

            var auth = new AuthApi(baseUrl);
            var login = await auth.LoginAsync(us.Username ?? string.Empty, us.Password ?? string.Empty);
            if (login.Code != 200 || string.IsNullOrWhiteSpace(login.Data?.Token))
            {
                Status = $"��¼ʧ��: {login.Code} {login.Message}";
                Logger.Warn("Refresh login failed: {code} {msg}", login.Code, login.Message);
                return;
            }
            var token = login.Data!.Token!;

            var fsApi = new FileSystemApi(baseUrl);
            var result = await fsApi.DownloadAsync(token, filePath);
            if (result.Code != 200 || result.Content is null)
            {
                Status = $"����ʧ��: {result.Code} {result.Message}";
                Logger.Warn("Download project.json failed: {code} {msg}", result.Code, result.Message);
                return;
            }

            var bytes = result.Content;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) bytes = bytes[3..];
            var json = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Trim();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Object)
            {
                Status = "�嵥��ʽ����ȱ�� projects��";
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

            Status = $"�Ѽ��� {Projects.Count} ����Ŀ";
            Logger.Info("Projects loaded: {count}", Projects.Count);
        }
        catch (Exception ex)
        {
            Status = $"ˢ��ʧ��: {ex.Message}";
            Logger.Error(ex, "RefreshAsync failed.");
        }
    }

    public async Task<bool> UploadPendingAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(PendingProjectName)) { Status = "����Ŀ"; Logger.Warn("Upload aborted: no project name."); return false; }
            var us = await LoadUploadSettingsAsync();
            if (us is null || string.IsNullOrWhiteSpace(us.BaseUrl)) { Status = "δ���÷�������ַ"; Logger.Warn("Upload aborted: no baseUrl."); return false; }

            var toUpload = PendingEpisodes.Where(e => e.Include).ToList();
            var episodesCount = toUpload.Count;
            var plannedFilesCount = toUpload.Sum(e => e.LocalFiles.Count);
            var includeAggregate = !_uploadToExistingProject; // only for new project
            int preSteps = 0
                + 1  // ��¼
                + 1  // ������ĿĿ¼
                + episodesCount // ��������Ŀ¼
                + plannedFilesCount // ��ȡ��滮�ļ�
                + (includeAggregate ? 2 : 0) // ����/�ϴ����嵥
                + 1  // ������ĿJSON
                + 1  // �ϲ�JSON
                + 1; // �ϴ���ĿJSON
            int uploadSteps = plannedFilesCount;
            int totalSteps = preSteps + uploadSteps;

            UploadCompleted = 0;
            UploadTotal = totalSteps;
            CurrentUploadingPath = null;
            void Step(string message, string? path = null)
            {
                Status = message;
                CurrentUploadingPath = path;
                UploadCompleted = Math.Min(UploadCompleted + 1, UploadTotal);
            }

            Status = "��¼��...";
            var baseUrl = us.BaseUrl!.TrimEnd('/');
            var auth = new AuthApi(baseUrl);
            var login = await auth.LoginAsync(us.Username ?? string.Empty, us.Password ?? string.Empty);
            if (login.Code != 200 || string.IsNullOrWhiteSpace(login.Data?.Token)) { Status = $"��¼ʧ��: {login.Code} {login.Message}"; Logger.Warn("Upload login failed: {code} {msg}", login.Code, login.Message); return false; }
            Step("��¼���");
            var token = login.Data!.Token!;
            var fsApi = new FileSystemApi(baseUrl);

            var projectDir = "/" + PendingProjectName!.Trim('/') + "/";
            var mkProj = await fsApi.MkdirAsync(token, projectDir);
            if (mkProj.Code is >= 400 and < 500) { Status = $"����Ŀ¼ʧ��: {mkProj.Message}"; return false; }
            Step("�Ѵ�����ĿĿ¼", projectDir);

            foreach (var ep in toUpload)
            {
                var epDir = projectDir + ep.Number + "/";
                var mk = await fsApi.MkdirAsync(token, epDir);
                if (mk.Code is >= 400 and < 500) { Status = $"��������Ŀ¼ʧ��: {mk.Message}"; return false; }
                Step($"Ŀ¼����: {ep.Number}", epDir);
            }

            // Build items and map paths; count planning steps
            var items = new List<FileUploadItem>();
            var resultMap = new Dictionary<int, (string? source, string? translate, string? typeset)>();
            foreach (var ep in toUpload)
            {
                var epDir = projectDir + ep.Number + "/";
                string? sourcePath = null; string? translatePath = null; string? typesetPath = null;
                foreach (var file in ep.LocalFiles)
                {
                    if (!File.Exists(file)) { Step("����ȱʧ�ļ�", file); continue; }
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
                        if (lower.Contains("У��") || lower.Contains("У�") || lower.Contains("check")) translatePath = remote;
                    }
                    else if (ext.Equals(".psd", StringComparison.OrdinalIgnoreCase))
                        typesetPath ??= remote;
                    Step("�ѹ滮�ļ�", remote);
                }
                resultMap[ep.Number] = (sourcePath, translatePath, typesetPath);
            }

            // Update aggregate /project.json if creating a new project
            string projectJsonPath = ProjectMap.TryGetValue(PendingProjectName, out var pj) && !string.IsNullOrWhiteSpace(pj)
                ? pj
                : projectDir + PendingProjectName + "_project.json";

            if (includeAggregate)
            {
                const string aggregatePath = "/project.json";
                var agg = await fsApi.DownloadAsync(token, aggregatePath);
                if (agg.Code != 200 || agg.Content is null)
                {
                    Status = $"���ؾۺ��嵥ʧ��: {agg.Code} {agg.Message}";
                    return false;
                }
                Step("�����ؾۺ��嵥", aggregatePath);

                Dictionary<string, string> map = new();
                try
                {
                    using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(agg.Content).TrimStart('\uFEFF'));
                    if (!doc.RootElement.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Object)
                    {
                        Status = "�ۺ��嵥��ʽ����"; return false;
                    }
                    foreach (var prop in projects.EnumerateObject())
                    {
                        map[prop.Name] = prop.Value.GetString() ?? string.Empty;
                    }
                }
                catch
                {
                    Status = "�����ۺ��嵥ʧ��"; return false;
                }

                // merge
                map[PendingProjectName] = projectJsonPath;
                var aggObj = new AggregateProjects { Projects = map };
                var aggOut = System.Text.Json.JsonSerializer.Serialize(aggObj, AppJsonContext.Default.AggregateProjects);
                var putAgg = await fsApi.SafePutAsync(token, aggregatePath, Encoding.UTF8.GetBytes(aggOut));
                if (putAgg.Code != 200) { Status = $"���¾ۺ��嵥ʧ��: {putAgg.Message}"; return false; }
                Step("�ۺ��嵥�Ѹ���", aggregatePath);
            }

            // Download and merge per-project JSON
            ProjectCn cn;
            bool requireMergeExisting = _uploadToExistingProject;
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
                        if (requireMergeExisting) { Status = "�޷���ȡ��ĿJSON����ȡ��"; return false; }
                        cn = new ProjectCn();
                    }
                }
                else
                {
                    if (requireMergeExisting) { Status = "δ��������ĿJSON����ȡ��"; return false; }
                    cn = new ProjectCn();
                }
            }
            else
            {
                if (requireMergeExisting) { Status = "δ�ҵ���ĿJSON����ȡ��"; return false; }
                cn = new ProjectCn();
            }
            Step("�ѻ�ȡ��ĿJSON", projectJsonPath);

            foreach (var ep in toUpload)
            {
                resultMap.TryGetValue(ep.Number, out var pmap);
                var key = ep.Number.ToString("00");
                cn.Items[key] = new EpisodeCn { Status = ep.Status, SourcePath = pmap.source, TranslatePath = pmap.translate, TypesetPath = pmap.typeset };
            }
            cn.Items = cn.Items.OrderByDescending(kv => int.TryParse(kv.Key, out var n) ? n : 0).ToDictionary(k => k.Key, v => v.Value);
            Step("JSON �ϲ����", projectJsonPath);

            var ctx = new AppJsonContext(new JsonSerializerOptions(AppJsonContext.Default.Options)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                WriteIndented = true
            });
            var jsonOut = JsonSerializer.Serialize(cn, ctx.ProjectCn);
            var bytesOut = Encoding.UTF8.GetBytes(jsonOut);
            var putJson = await fsApi.SafePutAsync(token, projectJsonPath, bytesOut);
            if (putJson.Code != 200) { Status = $"������ĿJSONʧ��: {putJson.Message}"; return false; }
            Step("JSON ���ϴ�", projectJsonPath);

            // Offset for upload progress
            var offset = UploadCompleted;
            var progress = new Progress<UploadProgress>(p =>
            {
                UploadTotal = totalSteps; // keep total constant
                UploadCompleted = Math.Min(offset + p.Completed, UploadTotal);
                CurrentUploadingPath = p.CurrentRemotePath;
                Status = $"�ϴ��� {UploadCompleted}/{UploadTotal}: {CurrentUploadingPath}";
            });

            var res = await fsApi.PutManyAsync(token, items, progress, maxConcurrency: 6, asTask: false);
            if (res.Any(r => r.Code != 200))
            {
                var first = res.FirstOrDefault(r => r.Code != 200);
                Status = $"�ϴ�ʧ��: {first?.Message}";
                return false;
            }

            Status = "�ϴ����";
            Logger.Info("Upload completed successfully.");
            await RefreshAsync();
            return true;
        }
        catch (Exception ex)
        {
            Status = $"�ϴ��쳣: {ex.Message}";
            Logger.Error(ex, "UploadPendingAsync failed.");
            return false;
        }
    }
}
