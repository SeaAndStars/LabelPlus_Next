using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LabelPlus_Next.Views.Windows;

public partial class CustomServerWindow : Ursa.Controls.UrsaWindow
{
    public CustomServerWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnVerifyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.SettingsViewModel vm)
        {
            await vm.VerifyHttpAsync();
        }
    }
}
