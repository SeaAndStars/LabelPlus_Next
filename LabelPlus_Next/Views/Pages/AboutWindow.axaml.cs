using Avalonia.Input;
using Avalonia.Interactivity;
using System.Diagnostics;
using System.Text.Json;
using Ursa.Controls;

namespace LabelPlus_Next.Views.Pages;

public partial class AboutWindow : UrsaWindow
{
    public AboutWindow()
    {
        InitializeComponent();
        SetVersionText();
    }

    private void SetVersionText()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Client.version.json");
            if (File.Exists(path))
            {
                using var fs = File.OpenRead(path);
                using var doc = JsonDocument.Parse(fs);
                if (doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String)
                {
                    VersionText.Text = $"版本: {v.GetString()}";
                    return;
                }
            }
        }
        catch { }

        // Fallback: assembly version
        var ver = typeof(App).Assembly.GetName().Version?.ToString() ?? "";
        VersionText.Text = $"版本: {ver}";
    }

    private void OnProjectLinkPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var url = "https://github.com/SeaAndStars/LabelPlus_Next";
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
