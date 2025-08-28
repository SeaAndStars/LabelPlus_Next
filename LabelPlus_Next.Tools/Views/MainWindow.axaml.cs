using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LabelPlus_Next.Tools.Models;
using LabelPlus_Next.Tools.ViewModels;
using System.Collections.Generic;
using Ursa.Controls;

namespace LabelPlus_Next.Tools.Views;

public partial class MainWindow : UrsaWindow
{
    public MainWindow()
    {
        InitializeComponent();

        var tree = this.FindControl<TreeView>("Tree");
        if (tree != null)
        {
            tree.AddHandler(TreeViewItem.ExpandedEvent, OnTreeItemExpanded, RoutingStrategies.Bubble);
        }
    }

    private void OnOpenPackWindow(object? sender, RoutedEventArgs e)
    {
        var win = new PackWindow();
        win.Show(this);
    }

    private async void OnTreeItemExpanded(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not TreeViewItem tvi) return;
        if (tvi.DataContext is not DavNode node) return;
        if (DataContext is not MainWindowViewModel vm) return;
        await vm.RefreshNodeAsync(node);
    }

    private async void OnUploadClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var provider = GetTopLevel(this)?.StorageProvider;
        if (provider is null) return;
        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要上传的文件",
            AllowMultiple = true
        });
        if (files is null || files.Count == 0) return;
        var paths = new List<string>();
        foreach (var f in files)
        {
            if (f.Path is not null)
            {
                paths.Add(f.Path.LocalPath);
            }
        }
        if (paths.Count > 0)
        {
            await vm.UploadFilesAsync(paths);
        }
    }

    private async void OnUploadToFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var provider = GetTopLevel(this)?.StorageProvider;
        if (provider is null) return;
        if (this.FindControl<TreeView>("Tree")?.SelectedItem is not DavNode node || !node.IsCollection) return;

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要上传的文件 (目标: 选中文件夹)",
            AllowMultiple = true
        });
        if (files is null || files.Count == 0) return;
        var paths = new List<string>();
        foreach (var f in files)
        {
            if (f.Path is not null)
            {
                paths.Add(f.Path.LocalPath);
            }
        }
        if (paths.Count > 0)
        {
            await vm.UploadFilesAsync(paths, node.Uri);
        }
    }
}
