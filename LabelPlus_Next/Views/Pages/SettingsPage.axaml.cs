using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.ViewModels;
using LabelPlus_Next.Views.Windows;

namespace LabelPlus_Next.Views.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnVerifyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            await vm.VerifyHttpAsync();
        }
    }

    private async void OnOpenCustomServerClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            var win = new CustomServerWindow { DataContext = vm };
            var owner = VisualRoot as Window;
            if (owner is not null)
                await win.ShowDialog(owner);
            else
                win.Show();
        }
    }
}
