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
using Avalonia.Threading;

namespace LabelPlus_Next.CustomControls;

public enum ViewerMode { Browse, Label, Input, Check }

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
    private bool _panning;

    // Track whether a left press in label mode should add a label on release (i.e., treated as click, not drag)
    private bool _pendingAddLabelLeft;
    private Point _pressOverlayPos;
    private const double ClickThreshold = 4.0; // pixels

    public static readonly StyledProperty<Control?> OverlayerProperty = AvaloniaProperty.Register<PicViewer, Control?>(nameof(Overlayer));
    public Control? Overlayer { get => GetValue(OverlayerProperty); set => SetValue(OverlayerProperty, value); }

    public static readonly StyledProperty<IImage?> SourceProperty = Image.SourceProperty.AddOwner<PicViewer>();
    public IImage? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    public static readonly StyledProperty<System.Collections.Generic.IEnumerable<LabelItem>?> LabelsProperty =
        AvaloniaProperty.Register<PicViewer, System.Collections.Generic.IEnumerable<LabelItem>?>(nameof(Labels));
    public System.Collections.Generic.IEnumerable<LabelItem>? Labels { get => GetValue(LabelsProperty); set => SetValue(LabelsProperty, value); }

    public static readonly StyledProperty<int> HighlightIndexProperty = AvaloniaProperty.Register<PicViewer, int>(nameof(HighlightIndex), -1);
    public int HighlightIndex { get => GetValue(HighlightIndexProperty); set => SetValue(HighlightIndexProperty, value); }

    public static readonly StyledProperty<LabelItem?> SelectedLabelProperty = AvaloniaProperty.Register<PicViewer, LabelItem?>(nameof(SelectedLabel));
    public LabelItem? SelectedLabel { get => GetValue(SelectedLabelProperty); set => SetValue(SelectedLabelProperty, value); }

    public static readonly StyledProperty<ViewerMode> ModeProperty = AvaloniaProperty.Register<PicViewer, ViewerMode>(nameof(Mode), defaultValue: ViewerMode.Browse);
    public ViewerMode Mode { get => GetValue(ModeProperty); set => SetValue(ModeProperty, value); }

    // Event to request adding a label at clicked position (percent coords in content space)
    public event EventHandler<AddLabelRequestedEventArgs>? AddLabelRequested;

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
        // Sync overlay highlight with external selection and labels changes
        SelectedLabelProperty.Changed.AddClassHandler<PicViewer>((o, e) => o.OnSelectedLabelChanged(e));
        LabelsProperty.Changed.AddClassHandler<PicViewer>((o, e) => o.OnLabelsChanged(e));
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

    private void OnSelectedLabelChanged(AvaloniaPropertyChangedEventArgs _)
    {
        UpdateHighlightFromSelection();
        // Try center on the newly selected label
        if (SelectedLabel is { })
        {
            // Post to UI thread to ensure geometry is up-to-date
            Dispatcher.UIThread.Post(() =>
            {
                if (SelectedLabel is { } l)
                    CenterOnPercent(l.XPercent, l.YPercent);
            });
        }
    }

    private void OnLabelsChanged(AvaloniaPropertyChangedEventArgs _)
    {
        UpdateHighlightFromSelection();
    }

    private void UpdateHighlightFromSelection()
    {
        int idx = -1;
        if (Labels is { } labels && SelectedLabel is { } sel)
        {
            var list = labels.ToList();
            idx = list.IndexOf(sel);
        }
        if (HighlightIndex != idx)
        {
            HighlightIndex = idx;
        }
        _overlay?.InvalidateVisual();
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
        // Initial sync in case SelectedLabel was set before template applied
        UpdateHighlightFromSelection();
    }

    private void HookPointerHandlers()
    {
        if (_handlersHooked) return;
        // No AddHandler here; rely on virtual overrides to avoid double invocation
        _handlersHooked = true;
    }

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

        // Crosshair in Label mode or when holding Ctrl in Browse mode
        if (Mode == ViewerMode.Label || (e.KeyModifiers & KeyModifiers.Control) != 0)
            Cursor = new Cursor(StandardCursorType.Cross);
        else if (!_panning)
            Cursor = null;

        // If we have a pending left press in label mode, start panning only when movement exceeds threshold
        if (!_panning && _pendingAddLabelLeft && _overlay is { })
        {
            var pt = e.GetCurrentPoint(this);
            if (pt.Properties.IsLeftButtonPressed)
            {
                var cur = e.GetPosition(_overlay);
                if (Math.Abs(cur.X - _pressOverlayPos.X) > ClickThreshold || Math.Abs(cur.Y - _pressOverlayPos.Y) > ClickThreshold)
                {
                    _pendingAddLabelLeft = false;
                    e.Pointer.Capture(this);
                    _panning = true;
                    _lastClickPoint = e.GetPosition(this);
                    _lastLocation = new Point(TranslateX, TranslateY);
                }
            }
        }

        if (_panning && Equals(e.Pointer.Captured, this) && _lastClickPoint != null)
        {
            var p = e.GetPosition(this);
            var deltaX = p.X - _lastClickPoint.Value.X;
            var deltaY = p.Y - _lastClickPoint.Value.Y;
            TranslateX = deltaX + (_lastLocation?.X ?? 0);
            TranslateY = deltaY + (_lastLocation?.Y ?? 0);
            return;
        }

        // Hover highlight only
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

        // Dragging updates
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
                HighlightIndex = _draggingLabelIndex; // keep highlight while dragging
                _overlay?.InvalidateVisual();
            }
        }
    }

    private int HitTestLabelIndex(Point pos)
    {
        if (Labels is not { } labels || _overlay is null) return -1;
        var list = labels.ToList();
        var cw = ContentWidth;
        var ch = ContentHeight;
        var side = LabelOverlay.LabelSideLength(cw, ch);
        for (int idx = 0; idx < list.Count; idx++)
        {
            var label = list[idx];
            var centerX = label.XPercent * cw;
            var centerY = label.YPercent * ch;
            var rect = LabelOverlay.GetLabelRect(centerX, centerY, side);
            if (rect.Contains(pos)) return idx;
        }
        return -1;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var isLabelMode = Mode == ViewerMode.Label || ctrl; // in Label mode or holding Ctrl

        var pt = e.GetCurrentPoint(this);
        var isLeft = pt.Properties.IsLeftButtonPressed;
        var isRight = pt.Properties.IsRightButtonPressed;
        if (!isLeft && !isRight)
            return;

        if (_overlay is { })
        {
            var posOverlay = e.GetPosition(_overlay);
            // In label mode, prefer dragging when clicking an existing label
            if (isLabelMode)
            {
                int hit = HitTestLabelIndex(posOverlay);
                if (hit >= 0)
                {
                    e.Pointer.Capture(this);
                    _draggingLabelIndex = hit;
                    HighlightIndex = hit;
                    if (Labels is { } labelsList)
                    {
                        var list = labelsList.ToList();
                        if (hit < list.Count)
                            SelectedLabel = list[hit];
                    }
                    _overlay?.InvalidateVisual();
                    return;
                }
            }

            // Right-click always adds label 2 immediately
            if (isRight && isLabelMode)
            {
                var cw = ContentWidth;
                var ch = ContentHeight;
                if (cw > 0 && ch > 0)
                {
                    var nxR = Math.Clamp(posOverlay.X / cw, 0, 1);
                    var nyR = Math.Clamp(posOverlay.Y / ch, 0, 1);
                    AddLabelRequested?.Invoke(this, new AddLabelRequestedEventArgs(nxR, nyR, 2));
                    e.Handled = true;
                    return;
                }
            }

            // Left-click on empty space in label mode: mark as potential click-to-add; don't start panning yet
            if (isLeft && isLabelMode)
            {
                _pendingAddLabelLeft = true;
                _pressOverlayPos = posOverlay;
                _lastClickPoint = e.GetPosition(this);
                return;
            }
        }

        // Browse mode panning and dragging fallback
        // Only respond to left button for drag/pan (Browse mode behavior)
        if (!isLeft)
            return;

        // Try start dragging if hit in non-label mode
        if (_overlay is { })
        {
            var pos = e.GetPosition(_overlay);
            int hit = HitTestLabelIndex(pos);
            if (hit >= 0)
            {
                e.Pointer.Capture(this);
                _draggingLabelIndex = hit;
                HighlightIndex = hit;
                if (Labels is { } labelsList)
                {
                    var list = labelsList.ToList();
                    if (hit < list.Count)
                        SelectedLabel = list[hit];
                }
                _overlay?.InvalidateVisual();
                return;
            }
        }

        // Not hitting a label: start panning immediately in browse mode
        e.Pointer.Capture(this);
        _lastClickPoint = e.GetPosition(this);
        _lastLocation = new Point(TranslateX, TranslateY);
        _panning = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // If this was a pending left-click in label mode with no drag, add a label (category 1) at release position
        if (_pendingAddLabelLeft && _overlay is { } && (Mode == ViewerMode.Label || (e.KeyModifiers & KeyModifiers.Control) != 0))
        {
            var cw = ContentWidth;
            var ch = ContentHeight;
            if (cw > 0 && ch > 0)
            {
                var rel = e.GetPosition(_overlay);
                var dx = Math.Abs(rel.X - _pressOverlayPos.X);
                var dy = Math.Abs(rel.Y - _pressOverlayPos.Y);
                if (dx <= ClickThreshold && dy <= ClickThreshold)
                {
                    var nx = Math.Clamp(rel.X / cw, 0, 1);
                    var ny = Math.Clamp(rel.Y / ch, 0, 1);
                    AddLabelRequested?.Invoke(this, new AddLabelRequestedEventArgs(nx, ny, 1));
                }
            }
        }

        _pendingAddLabelLeft = false;

        if (Equals(e.Pointer.Captured, this))
            e.Pointer.Capture(null);

        if (_panning)
        {
            _lastLocation = new Point(TranslateX, TranslateY);
        }
        _panning = false;
        _draggingLabelIndex = -1;

        // Clear hover highlight when interaction ends unless still hovering
        if (_overlay is { })
        {
            var pos = e.GetPosition(_overlay);
            var cw = ContentWidth;
            var ch = ContentHeight;
            var side = LabelOverlay.LabelSideLength(cw, ch);
            var hit = -1;
            if (Labels is { } labels)
            {
                var list = labels.ToList();
                for (int idx = 0; idx < list.Count; idx++)
                {
                    var centerX = list[idx].XPercent * cw;
                    var centerY = list[idx].YPercent * ch;
                    var rect = LabelOverlay.GetLabelRect(centerX, centerY, side);
                    if (rect.Contains(pos)) { hit = idx; break; }
                }
            }
            HighlightIndex = hit;
        }
        _overlay?.InvalidateVisual();
    }

    private int _draggingLabelIndex = -1;

    public class AddLabelRequestedEventArgs : EventArgs
    {
        public double XPercent { get; }
        public double YPercent { get; }
        public int Category { get; }
        public AddLabelRequestedEventArgs(double xPercent, double yPercent, int category)
        {
            XPercent = xPercent;
            YPercent = yPercent;
            Category = category;
        }
    }

    // Center view so that the given content percent (0..1) is in the middle of the control
    public void CenterOnPercent(double xPercent, double yPercent)
    {
        if (_image is null) return;
        // Ensure we have recent geometry
        RecomputeContentGeometry();
        var cw = ContentWidth;
        var ch = ContentHeight;
        if (cw <= 0 || ch <= 0) return;

        // Target point in control coords (content offset + percent within content)
        var targetX = ContentOffsetX + xPercent * cw;
        var targetY = ContentOffsetY + yPercent * ch;
        var centerX = Bounds.Width / 2.0;
        var centerY = Bounds.Height / 2.0;

        TranslateX = centerX - targetX;
        TranslateY = centerY - targetY;
    }

    public void CenterOnLabel(LabelItem? item)
    {
        if (item is null) return;
        CenterOnPercent(item.XPercent, item.YPercent);
    }
}