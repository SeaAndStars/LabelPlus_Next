using Avalonia.Markup.Xaml;
using LabelPlus_Next.ViewModels;
using Ursa.Controls;

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
