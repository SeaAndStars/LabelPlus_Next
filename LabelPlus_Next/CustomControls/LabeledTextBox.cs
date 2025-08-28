using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace LabelPlus_Next.CustomControls;

// Make this control inherit TextBox to leverage built-in IME, caret and focus behavior
public class LabeledTextBox : TextBox
{
    public static readonly StyledProperty<int> IndexProperty = AvaloniaProperty.Register<LabeledTextBox, int>(nameof(Index));

    public static readonly StyledProperty<IBrush> SeparatorBrushProperty = AvaloniaProperty.Register<LabeledTextBox, IBrush>(nameof(SeparatorBrush), Brushes.Gray);

    public static readonly StyledProperty<double> SeparatorThicknessProperty = AvaloniaProperty.Register<LabeledTextBox, double>(nameof(SeparatorThickness), 2);

    public static readonly StyledProperty<IBrush> IndexBackgroundBrushProperty = AvaloniaProperty.Register<LabeledTextBox, IBrush>(nameof(IndexBackgroundBrush), Brushes.Transparent);

    public static readonly StyledProperty<IBrush> IndexForegroundBrushProperty = AvaloniaProperty.Register<LabeledTextBox, IBrush>(nameof(IndexForegroundBrush), Brushes.White);

    static LabeledTextBox()
    {
        // Provide comfortable padding and enable multiline by default
        PaddingProperty.OverrideDefaultValue<LabeledTextBox>(new Thickness(8, 12, 8, 8));
        AcceptsReturnProperty.OverrideDefaultValue<LabeledTextBox>(true);
    }
    public int Index { get => GetValue(IndexProperty); set => SetValue(IndexProperty, value); }
    public IBrush SeparatorBrush { get => GetValue(SeparatorBrushProperty); set => SetValue(SeparatorBrushProperty, value); }
    public double SeparatorThickness { get => GetValue(SeparatorThicknessProperty); set => SetValue(SeparatorThicknessProperty, value); }
    public IBrush IndexBackgroundBrush { get => GetValue(IndexBackgroundBrushProperty); set => SetValue(IndexBackgroundBrushProperty, value); }
    public IBrush IndexForegroundBrush { get => GetValue(IndexForegroundBrushProperty); set => SetValue(IndexForegroundBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        var lineY = SeparatorThickness / 2;
        var pen = new Pen(SeparatorBrush, SeparatorThickness);

        Rect? capsule = null;
        if (Index > 0)
        {
            var text = Index.ToString();
            var fontSize = 16d;
            var layout = new TextLayout(text, Typeface.Default, fontSize, IndexForegroundBrush);
            double w = 0, h = 0;
            var hitRects = layout.HitTestTextRange(0, text.Length);
            foreach (var r in hitRects)
            {
                w = Math.Max(w, r.Right);
                h = Math.Max(h, r.Bottom);
            }
            if (w <= 0) w = fontSize * text.Length * 0.6;
            if (h <= 0) h = fontSize;
            var padX = 8;
            var padY = 2;
            var cap = new Rect(4, lineY - h / 2 - padY - SeparatorThickness, w + padX * 2, h + padY * 2 + SeparatorThickness * 2);
            capsule = cap;

            context.FillRectangle(IndexBackgroundBrush, cap);
            layout.Draw(context, new Point(cap.X + padX, cap.Y + padY));
        }

        if (capsule is Rect rc)
        {
            if (rc.X > 0)
                context.DrawLine(pen, new Point(0, lineY), new Point(rc.X, lineY));
            var rightStart = rc.Right;
            if (rightStart < bounds.Width)
                context.DrawLine(pen, new Point(rightStart, lineY), new Point(bounds.Width, lineY));
        }
        else
        {
            context.DrawLine(pen, new Point(0, lineY), new Point(bounds.Width, lineY));
        }
    }
}
