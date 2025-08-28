using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.Views.Windows;
using System.Threading.Tasks;

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
        if (DataContext is ViewModels.SettingsViewModel vm)
        {
            await vm.VerifyHttpAsync();
        }
    }

    private async void OnOpenCustomServerClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.SettingsViewModel vm)
        {
            var win = new CustomServerWindow { DataContext = vm };
            var owner = this.VisualRoot as Window;
            if (owner is not null)
                await win.ShowDialog(owner);
            else
                win.Show();
        }
    }
}
