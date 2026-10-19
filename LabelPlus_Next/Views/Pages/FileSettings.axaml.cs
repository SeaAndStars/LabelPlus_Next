using Avalonia.Interactivity;
using LabelPlus_Next.Models;
using LabelPlus_Next.ViewModels;
using System.Reflection;
using Ursa.Controls;

namespace LabelPlus_Next.Views.Pages;

public partial class FileSettings : UrsaWindow
{
    public FileSettings()
    {
        InitializeComponent();
    }

    private FileSettingsViewModel? VM
    {
        get => DataContext as FileSettingsViewModel;
    }

    private void Save(object? sender, RoutedEventArgs e)
    {
        if (Owner is not MainWindow main)
        {
            Close(false);
            return;
        }
        var vm = main.DataContext as MainWindowViewModel;
        if (vm == null)
        {
            Close(false);
            return;
        }

        // Access LabelFileManager and update header
        var managerField = typeof(MainWindowViewModel).GetField("LabelFileManager1", BindingFlags.NonPublic | BindingFlags.Static);
        var manager = managerField?.GetValue(null) as LabelFileManager;
        if (manager != null && VM != null)
        {
            manager.UpdateHeader(VM.GroupList.ToList(), VM.Notes ?? string.Empty);
        }

        Close(true);
    }
}
