using Avalonia;
using Avalonia.Media;
using System;

namespace LabelPlus_Next.Desktop;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

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
