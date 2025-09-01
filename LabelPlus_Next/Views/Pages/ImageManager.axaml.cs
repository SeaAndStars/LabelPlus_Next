using Avalonia.Controls;
using Avalonia.Interactivity;
using LabelPlus_Next.ViewModels;
using NLog;
using Ursa.Controls;
using System.Collections.ObjectModel;

namespace LabelPlus_Next.Views.Pages;

public partial class ImageManager : UrsaWindow
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    // Host view model to operate on current translation session (optional)
    public TranslateViewModel? Host { get; set; }

    public ImageManager()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private ImageManagerViewModel? VM => DataContext as ImageManagerViewModel;

    private static void SortList(ObservableCollection<string> list)
    {
        var sorted = list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        list.Clear();
        foreach (var s in sorted) list.Add(s);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (VM is null) return;

        if (Host is not null)
        {
            // Full manage mode: sync with current TranslateViewModel
            Logger.Info("ImageManager opened (manage mode) for translation: {file}", Host.OpenTranslationFilePath);

            // Load included list from current translation file store keys
            VM.FileList.Clear();
            foreach (var name in Host.GetIncludedImageFiles())
                VM.FileList.Add(name);
            SortList(VM.FileList);

            // Scan directory for all images and diff
            var transPath = Host.OpenTranslationFilePath;
            if (string.IsNullOrEmpty(transPath)) return;
            var dir = Path.GetDirectoryName(transPath)!;
            var all = new List<string>();
            var patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" };
            foreach (var pat in patterns)
            {
                try
                {
                    foreach (var f in Directory.GetFiles(dir, pat, SearchOption.TopDirectoryOnly))
                    {
                        var rel = Path.GetFileName(f);
                        if (!string.IsNullOrEmpty(rel)) all.Add(rel);
                    }
                }
                catch (Exception ex) { Logger.Warn(ex, "Failed to enumerate files with pattern {pat}", pat); }
            }
            // Put those not in included to left list (ignored)
            VM.FileFolderList.Clear();
            var included = VM.FileList.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var f in all.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!included.Contains(f)) VM.FileFolderList.Add(f);
            }
            SortList(VM.FileFolderList);
            Logger.Info("ImageManager loaded: included={included}, ignored={ignored}", VM.FileList.Count, VM.FileFolderList.Count);
        }
        else
        {
            // Picker mode: no host, used by AvaloniaFileDialogService.ChooseImagesAsync
            Logger.Info("ImageManager opened (picker mode) for folder: {folder}", VM.FolderPath);

            // If not pre-populated, scan folder to populate left list
            if ((VM.FileFolderList?.Count ?? 0) == 0 && !string.IsNullOrEmpty(VM.FolderPath) && Directory.Exists(VM.FolderPath))
            {
                var patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" };
                foreach (var pat in patterns)
                {
                    try
                    {
                        foreach (var f in Directory.GetFiles(VM.FolderPath!, pat, SearchOption.TopDirectoryOnly))
                        {
                            var rel = Path.GetFileName(f);
                            if (!string.IsNullOrEmpty(rel) && !VM.FileFolderList!.Contains(rel)) VM.FileFolderList!.Add(rel);
                        }
                    }
                    catch (Exception ex) { Logger.Warn(ex, "Failed to enumerate files with pattern {pat}", pat); }
                }
            }
            SortList(VM.FileFolderList!);
            SortList(VM.FileList!);
        }
    }

    private void SelectOneFile(object? sender, RoutedEventArgs e)
    {
        if (VM?.SelectedFolerFile is string item)
        {
            VM.FileFolderList.Remove(item);
            if (!VM.FileList.Contains(item)) VM.FileList.Add(item);
            SortList(VM.FileList);
            SortList(VM.FileFolderList);
        }
    }

    private void SelectAllFile(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        foreach (var item in VM.FileFolderList.ToList())
        {
            VM.FileFolderList.Remove(item);
            if (!VM.FileList.Contains(item)) VM.FileList.Add(item);
        }
        SortList(VM.FileList);
        SortList(VM.FileFolderList);
    }

    private async void UnselectOneFile(object? sender, RoutedEventArgs e)
    {
        if (VM?.SelectedFile is string item)
        {
            if (Host is not null && Host.HasLabelsForFile(item))
            {
                var res = await MessageBox.ShowAsync($"文件 {item} 仍包含标签，移到忽略将丢失这些标签，是否继续？", "确认", MessageBoxIcon.Warning, MessageBoxButton.YesNo);
                if (res != MessageBoxResult.Yes) return;
            }
            VM.FileList.Remove(item);
            if (!VM.FileFolderList.Contains(item)) VM.FileFolderList.Add(item);
            SortList(VM.FileList);
            SortList(VM.FileFolderList);
        }
    }

    private async void UnselectAllFile(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        // If Host present, warn if any in FileList has labels
        if (Host is not null)
        {
            var candidates = VM.FileList.ToList();
            var withLabels = candidates.Where(f => Host.HasLabelsForFile(f)).ToList();
            if (withLabels.Count > 0)
            {
                var preview = string.Join("\n", withLabels.Take(8));
                var more = withLabels.Count > 8 ? $"\n... 以及另外 {withLabels.Count - 8} 个" : string.Empty;
                var msg = $"下列被移出包含的文件仍有标签，将丢失这些标签：\n{preview}{more}\n是否继续？";
                var res = await MessageBox.ShowAsync(msg, "确认", MessageBoxIcon.Warning, MessageBoxButton.YesNo);
                if (res != MessageBoxResult.Yes) return;
            }
        }
        foreach (var item in VM.FileList.ToList())
        {
            VM.FileList.Remove(item);
            if (!VM.FileFolderList.Contains(item)) VM.FileFolderList.Add(item);
        }
        SortList(VM.FileList);
        SortList(VM.FileFolderList);
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (VM is null)
        {
            Close(false);
            return;
        }

        if (Host is null)
        {
            // Picker mode: just close with true
            Close(true);
            return;
        }

        // Manage mode: apply diffs to host viewmodel
        var before = Host.GetIncludedImageFiles().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var after = VM.FileList.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toRemove = before.Except(after, StringComparer.OrdinalIgnoreCase).ToList();
        var toAdd = after.Except(before, StringComparer.OrdinalIgnoreCase).ToList();

        // Warn if removing files that still have labels
        var withLabels = toRemove.Where(f => Host.HasLabelsForFile(f)).ToList();
        if (withLabels.Count > 0)
        {
            var preview = string.Join("\n", withLabels.Take(8));
            var more = withLabels.Count > 8 ? $"\n... 以及另外 {withLabels.Count - 8} 个" : string.Empty;
            var msg = $"下列被移出包含的文件仍有标签，将丢失这些标签：\n{preview}{more}\n是否继续？";
            var res = await MessageBox.ShowAsync(msg, "确认", MessageBoxIcon.Warning, MessageBoxButton.YesNo);
            if (res != MessageBoxResult.Yes)
            {
                return; // keep dialog open for user to adjust
            }
        }

        Logger.Info("ImageManager confirm: toAdd={add}, toRemove={remove}", toAdd.Count, toRemove.Count);
        foreach (var f in toRemove)
        {
            await Host.RemoveImageFileAsync(f);
        }
        foreach (var f in toAdd)
        {
            await Host.AddImageFileAsync(f);
        }
        Host.RefreshImagesList();
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Logger.Info("ImageManager canceled");
        Close(false);
    }
}
