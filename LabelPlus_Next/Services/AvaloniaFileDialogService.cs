using Avalonia.Controls;
using Antelcat.I18N.Avalonia;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LabelPlus_Next.Lang;
using LabelPlus_Next.ViewModels;
using LabelPlus_Next.Views.Pages;
using Ursa.Controls;
using NLog;

namespace LabelPlus_Next.Services;

public interface ITopLevelProvider
{
    TopLevel? TopLevel { get; set; }
}

public class TopLevelProvider : ITopLevelProvider
{
    private TopLevel? _topLevel;
    public TopLevel? TopLevel
    {
        get => _topLevel is Window w && !w.IsVisible ? null : _topLevel;
        set
        {
            if (_topLevel == value) return;
            if (_topLevel is Window oldWin)
            {
                oldWin.Closed -= OnOwnerClosed;
            }
            _topLevel = value;
            if (_topLevel is Window win)
            {
                win.Closed += OnOwnerClosed;
            }
        }
    }
    private void OnOwnerClosed(object? sender, EventArgs e)
    {
        // Clear reference when window closes to avoid using closed owner
        if (sender == _topLevel) _topLevel = null;
    }
}

public class AvaloniaFileDialogService : IFileDialogService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly ITopLevelProvider _top;
    public AvaloniaFileDialogService(ITopLevelProvider top)
    {
        _top = top;
    }

    private TopLevel? GetSafeTop() => _top.TopLevel is Window w && w.IsVisible ? w : null;

    public async Task<string?> OpenTranslationFileAsync()
    {
        var topLevel = GetSafeTop();
        if (topLevel is null) return null;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = I18NExtension.Translate(LangKeys.openToolStripMenuItem),
            FileTypeFilter = new List<FilePickerFileType> { FilePickerFileTypes.TextPlain }
        });
        if (files == null || files.Count == 0) return null;
        var file = files[0];
        return ToLocalPath(file.Path);
    }

    public async Task<string?> SaveAsTranslationFileAsync(string suggestedFileName = "translation")
    {
        var topLevel = GetSafeTop();
        if (topLevel is null) return null;
        var fileTypeChoices = new List<FilePickerFileType> { new("Text") { Patterns = new List<string> { "*.txt" } } };
        var saveFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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
        var topLevel = GetSafeTop();
        if (topLevel is null) return null;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        if (folders is null || folders.Count == 0) return null;
        return ToLocalPath(folders[0].Path);
    }

    public async Task<IReadOnlyList<string>?> PickFoldersAsync(string title)
    {
        var topLevel = GetSafeTop();
        if (topLevel is null) return null;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = true
        });
        if (folders is null || folders.Count == 0) return null;
        return folders.Select(f => ToLocalPath(f.Path)).Where(p => p != null).Select(p => p!).ToList();
    }

    public async Task<IReadOnlyList<string>?> PickFilesAsync(string title)
    {
        var topLevel = GetSafeTop();
        if (topLevel is null) return null;
        var types = new List<FilePickerFileType>
        {
            new(UiText.T(LangKeys.ui0209, "常见类型")) { Patterns = new List<string> { "*.zip", "*.7z", "*.rar", "*.txt", "*.psd", "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif", "*.webp" } },
            new(UiText.T(LangKeys.ui0210, "压缩包")) { Patterns = new List<string> { "*.zip", "*.7z", "*.rar" } },
            new(UiText.T(LangKeys.ui0211, "文本")) { Patterns = new List<string> { "*.txt" } },
            new(UiText.T(LangKeys.ui0212, "图片")) { Patterns = new List<string> { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif", "*.webp" } },
            new("PSD") { Patterns = new List<string> { "*.psd" } }
        };
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = types
        });
        if (files is null || files.Count == 0) return null;
        return files.Select(f => ToLocalPath(f.Path)).Where(p => p != null).Select(p => p!).ToList();
    }

    public async Task<IReadOnlyList<string>?> ChooseImagesAsync(string folderPath)
    {
        var topLevel = GetSafeTop();
        if (topLevel is null) return null;
        if (!Directory.Exists(folderPath)) return null;
        var exts = new HashSet<string>(new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" }, StringComparer.OrdinalIgnoreCase);
        var files = Directory.EnumerateFiles(folderPath)
            .Where(p => exts.Contains(Path.GetExtension(p)))
            .OrderBy(p => p)
            .ToList();
        var vm = new ImageManagerViewModel { FolderPath = folderPath };
        foreach (var f in files)
            vm.FileFolderList.Add(Path.GetFileName(f));

        if (topLevel is not Window owner) return null;

        var tcs = new TaskCompletionSource<IReadOnlyList<string>?>();
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dlg = new ImageManager { DataContext = vm, CanResize = true, Host = null };
            bool? result = null;
            try
            {
                result = await dlg.ShowDialog<bool?>(owner);
            }
            catch (ObjectDisposedException ex)
            {
                Logger.Warn(ex, "对话框父窗口已释放，改为以非模态方式显示");
                // Owner might have closed; show modeless fallback
                try
                {
                    dlg.Show();
                }
                catch (ObjectDisposedException innerEx)
                {
                    Logger.Warn(innerEx, "图像管理器对话框已被释放");
                }
                catch (InvalidOperationException innerEx)
                {
                    Logger.Warn(innerEx, "无法以非模态方式显示图像管理器对话框");
                }
                catch (Exception innerEx)
                {
                    Logger.Error(innerEx, "显示图像管理器对话框时发生未预期异常");
                    throw;
                }
            }
            catch (InvalidOperationException ex)
            {
                Logger.Warn(ex, "对话框父窗口已关闭，改为以非模态方式显示");
                // Owner might have closed; show modeless fallback
                try
                {
                    dlg.Show();
                }
                catch (ObjectDisposedException innerEx)
                {
                    Logger.Warn(innerEx, "图像管理器对话框已被释放");
                }
                catch (InvalidOperationException innerEx)
                {
                    Logger.Warn(innerEx, "无法以非模态方式显示图像管理器对话框");
                }
                catch (Exception innerEx)
                {
                    Logger.Error(innerEx, "显示图像管理器对话框时发生未预期异常");
                    throw;
                }
            }
            tcs.TrySetResult(result == true ? vm.FileList.ToList() : null);
        });
        return await tcs.Task;
    }

    public async Task ShowMessageAsync(string message)
    {
        var topLevel = GetSafeTop();
        if (topLevel is Window win)
        {
            try { await MessageBox.ShowAsync(message, UiText.T(LangKeys.ui0207, "提示"), MessageBoxIcon.None, MessageBoxButton.OK); return; }
            catch (ObjectDisposedException ex)
            {
                Logger.Warn(ex, "消息对话框所属窗口已释放，将使用无所有者模式");
            }
            catch (InvalidOperationException ex)
            {
                Logger.Warn(ex, "消息对话框显示失败，将使用无所有者模式");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "显示消息对话框时发生未预期异常");
                throw;
            }
        }
        await MessageBox.ShowAsync(message);
    }

    private static string? ToLocalPath(Uri? uri)
    {
        if (uri is null) return null;
        return uri.IsAbsoluteUri ? uri.LocalPath : uri.ToString();
    }
}
