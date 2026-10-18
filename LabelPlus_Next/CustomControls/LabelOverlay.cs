using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using LabelPlus_Next.Models;
using System.Collections.Specialized;
using System.ComponentModel;

namespace LabelPlus_Next.CustomControls;

public class LabelOverlay : Control
{
    public static readonly StyledProperty<IEnumerable<LabelItem>?> LabelsProperty =
        AvaloniaProperty.Register<LabelOverlay, IEnumerable<LabelItem>?>(nameof(Labels));

    public static readonly StyledProperty<double> ImageWidthProperty =
        AvaloniaProperty.Register<LabelOverlay, double>(nameof(ImageWidth));

    public static readonly StyledProperty<double> ImageHeightProperty =
        AvaloniaProperty.Register<LabelOverlay, double>(nameof(ImageHeight));

    // Content area of the image when Stretch != Fill (letterboxing). Values are in overlay coordinates.
    public static readonly StyledProperty<double> ContentWidthProperty =
        AvaloniaProperty.Register<LabelOverlay, double>(nameof(ContentWidth));

    public static readonly StyledProperty<double> ContentHeightProperty =
        AvaloniaProperty.Register<LabelOverlay, double>(nameof(ContentHeight));

    public static readonly StyledProperty<double> ContentOffsetXProperty =
        AvaloniaProperty.Register<LabelOverlay, double>(nameof(ContentOffsetX));

    public static readonly StyledProperty<double> ContentOffsetYProperty =
        AvaloniaProperty.Register<LabelOverlay, double>(nameof(ContentOffsetY));

    public static readonly StyledProperty<int> HighlightIndexProperty =
        AvaloniaProperty.Register<LabelOverlay, int>(nameof(HighlightIndex), -1);

    public static readonly StyledProperty<double> LabelIndexFontScaleProperty =
        AvaloniaProperty.Register<LabelOverlay, double>(nameof(LabelIndexFontScale), 1.0);

    public static readonly StyledProperty<double> LabelTipFontScaleProperty =
        AvaloniaProperty.Register<LabelOverlay, double>(nameof(LabelTipFontScale), 1.0);

    public static readonly StyledProperty<double> LabelTipBackgroundOpacityProperty =
        AvaloniaProperty.Register<LabelOverlay, double>(nameof(LabelTipBackgroundOpacity), 0.7);
    private readonly List<INotifyPropertyChanged> _itemSubscriptions = new();

    private INotifyCollectionChanged? _labelsCollectionChanged;

