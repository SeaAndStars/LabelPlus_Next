using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Models;

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

    public List<string> LangList { get; } = new() { "default", "en", "zh-hant-tw" };

    // 新增：ListBox编号辅助属性
    public int GetLabelIndex(LabelItem item)
    {
        return CurrentLabels.IndexOf(item) + 1;
    }

    // 新增：分类辅助属性
    public string GetCategoryString(LabelItem item)
    {
        return item.Category == 1 ? "框内" : "框外";
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

    partial void OnSelectedImageFileChanged(string? value)
    {
        UpdateCurrentLabels();
        // 修复：切换图片时自动选中第一个标签并同步TextBox
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
        if (value != null)
            CurrentText = value.Text;
        else
            CurrentText = string.Empty;
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

            if (CurrentLabels.Count > 0)
                SelectedLabel = CurrentLabels[0];
        }
    }
}