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
// for IImage
using Notification = Ursa.Controls.Notification;
using WindowNotificationManager = Ursa.Controls.WindowNotificationManager;

namespace LabelPlus_Next.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly LabelFileManager LabelFileManager1 = new();
    private readonly TimeSpan _autoSaveInterval = TimeSpan.FromMinutes(1);
    private int _autoSaveBusy; // 0 = idle, 1 = running

    // Auto-save timer
    private Timer? _autoSaveTimer;

    private readonly IFileDialogService _dialogs;
    private string? _lastBackupSignature; // avoid duplicate backups


    public MainWindowViewModel(IFileDialogService dialogs)
    {
        _dialogs = dialogs;
    }

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

    // Notification manager (injected from MainWindow)
    public WindowNotificationManager? NotificationManager { get; set; }

    public List<string> LangList { get; } = new() { "default", "en", "zh-hant-tw" };

    public bool HasUnsavedChanges
    {
        get => LabelFileManager1.StoreManager.IsDirty;
    }

    // Start/Stop auto-save
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
            catch (Exception ex)
            {
                Logger.Error(ex, "Autosave write failed: {outPath}", outPath);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _autoSaveBusy, 0);
        }
    }

    // Utilities for Image Manager integration
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
        foreach (var key in LabelFileManager1.StoreManager.Store.Keys)
            ImageFileNames.Add(key);
        if (!string.IsNullOrEmpty(selected) && ImageFileNames.Contains(selected))
        {
            SelectedImageFile = selected;
        }
        else if (ImageFileNames.Count > 0)
        {
            SelectedImageFile = ImageFileNames[0];
        }
        else
        {
            SelectedImageFile = null;
        }
        UpdateCurrentLabels();
    }

    // Quickly set selected label category
    public void SetSelectedCategory(int category)
    {
        if (SelectedLabel is null) return;
        if (category != 1 && category != 2) return;
        SelectedLabel.Category = category;
        SelectedLabel.CategoryString = category == 1 ? "框内" : "框外";
        LabelFileManager1.StoreManager.TouchDirty();
    }

    // Expose file header settings
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

    // Add: create label at percent coords and update selection
    public async Task AddLabelAtAsync(float xPercent, float yPercent, int category = 1)
    {
        if (string.IsNullOrEmpty(SelectedImageFile)) return;
        var newLabel = new LabelItem { XPercent = xPercent, YPercent = yPercent, Text = "新标签", Category = category };
        await LabelManager.Instance.AddLabelAsync(LabelFileManager1, SelectedImageFile, newLabel);
        UpdateCurrentLabels();
        if (CurrentLabels.Count > 0)
        {
            SelectedLabel = CurrentLabels[^1];
            CurrentText = SelectedLabel.Text;
        }
    }

    [RelayCommand]
    public async Task AddLabelCommand()
    {
        if (string.IsNullOrEmpty(SelectedImageFile)) return;
        var newLabel = new LabelItem { XPercent = 0.5f, YPercent = 0.5f, Text = "新标签", Category = 1 };
        await LabelManager.Instance.AddLabelAsync(LabelFileManager1, SelectedImageFile, newLabel);
        UpdateCurrentLabels();
        if (CurrentLabels.Count > 0)
        {
            SelectedLabel = CurrentLabels[^1];
            CurrentText = SelectedLabel.Text;
        }
    }

    [RelayCommand]
    public async Task RemoveLabelCommand()
    {
        if (!string.IsNullOrEmpty(SelectedImageFile))
        {
            await LabelManager.Instance.RemoveSelectedAsync(LabelFileManager1, SelectedImageFile, CurrentLabels, SelectedLabel);
            UpdateCurrentLabels();
            SelectedLabel = CurrentLabels.Count > 0 ? CurrentLabels[^1] : null;
            CurrentText = SelectedLabel?.Text ?? string.Empty;
        }
    }

    [RelayCommand]
    public async Task UndoRemoveLabelCommand()
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

    [RelayCommand]
    public async Task NewTranslationCommand()
    {
        if (_dialogs is null) return;
        var folder = await _dialogs.PickFolderAsync("选择翻译目录");
        if (string.IsNullOrEmpty(folder)) return;

        // show image chooser
        var selected = await _dialogs.ChooseImagesAsync(folder);
        if (selected == null || selected.Count == 0) return;

        // Build default header
        var header = string.Join(Environment.NewLine, new[]
        {
            "1,0",
            "-",
            "框内",
            "框外",
            "-",
            "Default Comment",
            " You can edit me",
            string.Empty
        });

        // Compose content
        var nl = Environment.NewLine;
        var sb = new StringBuilder();
        sb.Append(header);
        foreach (var name in selected)
        {
            sb.Append($">>>>>>>>[{name}]<<<<<<<");
            sb.Append(nl);
            sb.Append($">>>>>>>>[{name}]<<<<<<<<");
            sb.Append(nl);
        }

        // Save as translation.txt in folder
        var outPath = Path.Combine(folder, "translation.txt");
        await File.WriteAllTextAsync(outPath, sb.ToString(), Encoding.UTF8);

        await _dialogs.ShowMessageAsync($"创建完成:{nl}{outPath}{nl}共 {selected.Count} 个文件。");
        OpenTranslationFilePath = outPath;
        await LoadTranslationFile(outPath);
    }

    [RelayCommand]
    public async Task OpenTranslationFileCommand()
    {
        if (_dialogs is null) return;
        try
        {
            var path = await _dialogs.OpenTranslationFileAsync();
            if (string.IsNullOrEmpty(path)) return;
            OpenTranslationFilePath = path;
            await LoadTranslationFile(path);
            if (NotificationManager is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    NotificationManager.Show(new Notification("提示", "打开成功。"), showIcon: true, showClose: true, type: NotificationType.Success, classes: ["Light"]));
            }
        }
        catch (Exception ex)
        {
            if (NotificationManager is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    NotificationManager.Show(new Notification("错误", $"打开失败：{ex.Message}"), showIcon: true, showClose: true, type: NotificationType.Error, classes: ["Light"]));
            }
            else if (_dialogs is not null)
            {
                await _dialogs.ShowMessageAsync($"打开失败：{ex.Message}");
            }
            Logger.Error(ex, "Open translation file failed.");
        }
    }

    [RelayCommand]
    public async Task SaveCurrentCommand()
    {
        if (_dialogs is null) return;
        if (string.IsNullOrEmpty(OpenTranslationFilePath))
        {
            await _dialogs.ShowMessageAsync("未打开翻译文件，无法保存。请使用‘另存为’。");
            return;
        }
        try
        {
            await FileSave(OpenTranslationFilePath);
            if (NotificationManager is not null)
            {
                var file = Path.GetFileName(OpenTranslationFilePath);
                await Dispatcher.UIThread.InvokeAsync(() =>
                    NotificationManager.Show(new Notification("提示", $"保存成功：{file}"), showIcon: true, showClose: true, type: NotificationType.Success, classes: ["Light"]));
            }
            Logger.Info("File saved: {file}", OpenTranslationFilePath);
        }
        catch (Exception ex)
        {
            if (NotificationManager is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    NotificationManager.Show(new Notification("错误", $"保存失败：{ex.Message}"), showIcon: true, showClose: true, type: NotificationType.Error, classes: ["Light"]));
            }
            else
            {
                await _dialogs.ShowMessageAsync($"保存失败：{ex.Message}");
            }
            Logger.Error(ex, "Save current file failed.");
        }
    }

    [RelayCommand]
    public async Task SaveAsCommand()
    {
        if (_dialogs is null) return;
        var path = await _dialogs.SaveAsTranslationFileAsync();
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            await FileSave(path);
            OpenTranslationFilePath = path; // future Ctrl+S writes here
            if (NotificationManager is not null)
            {
                var file = Path.GetFileName(path);
                await Dispatcher.UIThread.InvokeAsync(() =>
                    NotificationManager.Show(new Notification("提示", $"另存为成功：{file}"), showIcon: true, showClose: true, type: NotificationType.Success, classes: ["Light"]));
            }
            Logger.Info("File saved as: {file}", path);
        }
        catch (Exception ex)
        {
            if (NotificationManager is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    NotificationManager.Show(new Notification("错误", $"另存为失败：{ex.Message}"), showIcon: true, showClose: true, type: NotificationType.Error, classes: ["Light"]));
            }
            else
            {
                await _dialogs.ShowMessageAsync($"另存为失败：{ex.Message}");
            }
            Logger.Error(ex, "Save as file failed.");
        }
    }

    [RelayCommand]
    public async Task LoadTranslationFile(string path)
    {
        await LabelFileManager1.LoadAsync(path);
        ImageFileNames.Clear();
        foreach (var key in LabelFileManager1.StoreManager.Store.Keys)
            ImageFileNames.Add(key);
        if (ImageFileNames.Count > 0)
            SelectedImageFile = ImageFileNames[0];
        UpdateCurrentLabels();
        // Ensure auto-save is running
        StartAutoSave();
        Logger.Info("Translation file loaded: {file}", path);
    }

    [RelayCommand]
    public async Task FileSave(string path) => await LabelFileManager1.SaveAsync(path);

    partial void OnOpenTranslationFilePathChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            StartAutoSave();
        else
            StopAutoSave();
    }

    partial void OnSelectedImageFileChanged(string? value)
    {
        UpdateCurrentLabels();
        if (!string.IsNullOrEmpty(value))
        {
            var translationFilePath = OpenTranslationFilePath ?? NewTranslationPath;
            string? translationDir = null;
            if (!string.IsNullOrEmpty(translationFilePath))
                translationDir = Path.GetDirectoryName(translationFilePath);

            var imagePath = value;
            if (translationDir != null && !Path.IsPathRooted(imagePath))
                imagePath = Path.Combine(translationDir, imagePath);

            imagePath = imagePath.Replace('/', Path.DirectorySeparatorChar).Replace("\\", Path.DirectorySeparatorChar.ToString());
            imagePath = Uri.UnescapeDataString(imagePath);
            imagePath = Path.GetFullPath(imagePath);

            if (File.Exists(imagePath))
            {
                try { PicImageSource = new Bitmap(imagePath); }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Failed to load image bitmap: {path}", imagePath);
                    PicImageSource = null;
                }
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
        if (!string.IsNullOrEmpty(SelectedImageFile) &&
            LabelFileManager1.StoreManager.Store.TryGetValue(SelectedImageFile, out var labels))
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

    [RelayCommand]
    public async Task ManageImagesCommand()
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
}
