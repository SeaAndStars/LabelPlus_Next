using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using LabelPlus_Next.Models;
using System;
using System.Linq;

namespace LabelPlus_Next.CustomControls;

[TemplatePart(PART_Image, typeof(Image))]
[TemplatePart(PART_Overlay, typeof(LabelOverlay))]
[PseudoClasses(PC_Moving)]
public class PicViewer : TemplatedControl
{
    public const string PART_Image = "PART_Image";
    public const string PART_Overlay = "PART_Overlay";
    public const string PC_Moving = ":moving";

    private Image? _image;
    private LabelOverlay? _overlay;
    private Point? _lastClickPoint;
    private Point? _lastLocation;
    private bool _handlersHooked;
    private bool _spacePressed;
    private bool _panning;

    public static readonly StyledProperty<Control?> OverlayerProperty = AvaloniaProperty.Register<PicViewer, Control?>(nameof(Overlayer));
    public Control? Overlayer { get => GetValue(OverlayerProperty); set => SetValue(OverlayerProperty, value); }

    public static readonly StyledProperty<IImage?> SourceProperty = Image.SourceProperty.AddOwner<PicViewer>();
    public IImage? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    public static readonly StyledProperty<System.Collections.Generic.IEnumerable<LabelItem>?> LabelsProperty =
        AvaloniaProperty.Register<PicViewer, System.Collections.Generic.IEnumerable<LabelItem>?>(nameof(Labels));
    public System.Collections.Generic.IEnumerable<LabelItem>? Labels { get => GetValue(LabelsProperty); set => SetValue(LabelsProperty, value); }

    public static readonly StyledProperty<int> HighlightIndexProperty = AvaloniaProperty.Register<PicViewer, int>(nameof(HighlightIndex), -1);
    public int HighlightIndex { get => GetValue(HighlightIndexProperty); set => SetValue(HighlightIndexProperty, value); }

    private double _scale = 1;
    public static readonly DirectProperty<PicViewer, double> ScaleProperty = AvaloniaProperty.RegisterDirect<PicViewer, double>(nameof(Scale), o => o.Scale, (o, v) => o.Scale = v, unsetValue: 1);
    public double Scale { get => _scale; set => SetAndRaise(ScaleProperty, ref _scale, value); }

    public static readonly DirectProperty<PicViewer, double> MinScaleProperty = AvaloniaProperty.RegisterDirect<PicViewer, double>(nameof(MinScale), o => o.MinScale, (o, v) => o.MinScale = v, unsetValue: 0.1);
    public double MinScale { get => _minScale; set => SetAndRaise(MinScaleProperty, ref _minScale, value); }
    private double _minScale = 1;

    private double _translateX;
    public static readonly DirectProperty<PicViewer, double> TranslateXProperty = AvaloniaProperty.RegisterDirect<PicViewer, double>(nameof(TranslateX), o => o.TranslateX, (o, v) => o.TranslateX = v, unsetValue: 0);
    public double TranslateX { get => _translateX; set => SetAndRaise(TranslateXProperty, ref _translateX, value); }

    private double _translateY;
    public static readonly DirectProperty<PicViewer, double> TranslateYProperty = AvaloniaProperty.RegisterDirect<PicViewer, double>(nameof(TranslateY), o => o.TranslateY, (o, v) => o.TranslateY = v, unsetValue: 0);
    public double TranslateY { get => _translateY; set => SetAndRaise(TranslateYProperty, ref _translateY, value); }

    public static readonly StyledProperty<double> SmallChangeProperty = AvaloniaProperty.Register<PicViewer, double>(nameof(SmallChange), defaultValue: 1);
    public double SmallChange { get => GetValue(SmallChangeProperty); set => SetValue(SmallChangeProperty, value); }

    public static readonly StyledProperty<double> LargeChangeProperty = AvaloniaProperty.Register<PicViewer, double>(nameof(LargeChange), defaultValue: 10);
    public double LargeChange { get => GetValue(LargeChangeProperty); set => SetValue(LargeChangeProperty, value); }

    public static readonly StyledProperty<Stretch> StretchProperty = Image.StretchProperty.AddOwner<PicViewer>(new StyledPropertyMetadata<Stretch>(Stretch.Uniform));
    public Stretch Stretch { get => GetValue(StretchProperty); set => SetValue(StretchProperty, value); }

    private double _sourceMinScale = 0.1;

    // Content geometry (image drawn area inside Image bounds for Stretch Uniform, etc.)
    private double _contentWidth, _contentHeight, _contentOffsetX, _contentOffsetY;
    public static readonly DirectProperty<PicViewer, double> ContentWidthProperty = AvaloniaProperty.RegisterDirect<PicViewer, double>(nameof(ContentWidth), o => o.ContentWidth, (o, v) => o.ContentWidth = v);
    public static readonly DirectProperty<PicViewer, double> ContentHeightProperty = AvaloniaProperty.RegisterDirect<PicViewer, double>(nameof(ContentHeight), o => o.ContentHeight, (o, v) => o.ContentHeight = v);
    public static readonly DirectProperty<PicViewer, double> ContentOffsetXProperty = AvaloniaProperty.RegisterDirect<PicViewer, double>(nameof(ContentOffsetX), o => o.ContentOffsetX, (o, v) => o.ContentOffsetX = v);
    public static readonly DirectProperty<PicViewer, double> ContentOffsetYProperty = AvaloniaProperty.RegisterDirect<PicViewer, double>(nameof(ContentOffsetY), o => o.ContentOffsetY, (o, v) => o.ContentOffsetY = v);

