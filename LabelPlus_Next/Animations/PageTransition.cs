using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace LabelPlus_Next.Animations;

/// <summary>
/// Entrance animation for navigation pages: the incoming page fades in while sliding a few pixels up.
/// Only the incoming page animates, so cached pages can be re-hosted instantly without two visual parents.
/// </summary>
public static class PageTransition
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(240);
    private const double Offset = 14;

    private static readonly Animation Enter = new()
    {
        Duration = Duration,
        Easing = new CubicEaseOut(),
        // End frame equals the base values, so nothing needs to be held after the run
        FillMode = FillMode.None,
        Children =
        {
            new KeyFrame
            {
                Cue = new Cue(0d),
                Setters = { new Setter(Visual.OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, Offset) }
            },
            new KeyFrame
            {
                Cue = new Cue(1d),
                Setters = { new Setter(Visual.OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) }
            }
        }
    };

    private static CancellationTokenSource? _running;

    public static async void PlayEnter(Control page)
    {
        _running?.Cancel();
        var cts = new CancellationTokenSource();
        _running = cts;
        try
        {
            page.RenderTransform ??= new TranslateTransform();
            await Enter.RunAsync(page, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_running, cts)) _running = null;
            cts.Dispose();
        }
    }
}
