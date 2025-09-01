using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LabelPlus_Next.Tools.ViewModels;
using System.Linq;

namespace LabelPlus_Next.Tools.Views;

public partial class PackTabView : UserControl
{
    public PackTabView()
    {
        InitializeComponent();
        if (DataContext is null) DataContext = new PackWindowViewModel();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private async void OnBrowseFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PackWindowViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择要打包的目录",
            AllowMultiple = false
        });
        var folder = folders?.FirstOrDefault();
        if (folder?.Path is null) return;
        await vm.SetFolderAsync(folder.Path.LocalPath);
    }

    private async void OnBrowseSolutionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PackWindowViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择解决方案文件 (.sln)",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Solution") { Patterns = new[] { "*.sln" } } }
        });
        var file = files?.FirstOrDefault();
        if (file?.Path is null) return;
        vm.SolutionPath = file.Path.LocalPath;
    }
}
