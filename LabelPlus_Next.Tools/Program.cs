using Avalonia;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using LabelPlus_Next.Tools.Models;

namespace LabelPlus_Next.Tools;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "--publish", StringComparison.OrdinalIgnoreCase))
        {
            // Usage: --publish <version> <artifactsDir> [--settings settings.json]
            var version = args.Length > 1 ? args[1] : throw new ArgumentException("version required");
            var artifacts = args.Length > 2 ? args[2] : throw new ArgumentException("artifactsDir required");
            var settings = new PublishSettings();
            for (int i = 3; i < args.Length; i++)
            {
                if (args[i] == "--settings" && i + 1 < args.Length)
                {
                    var p = args[i + 1];
                    if (File.Exists(p))
                    {
                        var json = File.ReadAllText(p);
                        settings = JsonSerializer.Deserialize<PublishSettings>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? settings;
                    }
                    i++;
                }
            }
            var code = Task.Run(() => Publisher.RunAsync(version, artifacts, settings)).GetAwaiter().GetResult();
            Environment.Exit(code);
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
