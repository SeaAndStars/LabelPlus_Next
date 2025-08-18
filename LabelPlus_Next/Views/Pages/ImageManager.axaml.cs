using Avalonia.Controls;
using Avalonia.Interactivity;
using LabelPlus_Next.ViewModels;
using System.Linq;

namespace LabelPlus_Next.Views.Pages;

public partial class ImageManager : Window
{
    public ImageManager()
    {
        InitializeComponent();
    }

    private ImageManagerViewModel? VM => DataContext as ImageManagerViewModel;

    private void SelectOneFile(object? sender, RoutedEventArgs e)
    {
        if (VM?.SelectedFolerFile is string item)
        {
            VM.FileFolderList.Remove(item);
            if (!VM.FileList.Contains(item)) VM.FileList.Add(item);
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
    }

    private void UnselectOneFile(object? sender, RoutedEventArgs e)
    {
        if (VM?.SelectedFile is string item)
        {
            VM.FileList.Remove(item);
            if (!VM.FileFolderList.Contains(item)) VM.FileFolderList.Add(item);
        }
    }

    private void UnselectAllFile(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        foreach (var item in VM.FileList.ToList())
        {
            VM.FileList.Remove(item);
            if (!VM.FileFolderList.Contains(item)) VM.FileFolderList.Add(item);
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}