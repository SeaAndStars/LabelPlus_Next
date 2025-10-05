using Avalonia;
using Avalonia.Media;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Linq;
using System.Net.Sockets;

namespace LabelPlus_Next.Desktop;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            // If args contains a labelplus:// URI and a running instance is present, forward it and exit
            var uriArg = args?.FirstOrDefault(a => a.StartsWith("labelplus://", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(uriArg))
            {
                try
                {
                    // Only attempt named-pipe forwarding on Windows; fallback to starting normally otherwise
                    if (OperatingSystem.IsWindows())
                    {
                        try
                        {
                            // Retry a few times in case the main instance is still starting its IPC server
                            const int maxAttempts = 6;
                            for (int attempt = 1; attempt <= maxAttempts; attempt++)
                            {
                                try
                                {
                                    using var client = new NamedPipeClientStream(".", "labelplus_ipc_pipe_v1", PipeDirection.Out);
                                    client.Connect(500);
                                    if (client.IsConnected)
                                    {
                                        using var sw = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
                                        sw.WriteLine(uriArg);
                                        return; // forwarded
                                    }
                                }
                                catch (TimeoutException) { }
                                catch (IOException) { }
                                catch { }
                                // backoff a bit
                                System.Threading.Thread.Sleep(100);
                            }
                        }
                        catch { /* server not available */ }
                    }
                }
                catch { /* ignore and continue to start normally */ }
            }
        }
        catch { }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args ?? Array.Empty<string>());
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new FontManagerOptions
            {
                // Keep Inter for Latin, but provide fallbacks for CJK/Emoji to avoid tofu on Linux
                FontFallbacks = new[]
                {
                    new FontFallback { FontFamily = new FontFamily("Noto Sans CJK SC") },
                    new FontFallback { FontFamily = new FontFamily("Noto Sans CJK TC") },
                    new FontFallback { FontFamily = new FontFamily("Noto Sans CJK JP") },
                    new FontFallback { FontFamily = new FontFamily("WenQuanYi Zen Hei") },
                    new FontFallback { FontFamily = new FontFamily("WenQuanYi Micro Hei") },
                    new FontFallback { FontFamily = new FontFamily("Microsoft YaHei") },
                    new FontFallback { FontFamily = new FontFamily("Noto Color Emoji") }
                }
            })
            .LogToTrace();
}