    public double ContentWidth { get => _contentWidth; private set => SetAndRaise(ContentWidthProperty, ref _contentWidth, value); }
    public double ContentHeight { get => _contentHeight; private set => SetAndRaise(ContentHeightProperty, ref _contentHeight, value); }
    public double ContentOffsetX { get => _contentOffsetX; private set => SetAndRaise(ContentOffsetXProperty, ref _contentOffsetX, value); }
    public double ContentOffsetY { get => _contentOffsetY; private set => SetAndRaise(ContentOffsetYProperty, ref _contentOffsetY, value); }

    static PicViewer()
    {
        FocusableProperty.OverrideDefaultValue<PicViewer>(true);
        OverlayerProperty.Changed.AddClassHandler<PicViewer>((o, e) => o.OnOverlayerChanged(e));
        SourceProperty.Changed.AddClassHandler<PicViewer>((o, e) => o.OnSourceChanged(e));
        TranslateXProperty.Changed.AddClassHandler<PicViewer>((o, e) => o.OnTranslateXChanged(e));
        TranslateYProperty.Changed.AddClassHandler<PicViewer>((o, e) => o.OnTranslateYChanged(e));
        StretchProperty.Changed.AddClassHandler<PicViewer>((o, e) => o.OnStretchChanged(e));
        MinScaleProperty.Changed.AddClassHandler<PicViewer>((o, e) => o.OnMinScaleChanged(e));
    }

    private void RecomputeContentGeometry()
    {
        if (_image is null)
        {
            ContentWidth = ContentHeight = ContentOffsetX = ContentOffsetY = 0;
            return;
        }
        var bounds = _image.Bounds.Size;
        if (bounds.Width <= 0 || bounds.Height <= 0 || Source is null)
        {
            ContentWidth = bounds.Width;
            ContentHeight = bounds.Height;
            ContentOffsetX = ContentOffsetY = 0;
            return;
        }
        var src = Source.Size;
        double sx = bounds.Width / src.Width;
        double sy = bounds.Height / src.Height;
        double s = Stretch switch
        {
            Stretch.None => 1,
            Stretch.Fill => Math.Min(sx, sy),
            Stretch.Uniform => Math.Min(sx, sy),
            Stretch.UniformToFill => Math.Max(sx, sy),
            _ => Math.Min(sx, sy)
        };
        var cw = src.Width * s;
        var ch = src.Height * s;
        var ox = (bounds.Width - cw) / 2;
        var oy = (bounds.Height - ch) / 2;
        ContentWidth = cw;
        ContentHeight = ch;
        ContentOffsetX = ox;
        ContentOffsetY = oy;
    }

    private void OnTranslateYChanged(AvaloniaPropertyChangedEventArgs args)
    {
        if (_panning) return;
        var newValue = args.GetNewValue<double>();
        _lastLocation = _lastLocation?.WithY(newValue) ?? new Point(0, newValue);
    }

    private void OnTranslateXChanged(AvaloniaPropertyChangedEventArgs args)
    {
        if (_panning) return;
        var newValue = args.GetNewValue<double>();
        _lastLocation = _lastLocation?.WithX(newValue) ?? new Point(newValue, 0);
    }

    private void OnOverlayerChanged(AvaloniaPropertyChangedEventArgs args)
    {
        var control = args.GetNewValue<Control?>();
        if (control is { } c)
        {
            AdornerLayer.SetAdorner(this, c);
        }
    }

    private void OnSourceChanged(AvaloniaPropertyChangedEventArgs args)
    {
        RecomputeContentGeometry();
    }

    private void OnStretchChanged(AvaloniaPropertyChangedEventArgs args)
    {
        RecomputeContentGeometry();
    }

