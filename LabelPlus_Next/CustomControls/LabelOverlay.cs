using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using LabelPlus_Next.Models;

namespace LabelPlus_Next.CustomControls;

public class LabelOverlay : Control
{
    public static readonly StyledProperty<IEnumerable<LabelItem>?> LabelsProperty =
        AvaloniaProperty.Register<LabelOverlay, IEnumerable<LabelItem>?>(nameof(Labels));

    public IEnumerable<LabelItem>? Labels
    {
        get => GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    public static readonly StyledProperty<double> ImageWidthProperty =
        AvaloniaProperty.Register<LabelOverlay, double>(nameof(ImageWidth));

    public double ImageWidth
    {
        get => GetValue(ImageWidthProperty);
        set => SetValue(ImageWidthProperty, value);
    }

    public static readonly StyledProperty<double> ImageHeightProperty =
        AvaloniaProperty.Register<LabelOverlay, double>(nameof(ImageHeight));

    public double ImageHeight
    {
        get => GetValue(ImageHeightProperty);
        set => SetValue(ImageHeightProperty, value);
    }

    // Content area of the image when Stretch != Fill (letterboxing). Values are in overlay coordinates.
    public static readonly StyledProperty<double> ContentWidthProperty =
        AvaloniaProperty.Register<LabelOverlay, double>(nameof(ContentWidth));
    public double ContentWidth { get => GetValue(ContentWidthProperty); set => SetValue(ContentWidthProperty, value); }

    public static readonly StyledProperty<double> ContentHeightProperty =
        AvaloniaProperty.Register<LabelOverlay, double>(nameof(ContentHeight));
    public double ContentHeight { get => GetValue(ContentHeightProperty); set => SetValue(ContentHeightProperty, value); }

    public static readonly StyledProperty<double> ContentOffsetXProperty =
        AvaloniaProperty.Register<LabelOverlay, double>(nameof(ContentOffsetX));
    public double ContentOffsetX { get => GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }

    public static readonly StyledProperty<double> ContentOffsetYProperty =
        AvaloniaProperty.Register<LabelOverlay, double>(nameof(ContentOffsetY));
    public double ContentOffsetY { get => GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }

    public static readonly StyledProperty<int> HighlightIndexProperty =
        AvaloniaProperty.Register<LabelOverlay, int>(nameof(HighlightIndex), -1);

    public int HighlightIndex
    {
        get => GetValue(HighlightIndexProperty);
        set => SetValue(HighlightIndexProperty, value);
    }

    private INotifyCollectionChanged? _labelsCollectionChanged;
    private readonly List<INotifyPropertyChanged> _itemSubscriptions = new();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LabelsProperty)
        {
            UnsubscribeFromLabels(change.GetOldValue<IEnumerable<LabelItem>?>());
            SubscribeToLabels(change.GetNewValue<IEnumerable<LabelItem>?>());
            InvalidateVisual();
            return;
        }
        if (change.Property == ImageWidthProperty || change.Property == ImageHeightProperty || change.Property == HighlightIndexProperty
            || change.Property == ContentWidthProperty || change.Property == ContentHeightProperty || change.Property == ContentOffsetXProperty || change.Property == ContentOffsetYProperty)
        {
            InvalidateVisual();
        }
    }

    private void SubscribeToLabels(IEnumerable<LabelItem>? labels)
    {
        if (labels is null) return;
        if (labels is INotifyCollectionChanged ncc)
        {
            _labelsCollectionChanged = ncc;
            ncc.CollectionChanged += OnLabelsCollectionChanged;
        }
        foreach (var l in labels.OfType<INotifyPropertyChanged>())
        {
            l.PropertyChanged += OnLabelItemPropertyChanged;
            _itemSubscriptions.Add(l);
        }
    }

    private void UnsubscribeFromLabels(IEnumerable<LabelItem>? labels)
    {
        if (_labelsCollectionChanged != null)
        {
            _labelsCollectionChanged.CollectionChanged -= OnLabelsCollectionChanged;
            _labelsCollectionChanged = null;
        }
        foreach (var l in _itemSubscriptions)
        {
            l.PropertyChanged -= OnLabelItemPropertyChanged;
        }
        _itemSubscriptions.Clear();
    }

    private void OnLabelsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var l in e.OldItems.OfType<INotifyPropertyChanged>())
                l.PropertyChanged -= OnLabelItemPropertyChanged;
        }
        if (e.NewItems != null)
        {
            foreach (var l in e.NewItems.OfType<INotifyPropertyChanged>())
            {
                l.PropertyChanged += OnLabelItemPropertyChanged;
                _itemSubscriptions.Add(l);
            }
        }
        InvalidateVisual();
    }

    private void OnLabelItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LabelItem.XPercent) || e.PropertyName == nameof(LabelItem.YPercent))
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Labels is null || (ImageWidth <= 0 && ContentWidth <= 0) || (ImageHeight <= 0 && ContentHeight <= 0))
            return;

        // Prefer content rect if provided, otherwise fall back to full image size without offset.
        var cw = ContentWidth > 0 ? ContentWidth : ImageWidth;
        var ch = ContentHeight > 0 ? ContentHeight : ImageHeight;
        var ox = ContentWidth > 0 || ContentHeight > 0 ? ContentOffsetX : 0;
        var oy = ContentWidth > 0 || ContentHeight > 0 ? ContentOffsetY : 0;

        double side = LabelSideLength(cw, ch);
        var innerBrush = Brushes.OrangeRed;
        var outerBrush = Brushes.DodgerBlue;
        double radius = side / 4.0;

        int i = 0;
        foreach (var label in Labels)
        {
            i++;
            double x = ox + label.XPercent * cw;
            double y = oy + label.YPercent * ch;

            var rect = GetLabelRect(x, y, side);
            var brush = label.Category == 1 ? innerBrush : outerBrush;
            var penThickness = (i - 1) == HighlightIndex ? side / 5.0 : side / 10.0;
            var pen = new Pen(brush, penThickness);

            var rrect = new RoundedRect(rect, radius, radius);

            if ((i - 1) == HighlightIndex)
            {
                if (brush is ISolidColorBrush scb)
                    context.FillRectangle(new SolidColorBrush(scb.Color, 0.18), rrect.Rect);
                else
                    context.FillRectangle(brush, rrect.Rect);
            }

            context.DrawRectangle(null, pen, rrect);

            // 居中显示加粗编号
            var head = i.ToString();
            var boldTf = new Typeface(Typeface.Default.FontFamily, FontStyle.Normal, FontWeight.SemiBold);
            var indexFontSize = side / 1.8;
            var layout = new TextLayout(head, boldTf, indexFontSize, brush, TextAlignment.Center, TextWrapping.NoWrap, maxWidth: rect.Width);
            var yTop = rect.Y + (rect.Height - indexFontSize) / 2;
            layout.Draw(context, new Avalonia.Point(rect.X, yTop));

            // 悬浮显示 label text（在高亮时显示）
            if ((i - 1) == HighlightIndex && label.Text is string tip && !string.IsNullOrWhiteSpace(tip))
            {
                var maxTipWidth = System.Math.Min(cw * 0.6, 480);
                var tipFontSize = side / 2.2;
                var tipLayout = new TextLayout(
                    tip,
                    Typeface.Default,
                    tipFontSize,
                    Brushes.White,
                    TextAlignment.Left,
                    TextWrapping.Wrap,
                    TextTrimming.None,
                    null,
                    FlowDirection.LeftToRight,
                    maxWidth: maxTipWidth);

                double textW = 0, textH = 0;
                foreach (var r in tipLayout.HitTestTextRange(0, tip.Length))
                {
                    textW = System.Math.Max(textW, r.Right);
                    textH = System.Math.Max(textH, r.Bottom);
                }
                if (textW <= 0) textW = maxTipWidth;
                if (textH <= 0) textH = tipFontSize;

                var padH = 6;
                var padW = 8;
                var bgW = textW + padW * 2;
                var bgH = textH + padH * 2;

                double tipX = rect.X;
                double tipY = rect.Y - bgH - 4;
                // 边界修正，基于内容区域
                if (tipX + bgW > ox + cw)
                    tipX = System.Math.Max(ox, ox + cw - bgW - 1);
                if (tipX < ox) tipX = ox;
                if (tipY < oy)
                    tipY = rect.Bottom + 4;
                if (tipY + bgH > oy + ch)
                    tipY = System.Math.Max(oy, rect.Y - bgH - 4);

                var bg = new SolidColorBrush(Colors.Black, 0.7);
                var bgRect = new Rect(tipX, tipY, bgW, bgH);
                context.FillRectangle(bg, bgRect);
                tipLayout.Draw(context, new Avalonia.Point(tipX + padW, tipY + padH));
            }
        }
    }

    internal static double LabelSideLength(double w, double h)
        => System.Math.Min(w, h) * 0.035;

    internal static Rect GetLabelRect(double x, double y, double side)
    {
        var width = 1.6 * side;
        var height = 1.1 * side;
        return new Rect(x - width / 2, y - height / 2, width, height);
    }
}
