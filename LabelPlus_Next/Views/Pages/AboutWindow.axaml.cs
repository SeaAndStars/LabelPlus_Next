using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Diagnostics;

namespace LabelPlus_Next.Views.Pages;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
#if NET9_0
        VersionText.Text = $"°æ±¾£º{typeof(App).Assembly.GetName().Version}";
#endif
    }

    private void OnProjectLinkPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        try
        {
            var url = "https://github.com/SeaStar/LabelPlus_Next";
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