    private void OnMinScaleChanged(AvaloniaPropertyChangedEventArgs _)
    {
        if (_sourceMinScale > Scale)
        {
            Scale = _sourceMinScale;
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _image = e.NameScope.Get<Image>(PART_Image);
        _overlay = e.NameScope.Find<LabelOverlay>(PART_Overlay);
        if (_image is not null)
        {
            _image.PropertyChanged += (s, ev) =>
            {
                if (ev.Property == BoundsProperty)
                    RecomputeContentGeometry();
            };
        }
        HookPointerHandlers();
        if (Overlayer is { } c)
        {
            AdornerLayer.SetAdorner(this, c);
        }
        RecomputeContentGeometry();
    }

    private void HookPointerHandlers()
    {
        if (_handlersHooked) return;
        AddHandler(InputElement.PointerWheelChangedEvent, HandlePointerWheelChanged, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.PointerMovedEvent, HandlePointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.PointerPressedEvent, HandlePointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.PointerReleasedEvent, HandlePointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.KeyDownEvent, HandleKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.KeyUpEvent, HandleKeyUp, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        _handlersHooked = true;
    }

    private void HandlePointerWheelChanged(object? sender, PointerWheelEventArgs e) => OnPointerWheelChanged(e);
    private void HandlePointerMoved(object? sender, PointerEventArgs e) => OnPointerMoved(e);
    private void HandlePointerPressed(object? sender, PointerPressedEventArgs e) => OnPointerPressed(e);
    private void HandlePointerReleased(object? sender, PointerReleasedEventArgs e) => OnPointerReleased(e);
    private void HandleKeyDown(object? sender, KeyEventArgs e) => OnKeyDown(e);
    private void HandleKeyUp(object? sender, KeyEventArgs e) => OnKeyUp(e);

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        RecomputeContentGeometry();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y > 0)
            Scale *= 1.1;
        else
        {
            var scale = Scale / 1.1;
            if (scale < _sourceMinScale) scale = _sourceMinScale;
            Scale = scale;
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_panning && Equals(e.Pointer.Captured, this) && _lastClickPoint != null)
        {
            var p = e.GetPosition(this);
            var deltaX = p.X - _lastClickPoint.Value.X;
            var deltaY = p.Y - _lastClickPoint.Value.Y;
            TranslateX = deltaX + (_lastLocation?.X ?? 0);
            TranslateY = deltaY + (_lastLocation?.Y ?? 0);
            return;
        }

        // 悬浮高亮（在 Overlay 的局部坐标中命中测试，区域为整个矩形）
        if (_draggingLabelIndex < 0 && Labels is { } labelsHover && _overlay is { })
        {
            var list = labelsHover.ToList();
            var cw = ContentWidth;
            var ch = ContentHeight;
            var side = LabelOverlay.LabelSideLength(cw, ch);
            var pos = e.GetPosition(_overlay);

            int hit = -1;
            for (int idx = 0; idx < list.Count; idx++)
            {
                var label = list[idx];
                var centerX = label.XPercent * cw;
                var centerY = label.YPercent * ch;
                var rect = LabelOverlay.GetLabelRect(centerX, centerY, side);
                if (rect.Contains(pos))
                {
                    hit = idx;
                    break;
                }
            }
            if (hit != HighlightIndex)
            {
                HighlightIndex = hit;
                _overlay?.InvalidateVisual();
            }
        }

        // 拖动标签实时更新（同样在 Overlay 局部坐标中）
        if (_draggingLabelIndex >= 0 && Labels is { } labels && _overlay is { })
        {
            var pos = e.GetPosition(_overlay);
            var cw = ContentWidth;
            var ch = ContentHeight;

            var nx = Math.Clamp(pos.X / cw, 0, 1);
            var ny = Math.Clamp(pos.Y / ch, 0, 1);

            var list = labels.ToList();
            if (_draggingLabelIndex < list.Count)
            {
                var item = list[_draggingLabelIndex];
                item.XPercent = (float)nx;
                item.YPercent = (float)ny;
                HighlightIndex = _draggingLabelIndex; // 拖动中保持高亮
                _overlay?.InvalidateVisual();
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_spacePressed)
        {
            e.Pointer.Capture(this);
            _lastClickPoint = e.GetPosition(this);
            _panning = true;
            return;
        }

        if (Labels is { } labels && _overlay is { })
        {
            var list = labels.ToList();
            var cw = ContentWidth;
            var ch = ContentHeight;
            var side = LabelOverlay.LabelSideLength(cw, ch);

            var pos = e.GetPosition(_overlay);

            int hit = -1;
            for (int idx = 0; idx < list.Count; idx++)
            {
                var label = list[idx];
                var centerX = label.XPercent * cw;
                var centerY = label.YPercent * ch;
                var rect = LabelOverlay.GetLabelRect(centerX, centerY, side);
                if (rect.Contains(pos))
                {
                    hit = idx;
                    break;
                }
            }

            if (hit >= 0)
            {
                e.Pointer.Capture(this);
                _draggingLabelIndex = hit;
                HighlightIndex = hit;
                _overlay?.InvalidateVisual();
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (Equals(e.Pointer.Captured, this))
            e.Pointer.Capture(null);

        if (_panning)
        {
            _lastLocation = new Point(TranslateX, TranslateY);
        }
        _panning = false;
        _draggingLabelIndex = -1;
        _overlay?.InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _spacePressed = true;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _spacePressed = false;
            if (_panning)
            {
                _lastLocation = new Point(TranslateX, TranslateY);
                _panning = false;
            }
            e.Handled = true;
        }
        base.OnKeyUp(e);
    }

    private int _draggingLabelIndex = -1;
}