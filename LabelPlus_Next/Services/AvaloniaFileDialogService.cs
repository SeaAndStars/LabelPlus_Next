using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LabelPlus_Next.Lang;
using LabelPlus_Next.ViewModels;
using LabelPlus_Next.Views.Pages;
using Ursa.Controls;

namespace LabelPlus_Next.Services;

public class AvaloniaFileDialogService : IFileDialogService
{
    private readonly TopLevel _topLevel;
    public AvaloniaFileDialogService(TopLevel topLevel)
    {
        _topLevel = topLevel;
    }

    public async Task<string?> OpenTranslationFileAsync()
    {
        var files = await _topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = I18NExtension.Translate(LangKeys.openToolStripMenuItem),
            FileTypeFilter = new List<FilePickerFileType> { FilePickerFileTypes.TextPlain }
        });
        if (files == null || files.Count == 0) return null;
        var file = files[0];
        return ToLocalPath(file?.Path);
    }

    public async Task<string?> SaveAsTranslationFileAsync(string suggestedFileName = "translation")
    {
        var fileTypeChoices = new List<FilePickerFileType> { new("Text") { Patterns = new List<string> { "*.txt" } } };
        var saveFile = await _topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = I18NExtension.Translate(LangKeys.saveAsDToolStripMenuItem),
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = fileTypeChoices,
            DefaultExtension = ".txt"
        });
        return ToLocalPath(saveFile?.Path);
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await _topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        if (folders is null || folders.Count == 0) return null;
        return ToLocalPath(folders[0]?.Path);
    }

    public async Task<IReadOnlyList<string>?> PickFoldersAsync(string title)
    {
        var folders = await _topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = true
        });
        if (folders is null || folders.Count == 0) return null;
        return folders.Select(f => ToLocalPath(f.Path)!).Where(p => p is not null).ToList();
    }

    public async Task<IReadOnlyList<string>?> PickFilesAsync(string title)
    {
        var types = new List<FilePickerFileType>
        {
            new("常见类型") { Patterns = new List<string> { "*.zip", "*.7z", "*.rar", "*.txt", "*.psd", "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif", "*.webp" } },
            new("压缩包") { Patterns = new List<string> { "*.zip", "*.7z", "*.rar" } },
            new("文本") { Patterns = new List<string> { "*.txt" } },
            new("图片") { Patterns = new List<string> { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif", "*.webp" } },
            new("PSD") { Patterns = new List<string> { "*.psd" } }
        };
        var files = await _topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = types
        });
        if (files is null || files.Count == 0) return null;
        return files.Select(f => ToLocalPath(f.Path)!).Where(p => p is not null).ToList();
    }

    public async Task<IReadOnlyList<string>?> ChooseImagesAsync(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return null;
        var exts = new HashSet<string>(new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" }, StringComparer.OrdinalIgnoreCase);
        var files = Directory.EnumerateFiles(folderPath)
            .Where(p => exts.Contains(Path.GetExtension(p)))
            .OrderBy(p => p)
            .ToList();
        var vm = new ImageManagerViewModel { FolderPath = folderPath };
        foreach (var f in files)
            vm.FileFolderList.Add(Path.GetFileName(f));

        var owner = _topLevel as Window;
        if (owner == null)
            return null;

        var tcs = new TaskCompletionSource<IReadOnlyList<string>?>();
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dlg = new ImageManager { DataContext = vm, Width = 650, Height = 500, CanResize = true, Host = null };
            var result = await dlg.ShowDialog<bool?>(owner);
            tcs.TrySetResult(result == true ? vm.FileList.ToList() : null);
        });
        return await tcs.Task;
    }

    public async Task ShowMessageAsync(string message)
    {
        await MessageBox.ShowAsync(message);
    }

    private static string? ToLocalPath(Uri? uri)
    {
        if (uri is null) return null;
        return uri.IsAbsoluteUri ? uri.LocalPath : uri.ToString();
    }
}
