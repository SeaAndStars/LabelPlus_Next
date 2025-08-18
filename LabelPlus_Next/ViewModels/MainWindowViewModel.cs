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

    private Stack<(LabelItem, int)> deletedLabelStack = new();

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
        await LabelFileManager1.StoreManager.AddLabelAsync(SelectedImageFile, newLabel);
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
        // 1. 检查当前选中的图片文件
        if (string.IsNullOrEmpty(SelectedImageFile))
            return;

        // 2. 创建一个新的LabelItem（默认参数，可根据需要调整）
        var newLabel = new LabelItem
        {
            XPercent = 0.5f,
            YPercent = 0.5f,
            Text = "新标签",
            Category = 1
        };

        // 3. 添加到StoreManager
        await LabelFileManager1.StoreManager.AddLabelAsync(SelectedImageFile, newLabel);

        // 4. 刷新当前标签列表
        UpdateCurrentLabels();

        // 5. 选中新添加的标签
        if (CurrentLabels.Count > 0)
        {
            SelectedLabel = CurrentLabels[^1];
            CurrentText = SelectedLabel.Text;
        }
    }

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

    [RelayCommand]
    public async Task RemoveLabelCommand()
    {
        if (SelectedLabel != null && !string.IsNullOrEmpty(SelectedImageFile))
        {
            var index = CurrentLabels.IndexOf(SelectedLabel);
            deletedLabelStack.Push((SelectedLabel, index));
            await LabelFileManager1.StoreManager.RemoveLabelAsync(SelectedImageFile, index);
            UpdateCurrentLabels();
            SelectedLabel = CurrentLabels.Count > 0 ? CurrentLabels[^1] : null;
            CurrentText = SelectedLabel?.Text ?? string.Empty;
        }
    }

    [RelayCommand]
    public async Task UndoRemoveLabelCommand()
    {
        if (deletedLabelStack.Count > 0 && !string.IsNullOrEmpty(SelectedImageFile))
        {
            var (label, index) = deletedLabelStack.Pop();
            if (!LabelFileManager1.StoreManager.Store.ContainsKey(SelectedImageFile))
                LabelFileManager1.StoreManager.Store[SelectedImageFile] = new List<LabelItem>();
            var labels = LabelFileManager1.StoreManager.Store[SelectedImageFile];
            if (index > labels.Count) index = labels.Count;
            labels.Insert(index, label);
            UpdateCurrentLabels();
            SelectedLabel = label;
            CurrentText = label.Text;
        }
    }

    [RelayCommand]
    public async Task NewTranslationCommand()
    {
        // 这里假设有 StorageProvider 可用，实际项目中需注入或传递
        // 这里只做演示，实际调用需在 View 层传递 StorageProvider
    }

    [RelayCommand]
    public async Task OpenTranslationFileCommand()
    {
        // 这里只做演示，实际调用需在 View 层传递 StorageProvider
    }

    [RelayCommand]
    public async Task SaveFileCommand()
    {
        // 这里只做演示，实际调用需在 View 层传递 StorageProvider
    }

    [RelayCommand]
    public async Task SaveAsAnotherFileCommand()
    {
        // 这里只做演示，实际调用需在 View 层传递 StorageProvider
    }

    [RelayCommand]
    public void ImagineManagerCommand()
    {
        // 打开图片管理窗口，实际应由 View 层实现
    }

    [RelayCommand]
    public void OutputCommand()
    {
        // 打开输出窗口，实际应由 View 层实现
    }

    [RelayCommand]
    public void ViewHelpCommand()
    {
        // 打开帮助，实际应由 View 层实现
    }

    [RelayCommand]
    public void AboutCommand()
    {
        // 打开关于，实际应由 View 层实现
    }

    partial void OnSelectedImageFileChanged(string? value)
    {
        UpdateCurrentLabels();
        if (!string.IsNullOrEmpty(value))
        {
            // 获取翻译文件的路径
            string? translationFilePath = OpenTranslationFilePath ?? NewTranslationPath;
            string? translationDir = null;
            if (!string.IsNullOrEmpty(translationFilePath))
            {
                translationDir = System.IO.Path.GetDirectoryName(translationFilePath);
            }

            // 构建完整的图像路径
            string imagePath = value;
            if (translationDir != null && !System.IO.Path.IsPathRooted(imagePath))
            {
                imagePath = System.IO.Path.Combine(translationDir, imagePath);
            }

            // 确保路径分隔符符合当前平台的格式
            imagePath = imagePath.Replace('/', System.IO.Path.DirectorySeparatorChar).Replace("\\", System.IO.Path.DirectorySeparatorChar.ToString());
            imagePath = Uri.UnescapeDataString(imagePath);
            imagePath = System.IO.Path.GetFullPath(imagePath);

            // 检查文件是否存在
            if (System.IO.File.Exists(imagePath))
            {
                try
                {
                    PicImageSource = new Bitmap(imagePath);
                }
                catch (Exception ex)
                {
                    // 如果加载失败，PicImageSource置为null
                    PicImageSource = null;
                    Console.WriteLine("Error loading image: " + ex.Message);
                }
            }
            else
            {
                // 如果文件不存在，PicImageSource也置为null
                PicImageSource = null;
            }
        }
        else
        {
            // 如果路径为空，PicImageSource置为null
            PicImageSource = null;
        }

        // 更新当前标签
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
            // 移除自动设置 SelectedLabel，交由外部逻辑处理
        }
    }
}