    public IEnumerable<LabelItem>? Labels
    {
        get => GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    public double ImageWidth
    {
        get => GetValue(ImageWidthProperty);
        set => SetValue(ImageWidthProperty, value);
    }

    public double ImageHeight
    {
        get => GetValue(ImageHeightProperty);
        set => SetValue(ImageHeightProperty, value);
    }

    public double ContentWidth { get => GetValue(ContentWidthProperty); set => SetValue(ContentWidthProperty, value); }
    public double ContentHeight { get => GetValue(ContentHeightProperty); set => SetValue(ContentHeightProperty, value); }
    public double ContentOffsetX { get => GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }

    public int HighlightIndex
    {
        get => GetValue(HighlightIndexProperty);
        set => SetValue(HighlightIndexProperty, value);
    }

    public double LabelIndexFontScale
    {
        get => GetValue(LabelIndexFontScaleProperty);
        set => SetValue(LabelIndexFontScaleProperty, value);
    }

    public double LabelTipFontScale
    {
        get => GetValue(LabelTipFontScaleProperty);
        set => SetValue(LabelTipFontScaleProperty, value);
    }

    public double LabelTipBackgroundOpacity
    {
        get => GetValue(LabelTipBackgroundOpacityProperty);
        set => SetValue(LabelTipBackgroundOpacityProperty, value);
    }

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
            || change.Property == ContentWidthProperty || change.Property == ContentHeightProperty || change.Property == ContentOffsetXProperty || change.Property == ContentOffsetYProperty
            || change.Property == LabelIndexFontScaleProperty || change.Property == LabelTipFontScaleProperty || change.Property == LabelTipBackgroundOpacityProperty)
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
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            // Reset carries no OldItems: resubscribe from scratch to avoid leaking handlers on cleared items
            var labels = Labels;
            UnsubscribeFromLabels(labels);
            SubscribeToLabels(labels);
            InvalidateVisual();
            return;
        }
        if (e.OldItems != null)
        {
            foreach (var l in e.OldItems.OfType<INotifyPropertyChanged>())
            {
                l.PropertyChanged -= OnLabelItemPropertyChanged;
                _itemSubscriptions.Remove(l);
            }
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
        if (e.PropertyName is nameof(LabelItem.XPercent) or nameof(LabelItem.YPercent) or nameof(LabelItem.Category)
            || (e.PropertyName == nameof(LabelItem.Text) && HighlightIndex >= 0))
        {
            InvalidateVisual();
        }
    }

    private static readonly Typeface IndexTypeface = new(Typeface.Default.FontFamily, FontStyle.Normal, FontWeight.Bold);
    private static readonly IImmutableSolidColorBrush InnerBrush = Brushes.OrangeRed;
    private static readonly IImmutableSolidColorBrush OuterBrush = Brushes.DodgerBlue;
    private static readonly IImmutableSolidColorBrush InnerHighlightFill = new ImmutableSolidColorBrush(Colors.OrangeRed, 0.18);
    private static readonly IImmutableSolidColorBrush OuterHighlightFill = new ImmutableSolidColorBrush(Colors.DodgerBlue, 0.18);

    // Render caches: pens and index text layouts only depend on label size/font scale, not on label positions.
    // Rebuilding them per label per frame was the dominant cost when pages have hundreds of labels.
    private readonly Dictionary<(int Index, bool Inner), TextLayout> _indexLayouts = new();
    private double _cachedSide = double.NaN;
    private double _cachedIndexFontSize = double.NaN;
    private ImmutablePen? _innerPen, _outerPen, _innerPenHi, _outerPenHi;

    private void EnsureCaches(double side, double indexFontSize)
    {
        if (side != _cachedSide)
        {
            _cachedSide = side;
            _innerPen = new ImmutablePen(InnerBrush, side / 24.0);
            _outerPen = new ImmutablePen(OuterBrush, side / 24.0);
            _innerPenHi = new ImmutablePen(InnerBrush, side / 14.0);
            _outerPenHi = new ImmutablePen(OuterBrush, side / 14.0);
        }
        if (indexFontSize != _cachedIndexFontSize)
        {
            _cachedIndexFontSize = indexFontSize;
            ClearIndexLayouts();
        }
    }

    private TextLayout GetIndexLayout(int index, bool inner, double fontSize)
    {
        if (_indexLayouts.TryGetValue((index, inner), out var layout)) return layout;
        layout = new TextLayout(index.ToString(), IndexTypeface, fontSize, inner ? InnerBrush : OuterBrush, TextAlignment.Left, TextWrapping.NoWrap, TextTrimming.None);
        _indexLayouts[(index, inner)] = layout;
        return layout;
    }

    private void ClearIndexLayouts()
    {
        foreach (var l in _indexLayouts.Values) l.Dispose();
        _indexLayouts.Clear();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ClearIndexLayouts();
        _cachedIndexFontSize = double.NaN;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Labels is null)
            return;

        // Use content rect size; the overlay is already translated by ContentOffset in the template
        var cw = ContentWidth > 0 ? ContentWidth : ImageWidth;
        var ch = ContentHeight > 0 ? ContentHeight : ImageHeight;

        if (cw <= 0 || ch <= 0)
            return;

        var side = LabelSideLength(cw, ch);
        var radius = side / 4.0;
        var indexFontSize = side / 1.15 * Math.Clamp(LabelIndexFontScale, 0.5, 2.5);
        EnsureCaches(side, indexFontSize);

        var i = 0;
        foreach (var label in Labels)
        {
            i++;
            var highlighted = i - 1 == HighlightIndex;
            var inner = label.Category == 1;
            // Local coordinates in content space [0,cw]x[0,ch]
            var rect = GetLabelRect(label.XPercent * cw, label.YPercent * ch, side);

            if (highlighted)
                context.FillRectangle(inner ? InnerHighlightFill : OuterHighlightFill, rect);

            var pen = highlighted ? (inner ? _innerPenHi : _outerPenHi) : (inner ? _innerPen : _outerPen);
            context.DrawRectangle(null, pen, new RoundedRect(rect, radius, radius));

            // Index number centered inside rect
            var layout = GetIndexLayout(i, inner, indexFontSize);
            var textW = layout.Width > 0 ? layout.Width : indexFontSize;
            var textH = layout.Height > 0 ? layout.Height : indexFontSize;
            layout.Draw(context, new Point(rect.X + (rect.Width - textW) / 2, rect.Y + (rect.Height - textH) / 2));

            // Tip text for highlighted label
            if (highlighted && label.Text is string tip && !string.IsNullOrWhiteSpace(tip))
                DrawTip(context, tip, rect, side, cw, ch);
        }
    }

    private void DrawTip(DrawingContext context, string tip, Rect rect, double side, double cw, double ch)
    {
        var maxTipWidth = Math.Min(cw * 0.6, 480);
        var tipFontSize = side / 2.2 * Math.Clamp(LabelTipFontScale, 0.5, 2.5);
        using var tipLayout = new TextLayout(
            tip,
            Typeface.Default,
            tipFontSize,
            Brushes.White,
            TextAlignment.Left,
            TextWrapping.Wrap,
            TextTrimming.None,
            null,
            FlowDirection.LeftToRight,
            maxTipWidth);

        var tW = tipLayout.Width > 0 ? tipLayout.Width : maxTipWidth;
        var tH = tipLayout.Height > 0 ? tipLayout.Height : tipFontSize;

        const double padH = 6;
        const double padW = 8;
        var bgW = tW + padW * 2;
        var bgH = tH + padH * 2;

        var tipX = rect.X;
        var tipY = rect.Y - bgH - 4;
        // Clamp to content bounds [0,cw]x[0,ch]
        if (tipX + bgW > cw)
            tipX = Math.Max(0, cw - bgW - 1);
        if (tipX < 0) tipX = 0;
        if (tipY < 0)
            tipY = rect.Bottom + 4;
        if (tipY + bgH > ch)
            tipY = Math.Max(0, rect.Y - bgH - 4);

        var bg = new ImmutableSolidColorBrush(Colors.Black, Math.Clamp(LabelTipBackgroundOpacity, 0.0, 1.0));
        context.FillRectangle(bg, new Rect(tipX, tipY, bgW, bgH));
        tipLayout.Draw(context, new Point(tipX + padW, tipY + padH));
    }

    internal static double LabelSideLength(double w, double h)
        => Math.Min(w, h) * 0.035;

    internal static Rect GetLabelRect(double x, double y, double side)
    {
        var width = 1.6 * side;
        var height = 1.1 * side;
        return new Rect(x - width / 2, y - height / 2, width, height);
    }
}
