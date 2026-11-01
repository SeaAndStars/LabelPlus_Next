using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Globalization;

namespace LabelPlus_Next.CustomControls;

/// <summary>
/// Arranges a horizontal row of controls on a shared text baseline. Avalonia
/// has no built-in baseline alignment for layout panels, so mixed-size text
/// rows use this panel explicitly.
/// </summary>
public class BaselinePanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<BaselinePanel, double>(nameof(Spacing));

    static BaselinePanel()
    {
        AffectsMeasure<BaselinePanel>(SpacingProperty);
        AffectsArrange<BaselinePanel>(SpacingProperty);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0d;
        var maxBaseline = 0d;
        var maxDescent = 0d;
        var hasChild = false;

        foreach (var child in Children)
        {
            if (!child.IsVisible || IsEmptyText(child)) continue;

            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            var size = child.DesiredSize;
            var baseline = GetBaseline(child);

            width += size.Width;
            if (hasChild) width += Spacing;
            hasChild = true;

            maxBaseline = Math.Max(maxBaseline, baseline);
            maxDescent = Math.Max(maxDescent, size.Height - baseline);
        }

        return new Size(width, maxBaseline + maxDescent);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var baseline = 0d;
        foreach (var child in Children)
            if (child.IsVisible && !IsEmptyText(child))
                baseline = Math.Max(baseline, GetBaseline(child));

        var x = 0d;
        foreach (var child in Children)
        {
            if (!child.IsVisible || IsEmptyText(child))
            {
                child.Arrange(new Rect(x, 0, 0, 0));
                continue;
            }

            var size = child.DesiredSize;
            var y = baseline - GetBaseline(child);
            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + Spacing;
        }

        return finalSize;
    }

    private static bool IsEmptyText(Control child)
        => child is TextBlock textBlock && string.IsNullOrWhiteSpace(textBlock.Text);

    private static double GetBaseline(Control child)
    {
        if (child is TextBlock textBlock)
        {
            var text = textBlock.Text ?? string.Empty;
            var typeface = new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch);
            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                textBlock.FontSize,
                Brushes.Black);

            var lineHeight = textBlock.LineHeight > 0 ? textBlock.LineHeight : formatted.Height;
            var leading = Math.Max(0, lineHeight - formatted.Height) / 2;
            return textBlock.Padding.Top + leading + formatted.Baseline;
        }

        return child.DesiredSize.Height;
    }
}
