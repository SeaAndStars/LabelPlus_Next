using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Ursa.Controls;
using LabelPlus_Next.Tools.ViewModels;
using LabelPlus_Next.Tools.Models;

namespace LabelPlus_Next.Tools.Views
{
    public partial class MainWindow : UrsaWindow
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void OnUploadClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
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

        private async void OnUploadToFolderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
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
}