using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Media; // for IImage
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LabelPlus_Next.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private IFileDialogService? _dialogs;
    protected static LabelFileManager LabelFileManager1 = new();

    public void InitializeServices(IFileDialogService dialogs) => _dialogs ??= dialogs;

    public MainWindowViewModel() { }

    [ObservableProperty] private ObservableCollection<LabelItem> currentLabels = new();
    [ObservableProperty] private string? currentText;
    [ObservableProperty] private ObservableCollection<string> imageFileNames = new();
    [ObservableProperty] private string? newTranslationPath;
    [ObservableProperty] private string? openTranslationFilePath;
    [ObservableProperty] private string? selectedImageFile;
    [ObservableProperty] private LabelItem? selectedLabel;
    [ObservableProperty] private ObservableCollection<LabelItem> deletedLabels = new();
    [ObservableProperty] private IImage? picImageSource;

    public List<string> LangList { get; } = new() { "default", "en", "zh-hant-tw" };

    [ObservableProperty]
    private string? selectedLang = "default";

    public bool HasUnsavedChanges => LabelFileManager1.StoreManager.IsDirty;

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
    public (List<string> groupList, string notes) GetFileSettings()
    {
        return (new List<string>(LabelFileManager1.GroupStringList), LabelFileManager1.Comment);
    }

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
            await LabelManager.Instance.UndoRemoveAsync(LabelFileManager1, SelectedImageFile);
            UpdateCurrentLabels();
            if (SelectedLabel is null && CurrentLabels.Count > 0)
                SelectedLabel = CurrentLabels[^1];
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
        var sb = new System.Text.StringBuilder();
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
        await File.WriteAllTextAsync(outPath, sb.ToString(), System.Text.Encoding.UTF8);

        await _dialogs.ShowMessageAsync($"创建完成:{nl}{outPath}{nl}共 {selected.Count} 个文件。");
        OpenTranslationFilePath = outPath;
        await LoadTranslationFile(outPath);
    }

    [RelayCommand]
    public async Task OpenTranslationFileCommand()
    {
        if (_dialogs is null) return;
        var path = await _dialogs.OpenTranslationFileAsync();
        if (string.IsNullOrEmpty(path)) return;
        OpenTranslationFilePath = path;
        await LoadTranslationFile(path);
        await _dialogs.ShowMessageAsync("打开成功。");
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
        await FileSave(OpenTranslationFilePath);
        await _dialogs.ShowMessageAsync("保存成功。");
    }

    [RelayCommand]
    public async Task SaveAsCommand()
    {
        if (_dialogs is null) return;
        var path = await _dialogs.SaveAsTranslationFileAsync();
        if (string.IsNullOrEmpty(path)) return;
        await FileSave(path);
        OpenTranslationFilePath = path; // future Ctrl+S writes here
        await _dialogs.ShowMessageAsync("另存为成功。");
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
    }

    [RelayCommand]
    public async Task FileSave(string path) => await LabelFileManager1.SaveAsync(path);

    partial void OnSelectedImageFileChanged(string? value)
    {
        UpdateCurrentLabels();
        if (!string.IsNullOrEmpty(value))
        {
            string? translationFilePath = OpenTranslationFilePath ?? NewTranslationPath;
            string? translationDir = null;
            if (!string.IsNullOrEmpty(translationFilePath))
                translationDir = Path.GetDirectoryName(translationFilePath);

            string imagePath = value;
            if (translationDir != null && !Path.IsPathRooted(imagePath))
                imagePath = Path.Combine(translationDir, imagePath);

            imagePath = imagePath.Replace('/', Path.DirectorySeparatorChar).Replace("\\", Path.DirectorySeparatorChar.ToString());
            imagePath = Uri.UnescapeDataString(imagePath);
            imagePath = Path.GetFullPath(imagePath);

            if (File.Exists(imagePath))
            {
                try { PicImageSource = new Bitmap(imagePath); }
                catch { PicImageSource = null; }
            }
            else { PicImageSource = null; }
        }
        else { PicImageSource = null; }

        if (CurrentLabels.Count > 0)
        { SelectedLabel = CurrentLabels[0]; CurrentText = SelectedLabel.Text; }
        else
        { SelectedLabel = null; CurrentText = string.Empty; }
    }

    partial void OnSelectedLabelChanged(LabelItem? value) => CurrentText = value?.Text ?? string.Empty;

    partial void OnCurrentTextChanged(string? value)
    {
        if (SelectedLabel != null)
        {
            SelectedLabel.Text = value;
            LabelFileManager1.StoreManager.TouchDirty();
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