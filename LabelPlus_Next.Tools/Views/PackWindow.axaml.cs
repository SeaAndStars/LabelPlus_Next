using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LabelPlus_Next.Tools.ViewModels;
using System.Linq;
using Ursa.Controls;

namespace LabelPlus_Next.Tools.Views;

public partial class PackWindow : UrsaWindow
{
    public PackWindow()
    {
        InitializeComponent();
        // Ensure DataContext at runtime
        DataContext ??= new PackWindowViewModel();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PackWindowViewModel vm) return;
        var top = GetTopLevel(this);
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
}
