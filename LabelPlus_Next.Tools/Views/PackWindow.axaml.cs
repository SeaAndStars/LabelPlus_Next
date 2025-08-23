using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Ursa.Controls;
using LabelPlus_Next.Tools.ViewModels;

namespace LabelPlus_Next.Tools.Views
{
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
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
        }

        private async void OnBrowseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
    }
}
