using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.ViewModels;

namespace LabelPlus_Next.Views.Windows;

public partial class UploadSettingsWindow : Ursa.Controls.UrsaWindow
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

    private async void OnSaveAndRefreshClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is UploadSettingsViewModel vm)
        {
            await vm.SaveCommand.ExecuteAsync(null);
            this.Close(true);
        }
        else
        {
            this.Close(false);
        }
    }
}
