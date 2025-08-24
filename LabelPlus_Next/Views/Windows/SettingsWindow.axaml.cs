using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Ursa.Controls;
using LabelPlus_Next.ViewModels;

namespace LabelPlus_Next.Views.Windows;

public partial class SettingsWindow : UrsaWindow
{
    public SettingsWindow()
    {
        InitializeComponent();
        if (DataContext is null) DataContext = new SettingsViewModel();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
