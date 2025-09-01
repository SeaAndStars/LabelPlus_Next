using Avalonia.Interactivity;
using LabelPlus_Next.Tools.ViewModels;
using Ursa.Controls;

namespace LabelPlus_Next.Tools.Views;

public partial class ServerSettingsWindow : UrsaWindow
{
    public ServerSettingsWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
