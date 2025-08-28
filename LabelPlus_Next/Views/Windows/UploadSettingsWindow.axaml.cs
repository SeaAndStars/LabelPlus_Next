using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.ViewModels;
using Ursa.Controls;

namespace LabelPlus_Next.Views.Windows;

public partial class UploadSettingsWindow : UrsaWindow
{
    public UploadSettingsWindow()
    {
        InitializeComponent();
        if (DataContext is null) DataContext = new UploadSettingsViewModel();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnSaveAndRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is UploadSettingsViewModel vm)
        {
            await vm.SaveCommand.ExecuteAsync(null);
            Close(true);
        }
        else
        {
            Close(false);
        }
    }
}
