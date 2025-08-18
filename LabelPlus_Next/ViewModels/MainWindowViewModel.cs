using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Lang;
using LabelPlus_Next.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;

namespace LabelPlus_Next.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    protected static LabelFileManager LabelFileManager1 = new();

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

    // Add: create label at percent coords and update selection
    public async Task AddLabelAtAsync(float xPercent, float yPercent)
    {
        if (string.IsNullOrEmpty(SelectedImageFile)) return;
        var newLabel = new LabelItem
        {
            XPercent = xPercent,
            YPercent = yPercent,
            Text = "新标签",
            Category = 1
        };
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
        if (string.IsNullOrEmpty(SelectedImageFile))
            return;
        var newLabel = new LabelItem
        {
            XPercent = 0.5f,
            YPercent = 0.5f,
            Text = "新标签",
            Category = 1
        };
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
    public async Task FileSave(string path)
    {
        await LabelFileManager1.SaveAsync(path);
    }

    [RelayCommand]
    public async Task NewTranslationCommand() { }

    [RelayCommand]
    public async Task OpenTranslationFileCommand() { }

    [RelayCommand]
    public async Task SaveFileCommand() { }

    [RelayCommand]
    public async Task SaveAsAnotherFileCommand() { }

    [RelayCommand]
    public void ImagineManagerCommand() { }

    [RelayCommand]
    public void OutputCommand() { }

    [RelayCommand]
    public void ViewHelpCommand() { }

    [RelayCommand]
    public void AboutCommand() { }

    partial void OnSelectedImageFileChanged(string? value)
    {
        UpdateCurrentLabels();
        if (!string.IsNullOrEmpty(value))
        {
            string? translationFilePath = OpenTranslationFilePath ?? NewTranslationPath;
            string? translationDir = null;
            if (!string.IsNullOrEmpty(translationFilePath))
            {
                translationDir = System.IO.Path.GetDirectoryName(translationFilePath);
            }

            string imagePath = value;
            if (translationDir != null && !System.IO.Path.IsPathRooted(imagePath))
            {
                imagePath = System.IO.Path.Combine(translationDir, imagePath);
            }

            imagePath = imagePath.Replace('/', System.IO.Path.DirectorySeparatorChar).Replace("\\", System.IO.Path.DirectorySeparatorChar.ToString());
            imagePath = Uri.UnescapeDataString(imagePath);
            imagePath = System.IO.Path.GetFullPath(imagePath);

            if (System.IO.File.Exists(imagePath))
            {
                try
                {
                    PicImageSource = new Bitmap(imagePath);
                }
                catch (Exception ex)
                {
                    PicImageSource = null;
                    Console.WriteLine("Error loading image: " + ex.Message);
                }
            }
            else
            {
                PicImageSource = null;
            }
        }
        else
        {
            PicImageSource = null;
        }

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

    partial void OnSelectedLabelChanged(LabelItem? value)
    {
        CurrentText = value?.Text ?? string.Empty;
    }

    partial void OnCurrentTextChanged(string? value)
    {
        if (SelectedLabel != null)
            SelectedLabel.Text = value;
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
}