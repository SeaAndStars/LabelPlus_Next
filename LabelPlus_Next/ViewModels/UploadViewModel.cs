using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Lang;
using LabelPlus_Next.Models;
using LabelPlus_Next.Serialization;
using LabelPlus_Next.Services;
using LabelPlus_Next.Services.Api;
using LabelPlus_Next.Views.Windows;
using NLog;
using Ursa.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace LabelPlus_Next.ViewModels;

public partial class UploadViewModel : ObservableObject
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly ISettingsService _settingsService;
    private readonly IFileDialogService _dialogs;

    private bool _uploadToExistingProject;

    [ObservableProperty] private string? currentUploadingPath;
    [ObservableProperty] private string? searchText;
    [ObservableProperty] private string? selectedProject;
    [ObservableProperty] private string? status;
    [ObservableProperty] private int uploadCompleted;
    [ObservableProperty] private int uploadTotal;
    [ObservableProperty] private bool isBusy;

    /// <summary>Hide the progress bar until an upload actually has steps/paths.</summary>
    public bool HasUploadProgress => UploadTotal > 0 || !string.IsNullOrEmpty(CurrentUploadingPath);
    partial void OnUploadTotalChanged(int value) => OnPropertyChanged(nameof(HasUploadProgress));
    partial void OnCurrentUploadingPathChanged(string? value) => OnPropertyChanged(nameof(HasUploadProgress));

    [ObservableProperty] private bool isTankobonFallback;
    [ObservableProperty] private int? tankobonVolumeNumber;
    [ObservableProperty] private bool tankobonAcknowledged;

    public ObservableCollection<string> Suggestions { get; } = new();
    public ObservableCollection<string> Projects { get; } = new();

    public IAsyncRelayCommand AddProjectCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand PickUploadFolderCommand { get; }
    public IAsyncRelayCommand PickUploadFilesCommand { get; }
    public IAsyncRelayCommand OpenSettingsCommand { get; }
    public IRelayCommand CancelMetaCommand { get; }
    public IAsyncRelayCommand ConfirmMetaCommand { get; }

    public Dictionary<string, string> ProjectMap { get; } = new();

    public string? LastSelectedFolderPath { get; set; }
    public ObservableCollection<EpisodeEntry> PendingEpisodes { get; } = new();

    private readonly string? _uploadSettingsPath;
    private string? _pendingProjectName;
    public string? PendingProjectName
    {
        get => _pendingProjectName;
        set => SetProperty(ref _pendingProjectName, value);
    }

    public bool HasDuplicates { get; private set; }

    public event EventHandler? MetadataReady;
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler<IReadOnlyList<UploadViewModel>>? MultiMetadataReady;
    public event EventHandler<bool>? MetaWindowCloseRequested;

    public UploadViewModel(ISettingsService settingsService, IFileDialogService dialogs) : this(settingsService, dialogs, true) { }

    public UploadViewModel(ISettingsService settingsService, IFileDialogService dialogs, bool autoRefresh)
        : this(settingsService, dialogs, autoRefresh, null) { }

    public UploadViewModel(ISettingsService settingsService, IFileDialogService dialogs, bool autoRefresh, string? uploadSettingsPath)
    {
        _settingsService = settingsService;
        _dialogs = dialogs;
        _uploadSettingsPath = string.IsNullOrWhiteSpace(uploadSettingsPath) ? null : uploadSettingsPath;
        AddProjectCommand = new AsyncRelayCommand(AddProjectAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        PickUploadFolderCommand = new AsyncRelayCommand(PickUploadFolderAsync);
        PickUploadFilesCommand = new AsyncRelayCommand(PickUploadFilesAsync);
        OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
        CancelMetaCommand = new RelayCommand(() => MetaWindowCloseRequested?.Invoke(this, false));
        ConfirmMetaCommand = new AsyncRelayCommand(ConfirmMetaAsync);
        if (autoRefresh) _ = RefreshAsync();
    }

    private async Task<UploadSettings?> LoadUploadSettingsAsync()
    {
        try
        {
            var settings = await UploadSettingsStore.LoadAsync(_uploadSettingsPath);
            if (settings is not null) return settings;

            var def = new UploadSettings { BaseUrl = "https://alist1.seastarss.cn" };
            await UploadSettingsStore.SaveAsync(def, _uploadSettingsPath);
            return def;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "LoadUploadSettingsAsync failed");
            return null;
        }
    }

    private record ApiContext(string BaseUrl, string Token);

    private async Task<ApiContext?> GetApiContextAsync()
    {
        var us = await LoadUploadSettingsAsync();
        if (us is null || string.IsNullOrWhiteSpace(us.BaseUrl))
        {
            Status = UiText.Tr("未配置服务器地址");
            OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
            return null;
        }
        if (string.IsNullOrWhiteSpace(us.Username) || string.IsNullOrWhiteSpace(us.Password))
        {
            Status = UiText.Tr("未配置账号或密码");
            OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
            return null;
        }
        var baseUrl = us.BaseUrl!.TrimEnd('/');
        var auth = new AuthApi(baseUrl);
        var login = await auth.LoginAsync(us.Username!, us.Password!);
        if (login.Code != 200 || string.IsNullOrWhiteSpace(login.Data?.Token))
        {
            Status = UiText.Tr("登录失败: {0} {1}", login.Code, login.Message);
            return null;
        }
        return new ApiContext(baseUrl, login.Data!.Token!);
    }

    private async Task PickUploadFolderAsync()
    {
        var folder = await _dialogs.PickFolderAsync(UiText.Tr("选择要上传的文件夹"));
        if (string.IsNullOrWhiteSpace(folder)) return;
        _uploadToExistingProject = true;
        LastSelectedFolderPath = folder;
        var folderName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(SelectedProject)) SelectedProject = folderName;
        if (!string.Equals(SelectedProject, folderName, StringComparison.Ordinal))
        {
            await _dialogs.ShowMessageAsync(UiText.Tr("所选文件夹名“{0}”与目标项目名“{1}”不一致，请确认。", folderName, SelectedProject));
            Status = UiText.Tr("项目名不一致");
            Logger.Warn("Folder name mismatch: folder={folderName} selectedProject={selected}", folderName, SelectedProject);
            return;
        }
        PendingProjectName = SelectedProject;
        Status = UiText.Tr("已选择文件夹：{0}", folderName);
        Logger.Info("Picked upload folder: {folder}", folder);

        try
        {
            // Prepare episodes from local folder and open metadata window
            var episodes = ScanEpisodes(folder, out var isTanko, out var volNum);
            // 设置默认负责人
            var uploader = await GetUploaderNameAsync();
            foreach (var e in episodes)
            {
                if (string.IsNullOrWhiteSpace(e.Owner)) e.Owner = uploader;
            }
            PendingEpisodes.Clear();
            foreach (var e in episodes.OrderByDescending(e => e.Number)) PendingEpisodes.Add(e);
            HasDuplicates = false; // duplicates will be handled inside UploadPendingAsync merge stage
            IsTankobonFallback = isTanko;
            TankobonVolumeNumber = volNum;
            BuildMetaTree();
            MetadataReady?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "PickUploadFolderAsync failed");
            Status = UiText.Tr("未能读取文件");
        }
    }

    private async Task PickUploadFilesAsync()
    {
        var files = await _dialogs.PickFilesAsync(UiText.Tr("选择要上传的文件（可多选）"));
        if (files is null || files.Count == 0) return;
        _uploadToExistingProject = true;

        // Infer project from SelectedProject or parent folder name of first file
        var first = files[0];
        var parentFolder = Path.GetFileName(Path.GetDirectoryName(first) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(SelectedProject)) SelectedProject = parentFolder;
        if (!string.IsNullOrWhiteSpace(SelectedProject) && !string.Equals(SelectedProject, parentFolder, StringComparison.Ordinal))
        {
            await _dialogs.ShowMessageAsync(UiText.Tr("所选文件所在文件夹名“{0}”与目标项目名“{1}”不一致，请确认。", parentFolder, SelectedProject));
            Status = UiText.Tr("项目名不一致");
            Logger.Warn("PickUploadFilesAsync: parent folder mismatch. parent={parentFolder} selected={selected}", parentFolder, SelectedProject);
            return;
        }
        PendingProjectName = SelectedProject;

        // Group files by episode number inferred from file/folder name
        var byEpisode = new Dictionary<int, EpisodeEntry>();
        var bySpecial = new Dictionary<int, EpisodeEntry>();
        var specialBucket = new EpisodeEntry { Number = 0, IsSpecial = true, Status = "立项" };
        // 新增：杂项
        var miscBucket = new EpisodeEntry { Number = 0, IsMisc = true, Status = "立项", Display = "杂项", Kind = "杂项" };
        foreach (var path in files)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (IsSpecialName(name))
            {
                EpisodeEntry target;
                if (TryParseSpecialNumber(name, out var spNum, out _) && spNum > 0)
                {
                    if (!bySpecial.TryGetValue(spNum, out var t) || t is null)
                    {
                        t = new EpisodeEntry { Number = spNum, Status = "立项", IsSpecial = true };
                        bySpecial[spNum] = t;
                    }
                    target = bySpecial[spNum];
                }
                else target = specialBucket;
                target.LocalFiles.Add(path);
                UpgradeStatusFromFile(ref target, path);
                continue;
            }
            var isVolLocal = false;
            if (!TryParseEpisodeNumber(name, out var num))
            {
                // fallback: try parent folder
                var folder = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
                if (IsSpecialName(folder))
                {
                    EpisodeEntry target2;
                    if (TryParseSpecialNumber(folder, out var spNum2, out _) && spNum2 > 0)
                    {
                        if (!bySpecial.TryGetValue(spNum2, out var t2) || t2 is null)
                        {
                            t2 = new EpisodeEntry { Number = spNum2, Status = "立项", IsSpecial = true };
                            bySpecial[spNum2] = t2;
                        }
                        target2 = bySpecial[spNum2];
                    }
                    else target2 = specialBucket;
                    target2.LocalFiles.Add(path);
                    UpgradeStatusFromFile(ref target2, path);
                    continue;
                }
                if (!TryParseEpisodeNumber(folder, out num))
                {
                    var (kind, n2) = ParseEpisodeOrVolume(name);
                    if (kind == NameKind.Special)
                    {
                        EpisodeEntry t;
                        if (TryParseSpecialNumber(name, out var sn, out _) && sn > 0)
                        {
                            if (!bySpecial.TryGetValue(sn, out var ts) || ts is null)
                            {
                                ts = new EpisodeEntry { Number = sn, Status = "立项", IsSpecial = true };
                                bySpecial[sn] = ts;
                            }
                            t = bySpecial[sn];
                        }
                        else t = specialBucket;
                        t.LocalFiles.Add(path);
                        UpgradeStatusFromFile(ref t, path);
                        continue;
                    }
                    if (kind == NameKind.Volume) isVolLocal = true;
                    if (n2 <= 0) { miscBucket.LocalFiles.Add(path); UpgradeStatusFromFile(ref miscBucket, path); continue; }
                    num = n2;
                }
            }
            if (!byEpisode.TryGetValue(num, out var ep))
            {
                ep = new EpisodeEntry { Number = num, Status = "立项", IsVolume = isVolLocal };
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

        // 计算范围+默认负责人
        var uploaderName = await GetUploaderNameAsync();
        foreach (var ep in byEpisode.Values)
        {
            var range = DetectRangeFromFiles(ep.LocalFiles);
            ep.RangeStart = range.lo; ep.RangeEnd = range.hi; ep.RangeDisplay = range.disp;
            if (string.IsNullOrWhiteSpace(ep.Owner)) ep.Owner = uploaderName;
        }
        foreach (var sp in bySpecial.Values)
        {
            var range = DetectRangeFromFiles(sp.LocalFiles);
            sp.RangeStart = range.lo; sp.RangeEnd = range.hi; sp.RangeDisplay = range.disp;
            if (string.IsNullOrWhiteSpace(sp.Owner)) sp.Owner = uploaderName;
            if (string.IsNullOrWhiteSpace(sp.Display)) sp.Display = $"番外{sp.Number:00}";
        }
        if (specialBucket.LocalFiles.Count > 0)
        {
            var range = DetectRangeFromFiles(specialBucket.LocalFiles);
            specialBucket.RangeStart = range.lo; specialBucket.RangeEnd = range.hi; specialBucket.RangeDisplay = range.disp;
            if (string.IsNullOrWhiteSpace(specialBucket.Owner)) specialBucket.Owner = uploaderName;
        }
        if (miscBucket.LocalFiles.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(miscBucket.Owner)) miscBucket.Owner = uploaderName;
        }

        PendingEpisodes.Clear();
        foreach (var ep in byEpisode.Values.OrderByDescending(e => e.Number)) PendingEpisodes.Add(ep);
        foreach (var sp in bySpecial.Values.OrderByDescending(e => e.Number)) PendingEpisodes.Add(sp);
        if (specialBucket.LocalFiles.Count > 0) PendingEpisodes.Add(specialBucket);
        if (miscBucket.LocalFiles.Count > 0) PendingEpisodes.Add(miscBucket);
        LastSelectedFolderPath = Path.GetDirectoryName(first);
        Status = UiText.Tr("已选择文件：{0} 个", files.Count);
        HasDuplicates = false;
        BuildMetaTree();
        MetadataReady?.Invoke(this, EventArgs.Empty);
    }

    private Task OpenSettingsAsync()
    {
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
                candidateFolders.Add(LastSelectedFolderPath!);
            else
            {
                // let user pick multiple episode folders
                var pickedMany = await _dialogs.PickFoldersAsync(UiText.Tr("选择要上传的话数文件夹（可多选）"));
                if (pickedMany is { Count: > 0 }) candidateFolders.AddRange(pickedMany);
                else
                {
                    var pickedSingle = await _dialogs.PickFolderAsync(UiText.Tr("选择要上传的文件夹"));
                    if (!string.IsNullOrEmpty(pickedSingle)) candidateFolders.Add(pickedSingle);
                }
            }

            if (candidateFolders.Count == 0)
            {
                Status = UiText.Tr("请先选择上传文件夹");
                Logger.Warn("AddProject aborted: no valid folder.");
                return;
            }

            var ctx = await GetApiContextAsync();
            if (ctx is null) return;
            var fsApi = new FileSystemApi(ctx.BaseUrl);

            if (candidateFolders.Count == 1)
            {
                // Keep original single-folder flow
                var folder = candidateFolders[0];
                var projectName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                PendingProjectName = projectName;

                if (ProjectMap.ContainsKey(projectName))
                {
                    Logger.Warn("Aggregate already contains project {name}", projectName);
                    await MessageBox.ShowAsync(UiText.Tr("聚合清单中已存在项目“{0}”。建议使用‘上传到项目’以避免冲突。", projectName), UiText.Tr("提示"), MessageBoxIcon.Information);
                    return;
                }

                // Episode duplicate check (optional)
                var projectJsonPath = ProjectMap.TryGetValue(projectName, out var path) && !string.IsNullOrWhiteSpace(path)
                    ? path
                    : $"/{projectName}/{projectName}_project.json";
                var existingEpisodeNums = await GetExistingEpisodeNumbersAsync(fsApi, ctx.Token, projectJsonPath);
                var episodes = ScanEpisodes(folder, out var isTankoSingle, out var volNumSingle);
                IsTankobonFallback = isTankoSingle;
                TankobonVolumeNumber = volNumSingle;
                HasDuplicates = episodes.Any(e => existingEpisodeNums.Contains(e.Number));
                if (HasDuplicates)
                {
                    await MessageBox.ShowAsync(UiText.Tr("检测到与远端项目存在相同话数。建议使用‘上传到项目’功能以避免覆盖。"), UiText.Tr("提示"), MessageBoxIcon.Warning);
                    return;
                }
                PendingEpisodes.Clear();
                foreach (var e in episodes.OrderByDescending(e => e.Number)) PendingEpisodes.Add(e);
                BuildMetaTree();
                MetadataReady?.Invoke(this, EventArgs.Empty);

                // Build metadata tree for MVVM binding
                BuildMetaTree();
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
                        await _dialogs.ShowMessageAsync(UiText.Tr("聚合清单中已存在项目“{0}”，已跳过。", projectName));
                        continue;
                    }
                    var projectJsonPath = ProjectMap.TryGetValue(projectName, out var path) && !string.IsNullOrWhiteSpace(path)
                        ? path
                        : $"/{projectName}/{projectName}_project.json";
                    var existingEpisodeNums = await GetExistingEpisodeNumbersAsync(fsApi, ctx.Token, projectJsonPath);
                    var episodes = ScanEpisodes(folder, out var isTanko, out var volNum).OrderByDescending(e => e.Number).ToList();
                    var dup = episodes.Any(e => existingEpisodeNums.Contains(e.Number));
                    if (dup)
                    {
                        await _dialogs.ShowMessageAsync(UiText.Tr("项目“{0}”存在与远端相同话数，已跳过。请使用‘上传到项目’。", projectName));
                        continue;
                    }

                    var vm = App.Services.GetRequiredService<UploadViewModel>();
                    foreach (var kv in ProjectMap) vm.ProjectMap[kv.Key] = kv.Value;
                    vm.PendingProjectName = projectName;
                    vm.LastSelectedFolderPath = folder;
                    vm.PendingEpisodes.Clear();
                    foreach (var e in episodes) vm.PendingEpisodes.Add(e);
                    vm.HasDuplicates = false;
                    vm.IsTankobonFallback = isTanko;
                    vm.TankobonVolumeNumber = volNum;
                    list.Add(vm);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Prepare child vm failed for {folder}", folder);
                }
            }

            if (list.Count == 0)
            {
                Status = UiText.Tr("没有可上传的项目");
                return;
            }

            MultiMetadataReady?.Invoke(this, list);
        }
        catch (UnauthorizedAccessException ex)
        {
            Status = UiText.Tr("无法访问所选文件夹，请检查权限。");
            Logger.Warn(ex, "AddProjectAsync access denied");
        }
        catch (IOException ex)
        {
            Status = UiText.Tr("读取所选文件夹失败: {0}", ex.Message);
            Logger.Warn(ex, "AddProjectAsync IO failure");
        }
        catch (Exception ex)
        {
            Status = UiText.Tr("新增项目失败: {0}", ex.Message);
            Logger.Error(ex, "AddProjectAsync failed");
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
                    else if (doc.RootElement.TryGetProperty("项目", out var items) && items.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in items.EnumerateObject())
                            if (TryParseEpisodeNumber(prop.Name, out var n)) set.Add(n);
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            Logger.Warn(ex, "Failed to parse existing episode json at {path}", projectJsonPath);
        }
        catch (DecoderFallbackException ex)
        {
            Logger.Warn(ex, "Failed to decode existing episode bytes at {path}", projectJsonPath);
        }
        catch (HttpRequestException ex)
        {
            Logger.Warn(ex, "HTTP error when retrieving existing episode keys at {path}", projectJsonPath);
        }
        catch (TaskCanceledException ex)
        {
            Logger.Warn(ex, "Timeout retrieving existing episode keys at {path}", projectJsonPath);
        }
        return set;
    }

    private static int StatusRank(string status) => status switch
    {
        "发布" => 4,
        "嵌字" => 3,
        "校对" => 2,
        "翻译" => 1,
        _ => 0
    };

    private static string PickHigherStatus(string a, string b) => StatusRank(b) > StatusRank(a) ? b : a;

    private async Task<string?> GetUploaderNameAsync()
    {
        var us = await LoadUploadSettingsAsync();
        return !string.IsNullOrWhiteSpace(us?.Username) ? us!.Username! : Environment.UserName;
    }

    private static bool IsSpecialName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var needles = new[] { "番外", "特別篇", "特别篇", "SP", "Special", "Extra", "外传", "外傳", "特典", "付录", "附录", "後記", "后记", "前日谈", "前日譚", "後日談", "后日谈", "前传", "前傳", "序章", "序", "终章", "終章", "Prologue", "Epilogue", "Omake", "Bonus" };
        return needles.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseSpecialNumber(string? name, out int number, out string? label)
    {
        number = 0; label = null;
        if (string.IsNullOrWhiteSpace(name)) return false;
        var s = name.Trim();
        if (!IsSpecialName(s)) return false;
        var m = Regex.Match(s, @"(?:番外|外传|外傳|特典|SP|S\.?P\.?|Special|Extra)\s*[-_#]?(?:第)?(?<num>\d{1,4}|[一二三四五六七八九十百千两〇零]+)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var v = m.Groups["num"].Value;
            if (int.TryParse(v, out var n) && n > 0) { number = n; return true; }
            var cn = ParseChineseNumeral(v); if (cn > 0) { number = cn; return true; }
        }
        m = Regex.Match(s, @"(?<num>\d{1,4}|[一二三四五六七八九十百千两〇零]+)\s*(?:番外|外传|外傳|特典|SP|S\.?P\.?|Special|Extra)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var v = m.Groups["num"].Value;
            if (int.TryParse(v, out var n) && n > 0) { number = n; return true; }
            var cn = ParseChineseNumeral(v); if (cn > 0) { number = cn; return true; }
        }
        var prelude = new[] { "前传", "前傳", "序", "序章", "Prologue" };
        var epilogue = new[] { "后传", "後傳", "终章", "終章", "Epilogue", "后日谈", "後日談" };
        if (prelude.Any(k => s.Contains(k, StringComparison.OrdinalIgnoreCase))) { label = "前传"; number = 0; return true; }
        if (epilogue.Any(k => s.Contains(k, StringComparison.OrdinalIgnoreCase))) { label = "后传"; number = 999; return true; }
        var dm = Regex.Match(s, @"(?<!\d)(\d{1,4})(?!\d)");
        if (dm.Success && int.TryParse(dm.Groups[1].Value, out var dn) && dn > 0) { number = dn; return true; }
        return true;
    }

    private static bool ContainsVolumeKeyword(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var keys = new[] { "卷", "巻", "vol", "vol.", "volume", "v", "v.", "单行本", "單行本", "tankobon" };
        return keys.Any(k => s.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private enum NameKind { Episode, Volume, Special, Unknown }

    private static (NameKind kind, int number) ParseEpisodeOrVolume(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (NameKind.Unknown, 0);
        var s = name.Trim();
        if (IsSpecialName(s)) return (NameKind.Special, 0);

        var volTailPattern = new Regex(@"(?:(?<a>\d{1,5})\s*[-_~～—–至到]\s*(?<b>\d{1,5})|(?<n>\d{1,5})|第\s*(?<cn>[一二三四五六七八九十百千两〇零]+))\s*(卷|巻|vol\.?|volume|v\.?|单行本|單行本|tankobon)", RegexOptions.IgnoreCase);
        var volTailMatch = volTailPattern.Match(s);
        if (volTailMatch.Success)
        {
            if (int.TryParse(volTailMatch.Groups["a"].Value, out var a) && int.TryParse(volTailMatch.Groups["b"].Value, out var b))
                return (NameKind.Volume, Math.Max(a, b));
            if (int.TryParse(volTailMatch.Groups["n"].Value, out var nnum) && nnum > 0)
                return (NameKind.Volume, nnum);
            var cn = volTailMatch.Groups["cn"].Value; var ncn = ParseChineseNumeral(cn); if (ncn > 0) return (NameKind.Volume, ncn);
        }

        s = Regex.Replace(s, @"\b(19|20)\d{2}[-_.]?(0?[1-9]|1[0-2])[-_.]?(0?[1-9]|[12]\d|3[01])\b", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(\d{3,4})[xX](\d{3,4})\b", " ", RegexOptions.IgnoreCase);
        var map = new (NameKind kind, string[] keys)[]
        {
            (NameKind.Volume, new[]{"卷","巻","vol","vol.","volume","v","v.","单行本","單行本","tankobon"}),
            (NameKind.Episode, new[]{"话","話","chap","chapter","ch","ep","episode","e","集","章","篇","part","act","story","回"}),
        };
        foreach (var (kind, keys) in map)
        {
            foreach (var k in keys)
            {
                var idx = s.IndexOf(k, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var tail = s[(idx + k.Length)..];
                    if (TryParseRangeNumber(tail, out var nRange) && nRange > 0) return (kind, nRange);
                    var mEp = Regex.Match(tail, @"^\s*(?:S\d+E(?<e>\d+)|(?<n>\d+)(?:\.\d+)?|第\s*(?<cn>[一二三四五六七八九十百千两〇零]+))", RegexOptions.IgnoreCase);
                    if (mEp.Success)
                    {
                        if (int.TryParse(mEp.Groups["e"].Value, out var se)) return (kind, se);
                        if (int.TryParse(mEp.Groups["n"].Value, out var nn) && nn > 0) return (kind, nn);
                        var ncn = ParseChineseNumeral(mEp.Groups["cn"].Value); if (ncn > 0) return (kind, ncn);
                    }
                    var digits = new string(tail.Where(char.IsDigit).ToArray());
                    if (int.TryParse(digits, out var n) && n > 0) return (kind, n);
                    var ncn2 = ParseChineseNumeral(tail); if (ncn2 > 0) return (kind, ncn2);
                }
            }
        }
        foreach (var (kind, keys) in map)
        {
            if (keys.Any(k => s.StartsWith(k, StringComparison.OrdinalIgnoreCase)))
            {
                var rem = s[keys.Max(k => s.StartsWith(k, StringComparison.OrdinalIgnoreCase) ? k.Length : 0)..];
                var digits = new string(rem.Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out var n) && n > 0) return (kind, n);
                var ncn = ParseChineseNumeral(rem); if (ncn > 0) return (kind, ncn);
            }
        }
        if (TryParseRangeNumber(s, out var numRange) && numRange > 0)
        {
            if (ContainsVolumeKeyword(name)) return (NameKind.Volume, numRange);
            return (NameKind.Episode, numRange);
        }
        if (int.TryParse(new string(s.Where(char.IsDigit).ToArray()), out var num) && num > 0) return (NameKind.Episode, num);
        var nn2 = ParseChineseNumeral(s); if (nn2 > 0) return (NameKind.Episode, nn2);
        return (NameKind.Unknown, 0);
    }

    private static bool TryParseEpisodeNumber(string name, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (ContainsVolumeKeyword(name)) return false;
        var m1 = Regex.Match(name, @"第\s*([0-9一二三四五六七八九十百千两〇零]+)\s*(话|話|集|章|篇|回)?", RegexOptions.IgnoreCase);
        if (m1.Success)
        {
            var grp = m1.Groups[1].Value;
            if (int.TryParse(grp, out var n1)) { number = n1; return number > 0; }
            number = ParseChineseNumeral(grp); if (number > 0) return true;
        }
        var m2 = Regex.Match(name, @"(?<![0-9\.])(\d{1,4})(?:\.\d+)?(?!\d|\.\d)\s*(话|話|集|章|篇|回)", RegexOptions.IgnoreCase);
        if (m2.Success && int.TryParse(m2.Groups[1].Value, out var n2) && n2 > 0) { number = n2; return true; }
        var mEp = Regex.Match(name, @"\b(?:S\d+E(?<e>\d+)|EP\s*(?<e2>\d+)|E\s*(?<e3>\d+))\b", RegexOptions.IgnoreCase);
        if (mEp.Success)
        {
            if (int.TryParse(mEp.Groups["e"].Value, out var se)) { number = se; return true; }
            if (int.TryParse(mEp.Groups["e2"].Value, out var e2)) { number = e2; return true; }
            if (int.TryParse(mEp.Groups["e3"].Value, out var e3)) { number = e3; return true; }
        }
        if (TryParseRangeNumber(name, out var nRange) && nRange > 0) { number = nRange; return true; }
        var s = Regex.Replace(name, @"\b(19|20)\d{2}[-_.]?(0?[1-9]|1[0-2])[-_.]?(0?[1-9]|[12]\d|3[01])\b", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(\d{3,4})[xX](\d{3,4})\b", " ", RegexOptions.IgnoreCase);
        var matches = Regex.Matches(s, @"(?<![0-9\.])(\d{1,4})(?:\.\d+)?(?!\d|\.\d)");
        if (matches.Count > 0 && int.TryParse(matches[^1].Groups[1].Value, out var nLast) && nLast > 0) { number = nLast; return true; }
        number = ParseChineseNumeral(name);
        return number > 0;
    }

    private static int TryPickNumberFromFiles(IEnumerable<string> files)
    {
        if (files is null) return 0;
        var volCounts = new Dictionary<int, int>();
        var epCounts = new Dictionary<int, int>();

        foreach (var f in files)
        {
            var name = Path.GetFileNameWithoutExtension(f) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) continue;

            // 先尝试按“卷”关键字解析卷号
            var (kind, num) = ParseEpisodeOrVolume(name);
            if (kind == NameKind.Volume && num > 0)
            {
                volCounts[num] = volCounts.TryGetValue(num, out var c) ? c + 1 : 1;
                continue;
            }

            // 再尝试解析“话/集/章/ep/ch”等关键字；避免把纯数字页码当作话号
            if (TryParseEpisodeNumber(name, out var n) && n > 0)
            {
                var hasKeyword = Regex.IsMatch(name, @"(话|話|章|篇|集|ep|episode|ch|chap)", RegexOptions.IgnoreCase);
                var digitsOnly = name.All(ch => char.IsDigit(ch) || ch == '_' || ch == '-' || ch == '.' || char.IsWhiteSpace(ch));
                if (hasKeyword || !digitsOnly)
                {
                    epCounts[n] = epCounts.TryGetValue(n, out var c2) ? c2 + 1 : 1;
                }
            }
        }

        static int PickMostLikely(Dictionary<int, int> counts)
            => counts.Count == 0 ? 0 : counts.OrderByDescending(kv => kv.Value).ThenByDescending(kv => kv.Key).First().Key;

        var vol = PickMostLikely(volCounts);
        if (vol > 0) return vol;
        var ep = PickMostLikely(epCounts);
        return ep > 0 ? ep : 0;
    }

    private static string BuildSubDir(EpisodeEntry ep)
    {
        if (ep.IsSpecial)
        {
            var n = TryPickNumberFromFiles(ep.LocalFiles);
            return n > 0 ? $"番外{n:00}" : "番外";
        }
        if (ep.IsMisc)
        {
            return "杂项";
        }
        if (ep.IsVolume)
        {
            var n = TryPickNumberFromFiles(ep.LocalFiles);
            var use = n > 0 ? n : ep.Number;
            return $"卷{use:00}";
        }
        if (ep.RangeStart.HasValue && ep.RangeEnd.HasValue && ep.RangeEnd.Value >= ep.RangeStart.Value)
            return $"{ep.RangeStart}-{ep.RangeEnd}";
        return ep.Number.ToString("00");
    }

    private static string BuildEpisodeKey(EpisodeEntry ep, int useNum)
    {
        if (ep.IsSpecial)
        {
            int n = ep.Number > 0 ? ep.Number : TryPickNumberFromFiles(ep.LocalFiles);
            return n > 0 ? $"番外{n:00}" : "番外";
        }
        if (ep.IsMisc)
        {
            return "杂项";
        }
        if (ep.IsVolume)
        {
            var n = TryPickNumberFromFiles(ep.LocalFiles);
            var use = n > 0 ? n : useNum;
            return $"卷{use:00}";
        }
        if (ep.RangeStart.HasValue && ep.RangeEnd.HasValue && ep.RangeEnd.Value >= ep.RangeStart.Value)
            return $"{ep.RangeStart}-{ep.RangeEnd}";
        return useNum.ToString("00");
    }

    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Status = UiText.Tr("正在刷新项目列表...");
            var ctx = await GetApiContextAsync();
            if (ctx is null) return;

            var fsApi = new FileSystemApi(ctx.BaseUrl);
            var result = await fsApi.DownloadAsync(ctx.Token, "/project.json");
            if (result.Code != 200 || result.Content is null)
            {
                Status = UiText.Tr("下载失败: {0} {1}", result.Code, result.Message);
                return;
            }

            var bytes = result.Content;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) bytes = bytes[3..];
            var json = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Trim();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Object)
            {
                Status = UiText.Tr("清单格式错误（缺少 projects）");
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

            Status = UiText.Tr("已加载 {0} 个项目", Projects.Count);
        }
        catch (Exception ex)
        {
            Status = UiText.Tr("刷新失败: {0}", ex.Message);
            Logger.Error(ex, "RefreshAsync failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static (int? lo, int? hi, string? disp) DetectRangeFromFiles(IEnumerable<string> files)
    {
        int? bestLo = null, bestHi = null; string? bestDisp = null;
        foreach (var f in files)
        {
            var name = Path.GetFileNameWithoutExtension(f) ?? string.Empty;
            var m = Regex.Match(name, @"(\d{1,5})\s*[-_~～—–至到]\s*(\d{1,5})");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var a) && int.TryParse(m.Groups[2].Value, out var b))
            {
                var lo = Math.Min(a, b); var hi = Math.Max(a, b);
                if (!bestLo.HasValue || (bestHi - bestLo) < (hi - lo))
                {
                    bestLo = lo; bestHi = hi;
                    var disp = m.Value;
                    disp = disp.Replace("到", "-").Replace("至", "-").Replace("～", "-").Replace("~", "-").Replace("—", "-").Replace("–", "-").Replace("_", "-");
                    disp = Regex.Replace(disp, @"\s+", string.Empty);
                    disp = Regex.Replace(disp, @"-+", "-");
                    bestDisp = disp;
                }
            }
        }
        return (bestLo, bestHi, bestDisp);
    }

    private static bool TryParseRangeNumber(string s, out int hi)
    {
        hi = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var hasRangeSep = s.IndexOf('-') >= 0 || s.IndexOf('_') >= 0 || s.IndexOf('~') >= 0 || s.IndexOf('～') >= 0 || s.IndexOf('—') >= 0 || s.IndexOf('–') >= 0 || s.Contains("至") || s.Contains("到");
        var matches = Regex.Matches(s, @"(\d{1,5})\s*[-_~～—–至到]\s*(\d{1,5})");
        foreach (Match m in matches)
        {
            if (m.Success && int.TryParse(m.Groups[1].Value, out var a) && int.TryParse(m.Groups[2].Value, out var b))
            {
                var right = Math.Max(a, b);
                hi = Math.Max(hi, right);
            }
        }
        if (hi > 0) return true;
        if (hasRangeSep)
        {
            var cleaned = Regex.Replace(s, @"\b(19|20)\d{2}[-_.]?(0?[1-9]|1[0-2])[-_.]?(0?[1-9]|[12]\d|3[01])\b", " ", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\b(\d{3,4})[xX](\d{3,4})\b", " ", RegexOptions.IgnoreCase);
            var nums = Regex.Matches(cleaned, @"\d{1,5}");
            foreach (Match m in nums) if (int.TryParse(m.Value, out var n)) hi = Math.Max(hi, n);
        }
        return hi > 0;
    }

    public async Task<bool> UploadPendingAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(PendingProjectName))
            {
                Status = UiText.Tr("无项目");
                return false;
            }

            var ctx = await GetApiContextAsync();
            if (ctx is null) return false;

            var uploader = await GetUploaderNameAsync();

            var toUpload = PendingEpisodes.Where(e => e.Include).ToList();
            var episodesCount = toUpload.Count;
            var plannedFilesCount = toUpload.Sum(e => e.LocalFiles.Count);
            var includeAggregate = !_uploadToExistingProject;
            var preSteps = 0 + 1 + 1 + episodesCount + plannedFilesCount + (includeAggregate ? 2 : 0) + 1 + 1 + 1;
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

            if (IsTankobonFallback && !TankobonAcknowledged)
            {
                var volNum = TankobonVolumeNumber.GetValueOrDefault(1);
                var keyPreview = volNum.ToString("00");
                var msg = UiText.Tr("检测到‘单行本回退模式’：将把整个目录当作卷 {0} 上传，并在项目 JSON 中写入键名 {1}。\n是否继续？", volNum, keyPreview);
                var confirm = await ConfirmDialog.ShowAsync(null, new ConfirmDialogOptions
                {
                    Title = UiText.Tr("单行本回退模式"),
                    Message = msg,
                    Severity = ConfirmDialogSeverity.Warning,
                    BodyIcon = ConfirmBodyIcon.Upload,
                    YesText = UiText.T(LangKeys.buttonContinue, "继续"),
                    NoText = UiText.T(LangKeys.buttonCancel, "取消"),
                });
                if (confirm != ConfirmDialogResult.Yes)
                {
                    Status = UiText.Tr("已取消（单行本回退模式）");
                    return false;
                }
                TankobonAcknowledged = true;
            }

            Status = UiText.Tr("登录中...");
            Step(UiText.Tr("登录完成"));
            var fsApi = new FileSystemApi(ctx.BaseUrl);

            var projectDir = "/" + PendingProjectName!.Trim('/') + "/";
            var mkProj = await fsApi.MkdirAsync(ctx.Token, projectDir);
            if (mkProj.Code is >= 400 and < 500)
            {
                var chk = await fsApi.GetAsync(ctx.Token, projectDir.TrimEnd('/'));
                if (chk.Code != 200 || chk.Data is null || !chk.Data.IsDir)
                {
                    Status = UiText.Tr("创建目录失败: {0}", mkProj.Message);
                    return false;
                }
            }
            Step(UiText.Tr("已创建项目目录"), projectDir);

            foreach (var ep in toUpload)
            {
                var epDir = projectDir + BuildSubDir(ep) + "/";
                var mk = await fsApi.MkdirAsync(ctx.Token, epDir);
                if (mk.Code is >= 400 and < 500)
                {
                    var chk = await fsApi.GetAsync(ctx.Token, epDir.TrimEnd('/'));
                    if (chk.Code != 200 || chk.Data is null || !chk.Data.IsDir)
                    {
                        Status = UiText.Tr("创建话数目录失败: {0}", mk.Message);
                        return false;
                    }
                }
                Step(UiText.Tr("目录就绪: {0}", ep.Number), epDir);
            }

            var items = new List<FileUploadItem>();
            var resultMap = new Dictionary<EpisodeEntry, (string? source, string? translate, string? proof, string? typeset, (int? lo, int? hi, string? disp) range, List<string> all)>();
            foreach (var ep in toUpload)
            {
                var epDir = projectDir + BuildSubDir(ep) + "/";
                string? sourcePath = null, translatePath = null, proofPath = null, typesetPath = null;
                var remoteAll = new List<string>();
                foreach (var file in ep.LocalFiles)
                {
                    if (!File.Exists(file)) { Step(UiText.Tr("跳过缺失文件"), file); continue; }
                    var name = Path.GetFileName(file);
                    var remote = epDir + name;
                    remoteAll.Add(remote);
                    var ext = Path.GetExtension(name);
                    if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        var content = await File.ReadAllBytesAsync(file);
                        items.Add(new FileUploadItem { FilePath = remote, Content = content, LocalPath = null });
                        var lower = name.ToLowerInvariant();
                        var isCheck = lower.Contains("校对") || lower.Contains("校隊") || lower.Contains("check");
                        if (isCheck) proofPath = proofPath ?? remote; else translatePath = translatePath ?? remote;
                    }
                    else
                    {
                        items.Add(new FileUploadItem { FilePath = remote, LocalPath = file, Content = Array.Empty<byte>() });
                        if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) || ext.Equals(".7z", StringComparison.OrdinalIgnoreCase) || ext.Equals(".rar", StringComparison.OrdinalIgnoreCase))
                            sourcePath = sourcePath ?? remote;
                        else if (ext.Equals(".psd", StringComparison.OrdinalIgnoreCase))
                            typesetPath = typesetPath ?? remote;
                    }
                    Step(UiText.Tr("已规划文件"), remote);
                }
                var range = DetectRangeFromFiles(ep.LocalFiles);
                resultMap[ep] = (source: sourcePath, translate: translatePath, proof: proofPath, typeset: typesetPath, range: range, all: remoteAll);
            }

            var projectJsonPath = ProjectMap.TryGetValue(PendingProjectName!, out var pj) && !string.IsNullOrWhiteSpace(pj)
                ? pj!
                : projectDir + PendingProjectName + "_project.json";

            if (!_uploadToExistingProject)
            {
                if (ProjectMap.ContainsKey(PendingProjectName!))
                {
                    Status = UiText.Tr("聚合清单中已存在项目“PendingProjectName”，请更换名称或改用‘上传到项目’。");
                    return false;
                }
                const string aggregatePath = "/project.json";
                var agg = await fsApi.DownloadAsync(ctx.Token, aggregatePath);
                if (agg.Code != 200 || agg.Content is null)
                {
                    Status = UiText.Tr("下载聚合清单失败: {0} {1}", agg.Code, agg.Message);
                    return false;
                }
                Step(UiText.Tr("已下载聚合清单"), aggregatePath);

                Dictionary<string, string> map = new();
                try
                {
                    using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(agg.Content).TrimStart('\uFEFF'));
                    if (!doc.RootElement.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Object)
                    {
                        Status = UiText.Tr("聚合清单格式错误");
                        return false;
                    }
                    foreach (var prop in projects.EnumerateObject()) map[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
                catch
                {
                    Status = UiText.Tr("解析聚合清单失败");
                    return false;
                }

                map[PendingProjectName!] = projectJsonPath;
                var aggObj = new AggregateProjects { Projects = map };
                var aggCtx = new AppJsonContext(new JsonSerializerOptions(AppJsonContext.Default.Options)
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = true
                });
                var aggOut = JsonSerializer.Serialize(aggObj, aggCtx.AggregateProjects);
                var putAgg = await fsApi.SafePutAsync(ctx.Token, aggregatePath, Encoding.UTF8.GetBytes(aggOut));
                if (putAgg.Code != 200)
                {
                    Status = UiText.Tr("更新聚合清单失败: {0}", putAgg.Message);
                    return false;
                }
                Step(UiText.Tr("聚合清单已更新"), aggregatePath);
            }

            ProjectCn cn;
            var getMeta = await fsApi.GetAsync(ctx.Token, projectJsonPath);
            if (getMeta.Code == 200 && getMeta.Data is { IsDir: false })
            {
                var dl = await fsApi.DownloadAsync(ctx.Token, projectJsonPath);
                if (dl.Code == 200 && dl.Content is { Length: > 0 })
                {
                    try
                    {
                        var txt = Encoding.UTF8.GetString(dl.Content).TrimStart('\uFEFF');
                        cn = JsonSerializer.Deserialize(txt, AppJsonContext.Default.ProjectCn) ?? new ProjectCn();
                    }
                    catch (JsonException ex)
                    {
                        Logger.Warn(ex, "Project metadata JSON invalid at {path}", projectJsonPath);
                        cn = new ProjectCn();
                    }
                    catch (DecoderFallbackException ex)
                    {
                        Logger.Warn(ex, "Project metadata decoding failed at {path}", projectJsonPath);
                        cn = new ProjectCn();
                    }
                }
                else cn = new ProjectCn();
            }
            else cn = new ProjectCn();
            Step(UiText.Tr("已获取项目JSON"), projectJsonPath);

            foreach (var ep in toUpload)
            {
                resultMap.TryGetValue(ep, out var pmap);
                var useNum = IsTankobonFallback && TankobonVolumeNumber.HasValue && TankobonVolumeNumber.Value > 0 ? TankobonVolumeNumber.Value : ep.Number;
                var key = BuildEpisodeKey(ep, useNum);

                var now = DateTimeOffset.UtcNow;
                var kind = ep.IsSpecial ? "番外" : (IsTankobonFallback ? "单行本" : (ep.IsMisc ? "杂项" : (ep.IsVolume ? "卷" : "话")));
                var display = key;

                if (!cn.Items.TryGetValue(key, out var ecn))
                {
                    ecn = new EpisodeCn { CreatedAt = now };
                    cn.Items[key] = ecn;
                }

                ecn.Status = ep.Status;
                if (!string.IsNullOrWhiteSpace(pmap.source))
                {
                    ecn.SourcePath = pmap.source;
                    ecn.SourceOwner ??= uploader;
                    ecn.SourceCreatedAt ??= now;
                    ecn.SourceUpdatedAt = now;
                }
                if (!string.IsNullOrWhiteSpace(pmap.translate))
                {
                    ecn.TranslatePath = pmap.translate;
                    ecn.TranslateOwner ??= uploader;
                    ecn.TranslateCreatedAt ??= now;
                    ecn.TranslateUpdatedAt = now;
                }
                if (!string.IsNullOrWhiteSpace(pmap.proof))
                {
                    ecn.ProofPath = pmap.proof;
                    ecn.ProofOwner ??= uploader;
                    ecn.ProofCreatedAt ??= now;
                    ecn.ProofUpdatedAt = now;
                }
                if (!string.IsNullOrWhiteSpace(pmap.typeset))
                {
                    ecn.TypesetPath = pmap.typeset;
                    ecn.PublishOwner ??= uploader;
                    ecn.PublishCreatedAt ??= now;
                    ecn.PublishUpdatedAt = now;
                }

                // 写入文件路径列表
                if (pmap.all is { Count: > 0 }) ecn.FilePaths = pmap.all;

                if (!string.IsNullOrWhiteSpace(ep.Display)) ecn.Display = ep.Display;
                if (!string.IsNullOrWhiteSpace(ep.Kind)) ecn.Kind = ep.Kind; else ecn.Kind = kind;
                if (ep.RangeStart.HasValue) ecn.RangeStart = ep.RangeStart;
                if (ep.RangeEnd.HasValue) ecn.RangeEnd = ep.RangeEnd;
                if (!string.IsNullOrWhiteSpace(ep.RangeDisplay)) ecn.RangeDisplay = ep.RangeDisplay;
                if (!string.IsNullOrWhiteSpace(ep.Owner)) ecn.Owner = ep.Owner;
                if (ep.Tags is { Count: > 0 }) ecn.Tags = ep.Tags;
                if (!string.IsNullOrWhiteSpace(ep.Notes)) ecn.Notes = ep.Notes;

                ecn.Kind = kind;
                ecn.Number = useNum;
                ecn.Display = display;
                if (pmap.range.lo.HasValue && pmap.range.hi.HasValue && pmap.range.lo > 0 && pmap.range.hi >= pmap.range.lo)
                {
                    ecn.RangeStart = pmap.range.lo;
                    ecn.RangeEnd = pmap.range.hi;
                    ecn.RangeDisplay = pmap.range.disp ?? $"{pmap.range.lo}-{pmap.range.hi}";
                }
                ecn.UpdatedAt = now;
                ecn.CreatedAt ??= now;
            }

            static int ComputeRank(string key)
            {
                if (string.IsNullOrWhiteSpace(key)) return 0;
                int Extract(string s)
                {
                    var digits = new string((s ?? string.Empty).Where(char.IsDigit).ToArray());
                    return int.TryParse(digits, out var n) ? n : 0;
                }
                if (key.StartsWith("番外", StringComparison.OrdinalIgnoreCase)) return 2_000_000 + Extract(key);
                if (key.StartsWith("卷", StringComparison.OrdinalIgnoreCase) || key.StartsWith("V", StringComparison.OrdinalIgnoreCase)) return 1_000_000 + Extract(key);
                return Extract(key);
            }
            cn.Items = cn.Items.OrderByDescending(kv => ComputeRank(kv.Key)).ToDictionary(k => k.Key, v => v.Value);
            Step(UiText.Tr("JSON 合并完成"), projectJsonPath);

            var ctxSer = new AppJsonContext(new JsonSerializerOptions(AppJsonContext.Default.Options)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                WriteIndented = true
            });
            var jsonOut = JsonSerializer.Serialize(cn, ctxSer.ProjectCn);
            var bytesOut = Encoding.UTF8.GetBytes(jsonOut);
            var putJson = await fsApi.SafePutAsync(ctx.Token, projectJsonPath, bytesOut);
            if (putJson.Code != 200)
            {
                Status = UiText.Tr("更新项目JSON失败: {0}", putJson.Message);
                return false;
            }
            Step(UiText.Tr("JSON 已上传"), projectJsonPath);

            var offset = UploadCompleted;
            var progress = new Progress<UploadProgress>(p =>
            {
                UploadTotal = totalSteps;
                UploadCompleted = Math.Min(offset + p.Completed, UploadTotal);
                CurrentUploadingPath = p.CurrentRemotePath;
                if (p.SpeedMBps is double sp)
                    Status = UiText.Tr("上传中 {0}/{1}: {2} ({3:F2} MB/s)", UploadCompleted, UploadTotal, CurrentUploadingPath, sp);
                else
                    Status = UiText.Tr("上传中 {0}/{1}: {2}", UploadCompleted, UploadTotal, CurrentUploadingPath);
            });

            var res = await fsApi.PutManyAsync(ctx.Token, items, progress, 6);
            if (res.Any(r => r.Code != 200))
            {
                var first = res.FirstOrDefault(r => r.Code != 200);
                Status = UiText.Tr("上传失败: {0}", first?.Message);
                return false;
            }

            Status = UiText.Tr("上传完成");
            await RefreshAsync();
            try
            {
                if (_dialogs is not null) await _dialogs.ShowMessageAsync(UiText.Tr("上传完成"));
                else await MessageBox.ShowAsync(UiText.Tr("上传完成"), UiText.Tr("提示"), MessageBoxIcon.Information);
            }
            catch (InvalidOperationException ex)
            {
                Logger.Warn(ex, "Failed to display upload completion dialog");
            }
            catch (TaskCanceledException ex)
            {
                Logger.Warn(ex, "Upload completion dialog was canceled");
            }
            return true;
        }
        catch (Exception ex)
        {
            Status = UiText.Tr("上传异常: {0}", ex.Message);
            Logger.Error(ex, "UploadPendingAsync failed");
            return false;
        }
    }


    private List<MetaNode> _metaRoots = new();
    public ObservableCollection<MetaNode> MetaRows { get; } = new();

    private void BuildMetaTree()
    {
        _metaRoots = PendingEpisodes.Select(ep => CreateMetaRoot(ep)).ToList();
        foreach (var root in _metaRoots)
        {
            root.IsExpanded = true;
            WireMetaCommands(root);
        }
        RebuildMetaRows();
    }

    private void WireMetaCommands(MetaNode node)
    {
        node.ToggleCommand = ToggleMetaNodeCommand;
        foreach (var child in node.Children) WireMetaCommands(child);
    }

    private void RebuildMetaRows()
    {
        MetaRows.Clear();
        foreach (var root in _metaRoots) AddMetaRow(root, 0);
    }

    private void AddMetaRow(MetaNode node, int depth)
    {
        node.Depth = depth;
        MetaRows.Add(node);
        if (!node.IsExpanded) return;
        foreach (var child in node.Children) AddMetaRow(child, depth + 1);
    }

    [RelayCommand]
    private void ToggleMetaNode(MetaNode? node)
    {
        if (node is null || node.Children.Count == 0) return;
        node.IsExpanded = !node.IsExpanded;
        RebuildMetaRows();
    }

    private MetaNode CreateMetaRoot(EpisodeEntry ep)
    {
        var episodeDisp = !string.IsNullOrWhiteSpace(ep.Display)
            ? ep.Display!
            : (ep.IsSpecial ? "番外" : (ep.IsVolume ? string.Format("{0:00}(卷)", ep.Number) : string.Format("{0:00}", ep.Number)));
        var node = new MetaNode
        {
            IsFile = false,
            Name = BuildMetaName(ep, ep.RangeDisplay),
            Number = ep.Number,
            Include = ep.Include,
            Status = ep.Status,
            IsSpecial = ep.IsSpecial,
            IsVolume = ep.IsVolume,
            LocalFileCount = ep.LocalFiles.Count,
            EpisodeDisplay = episodeDisp,
            LocalFileCountDisplay = ep.LocalFiles.Count.ToString(),
            Ref = ep,
            Display = ep.Display,
            RangeStart = ep.RangeStart,
            RangeEnd = ep.RangeEnd,
            RangeDisplay = ep.RangeDisplay,
            Owner = ep.Owner,
            Tags = ep.Tags is null ? string.Empty : string.Join(",", ep.Tags),
            Notes = ep.Notes
        };
        foreach (var f in ep.LocalFiles)
        {
            node.Children.Add(new MetaNode
            {
                IsFile = true,
                Name = Path.GetFileName(f),
                Number = ep.Number,
                Include = true,
                Status = string.Empty,
                LocalFileCount = 0,
                EpisodeDisplay = episodeDisp,
                LocalFileCountDisplay = "0"
            });
        }
        return node;
    }

    private static int ParseChineseNumeral(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var map = new Dictionary<char, int>
        {
            ['零'] = 0,
            ['一'] = 1,
            ['二'] = 2,
            ['两'] = 2,
            ['三'] = 3,
            ['四'] = 4,
            ['五'] = 5,
            ['六'] = 6,
            ['七'] = 7,
            ['八'] = 8,
            ['九'] = 9,
            ['十'] = 10,
            ['百'] = 100,
            ['千'] = 1000,
            ['〇'] = 0
        };
        var total = 0; var section = 0; var number = 0;
        foreach (var ch in s)
        {
            if (!map.TryGetValue(ch, out var val)) continue;
            if (val is 10 or 100 or 1000)
            {
                if (number == 0) number = 1;
                section += number * val; number = 0;
            }
            else number = val;
        }
        total += section + number; return total;
    }

    private static string BuildMetaName(EpisodeEntry ep, string? rangeDisplay)
    {
        if (ep.IsSpecial) return "番外";
        if (ep.IsMisc) return "杂项";
        if (ep.IsVolume)
        {
            var coreVol = string.IsNullOrWhiteSpace(rangeDisplay) ? ep.Number.ToString() : Regex.Replace(rangeDisplay, @"[ 到至~～—–_]", "-");
            coreVol = Regex.Replace(coreVol, @"\s+", string.Empty);
            coreVol = Regex.Replace(coreVol, @"-+", "-");
            return $"第{coreVol}卷";
        }
        var core = string.IsNullOrWhiteSpace(rangeDisplay) ? ep.Number.ToString() : Regex.Replace(rangeDisplay, @"[ 到至~～—–_]", "-");
        core = Regex.Replace(core, @"\s+", string.Empty);
        core = Regex.Replace(core, @"-+", "-");
        return $"第{core}话";
    }

    private async Task ConfirmMetaAsync()
    {
        if (_metaRoots is not null)
        {
            foreach (var n in _metaRoots.Where(x => !x.IsFile))
            {
                if (n.Ref is null) continue;
                var ep = n.Ref;
                ep.Include = n.Include;
                ep.Status = n.Status;
                ep.Number = n.Number;
                ep.IsSpecial = n.IsSpecial;
                ep.IsVolume = n.IsVolume;
                ep.Display = n.Display;
                ep.RangeStart = n.RangeStart;
                ep.RangeEnd = n.RangeEnd;
                ep.RangeDisplay = n.RangeDisplay;
                ep.Owner = n.Owner;
                ep.Tags = string.IsNullOrWhiteSpace(n.Tags) ? null : n.Tags.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                ep.Notes = n.Notes;
            }
        }
        var ok = await UploadPendingAsync();
        MetaWindowCloseRequested?.Invoke(this, ok);
    }

    private static List<EpisodeEntry> ScanEpisodes(string folder, out bool isTankobonFallback, out int? volumeNumber)
    {
        isTankobonFallback = false;
        volumeNumber = null;
        var list = new List<EpisodeEntry>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return list;

        var byEpisode = new Dictionary<int, EpisodeEntry>();
        var bySpecial = new Dictionary<int, EpisodeEntry>();
        var special = new EpisodeEntry { Number = 0, IsSpecial = true, Status = "立项" };
        var misc = new EpisodeEntry { Number = 0, IsMisc = true, Status = "立项", Display = "杂项", Kind = "杂项" };

        // 先按子目录扫描
        foreach (var sub in Directory.EnumerateDirectories(folder, "*", SearchOption.TopDirectoryOnly))
        {
            var dirName = Path.GetFileName(sub);
            if (IsSpecialName(dirName))
            {
                if (TryParseSpecialNumber(dirName, out var folderSpNum, out _) && folderSpNum > 0)
                {
                    if (!bySpecial.TryGetValue(folderSpNum, out var epSpFolder) || epSpFolder is null)
                    {
                        epSpFolder = new EpisodeEntry { Number = folderSpNum, Status = "立项", IsSpecial = true };
                        bySpecial[folderSpNum] = epSpFolder;
                    }
                    foreach (var f in Directory.EnumerateFiles(sub, "*", SearchOption.AllDirectories))
                    {
                        epSpFolder.LocalFiles.Add(f);
                        UpgradeStatusFromFile(ref epSpFolder, f);
                    }
                    continue;
                }
                // 无明确番外编号则归入 special 桶
                foreach (var f in Directory.EnumerateFiles(sub, "*", SearchOption.AllDirectories))
                {
                    var baseName = Path.GetFileNameWithoutExtension(f);
                    if (TryParseSpecialNumber(baseName, out var spNum, out _))
                    {
                        EpisodeEntry target;
                        if (spNum > 0)
                        {
                            if (!bySpecial.TryGetValue(spNum, out var t) || t is null)
                            {
                                t = new EpisodeEntry { Number = spNum, Status = "立项", IsSpecial = true };
                                bySpecial[spNum] = t;
                            }
                            target = bySpecial[spNum];
                        }
                        else target = special;
                        target.LocalFiles.Add(f);
                        UpgradeStatusFromFile(ref target, f);
                    }
                    else
                    {
                        special.LocalFiles.Add(f);
                        UpgradeStatusFromFile(ref special, f);
                    }
                }
                continue;
            }

            var isVolLocal = false;
            if (!TryParseEpisodeNumber(dirName, out var num))
            {
                var (kind, n2) = ParseEpisodeOrVolume(dirName);
                if (kind == NameKind.Volume) isVolLocal = true;
                if (n2 <= 0)
                {
                    foreach (var f in Directory.EnumerateFiles(sub, "*", SearchOption.AllDirectories))
                    {
                        misc.LocalFiles.Add(f);
                        UpgradeStatusFromFile(ref misc, f);
                    }
                    continue;
                }
                num = n2;
            }

            if (!byEpisode.TryGetValue(num, out var ep))
            {
                ep = new EpisodeEntry { Number = num, Status = "立项", IsVolume = isVolLocal };
                byEpisode[num] = ep;
            }
            foreach (var f in Directory.EnumerateFiles(sub, "*", SearchOption.AllDirectories))
            {
                ep.LocalFiles.Add(f);
                UpgradeStatusFromFile(ref ep, f);
            }
        }

        // 根目录文件
        foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            if (IsSpecialName(name))
            {
                EpisodeEntry target;
                TryParseSpecialNumber(name, out var spNum, out _);
                if (spNum > 0)
                {
                    if (!bySpecial.TryGetValue(spNum, out var t) || t is null)
                    {
                        t = new EpisodeEntry { Number = spNum, Status = "立项", IsSpecial = true };
                        bySpecial[spNum] = t;
                    }
                    target = bySpecial[spNum];
                }
                else target = special;
                target.LocalFiles.Add(f);
                UpgradeStatusFromFile(ref target, f);
                continue;
            }
            var isVolRoot = false;
            if (!TryParseEpisodeNumber(name, out var numRoot))
            {
                var (kind, n) = ParseEpisodeOrVolume(name);
                if (kind == NameKind.Volume) isVolRoot = true;
                if (n <= 0) { misc.LocalFiles.Add(f); UpgradeStatusFromFile(ref misc, f); continue; }
                numRoot = n;
            }
            if (!byEpisode.TryGetValue(numRoot, out var epRoot))
            {
                epRoot = new EpisodeEntry { Number = numRoot, Status = "立项", IsVolume = isVolRoot };
                byEpisode[numRoot] = epRoot;
            }
            epRoot.LocalFiles.Add(f);
            UpgradeStatusFromFile(ref epRoot, f);
        }

        // 计算范围与显示
        foreach (var ep in byEpisode.Values)
        {
            var range = DetectRangeFromFiles(ep.LocalFiles);
            ep.RangeStart = range.lo; ep.RangeEnd = range.hi; ep.RangeDisplay = range.disp;
        }
        foreach (var sp in bySpecial.Values)
        {
            var range = DetectRangeFromFiles(sp.LocalFiles);
            sp.RangeStart = range.lo; sp.RangeEnd = range.hi; sp.RangeDisplay = range.disp;
            sp.Display ??= $"番外{sp.Number:00}";
        }
        if (special.LocalFiles.Count > 0)
        {
            var range = DetectRangeFromFiles(special.LocalFiles);
            special.RangeStart = range.lo; special.RangeEnd = range.hi; special.RangeDisplay = range.disp;
            special.Display ??= "番外";
            list.Add(special);
        }
        if (misc.LocalFiles.Count > 0)
        {
            misc.Display ??= "杂项";
            list.Add(misc);
        }
        list.AddRange(bySpecial.Values.OrderByDescending(e => e.Number));
        list.AddRange(byEpisode.Values.OrderByDescending(e => e.Number));

        isTankobonFallback = false;
        volumeNumber = null;
        return list;
    }

    private static void UpgradeStatusFromFile(ref EpisodeEntry ep, string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (ext.Equals(".psd", StringComparison.OrdinalIgnoreCase)) ep.Status = PickHigherStatus(ep.Status, "嵌字");
        else if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            var lower = Path.GetFileName(filePath).ToLowerInvariant();
            ep.Status = PickHigherStatus(ep.Status, lower.Contains("校对") || lower.Contains("校隊") || lower.Contains("check") ? "校对" : "翻译");
        }
    }
}
