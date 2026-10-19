using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.ViewModels;
using Ursa.Controls;

namespace LabelPlus_Next.Views.Windows;

public partial class CustomServerWindow : UrsaWindow
{
    public CustomServerWindow()
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
}
