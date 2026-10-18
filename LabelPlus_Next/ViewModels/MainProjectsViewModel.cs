using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Threading; // added for Interlocked
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Services;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LabelPlus_Next.Models;
using System.Text.RegularExpressions;
using NLog;

namespace LabelPlus_Next.ViewModels;

public class MainProjectsViewModel : ViewModelBase
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly IFileDialogService _dialogs;
    private readonly string _recentStorePath;

    private RecentProject? _selectedProject;
    private ObservableCollection<RecentProject> _recentProjects = new();
    private string? _previewStatus;
    private bool _isBusy;
    private int _previewLoadVersion; // version for cancellation

    private Bitmap? _currentPreviewImage;
    private ObservableCollection<LabelItem> _currentPreviewLabels = new();
    private ObservableCollection<string> _previewImageNames = new();
    private int _currentPreviewIndex = -1;
    private string? _currentPreviewFileName;

    private bool _recoveryChecked;

    public event EventHandler<string>? OpenProjectRequested;

    public MainProjectsViewModel() : this(App.Services.GetRequiredService<IFileDialogService>()) { }

    public MainProjectsViewModel(IFileDialogService dialogs)
    {
        _dialogs = dialogs;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LabelPlus_Next");
        Directory.CreateDirectory(dir);
        _recentStorePath = Path.Combine(dir, "recent_projects.json");
        LoadRecent();
        RecentProjects.CollectionChanged += (_, __) => OnPropertyChanged(nameof(IsRecentEmpty));
        CreateProjectCommand = new AsyncRelayCommand(CreateProjectAsync);
        OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync);
        OpenSelectedProjectCommand = new RelayCommand(OpenSelectedProject, () => SelectedProject?.TranslationFile is not null);
        RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => SelectedProject is not null);
        RefreshRecentCommand = new RelayCommand(RefreshRecent);
        PrevPreviewCommand = new RelayCommand(PrevPreview, CanNavigatePreview);
        NextPreviewCommand = new RelayCommand(NextPreview, CanNavigatePreview);
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public RecentProject? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (SetProperty(ref _selectedProject, value))
            {
                _ = LoadPreviewAsync(value); // fire & forget
                OpenSelectedProjectCommand.NotifyCanExecuteChanged();
                RemoveSelectedCommand.NotifyCanExecuteChanged();
                CheckAutosaveRecovery();
            }
        }
    }

    public ObservableCollection<RecentProject> RecentProjects { get => _recentProjects; set { if (SetProperty(ref _recentProjects, value)) CheckAutosaveRecovery(); } }
    public bool IsRecentEmpty => RecentProjects.Count == 0;
    public string? PreviewStatus { get => _previewStatus; set => SetProperty(ref _previewStatus, value); }
    public Bitmap? CurrentPreviewImage { get => _currentPreviewImage; set => SetProperty(ref _currentPreviewImage, value); }
    public ObservableCollection<LabelItem> CurrentPreviewLabels { get => _currentPreviewLabels; set => SetProperty(ref _currentPreviewLabels, value); }
    public ObservableCollection<string> PreviewImageNames { get => _previewImageNames; set => SetProperty(ref _previewImageNames, value); }
    public int CurrentPreviewIndex { get => _currentPreviewIndex; private set { if (SetProperty(ref _currentPreviewIndex, value)) UpdateNavCommands(); } }
    public string? CurrentPreviewFileName { get => _currentPreviewFileName; private set => SetProperty(ref _currentPreviewFileName, value); }

    public IAsyncRelayCommand CreateProjectCommand { get; }
    public IAsyncRelayCommand OpenProjectCommand { get; }
    public IRelayCommand OpenSelectedProjectCommand { get; }
    public IRelayCommand RemoveSelectedCommand { get; }
    public IRelayCommand RefreshRecentCommand { get; }
    public IRelayCommand PrevPreviewCommand { get; }
    public IRelayCommand NextPreviewCommand { get; }

    private bool CanNavigatePreview() => PreviewImageNames.Count > 1 && CurrentPreviewIndex >= 0;

    private void UpdateNavCommands()
    {
        (PrevPreviewCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (NextPreviewCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void PrevPreview()
    {
        if (PreviewImageNames.Count == 0) return;
        var idx = (CurrentPreviewIndex - 1 + PreviewImageNames.Count) % PreviewImageNames.Count;
        _ = LoadPreviewAtAsync(idx);
    }
    private void NextPreview()
    {
        if (PreviewImageNames.Count == 0) return;
        var idx = (CurrentPreviewIndex + 1) % PreviewImageNames.Count;
        _ = LoadPreviewAtAsync(idx);
    }

    private async Task LoadPreviewAtAsync(int index)
    {
        if (index < 0 || index >= PreviewImageNames.Count) return;
        var proj = SelectedProject; if (proj?.TranslationFile is null) return;
        var thisVersion = _previewLoadVersion; // don't increment; navigation stays in same selection
        var dir = Path.GetDirectoryName(proj.TranslationFile) ?? string.Empty;
        var imageName = PreviewImageNames[index];
        var full = Path.Combine(dir, imageName).Replace('/', Path.DirectorySeparatorChar);
        Bitmap? bmp = null;
        if (File.Exists(full))
        {
            try { bmp = await Task.Run(() => new Bitmap(full)); }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to load preview bitmap: {path}", full);
                bmp = null;
            }
        }
        if (thisVersion != _previewLoadVersion)
        {
            bmp?.Dispose();
            return; // selection changed while loading
        }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentPreviewImage?.Dispose();
            CurrentPreviewImage = bmp;
            CurrentPreviewLabels.Clear();
            foreach (var label in ParseLabelsForImage(proj.TranslationFile!, imageName))
                CurrentPreviewLabels.Add(label);
            CurrentPreviewIndex = index;
            CurrentPreviewFileName = imageName;
            PreviewStatus = $"{index + 1}/{PreviewImageNames.Count}";
        });
    }

    private async Task CreateProjectAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            var folder = await _dialogs.PickFolderAsync("选择新项目目录");
            if (string.IsNullOrWhiteSpace(folder)) return;
            var images = await _dialogs.ChooseImagesAsync(folder);
            if (images is null || images.Count == 0) return;
            var names = images.Select(p => Path.GetFileName(p)).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!);
            var content = TranslationFileUtils.BuildInitialContent(names);
            var outPath = TranslationFileUtils.GetNonConflictPath(folder, "translation.txt");
            await File.WriteAllTextAsync(outPath, content);
            AddOrUpdateRecent(outPath);
            SelectedProject = RecentProjects.FirstOrDefault(r => string.Equals(r.TranslationFile, outPath, StringComparison.OrdinalIgnoreCase));
            OpenProjectRequested?.Invoke(this, outPath);
        }
        finally { IsBusy = false; }
    }

    private async Task OpenProjectAsync()
    {
        if (IsBusy) return;
        var file = await _dialogs.OpenTranslationFileAsync();
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return;
        AddOrUpdateRecent(file);
        SelectedProject = RecentProjects.FirstOrDefault(r => string.Equals(r.TranslationFile, file, StringComparison.OrdinalIgnoreCase));
        OpenProjectRequested?.Invoke(this, file);
    }

    private void OpenSelectedProject()
    {
        if (IsBusy) return;
        var sel = SelectedProject;
        if (sel?.TranslationFile is null || !File.Exists(sel.TranslationFile)) return;
        sel.LastOpened = DateTimeOffset.Now;
        SaveRecent();
        OpenProjectRequested?.Invoke(this, sel.TranslationFile);
    }

    private void RemoveSelected()
    {
        if (IsBusy) return;
        var sel = SelectedProject;
        if (sel is null) return;
        // Confirmation handled in view code-behind before invoking this command
        RecentProjects.Remove(sel);
        SelectedProject = null;
        SaveRecent();
        ClearPreview();
        PreviewStatus = null;
    }

    private void RefreshRecent()
    {
        if (IsBusy) return;
        var removed = RecentProjects.Where(r => r.TranslationFile is null || !File.Exists(r.TranslationFile)).ToList();
        foreach (var r in removed) RecentProjects.Remove(r);
        var ordered = RecentProjects.OrderByDescending(r => r.LastOpened).ToList();
        RecentProjects.Clear();
        foreach (var r in ordered) RecentProjects.Add(r);
        SaveRecent();
    }

    private void ClearPreview()
    {
        CurrentPreviewImage?.Dispose();
        CurrentPreviewImage = null;
        CurrentPreviewLabels.Clear();
        PreviewImageNames.Clear();
        CurrentPreviewIndex = -1;
        CurrentPreviewFileName = null;
        UpdateNavCommands();
    }

    private async Task LoadPreviewAsync(RecentProject? proj)
    {
        var version = Interlocked.Increment(ref _previewLoadVersion);
        ClearPreview();
        if (proj?.TranslationFile is null)
        {
            PreviewStatus = null;
            return;
        }
        try
        {
            if (!File.Exists(proj.TranslationFile))
            {
                PreviewStatus = "文件不存在";
                return;
            }
            PreviewStatus = "加载预览...";
            var imageNames = ParseImageNames(proj.TranslationFile, 50); // load up to 50 names
            if (version != _previewLoadVersion) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var n in imageNames) PreviewImageNames.Add(n);
            });
            if (PreviewImageNames.Count == 0)
            {
                PreviewStatus = "无图片条目";
                return;
            }
            await LoadPreviewAtAsync(0);
        }
        catch (Exception ex)
        {
            if (version != _previewLoadVersion) return;
            PreviewStatus = "预览失败: " + ex.Message;
        }
    }

    private static List<string> ParseImageNames(string translationFile, int max)
    {
        var result = new List<string>();
        try
        {
            foreach (var line in File.ReadLines(translationFile))
            {
                if (!line.StartsWith(">>>>>>>>[")) continue;
                var name = line.Trim().Trim('>', '<');
                var start = name.IndexOf('[');
                var end = name.IndexOf(']');
                if (start >= 0 && end > start)
                {
                    var inner = name.Substring(start + 1, end - start - 1);
                    if (!string.IsNullOrWhiteSpace(inner) && !result.Contains(inner))
                        result.Add(inner);
                }
                if (result.Count >= max) break;
            }
        }
        catch (IOException ex)
        {
            Logger.Warn(ex, "Failed to parse image names from {file}", translationFile);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warn(ex, "No permission to read translation file {file}", translationFile);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unexpected error parsing image names from {file}", translationFile);
            throw;
        }
        return result;
    }

    private static IEnumerable<LabelItem> ParseLabelsForImage(string translationFile, string imageName)
    {
        var list = new List<LabelItem>();
        try
        {
            var lines = File.ReadAllLines(translationFile);
            var targetHeader = $">>>>>>>>[{imageName}]<<<<<<<<";
            var i = 0;
            while (i < lines.Length)
            {
                if (lines[i].Trim() == targetHeader)
                {
                    i++;
                    int index = 0;
                    while (i < lines.Length)
                    {
                        if (lines[i].StartsWith(">>>>>>>>[")) break; // next image
                        if (lines[i].StartsWith("----------------["))
                        {
                            var header = lines[i];
                            var coord = header.Substring(header.LastIndexOf('['));
                            var parts = coord.Trim('[', ']').Split(',');
                            float x = 0, y = 0; int cat = 1;
                            if (parts.Length >= 3)
                            {
                                float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x);
                                float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y);
                                int.TryParse(parts[2], out cat);
                            }
                            i++;
                            var sb = new System.Text.StringBuilder();
                            while (i < lines.Length && lines[i].Length > 0)
                            {
                                sb.AppendLine(lines[i]);
                                i++;
                            }
                            list.Add(new LabelItem { XPercent = x, YPercent = y, Category = cat, Text = sb.ToString().TrimEnd(), CategoryString = cat == 1 ? "框内" : "框外", Index = ++index });
                        }
                        else i++;
                    }
                    break;
                }
                i++;
            }
        }
        catch (IOException ex)
        {
            Logger.Warn(ex, "Failed to parse labels from {file}", translationFile);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warn(ex, "No permission to read labels from {file}", translationFile);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unexpected error parsing labels from {file}", translationFile);
            throw;
        }
        return list;
    }

    private void AddOrUpdateRecent(string translationFile)
    {
        var dir = Path.GetDirectoryName(translationFile) ?? string.Empty;
        var name = Path.GetFileName(Path.GetDirectoryName(translationFile)?.TrimEnd(Path.DirectorySeparatorChar) ?? dir);
        var existing = RecentProjects.FirstOrDefault(r => string.Equals(r.TranslationFile, translationFile, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new RecentProject
            {
                Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(translationFile) : name,
                TranslationFile = translationFile,
                Folder = dir,
                LastOpened = DateTimeOffset.Now
            };
            RecentProjects.Add(existing);
        }
        else
        {
            existing.LastOpened = DateTimeOffset.Now;
            existing.Folder = dir;
        }
        var ordered = RecentProjects.OrderByDescending(r => r.LastOpened).ToList();
        RecentProjects.Clear();
        foreach (var r in ordered) RecentProjects.Add(r);
        SaveRecent();
    }

    private void LoadRecent()
    {
        try
        {
            if (!File.Exists(_recentStorePath)) return;
            var txt = File.ReadAllText(_recentStorePath);
#pragma warning disable IL2026, IL3050
            var list = JsonSerializer.Deserialize<List<RecentProject>>(txt, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<RecentProject>();
#pragma warning restore IL2026, IL3050
            RecentProjects = new ObservableCollection<RecentProject>(list.OrderByDescending(r => r.LastOpened));
            CheckAutosaveRecovery();
        }
        catch (IOException ex)
        {
            Logger.Warn(ex, "Failed to load recent projects from {path}", _recentStorePath);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warn(ex, "No permission to load recent projects from {path}", _recentStorePath);
        }
        catch (JsonException ex)
        {
            Logger.Warn(ex, "Recent projects file is invalid JSON: {path}", _recentStorePath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unexpected error loading recent projects");
            throw;
        }
    }

    private void SaveRecent()
    {
        try
        {
            var list = RecentProjects.ToList();
#pragma warning disable IL2026, IL3050
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
#pragma warning restore IL2026, IL3050
            File.WriteAllText(_recentStorePath, json);
        }
        catch (IOException ex)
        {
            Logger.Warn(ex, "Failed to save recent projects to {path}", _recentStorePath);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warn(ex, "No permission to save recent projects to {path}", _recentStorePath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unexpected error saving recent projects to {path}", _recentStorePath);
            throw;
        }
    }

    private void CheckAutosaveRecovery()
    {
        if (_recoveryChecked) return;
        _recoveryChecked = true;
        try
        {
            var candidates = new List<string>();
            foreach (var proj in RecentProjects)
            {
                if (proj.TranslationFile is null) continue;
                var dir = Path.GetDirectoryName(proj.TranslationFile);
                if (string.IsNullOrEmpty(dir)) continue;
                var bakDir = Path.Combine(dir, "bak");
                if (!Directory.Exists(bakDir)) continue;
                var baseName = Path.GetFileName(proj.TranslationFile);
                // pattern: yyyyMMdd_HHmmss_basename
                var files = Directory.GetFiles(bakDir, "*_" + baseName, SearchOption.TopDirectoryOnly);
                // consider last 3 hours
                var now = DateTime.Now;
                foreach (var f in files)
                {
                    var stamp = Path.GetFileName(f).Split('_').FirstOrDefault();
                    if (stamp != null && DateTime.TryParseExact(stamp, "yyyyMMdd_HHmmss", null, System.Globalization.DateTimeStyles.None, out var dt))
                    {
                        if ((now - dt) < TimeSpan.FromHours(3)) candidates.Add(f);
                    }
                }
            }
            if (candidates.Count > 0)
            {
                PreviewStatus = $"发现 {candidates.Count} 个自动保存备份，可在对应项目 bak 文件夹中恢复。";
            }
        }
        catch (IOException ex)
        {
            Logger.Warn(ex, "Failed to check autosave recovery");
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warn(ex, "No permission to access autosave backup directories");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unexpected error checking autosave recovery");
            throw;
        }
    }
}

public sealed class RecentProject
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("translationFile")] public string? TranslationFile { get; set; }
    [JsonPropertyName("folder")] public string? Folder { get; set; }
    [JsonPropertyName("lastOpened")] public DateTimeOffset LastOpened { get; set; }
    [JsonPropertyName("imageCount")] public int ImageCount { get; set; }
}