using Avalonia.Controls.Notifications;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using NLog;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Notification = Ursa.Controls.Notification;
using WindowNotificationManager = Ursa.Controls.WindowNotificationManager;

namespace LabelPlus_Next.ViewModels;

using Notification = Notification;
using WindowNotificationManager = WindowNotificationManager;

public partial class TranslateViewModel : ViewModelBase
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    protected static LabelFileManager LabelFileManager1 = new();
    private readonly TimeSpan _autoSaveInterval = TimeSpan.FromMinutes(1);
    private int _autoSaveBusy;
    private Timer? _autoSaveTimer;
    private IFileDialogService? _dialogs;
    private string? _lastBackupSignature;
    private bool _autoSaveConflictNotified; // 防止重复通知
    public CollaborationSession? Collab { get; set; } // null for local-only
    
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
    public WindowNotificationManager? NotificationManager { get; set; }
    public List<string> LangList { get; } = new() { "default", "en", "zh-hant-tw" };

    public bool HasUnsavedChanges
    {
        get => LabelFileManager1.StoreManager.IsDirty;
    }

    public void InitializeServices(IFileDialogService dialogs)
    {
        _dialogs ??= dialogs;
        Logger.Debug("Services initialized.");
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
        catch (Exception ex)
        {
            Logger.Warn(ex, "NotifyAsync failed: {title} - {message}", title, message);
        }
    }

    private void StartAutoSave()
    {
        _autoSaveTimer ??= new Timer(async _ => await AutoSaveTickAsync(), null, _autoSaveInterval, _autoSaveInterval);
        Logger.Info("Auto-save started with interval {minutes} minutes.", _autoSaveInterval.TotalMinutes);
    }
    public void StopAutoSave()
    {
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = null;
        Logger.Info("Auto-save stopped.");
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
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            _ = NotifyAsync("提示", $"已打开：{dir}", NotificationType.Information);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "OpenTranslationFolder failed");
        }
    }

    private async Task<string> BuildSnapshotAsync()
    {
        Logger.Trace("Building snapshot for backup.");
        var header = await LabelFileHeaderManager.GenerateHeaderAsync(LabelFileManager1.FileHead, LabelFileManager1.GroupStringList, LabelFileManager1.Comment);
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
        if (Interlocked.Exchange(ref _autoSaveBusy, 1) == 1)
        {
            Logger.Debug("Skip autosave tick: busy.");
            return;
        }
        try
        {
            var path = OpenTranslationFilePath;
            if (string.IsNullOrEmpty(path))
            {
                Logger.Debug("Skip autosave: no open file.");
                return;
            }
            if (!HasUnsavedChanges)
            {
                Logger.Debug("Skip autosave: no changes.");
                return;
            }
            string snapshot;
            try { snapshot = await BuildSnapshotAsync(); }
            catch (Exception ex)
            {
                Logger.Error(ex, "Autosave snapshot build failed.");
                return;
            }
            var sig = ComputeHash(snapshot);
            if (string.Equals(sig, _lastBackupSignature, StringComparison.Ordinal))
            {
                Logger.Debug("Skip autosave: snapshot unchanged.");
                return;
            }
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir))
            {
                Logger.Warn("Autosave: cannot resolve directory of {path}.", path);
                return;
            }
            var bakDir = Path.Combine(dir, "bak");
            try { Directory.CreateDirectory(bakDir); }
            catch (Exception ex)
            {
                Logger.Error(ex, "Autosave: create bak dir failed: {bakDir}", bakDir);
                return;
            }
            var fileName = Path.GetFileName(path);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var outName = $"{stamp}_{fileName}";
            var outPath = Path.Combine(bakDir, outName);
            try
            {
                await File.WriteAllTextAsync(outPath, snapshot, Encoding.UTF8);
                _lastBackupSignature = sig;
                Logger.Info("Autosave created: {outPath}", outPath);
            }
            catch (Exception ex) { Logger.Error(ex, "Autosave write failed: {outPath}", outPath); }

            // Remote auto-upload when in collaboration session（带冲突检查：若远端有更新则不上传）
            try
            {
                if (Collab is not null)
                {
                    var fs = new Services.Api.FileSystemApi(Collab.BaseUrl);
                    string? remoteHash = null;
                    try
                    {
                        var remoteRes = await fs.DownloadAsync(Collab.Token, Collab.RemoteTranslatePath);
                        if (remoteRes.Code == 200 && remoteRes.Content is not null)
                        {
                            var remoteTxt = Encoding.UTF8.GetString(remoteRes.Content);
                            remoteHash = ComputeHash(remoteTxt);
                        }
                    }
                    catch (Exception ex2)
                    {
                        Logger.Debug(ex2, "Autosave: fetch remote for conflict check failed.");
                    }

                    var localHash = ComputeHash(snapshot);
                    // 若记录了上次远端哈希且检测到远端已变化，则不进行自动上传，仅做本地备份
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
                        await fs.SafePutAsync(Collab.Token, Collab.RemoteTranslatePath, bytes);
                        Collab.LastRemoteHash = localHash; // 无冲突时更新远端基线
                        _autoSaveConflictNotified = false;   // 一旦成功上传则清除提示状态
                        Logger.Info("Autosave remote uploaded: {remote}", Collab.RemoteTranslatePath);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Autosave remote upload failed.");
            }
        }
        finally { Interlocked.Exchange(ref _autoSaveBusy, 0); }
    }

    public IReadOnlyCollection<string> GetIncludedImageFiles() => LabelFileManager1.StoreManager.Store.Keys.ToList();
    public async Task AddImageFileAsync(string relativePath)
    {
        await LabelFileManager1.StoreManager.AddFileAsync(relativePath);
        Logger.Info("Image added to include list: {file}", relativePath);
    }
    public async Task RemoveImageFileAsync(string relativePath)
    {
        await LabelFileManager1.StoreManager.RemoveFileAsync(relativePath);
        Logger.Info("Image removed from include list: {file}", relativePath);
    }
    public void RefreshImagesList()
    {
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
        if (!string.IsNullOrEmpty(OpenTranslationFilePath))
        {
            await FileSave(OpenTranslationFilePath);
            return true;
        }
        return false;
    }
    public async Task AddLabelAtAsync(float xPercent, float yPercent, int category = 1)
    {
        if (string.IsNullOrEmpty(SelectedImageFile)) return;
        var newLabel = new LabelItem { XPercent = xPercent, YPercent = yPercent, Text = "", Category = category };
        await LabelManager.Instance.AddLabelAsync(LabelFileManager1, SelectedImageFile, newLabel);
        UpdateCurrentLabels();
        if (CurrentLabels.Count > 0)
        {
            SelectedLabel = CurrentLabels[^1];
            CurrentText = SelectedLabel.Text;
        }
    }
    [RelayCommand] public async Task AddLabelCommand()
    {
        if (string.IsNullOrEmpty(SelectedImageFile)) return;
        var newLabel = new LabelItem { XPercent = 0.5f, YPercent = 0.5f, Text = "", Category = 1 };
        await LabelManager.Instance.AddLabelAsync(LabelFileManager1, SelectedImageFile, newLabel);
        UpdateCurrentLabels();
        if (CurrentLabels.Count > 0)
        {
            SelectedLabel = CurrentLabels[^1];
            CurrentText = SelectedLabel.Text;
        }
    }
    [RelayCommand] public async Task RemoveLabelCommand()
    {
        if (!string.IsNullOrEmpty(SelectedImageFile))
        {
            // Keep selection near the deleted item instead of jumping to the last
            var oldIndex = SelectedLabel is null ? -1 : CurrentLabels.IndexOf(SelectedLabel);
            await LabelManager.Instance.RemoveSelectedAsync(LabelFileManager1, SelectedImageFile, CurrentLabels, SelectedLabel);
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
        }
    }
    [RelayCommand] public async Task UndoRemoveLabelCommand()
    {
        if (!string.IsNullOrEmpty(SelectedImageFile))
        {
            var restored = await LabelManager.Instance.UndoRemoveAsync(LabelFileManager1, SelectedImageFile);
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
        }
    }
    [RelayCommand] public async Task NewTranslationCommand()
    {
        if (_dialogs is null) return;
        IsBusy = true; Progress = 5; ProgressText = "选择图片...";
        try
        {
            var folder = await _dialogs.PickFolderAsync("选择翻译目录");
            if (string.IsNullOrEmpty(folder))
            {
                Progress = 100; ProgressText = "已取消";
                return;
            }
            var selected = await _dialogs.ChooseImagesAsync(folder);
            if (selected == null || selected.Count == 0)
            {
                Progress = 100; ProgressText = "未选择图片";
                return;
            }

            Progress = 25; ProgressText = "生成初始内容...";
            var names = selected.Select(p => Path.GetFileName(p))
                                .Where(n => !string.IsNullOrWhiteSpace(n))
                                .Select(n => n!);
            var content = TranslationFileUtils.BuildInitialContent(names);
            var outPath = TranslationFileUtils.GetNonConflictPath(folder, "translation.txt");
            Progress = 60; ProgressText = "写入文件...";
            await File.WriteAllTextAsync(outPath, content, Encoding.UTF8);
            Progress = 80; ProgressText = "加载到编辑器...";
            await _dialogs.ShowMessageAsync($"创建完成:{Environment.NewLine}{outPath}{Environment.NewLine}共 {selected.Count} 个文件。");
            OpenTranslationFilePath = outPath;
            await LoadTranslationFile(outPath);
            await NotifyAsync("提示", "新建翻译文件完成并已打开。", NotificationType.Success);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "NewTranslationCommand failed");
            await NotifyAsync("错误", $"新建翻译失败：{ex.Message}", NotificationType.Error);
        }
        finally
        {
            Progress = 100; ProgressText = "完成"; IsBusy = false;
        }
    }
    [RelayCommand] public async Task OpenTranslationFileCommand()
    {
        if (_dialogs is null) return;
        try
        {
            IsBusy = true; Progress = 10; ProgressText = "选择文件...";
            var path = await _dialogs.OpenTranslationFileAsync();
            if (string.IsNullOrEmpty(path)) return;
            Progress = 40; ProgressText = "读取内容...";
            OpenTranslationFilePath = path;
            await LoadTranslationFile(path);
            Progress = 100; ProgressText = "完成";
            await NotifyAsync("提示", "打开成功。", NotificationType.Success);
        }
        catch (Exception ex)
        {
            await NotifyAsync("错误", $"打开失败：{ex.Message}", NotificationType.Error);
            if (_dialogs is not null) { await _dialogs.ShowMessageAsync($"打开失败：{ex.Message}"); }
            Logger.Error(ex, "Open translation file failed.");
        }
        finally { IsBusy = false; }
    }
    [RelayCommand] public async Task SaveCurrentCommand()
    {
        if (_dialogs is null) return;
        if (string.IsNullOrEmpty(OpenTranslationFilePath))
        {
            await _dialogs.ShowMessageAsync("未打开翻译文件，无法保存。请使用‘另存为’。");
            return;
        }
        try
        {
            IsBusy = true; Progress = 10; ProgressText = "保存本地文件...";
            await FileSave(OpenTranslationFilePath);
            // Remote upload on manual save if collaboration, with conflict detection
            try
            {
                if (Collab is not null)
                {
                    Progress = 35; ProgressText = "检查远端版本...";
                    var fs = new Services.Api.FileSystemApi(Collab.BaseUrl);
                    // fetch remote to compute current hash
                    var remoteRes = await fs.DownloadAsync(Collab.Token, Collab.RemoteTranslatePath);
                    string? remoteHash = null;
                    if (remoteRes.Code == 200 && remoteRes.Content is not null)
                    {
                        var remoteTxt = Encoding.UTF8.GetString(remoteRes.Content);
                        remoteHash = ComputeHash(remoteTxt);
                    }

                    Progress = 55; ProgressText = "计算哈希...";
                    var localBytes = await File.ReadAllBytesAsync(OpenTranslationFilePath);
                    var localTxt = Encoding.UTF8.GetString(localBytes);
                    var localHash = ComputeHash(localTxt);

                    if (!string.IsNullOrEmpty(Collab.LastRemoteHash) && !string.Equals(remoteHash, Collab.LastRemoteHash, StringComparison.Ordinal))
                    {
                        ProgressText = "检测到冲突，准备合并..."; Progress = 65;
                        // conflict detected: open merge assistant
                        var remoteTxt = remoteRes.Content is not null ? Encoding.UTF8.GetString(remoteRes.Content) : string.Empty;
                        var fileName = Path.GetFileName(OpenTranslationFilePath);
                        string? merged = null;
                        try
                        {
                            var win = LabelPlus_Next.Views.MainWindow.Instance;
                            if (win is not null)
                            {
                                var dlg = new LabelPlus_Next.Views.Windows.MergeConflictWindow();
                                await NotifyAsync("保存冲突", "检测到远端更新，正在打开合并助手...", NotificationType.Warning);
                                merged = await dlg.ShowAsync(win, remoteTxt, localTxt, fileName);
                            }
                        }
                        catch { }
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
                            if (remoteRes.Content is not null) await File.WriteAllBytesAsync(remoteBackup, remoteRes.Content);
                            await File.WriteAllBytesAsync(localBackup, localBytes);
                            var msg = $"检测到保存冲突：远端译文已更新。已备份两份：\n{remoteBackup}\n{localBackup}\n请手动合并后再上传。";
                            await NotifyAsync("保存冲突", msg, NotificationType.Warning);
                            if (_dialogs is not null) await _dialogs.ShowMessageAsync(msg);
                            Logger.Warn("Save conflict: remote changed since last known hash.");
                            return;
                        }

                        // user provided merged content -> upload
                        Progress = 85; ProgressText = "上传合并结果到远端...";
                        var mergedBytes = Encoding.UTF8.GetBytes(merged);
                        await fs.SafePutAsync(Collab.Token, Collab.RemoteTranslatePath, mergedBytes);
                        Collab.LastRemoteHash = ComputeHash(merged);
                        await File.WriteAllBytesAsync(OpenTranslationFilePath, mergedBytes);
                        Logger.Info("Merged content uploaded and written locally.");
                        await NotifyAsync("提示", "合并完成并已上传。", NotificationType.Success);
                        return;
                    }

                    // no conflict -> upload and update last remote hash
                    Progress = 80; ProgressText = "上传到远端...";
                    await fs.SafePutAsync(Collab.Token, Collab.RemoteTranslatePath, localBytes);
                    Collab.LastRemoteHash = localHash;
                    Logger.Info("Save uploaded to remote: {remote}", Collab.RemoteTranslatePath);
                    await NotifyAsync("提示", "已上传到远端。", NotificationType.Success);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Manual save remote upload failed.");
                await NotifyAsync("错误", $"远端上传失败：{ex.Message}", NotificationType.Error);
            }
            var fileNameOnly = Path.GetFileName(OpenTranslationFilePath);
            await NotifyAsync("提示", $"保存成功：{fileNameOnly}", NotificationType.Success);
            Logger.Info("File saved: {file}", OpenTranslationFilePath);
        }
        catch (Exception ex)
        {
            await NotifyAsync("错误", $"保存失败：{ex.Message}", NotificationType.Error);
            if (_dialogs is not null) { await _dialogs.ShowMessageAsync($"保存失败：{ex.Message}"); }
            Logger.Error(ex, "Save current file failed.");
        }
        finally { Progress = 100; ProgressText = "完成"; IsBusy = false; }
    }
    [RelayCommand] public async Task SaveAsCommand()
    {
        if (_dialogs is null) return;
        var path = await _dialogs.SaveAsTranslationFileAsync();
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            IsBusy = true; Progress = 10; ProgressText = "写入文件...";
            await FileSave(path);
            OpenTranslationFilePath = path;
            Progress = 100; ProgressText = "完成";
            var file = Path.GetFileName(path);
            await NotifyAsync("提示", $"另存为成功：{file}", NotificationType.Success);
            Logger.Info("File saved as: {file}", path);
        }
        catch (Exception ex)
        {
            await NotifyAsync("错误", $"另存为失败：{ex.Message}", NotificationType.Error);
            if (_dialogs is not null) { await _dialogs.ShowMessageAsync($"另存为失败：{ex.Message}"); }
            Logger.Error(ex, "Save as file failed.");
        }
        finally { IsBusy = false; }
    }
    [RelayCommand] public async Task LoadTranslationFile(string path)
    {
    IsBusy = true; Progress = 15; ProgressText = "读取翻译文件...";
    OpenTranslationFilePath = path;
    await LabelFileManager1.LoadAsync(path);
        // Initialize remote hash if in collaboration session
        try
        {
            if (Collab is not null)
            {
        Progress = 55; ProgressText = "同步远端状态...";
                var fs = new Services.Api.FileSystemApi(Collab.BaseUrl);
                var res = await fs.DownloadAsync(Collab.Token, Collab.RemoteTranslatePath);
                if (res.Code == 200 && res.Content is not null)
                {
                    var txt = Encoding.UTF8.GetString(res.Content);
                    Collab.LastRemoteHash = ComputeHash(txt);
                }
                else Collab.LastRemoteHash = null;
            }
        }
        catch (Exception ex) { Logger.Warn(ex, "Init remote hash on load failed"); }
        ImageFileNames.Clear();
        foreach (var key in LabelFileManager1.StoreManager.Store.Keys) ImageFileNames.Add(key);
        if (ImageFileNames.Count > 0) SelectedImageFile = ImageFileNames[0];
        UpdateCurrentLabels();
        StartAutoSave();
    Progress = 100; ProgressText = "完成"; IsBusy = false;
        Logger.Info("Translation file loaded: {file}", path);
    }
    [RelayCommand] public async Task FileSave(string path) => await LabelFileManager1.SaveAsync(path);
    partial void OnOpenTranslationFilePathChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value)) StartAutoSave();
        else StopAutoSave();
    }
    partial void OnSelectedImageFileChanged(string? value)
    {
        UpdateCurrentLabels();
        if (!string.IsNullOrEmpty(value))
        {
            var translationFilePath = OpenTranslationFilePath ?? NewTranslationPath;
            var translationDir = !string.IsNullOrEmpty(translationFilePath) ? Path.GetDirectoryName(translationFilePath) : null;
            var imagePath = value;
            if (!string.IsNullOrEmpty(translationDir) && !Path.IsPathRooted(imagePath))
                imagePath = Path.Combine(translationDir!, imagePath);
            imagePath = imagePath.Replace('/', Path.DirectorySeparatorChar);
            imagePath = Uri.UnescapeDataString(imagePath);
            var fullPath = Path.GetFullPath(imagePath);
            Logger.Debug("Resolve image path: base={base}, value={value}, full={full}, exists={exists}", translationDir, value, fullPath, File.Exists(fullPath));
            if (File.Exists(fullPath))
            {
                try { PicImageSource = new Bitmap(fullPath); }
                catch { PicImageSource = null; }
            }
            else { PicImageSource = null; }
        }
        else { PicImageSource = null; }
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
    [RelayCommand] public async Task ManageImagesCommand()
    {
        if (_dialogs is null) return;
        var folder = await _dialogs.PickFolderAsync("选择图片目录");
        if (string.IsNullOrEmpty(folder)) return;
        var selected = await _dialogs.ChooseImagesAsync(folder);
        if (selected == null)
        {
            await _dialogs.ShowMessageAsync("已取消。");
            return;
        }
        await _dialogs.ShowMessageAsync($"已选择 {selected.Count} 个文件。");
    }
    public bool HasLabelsForFile(string file)
        => LabelFileManager1.StoreManager.HasLabels(file);
}
