using Avalonia.Controls.Notifications;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using NLog;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Notification = Ursa.Controls.Notification;
using WindowNotificationManager = Ursa.Controls.WindowNotificationManager;

namespace LabelPlus_Next.ViewModels;

using Notification = Notification;
using WindowNotificationManager = WindowNotificationManager;

public partial class TranslateViewModel : ViewModelBase
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly LabelFileManager LabelFileManager1 = new();
    private readonly TimeSpan _autoSaveInterval = TimeSpan.FromMinutes(1);
    private int _autoSaveBusy;
    private Timer? _autoSaveTimer;
    private readonly IFileDialogService _dialogs;
    private string? _lastBackupSignature;
    private bool _autoSaveConflictNotified; // 防止重复通知
    public CollaborationSession? Collab { get; set; } // null for local-only

    public TranslateViewModel() : this(new NoopFileDialogService()) { }
    public TranslateViewModel(IFileDialogService dialogs)
    {
        _dialogs = dialogs;
    }
    public WindowNotificationManager? NotificationManager { get; set; }
    public List<string> LangList { get; } = new() { "default", "en", "zh-hant-tw" };

    // UI progress and status
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private double progress; // 0-100
    [ObservableProperty] private string? progressText;

    [ObservableProperty] private ObservableCollection<LabelItem> currentLabels = new();
    [ObservableProperty] private string? currentText;
    [ObservableProperty] private ObservableCollection<LabelItem> deletedLabels = new();
    [ObservableProperty] private ObservableCollection<string> imageFileNames = new();
    [ObservableProperty] private string? newTranslationPath;
    [ObservableProperty] private string? openTranslationFilePath;
    [ObservableProperty] private IImage? picImageSource;
    [ObservableProperty] private string? selectedImageFile;
    [ObservableProperty] private LabelItem? selectedLabel;
    [ObservableProperty] private string? selectedLang = "default";

    private static bool OnUiThread => Dispatcher.UIThread.CheckAccess();

    private async Task RunOnUiAsync(Action act)
    {
        if (OnUiThread)
        {
            act();
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(act);
        }
    }

    [RelayCommand]
    public async Task AddLabelCommand()
    {
        if (string.IsNullOrEmpty(SelectedImageFile)) return;
        var newLabel = new LabelItem { XPercent = 0.5f, YPercent = 0.5f, Text = "", Category = 1 };
        await LabelManager.Instance.AddLabelAsync(LabelFileManager1, SelectedImageFile, newLabel).ConfigureAwait(false);
        await RunOnUiAsync(() =>
        {
            UpdateCurrentLabels();
            if (CurrentLabels.Count > 0)
            {
                SelectedLabel = CurrentLabels[^1];
                CurrentText = SelectedLabel.Text;
            }
        });
    }
    [RelayCommand]
    public async Task RemoveLabelCommand()
    {
        if (!string.IsNullOrEmpty(SelectedImageFile))
        {
            // Keep selection near the deleted item instead of jumping to the last
            var oldIndex = SelectedLabel is null ? -1 : CurrentLabels.IndexOf(SelectedLabel);
            await LabelManager.Instance.RemoveSelectedAsync(LabelFileManager1, SelectedImageFile, CurrentLabels, SelectedLabel).ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                UpdateCurrentLabels();
                if (CurrentLabels.Count > 0)
                {
                    var newIndex = oldIndex >= 0 ? Math.Min(oldIndex, CurrentLabels.Count - 1) : 0;
                    SelectedLabel = CurrentLabels[newIndex];
                }
                else
                {
                    SelectedLabel = null;
                }
                CurrentText = SelectedLabel?.Text ?? string.Empty;
            });
        }
    }
    [RelayCommand]
    public async Task UndoRemoveLabelCommand()
    {
        if (!string.IsNullOrEmpty(SelectedImageFile))
        {
            var restored = await LabelManager.Instance.UndoRemoveAsync(LabelFileManager1, SelectedImageFile).ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                UpdateCurrentLabels();
                if (restored is not null)
                {
                    SelectedLabel = restored;
                }
                else if (SelectedLabel is null && CurrentLabels.Count > 0)
                {
                    SelectedLabel = CurrentLabels[^1];
                }
                CurrentText = SelectedLabel?.Text ?? string.Empty;
            });
        }
    }
    [RelayCommand]
    private async Task NewTranslationCommand()
    {
        if (_dialogs is null) return;
        await RunOnUiAsync(() => { IsBusy = true; Progress = 5; ProgressText = "选择图片..."; });
        try
        {
            var folder = await _dialogs.PickFolderAsync("选择翻译目录").ConfigureAwait(false);
            if (string.IsNullOrEmpty(folder)) { await RunOnUiAsync(() => { Progress = 100; ProgressText = "已取消"; }); return; }
            var selected = await _dialogs.ChooseImagesAsync(folder).ConfigureAwait(false);
            if (selected == null || selected.Count == 0) { await RunOnUiAsync(() => { Progress = 100; ProgressText = "未选择图片"; }); return; }
            await RunOnUiAsync(() => { Progress = 25; ProgressText = "生成初始内容..."; });
            var names = selected.Select(p => Path.GetFileName(p)).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!);
            var content = TranslationFileUtils.BuildInitialContent(names);
            var outPath = TranslationFileUtils.GetNonConflictPath(folder, "translation.txt");
            await RunOnUiAsync(() => { Progress = 60; ProgressText = "写入文件..."; });
            await File.WriteAllTextAsync(outPath, content, Encoding.UTF8).ConfigureAwait(false);
            await RunOnUiAsync(() => { Progress = 80; ProgressText = "加载到编辑器..."; OpenTranslationFilePath = outPath; });
            await LoadTranslationFile(outPath).ConfigureAwait(false);
            AddOrUpdateRecentProject(outPath); // NEW: sync to recent projects
            await NotifyAsync("提示", "新建翻译文件完成并已打开。", NotificationType.Success).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "NewTranslationCommand failed");
            await NotifyAsync("错误", $"新建翻译失败：{ex.Message}", NotificationType.Error).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() => { Progress = 100; ProgressText = "完成"; IsBusy = false; });
        }
    }
    [RelayCommand]
    private async Task OpenTranslationFileCommand()
    {
        if (_dialogs is null) return;
        try
        {
            var path = await _dialogs.OpenTranslationFileAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(path)) return;
            await OpenTranslationFileAtPathAsync(path).ConfigureAwait(false);
            AddOrUpdateRecentProject(path); // NEW
        }
        catch (Exception ex)
        {
            await NotifyAsync("错误", $"打开失败：{ex.Message}", NotificationType.Error).ConfigureAwait(false);
            if (_dialogs is not null) { await _dialogs.ShowMessageAsync($"打开失败：{ex.Message}").ConfigureAwait(false); }
            Logger.Error(ex, "Open translation file failed.");
        }
    }

    // Central reusable open logic
    public async Task OpenTranslationFileAtPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            await RunOnUiAsync(() => { IsBusy = true; Progress = 15; ProgressText = "读取翻译文件..."; OpenTranslationFilePath = path; });
            await LabelFileManager1.LoadAsync(path).ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                ImageFileNames.Clear();
                foreach (var key in LabelFileManager1.StoreManager.Store.Keys) ImageFileNames.Add(key);
                if (ImageFileNames.Count > 0) SelectedImageFile = ImageFileNames[0];
                UpdateCurrentLabels();
                Progress = 100; ProgressText = "完成";
            });
            AddOrUpdateRecentProject(path); // NEW ensure also when called externally
            await NotifyAsync("提示", "打开成功。", NotificationType.Success).ConfigureAwait(false);
            Logger.Info("Translation file loaded: {file}", path);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "OpenTranslationFileAtPathAsync failed");
            await NotifyAsync("错误", $"加载失败：{ex.Message}", NotificationType.Error).ConfigureAwait(false);
        }
        finally { await RunOnUiAsync(() => IsBusy = false); }
    }

    [RelayCommand]
    public async Task FileSave(string path) => await LabelFileManager1.SaveAsync(path).ConfigureAwait(false);

    [RelayCommand]
    private async Task SaveCurrentCommand()
    {
        if (_dialogs is null) return;
        if (string.IsNullOrEmpty(OpenTranslationFilePath))
        {
            await _dialogs.ShowMessageAsync("未打开翻译文件，无法保存。请使用‘另存为’。").ConfigureAwait(false);
            return;
        }
        try
        {
            await RunOnUiAsync(() => { IsBusy = true; Progress = 10; ProgressText = "保存本地文件..."; });
            await FileSave(OpenTranslationFilePath).ConfigureAwait(false);
            // Remote upload on manual save if collaboration, with conflict detection
            try
            {
                if (Collab is not null)
                {
                    await RunOnUiAsync(() => { Progress = 35; ProgressText = "检查远端版本..."; });
                    var fs = new Services.Api.FileSystemApi(Collab.BaseUrl);
                    var remoteRes = await fs.DownloadAsync(Collab.Token, Collab.RemoteTranslatePath).ConfigureAwait(false);
                    string? remoteHash = null;
                    if (remoteRes.Code == 200 && remoteRes.Content is not null)
                    {
                        var remoteTxt = Encoding.UTF8.GetString(remoteRes.Content);
                        remoteHash = ComputeHash(remoteTxt);
                    }
                    await RunOnUiAsync(() => { Progress = 55; ProgressText = "计算哈希..."; });
                    var localBytes = await File.ReadAllBytesAsync(OpenTranslationFilePath).ConfigureAwait(false);
                    var localTxt = Encoding.UTF8.GetString(localBytes);
                    var localHash = ComputeHash(localTxt);
                    if (!string.IsNullOrEmpty(Collab.LastRemoteHash) && !string.Equals(remoteHash, Collab.LastRemoteHash, StringComparison.Ordinal))
                    {
                        await RunOnUiAsync(() => { ProgressText = "检测到冲突，准备合并..."; Progress = 65; });
                        var remoteTxt = remoteRes.Content is not null ? Encoding.UTF8.GetString(remoteRes.Content) : string.Empty;
                        var fileName = Path.GetFileName(OpenTranslationFilePath);
                        string? merged = null;
                        try
                        {
                            var win = LabelPlus_Next.Views.MainWindow.Instance;
                            if (win is not null)
                            {
                                var dlg = new LabelPlus_Next.Views.Windows.MergeConflictWindow();
                                await NotifyAsync("保存冲突", "检测到远端更新，正在打开合并助手...", NotificationType.Warning).ConfigureAwait(false);
                                merged = await dlg.ShowAsync(win, remoteTxt, localTxt, fileName).ConfigureAwait(false);
                            }
                        }
                        catch (InvalidOperationException ex)
                        {
                            Logger.Warn(ex, "Merge conflict dialog failed to open");
                        }
                        catch (TaskCanceledException ex)
                        {
                            Logger.Warn(ex, "Merge conflict dialog was canceled");
                        }
                        if (merged is null)
                        {
                            // fallback: just backup and notify
                            var dir = Path.GetDirectoryName(OpenTranslationFilePath)!;
                            var conflictDir = Path.Combine(dir, "conflict");
                            Directory.CreateDirectory(conflictDir);
                            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                            var baseName = Path.GetFileNameWithoutExtension(OpenTranslationFilePath);
                            var ext = Path.GetExtension(OpenTranslationFilePath);
                            var remoteBackup = Path.Combine(conflictDir, $"{baseName}_remote_{ts}{ext}");
                            var localBackup = Path.Combine(conflictDir, $"{baseName}_local_{ts}{ext}");
                            if (remoteRes.Content is not null) await File.WriteAllBytesAsync(remoteBackup, remoteRes.Content).ConfigureAwait(false);
                            await File.WriteAllBytesAsync(localBackup, localBytes).ConfigureAwait(false);
                            var msg = $"检测到保存冲突：远端译文已更新。已备份两份：\n{remoteBackup}\n{localBackup}\n请手动合并后再上传。";
                            await NotifyAsync("保存冲突", msg, NotificationType.Warning).ConfigureAwait(false);
                            if (_dialogs is not null) await _dialogs.ShowMessageAsync(msg).ConfigureAwait(false);
                            Logger.Warn("Save conflict: remote changed since last known hash.");
                            return;
                        }

                        // user provided merged content -> upload
                        await RunOnUiAsync(() => { Progress = 85; ProgressText = "上传合并结果到远端..."; });
                        var mergedBytes = Encoding.UTF8.GetBytes(merged);
                        await fs.SafePutAsync(Collab.Token, Collab.RemoteTranslatePath, mergedBytes).ConfigureAwait(false);
                        Collab.LastRemoteHash = ComputeHash(merged);
                        await File.WriteAllBytesAsync(OpenTranslationFilePath, mergedBytes).ConfigureAwait(false);
                        Logger.Info("Merged content uploaded and written locally.");
                        await NotifyAsync("提示", "合并完成并已上传。", NotificationType.Success).ConfigureAwait(false);
                        return;
                    }
                    // no conflict -> upload and update last remote hash
                    await RunOnUiAsync(() => { Progress = 80; ProgressText = "上传到远端..."; });
                    await fs.SafePutAsync(Collab.Token, Collab.RemoteTranslatePath, localBytes).ConfigureAwait(false);
                    Collab.LastRemoteHash = localHash;
                    Logger.Info("Save uploaded to remote: {remote}", Collab.RemoteTranslatePath);
                    await NotifyAsync("提示", "已上传到远端。", NotificationType.Success).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Manual save remote upload failed.");
                await NotifyAsync("错误", $"远端上传失败：{ex.Message}", NotificationType.Error).ConfigureAwait(false);
            }
            var fileNameOnly = Path.GetFileName(OpenTranslationFilePath);
            await NotifyAsync("提示", $"保存成功：{fileNameOnly}", NotificationType.Success).ConfigureAwait(false);
            Logger.Info("File saved: {file}", OpenTranslationFilePath);
        }
        catch (Exception ex)
        {
            await NotifyAsync("错误", $"保存失败：{ex.Message}", NotificationType.Error).ConfigureAwait(false);
            if (_dialogs is not null) { await _dialogs.ShowMessageAsync($"保存失败：{ex.Message}").ConfigureAwait(false); }
            Logger.Error(ex, "Save current file failed.");
        }
        finally { await RunOnUiAsync(() => { Progress = 100; ProgressText = "完成"; IsBusy = false; }); }
    }
    [RelayCommand]
    public async Task SaveAsCommand()
    {
        if (_dialogs is null) return;
        var path = await _dialogs.SaveAsTranslationFileAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            await RunOnUiAsync(() => { IsBusy = true; Progress = 10; ProgressText = "写入文件..."; });
            await FileSave(path).ConfigureAwait(false);
            await RunOnUiAsync(() => { OpenTranslationFilePath = path; Progress = 100; ProgressText = "完成"; });
            var file = Path.GetFileName(path);
            await NotifyAsync("提示", $"另存为成功：{file}", NotificationType.Success).ConfigureAwait(false);
            Logger.Info("File saved as: {file}", path);
        }
        catch (Exception ex)
        {
            await NotifyAsync("错误", $"另存为失败：{ex.Message}", NotificationType.Error).ConfigureAwait(false);
            if (_dialogs is not null) { await _dialogs.ShowMessageAsync($"另存为失败：{ex.Message}").ConfigureAwait(false); }
            Logger.Error(ex, "Save as file failed.");
        }
        finally { await RunOnUiAsync(() => IsBusy = false); }
    }
    [RelayCommand]
    public async Task LoadTranslationFile(string path)
    {
        await OpenTranslationFileAtPathAsync(path).ConfigureAwait(false);
    }
    [RelayCommand]
    private void OpenTranslationFolder()
    {
        try
        {
            var path = OpenTranslationFilePath;
            if (string.IsNullOrWhiteSpace(path)) return;
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
            var psi = new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
            _ = NotifyAsync("提示", $"已打开：{dir}", NotificationType.Information);
        }
        catch (Exception ex) { Logger.Warn(ex, "OpenTranslationFolder failed"); }
    }

    public bool HasUnsavedChanges => LabelFileManager1.StoreManager.IsDirty;
    public bool HasLabelsForFile(string? file)
    {
        if (string.IsNullOrWhiteSpace(file)) return false;
        return LabelFileManager1.StoreManager.Store.TryGetValue(file, out var labels) && labels is { Count: > 0 };
    }

    private async Task NotifyAsync(string title, string message, NotificationType type = NotificationType.Information)
    {
        try
        {
            if (NotificationManager is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() => NotificationManager.Show(new Notification(title, message), showIcon: true, showClose: true, type: type, classes: ["Light"]));
            }
        }
        catch (Exception ex) { Logger.Warn(ex, "NotifyAsync failed: {title} - {message}", title, message); }
    }

    private void StartAutoSave()
    {
        _autoSaveTimer ??= new Timer(async _ => await AutoSaveTickAsync().ConfigureAwait(false), null, _autoSaveInterval, _autoSaveInterval);
        Logger.Info("Auto-save started with interval {minutes} minutes.", _autoSaveInterval.TotalMinutes);
    }
    private void StopAutoSave()
    {
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = null;
        Logger.Info("Auto-save stopped.");
    }

    private async Task<string> BuildSnapshotAsync()
    {
        Logger.Trace("Building snapshot for backup.");
        var header = await LabelFileHeaderManager.GenerateHeaderAsync(LabelFileManager1.FileHead, LabelFileManager1.GroupStringList, LabelFileManager1.Comment).ConfigureAwait(false);
        var sb = new StringBuilder();
        sb.AppendLine(header);
        foreach (var kvp in LabelFileManager1.StoreManager.Store)
        {
            sb.AppendLine();
            sb.AppendLine($">>>>>>>>[{kvp.Key}]<<<<<<<<");
            var count = 0;
            foreach (var n in kvp.Value)
            {
                count++;
                var coord = string.Format(CultureInfo.InvariantCulture, "[{0:F3},{1:F3},{2}]", n.XPercent, n.YPercent, n.Category);
                sb.AppendLine($"----------------[{count}]----------------{coord}");
                sb.AppendLine(n.Text);
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }
    private static string ComputeHash(string content)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }
    private async Task AutoSaveTickAsync()
    {
        if (Interlocked.Exchange(ref _autoSaveBusy, 1) == 1) { Logger.Debug("Skip autosave tick: busy."); return; }
        try
        {
            var path = OpenTranslationFilePath;
            if (string.IsNullOrEmpty(path)) { Logger.Debug("Skip autosave: no open file."); return; }
            if (!HasUnsavedChanges) { Logger.Debug("Skip autosave: no changes."); return; }
            string snapshot;
            try { snapshot = await BuildSnapshotAsync().ConfigureAwait(false); }
            catch (Exception ex) { Logger.Error(ex, "Autosave snapshot build failed."); return; }
            var sig = ComputeHash(snapshot);
            if (string.Equals(sig, _lastBackupSignature, StringComparison.Ordinal)) { Logger.Debug("Skip autosave: snapshot unchanged."); return; }
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) { Logger.Warn("Autosave: cannot resolve directory of {path}.", path); return; }
            var bakDir = Path.Combine(dir, "bak");
            try { Directory.CreateDirectory(bakDir); }
            catch (Exception ex) { Logger.Error(ex, "Autosave: create bak dir failed: {bakDir}", bakDir); return; }
            var fileName = Path.GetFileName(path);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var outName = $"{stamp}_{fileName}";
            var outPath = Path.Combine(bakDir, outName);
            try { await File.WriteAllTextAsync(outPath, snapshot, Encoding.UTF8).ConfigureAwait(false); Logger.Info("Autosave backup created: {outPath}", outPath); }
            catch (Exception ex) { Logger.Error(ex, "Autosave write backup failed: {outPath}", outPath); }
            // NEW: write snapshot directly to active file
            try { await File.WriteAllTextAsync(path, snapshot, Encoding.UTF8).ConfigureAwait(false); Logger.Info("Autosave wrote active file: {path}", path); }
            catch (Exception ex) { Logger.Error(ex, "Autosave write active file failed: {path}", path); }
            _lastBackupSignature = sig;
            try
            {
                if (Collab is not null)
                {
                    var fs = new Services.Api.FileSystemApi(Collab.BaseUrl);
                    string? remoteHash = null;
                    try
                    {
                        var remoteRes = await fs.DownloadAsync(Collab.Token, Collab.RemoteTranslatePath).ConfigureAwait(false);
                        if (remoteRes.Code == 200 && remoteRes.Content is not null)
                        {
                            var remoteTxt = Encoding.UTF8.GetString(remoteRes.Content);
                            remoteHash = ComputeHash(remoteTxt);
                        }
                    }
                    catch (Exception ex2) { Logger.Debug(ex2, "Autosave: fetch remote for conflict check failed."); }
                    var localHash = ComputeHash(snapshot);
                    if (!string.IsNullOrEmpty(Collab.LastRemoteHash) && !string.IsNullOrEmpty(remoteHash) && !string.Equals(remoteHash, Collab.LastRemoteHash, StringComparison.Ordinal))
                    {
                        if (!_autoSaveConflictNotified)
                        {
                            _ = NotifyAsync("自动保存已暂停上传", "检测到远端内容已更新，将在手动保存时提示合并，避免覆盖。", NotificationType.Warning);
                            _autoSaveConflictNotified = true;
                        }
                    }
                    else
                    {
                        var bytes = Encoding.UTF8.GetBytes(snapshot);
                        await fs.SafePutAsync(Collab.Token, Collab.RemoteTranslatePath, bytes).ConfigureAwait(false);
                        Collab.LastRemoteHash = localHash;
                        _autoSaveConflictNotified = false;
                        Logger.Info("Autosave remote uploaded: {remote}", Collab.RemoteTranslatePath);
                    }
                }
            }
            catch (Exception ex) { Logger.Warn(ex, "Autosave remote upload failed."); }
        }
        finally { Interlocked.Exchange(ref _autoSaveBusy, 0); }
    }

    public IReadOnlyCollection<string> GetIncludedImageFiles() => LabelFileManager1.StoreManager.Store.Keys.ToList();
    public async Task AddImageFileAsync(string relativePath)
    {
        await LabelFileManager1.StoreManager.AddFileAsync(relativePath).ConfigureAwait(false);
        Logger.Info("Image added to include list: {file}", relativePath);
    }
    public async Task RemoveImageFileAsync(string relativePath)
    {
        await LabelFileManager1.StoreManager.RemoveFileAsync(relativePath).ConfigureAwait(false);
        Logger.Info("Image removed from include list: {file}", relativePath);
    }
    public void RefreshImagesList()
    {
        if (!OnUiThread) { Dispatcher.UIThread.Post(RefreshImagesList); return; }
        Logger.Debug("Refreshing image list UI.");
        var selected = SelectedImageFile;
        ImageFileNames.Clear();
        foreach (var key in LabelFileManager1.StoreManager.Store.Keys) ImageFileNames.Add(key);
        if (!string.IsNullOrEmpty(selected) && ImageFileNames.Contains(selected)) { SelectedImageFile = selected; }
        else if (ImageFileNames.Count > 0) { SelectedImageFile = ImageFileNames[0]; }
        else { SelectedImageFile = null; }
        UpdateCurrentLabels();
    }
    public void SetSelectedCategory(int category)
    {
        if (SelectedLabel is null) return;
        if (category != 1 && category != 2) return;
        SelectedLabel.Category = category;
        SelectedLabel.CategoryString = category == 1 ? "框内" : "框外";
        LabelFileManager1.StoreManager.TouchDirty();
    }
    public (List<string> groupList, string notes) GetFileSettings() => (new List<string>(LabelFileManager1.GroupStringList), LabelFileManager1.Comment);
    public async Task<bool> SaveFileSettingsAsync(List<string> groupList, string notes)
    {
        LabelFileManager1.UpdateHeader(groupList, notes);
        if (!string.IsNullOrEmpty(OpenTranslationFilePath)) { await FileSave(OpenTranslationFilePath).ConfigureAwait(false); return true; }
        return false;
    }
    public async Task AddLabelAtAsync(float xPercent, float yPercent, int category = 1)
    {
        if (string.IsNullOrEmpty(SelectedImageFile)) return;
        var newLabel = new LabelItem { XPercent = xPercent, YPercent = yPercent, Text = "", Category = category };
        await LabelManager.Instance.AddLabelAsync(LabelFileManager1, SelectedImageFile, newLabel).ConfigureAwait(false);
        await RunOnUiAsync(() =>
        {
            UpdateCurrentLabels();
            if (CurrentLabels.Count > 0)
            {
                SelectedLabel = CurrentLabels[^1];
                CurrentText = SelectedLabel.Text;
            }
        });
    }

    partial void OnOpenTranslationFilePathChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value)) StartAutoSave(); else StopAutoSave();
    }

    partial void OnSelectedImageFileChanged(string? value)
    {
        if (!OnUiThread) { Dispatcher.UIThread.Post(() => OnSelectedImageFileChanged(value)); return; }
        UpdateCurrentLabels();
        if (!string.IsNullOrEmpty(value))
        {
            var imagePath = ResolveImagePath(value);
            if (File.Exists(imagePath)) _ = LoadImageAsync(imagePath);
            else SetDisplayedImage(null, null);
            PrefetchNeighbor(value);
        }
        else { SetDisplayedImage(null, null); }

        if (CurrentLabels.Count > 0)
        {
            SelectedLabel = CurrentLabels[0];
            CurrentText = SelectedLabel.Text;
        }
        else
        {
            SelectedLabel = null;
            CurrentText = string.Empty;
        }
    }

    private readonly Services.BitmapCache _bitmapCache = new();
    private int _imageLoadVersion;

    private string ResolveImagePath(string relative)
    {
        var translationFilePath = OpenTranslationFilePath ?? NewTranslationPath;
        var translationDir = string.IsNullOrEmpty(translationFilePath) ? null : Path.GetDirectoryName(translationFilePath);
        var imagePath = relative;
        if (translationDir != null && !Path.IsPathRooted(imagePath)) imagePath = Path.Combine(translationDir, imagePath);
        imagePath = imagePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Uri.UnescapeDataString(imagePath));
    }

    // Decode off the UI thread; drop results that arrive after the user already moved to another page
    private async Task LoadImageAsync(string imagePath)
    {
        var version = ++_imageLoadVersion;
        var bitmap = await _bitmapCache.GetAsync(imagePath);
        if (version != _imageLoadVersion) return;
        await RunOnUiAsync(() =>
        {
            if (version == _imageLoadVersion) SetDisplayedImage(bitmap, bitmap is null ? null : imagePath);
        });
    }

    private void SetDisplayedImage(IImage? image, string? path)
    {
        if (path is null) _imageLoadVersion++;
        PicImageSource = image;
        _bitmapCache.MarkDisplayed(path);
    }

    private void PrefetchNeighbor(string current)
    {
        var idx = ImageFileNames.IndexOf(current);
        if (idx < 0 || idx + 1 >= ImageFileNames.Count) return;
        var next = ResolveImagePath(ImageFileNames[idx + 1]);
        if (File.Exists(next)) _bitmapCache.Prefetch(next);
    }

    partial void OnSelectedLabelChanged(LabelItem? value) => CurrentText = value?.Text ?? string.Empty;

    partial void OnCurrentTextChanged(string? value)
    {
        if (SelectedLabel != null)
        {
            SelectedLabel.Text = value;
            LabelFileManager1.StoreManager.TouchDirty();
            Logger.Debug("Label text updated: {text}", value);
        }
    }

    private void UpdateCurrentLabels()
    {
        if (!OnUiThread) { Dispatcher.UIThread.Post(UpdateCurrentLabels); return; }
        CurrentLabels.Clear();
        if (!string.IsNullOrEmpty(SelectedImageFile) && LabelFileManager1.StoreManager.Store.TryGetValue(SelectedImageFile, out var labels))
        {
            for (var i = 0; i < labels.Count; i++)
            {
                var item = labels[i];
                item.Index = i + 1;
                item.CategoryString = item.Category == 1 ? "框内" : "框外";
                CurrentLabels.Add(item);
            }
        }
    }

    // 新增：拖拽排序时移动标签
    public void MoveLabelWithinCurrentImage(int oldIndex, int newIndex)
    {
        if (string.IsNullOrEmpty(SelectedImageFile)) return;
        if (!LabelFileManager1.StoreManager.Store.TryGetValue(SelectedImageFile, out var list)) return;
        if (list.Count == 0) return;
        if (oldIndex < 0 || oldIndex >= list.Count) return;
        // 允许插入到末尾
        if (newIndex < 0) newIndex = 0;
        if (newIndex > list.Count) newIndex = list.Count;

        var moved = list[oldIndex];
        LabelFileManager1.StoreManager.MoveLabel(SelectedImageFile, oldIndex, newIndex);
        UpdateCurrentLabels();
        // 保持选中项为移动的对象
        SelectedLabel = moved;
        CurrentText = moved?.Text ?? string.Empty;
    }

    private static readonly string RecentStorePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LabelPlus_Next", "recent_projects.json");

    private sealed class RecentProjectStore
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("translationFile")] public string? TranslationFile { get; set; }
        [JsonPropertyName("folder")] public string? Folder { get; set; }
        [JsonPropertyName("lastOpened")] public DateTimeOffset LastOpened { get; set; }
        [JsonPropertyName("imageCount")] public int ImageCount { get; set; }
    }

    private static void AddOrUpdateRecentProject(string translationFile)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(translationFile)) return;
            var dir = Path.GetDirectoryName(translationFile) ?? string.Empty;
            Directory.CreateDirectory(Path.GetDirectoryName(RecentStorePath)!);
            List<RecentProjectStore> list;
            try
            {
                if (File.Exists(RecentStorePath))
                {
                    var txt = File.ReadAllText(RecentStorePath);
#pragma warning disable IL2026, IL3050
                    list = JsonSerializer.Deserialize<List<RecentProjectStore>>(txt, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<RecentProjectStore>();
#pragma warning restore IL2026, IL3050
                }
                else list = new List<RecentProjectStore>();
            }
            catch (IOException ex)
            {
                Logger.Warn(ex, "Failed to read recent projects store {path}", RecentStorePath);
                list = new List<RecentProjectStore>();
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Warn(ex, "Access denied reading recent projects store {path}", RecentStorePath);
                list = new List<RecentProjectStore>();
            }
            catch (JsonException ex)
            {
                Logger.Warn(ex, "Recent projects store contained invalid JSON {path}", RecentStorePath);
                list = new List<RecentProjectStore>();
            }
            var existing = list.FirstOrDefault(r => string.Equals(r.TranslationFile, translationFile, StringComparison.OrdinalIgnoreCase));
            var name = Path.GetFileName(Path.GetDirectoryName(translationFile)?.TrimEnd(Path.DirectorySeparatorChar) ?? dir);
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(translationFile);
            if (existing is null)
            {
                existing = new RecentProjectStore
                {
                    Name = name ?? string.Empty,
                    TranslationFile = translationFile,
                    Folder = dir,
                    LastOpened = DateTimeOffset.Now,
                    ImageCount = 0
                };
                list.Add(existing);
            }
            else
            {
                existing.LastOpened = DateTimeOffset.Now;
                existing.Folder = dir;
                existing.Name = name ?? existing.Name;
            }
            var ordered = list.OrderByDescending(r => r.LastOpened).ToList();
#pragma warning disable IL2026, IL3050
            var json = JsonSerializer.Serialize(ordered, new JsonSerializerOptions { WriteIndented = true });
#pragma warning restore IL2026, IL3050
            File.WriteAllText(RecentStorePath, json);
        }
        catch (IOException ex)
        {
            Logger.Warn(ex, "Failed to update recent projects store {path}", RecentStorePath);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warn(ex, "Access denied updating recent projects store {path}", RecentStorePath);
        }
        catch (JsonException ex)
        {
            Logger.Warn(ex, "Failed to serialize recent projects store {path}", RecentStorePath);
        }
    }

}
