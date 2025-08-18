using System;
using System.Linq;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace LabelPlus_Next.CustomControls;

public class LabeledTextBox : Control
{
    public static readonly StyledProperty<int> IndexProperty = AvaloniaProperty.Register<LabeledTextBox, int>(nameof(Index), 0);
    public int Index { get => GetValue(IndexProperty); set => SetValue(IndexProperty, value); }

    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<LabeledTextBox, string?>(nameof(Text), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public static readonly StyledProperty<IBrush> SeparatorBrushProperty = AvaloniaProperty.Register<LabeledTextBox, IBrush>(nameof(SeparatorBrush), Brushes.Gray);
    public IBrush SeparatorBrush { get => GetValue(SeparatorBrushProperty); set => SetValue(SeparatorBrushProperty, value); }

    public static readonly StyledProperty<double> SeparatorThicknessProperty = AvaloniaProperty.Register<LabeledTextBox, double>(nameof(SeparatorThickness), 2);
    public double SeparatorThickness { get => GetValue(SeparatorThicknessProperty); set => SetValue(SeparatorThicknessProperty, value); }

    public static readonly StyledProperty<IBrush> IndexBackgroundBrushProperty = AvaloniaProperty.Register<LabeledTextBox, IBrush>(nameof(IndexBackgroundBrush), Brushes.Transparent);
    public IBrush IndexBackgroundBrush { get => GetValue(IndexBackgroundBrushProperty); set => SetValue(IndexBackgroundBrushProperty, value); }

    public static readonly StyledProperty<IBrush> TextBrushProperty = AvaloniaProperty.Register<LabeledTextBox, IBrush>(nameof(TextBrush), Brushes.White);
    public IBrush TextBrush { get => GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    public static readonly StyledProperty<Thickness> EditorPaddingProperty = AvaloniaProperty.Register<LabeledTextBox, Thickness>(nameof(EditorPadding), new Thickness(8, 10, 8, 8));
    public Thickness EditorPadding { get => GetValue(EditorPaddingProperty); set => SetValue(EditorPaddingProperty, value); }

    public static readonly StyledProperty<bool> AcceptsReturnProperty = AvaloniaProperty.Register<LabeledTextBox, bool>(nameof(AcceptsReturn), true);
    public bool AcceptsReturn { get => GetValue(AcceptsReturnProperty); set => SetValue(AcceptsReturnProperty, value); }

    public static readonly StyledProperty<double> EditorFontSizeProperty = AvaloniaProperty.Register<LabeledTextBox, double>(nameof(EditorFontSize), 18d);
    public double EditorFontSize { get => GetValue(EditorFontSizeProperty); set => SetValue(EditorFontSizeProperty, value); }

    // Allow hiding caret completely
    public static readonly StyledProperty<bool> ShowCaretProperty = AvaloniaProperty.Register<LabeledTextBox, bool>(nameof(ShowCaret), false);
    public bool ShowCaret { get => GetValue(ShowCaretProperty); set => SetValue(ShowCaretProperty, value); }

    static LabeledTextBox()
    {
        FocusableProperty.OverrideDefaultValue<LabeledTextBox>(true);
    }

    private int _caretIndex;

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (string.IsNullOrEmpty(e.Text)) return;
        var t = Text ?? string.Empty;
        var incoming = e.Text;
        if (!AcceptsReturn)
            incoming = new string(incoming.Where(c => !char.IsControl(c) || c == '\t').ToArray());
        else
            incoming = new string(incoming.Where(c => !char.IsControl(c) || c == '\t' || c == '\n' || c == '\r').ToArray());
        if (string.IsNullOrEmpty(incoming)) return;
        incoming = incoming.Replace("\r\n", "\n").Replace('\r', '\n');
        _caretIndex = Math.Clamp(_caretIndex, 0, t.Length);
        t = t.Insert(_caretIndex, incoming);
        _caretIndex += incoming.Length;
        Text = t;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var t = Text ?? string.Empty;
        switch (e.Key)
        {
            case Key.Back:
                if (_caretIndex > 0 && t.Length > 0)
                {
                    t = t.Remove(_caretIndex - 1, 1);
                    _caretIndex--;
                    Text = t;
                    InvalidateVisual();
                }
                e.Handled = true;
                break;
            case Key.Delete:
                if (_caretIndex < t.Length && t.Length > 0)
                {
                    t = t.Remove(_caretIndex, 1);
                    Text = t;
                    InvalidateVisual();
                }
                e.Handled = true;
                break;
            case Key.Enter:
                if (AcceptsReturn)
                {
                    Text = t.Insert(_caretIndex, "\n");
                    _caretIndex++;
                    InvalidateVisual();
                    e.Handled = true;
                }
                break;
            case Key.Left:
                _caretIndex = Math.Max(0, _caretIndex - 1);
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.Right:
                _caretIndex = Math.Min((Text ?? string.Empty).Length, _caretIndex + 1);
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.End:
                _caretIndex = (Text ?? string.Empty).Length;
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.Home:
                _caretIndex = 0;
                InvalidateVisual();
                e.Handled = true;
                break;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
    }

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
            var layout = new TextLayout(text, Typeface.Default, fontSize, TextBrush, TextAlignment.Left, TextWrapping.NoWrap);
            double w = 0, h = 0;
            var hitRects = layout.HitTestTextRange(0, text.Length);
            foreach (var r in hitRects) { w = Math.Max(w, r.Right); h = Math.Max(h, r.Bottom); }
            if (w <= 0) w = fontSize * text.Length * 0.6;
            if (h <= 0) h = fontSize;
            var padX = 8; var padY = 2;
            // Over-extend height by SeparatorThickness to guarantee coverage with DPI/AA
            var cap = new Rect(4, lineY - (h / 2) - padY - SeparatorThickness, w + padX * 2, h + padY * 2 + SeparatorThickness * 2);
            capsule = cap;

            // Draw capsule background (same color as app Background via style)
            context.FillRectangle(IndexBackgroundBrush, cap);

            // Draw index text on top
            layout.Draw(context, new Point(cap.X + padX, cap.Y + padY));
        }

        // Draw separator line in two segments (left/right of capsule) to avoid drawing under the capsule
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

        // Draw editable text content starting from top-left below separator
        var origin = GetTextOrigin();
        var areaWidth = Math.Max(0, bounds.Width - EditorPadding.Left - EditorPadding.Right);
        var layoutText = (Text ?? string.Empty).Replace("\r\n", "\n");
        var tl = CreateTextLayout(layoutText, areaWidth);
        tl.Draw(context, origin);

        // Optional caret
        if (ShowCaret)
        {
            var (cx, cy, ch) = GetCaretPoint(tl);
            var caretPen = new Pen(TextBrush, 1);
            var caretTop = origin.Y + cy;
            var caretX = origin.X + cx;
            context.DrawLine(caretPen, new Point(caretX, caretTop), new Point(caretX, caretTop + ch));
        }
    }

