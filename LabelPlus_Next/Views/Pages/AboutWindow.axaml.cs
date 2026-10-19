using Avalonia.Input;
using Avalonia.Interactivity;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Ursa.Controls;
using NLog;

namespace LabelPlus_Next.Views.Pages;

public partial class AboutWindow : UrsaWindow
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

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
        catch (IOException ex)
        {
            Logger.Warn(ex, "读取 Client.version.json 失败");
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warn(ex, "读取 Client.version.json 权限不足");
        }
        catch (JsonException ex)
        {
            Logger.Warn(ex, "Client.version.json 格式错误");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "解析版本信息时发生未预期异常");
            throw;
        }

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
        catch (Exception ex)
        {
            Logger.Warn(ex, "打开项目主页失败");
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