    private Point GetTextOrigin()
    {
        var y = SeparatorThickness + EditorPadding.Top;
        var x = EditorPadding.Left;
        return new Point(x, y);
    }

    private TextLayout CreateTextLayout(string text, double maxWidth)
    {
        return new TextLayout(text,
            Typeface.Default,
            EditorFontSize,
            TextBrush,
            TextAlignment.Left,
            TextWrapping.Wrap,
            TextTrimming.None,
            null,
            FlowDirection.LeftToRight,
            maxWidth: maxWidth);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == IndexProperty || change.Property == SeparatorBrushProperty || change.Property == EditorFontSizeProperty || change.Property == TextBrushProperty || change.Property == EditorPaddingProperty)
        {
            var t = GetValue(TextProperty) ?? string.Empty;
            _caretIndex = Math.Clamp(_caretIndex, 0, t.Length);
            InvalidateVisual();
        }
    }

    private int HitTestToCaretIndex(TextLayout layout, Point p)
    {
        var t = Text ?? string.Empty;
        int best = t.Length;
        double bestDist = double.MaxValue;
        for (int i = 0; i <= t.Length; i++)
        {
            var ranges = layout.HitTestTextRange(0, i).ToList();
            if (ranges.Count == 0)
            {
                if (i == 0)
                {
                    var dx0 = 0 - p.X; var dy0 = 0 - p.Y; var d0 = dx0 * dx0 + dy0 * dy0;
                    if (d0 < bestDist) { bestDist = d0; best = 0; }
                }
                continue;
            }
            var last = ranges[^1];
            var pos = new Point(last.Right, last.Bottom - last.Height);
            var dx = pos.X - p.X;
            var dy = pos.Y - p.Y;
            var dist = dx * dx + dy * dy;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }
        return best;
    }

    private (double X, double Y, double Height) GetCaretPoint(TextLayout layout)
    {
        var t = Text ?? string.Empty;
        var caret = Math.Clamp(_caretIndex, 0, t.Length);
        if (caret == 0)
        {
            return (0, 0, layout.Height);
        }
        var rects = layout.HitTestTextRange(0, caret).ToList();
        if (rects.Count > 0)
        {
            var r = rects[^1];
            return (r.Right, r.Bottom - r.Height, r.Height);
        }
        var rectsAll = layout.HitTestTextRange(0, t.Length).ToList();
        if (rectsAll.Count > 0)
        {
            var r = rectsAll[^1];
            return (r.Right, r.Bottom - r.Height, r.Height);
        }
        return (0, 0, layout.Height);
    }
}
