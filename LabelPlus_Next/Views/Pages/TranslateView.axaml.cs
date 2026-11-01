using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LabelPlus_Next.CustomControls;
using LabelPlus_Next.Models;
using LabelPlus_Next.Lang;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;
using LabelPlus_Next.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using Ursa.Controls;
using WindowNotificationManager = Ursa.Controls.WindowNotificationManager;

namespace LabelPlus_Next.Views.Pages;

public partial class TranslateView : UserControl
{
    private bool _initialized;
    private DataGrid? _labelsGrid;
    private LabelItem? _lastCentered;
    private PicViewer? _picViewer;
    private SettingsViewModel? _settingsVm;
    private InputElement? _shortcutHost;
    private FindReplaceWindow? _searchWindow;
    private Popup? _quickInputPopup;
    private TextBox? _labelTextBox;
    private SelectionList? _quickInputList;
    private bool _suppressQuickInsert;

    // 拖拽相关
    private static readonly DataFormat<LabelItem> DragDataFormat =
        DataFormat.CreateInProcessFormat<LabelItem>("application/x-labelplus-labelitem");
    private Control? _lastDropHintRow;
    private LabelItem? _dragCandidate;
    private PointerPressedEventArgs? _dragStartArgs;
    private Point _dragStartPosition;
    private const double DragThreshold = 4;

    public TranslateView()
    {
        InitializeComponent();
        // Culture changes must refresh converter-produced grid text (category names).
        LabelPlus_Next.Services.LocalizationService.CultureChanged += OnCultureChanged;
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        if (Vm is null) return;
        foreach (var item in Vm.CurrentLabels)
            item.NotifyCategoryDisplayChanged();
    }

    private TranslateViewModel? Vm
    {
        get => DataContext as TranslateViewModel;
    }

    private void InitializeComponent() { AvaloniaXamlLoader.Load(this); }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachShortcutHost();
        if (_initialized) return;
        _initialized = true;
        _picViewer = this.FindControl<PicViewer>("Pic");
        _settingsVm = App.Services.GetRequiredService<SettingsViewModel>();
        _settingsVm.LabelOverlaySettingsChanged += OnLabelOverlaySettingsChanged;
        ApplyLabelOverlaySettings();
        _labelsGrid = this.FindControl<DataGrid>("LabelsGrid");
        ResolvePopupControls();
        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
        if (DataContext is TranslateViewModel vm)
        {
            SetNotification(vm);
            vm.SearchPreviewRequested += OnSearchPreviewRequested;
        }
        if (_labelsGrid is not null)
        {
            _labelsGrid.SelectionChanged += OnGridSelectionChanged;
            _labelsGrid.GotFocus += OnGridGotFocus;
            // 拖拽排序事件
            _labelsGrid.AddHandler(InputElement.PointerPressedEvent, OnGridPointerPressed, RoutingStrategies.Tunnel);
            _labelsGrid.AddHandler(InputElement.PointerMovedEvent, OnGridPointerMoved, RoutingStrategies.Tunnel);
            _labelsGrid.AddHandler(InputElement.PointerReleasedEvent, OnGridPointerReleased, RoutingStrategies.Tunnel);
            _labelsGrid.AddHandler(DragDrop.DragOverEvent, OnGridDragOver, RoutingStrategies.Bubble);
            _labelsGrid.AddHandler(DragDrop.DropEvent, OnGridDrop, RoutingStrategies.Bubble);
            _labelsGrid.AddHandler(DragDrop.DragLeaveEvent, OnGridDragLeave, RoutingStrategies.Bubble);
        }
        if (_picViewer is not null) _picViewer.AddLabelRequested += OnAddLabelRequested;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachShortcutHost();
        if (_settingsVm is not null) _settingsVm.LabelOverlaySettingsChanged -= OnLabelOverlaySettingsChanged;
        if (_picViewer is not null) _picViewer.AddLabelRequested -= OnAddLabelRequested;
        if (DataContext is TranslateViewModel vm) vm.SearchPreviewRequested -= OnSearchPreviewRequested;
        if (_searchWindow is { } searchWindow)
        {
            searchWindow.Closed -= OnSearchWindowClosed;
            searchWindow.Close();
            _searchWindow = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ResolvePopupControls();
    }

    private void ResolvePopupControls()
    {
        _quickInputPopup ??= this.FindControl<Popup>("QuickInputPopup");
        _labelTextBox ??= this.FindControl<TextBox>("LabelTextBox");
        _quickInputList ??= this.FindControl<SelectionList>("QuickInputList");

        // Popups are reparented to a PopupRoot; assign DataContext explicitly so
        // compiled bindings keep working after the popup opens.
        if (_quickInputPopup is not null)
        {
            _quickInputPopup.DataContext = DataContext;
            if (_quickInputPopup.Child is Control quickChild) quickChild.DataContext = DataContext;
        }
    }

    private void OnLabelOverlaySettingsChanged(object? sender, EventArgs e) => ApplyLabelOverlaySettings();

    private void ApplyLabelOverlaySettings()
    {
        if (_picViewer is null || _settingsVm is null) return;
        _picViewer.LabelIndexFontScale = _settingsVm.LabelIndexFontScale;
        _picViewer.LabelTipFontScale = _settingsVm.LabelTipFontScale;
        _picViewer.LabelTipBackgroundOpacity = _settingsVm.LabelTipBackgroundOpacity;
    }

    private void SetNotification(TranslateViewModel vm)
    {
        if (TopLevel.GetTopLevel(this) is not Window win) return;
        if (vm.NotificationManager is null)
        {
            vm.NotificationManager = WindowNotificationManager.TryGetNotificationManager(win, out var mgr) ? mgr : new WindowNotificationManager(win)
                { Position = NotificationPosition.TopRight };
        }
        if (vm.NotificationManager is not null)
        {
            // Leave the custom title bar and the top toolbar unobstructed.
            vm.NotificationManager.Position = NotificationPosition.TopRight;
            vm.NotificationManager.Margin = new Thickness(0, 104, 16, 0);
        }
    }

    private void OnGridGotFocus(object? sender, FocusChangedEventArgs e)
    {
        _lastCentered = null;
    }

    private void OnGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_labelsGrid is null || _picViewer is null) return;
        if (_labelsGrid.SelectedItem is LabelItem item)
        {
            _lastCentered = item;
            // Center only when focus is outside PicViewer, but still in a typing context (TextBox) or the grid itself
            var top = TopLevel.GetTopLevel(this);
            var focused = top?.FocusManager?.GetFocusedElement();
            var focusInPic = _picViewer.IsKeyboardFocusWithin;
            var typing = IsTypingContext(focused);
            var gridFocused = _labelsGrid.IsKeyboardFocusWithin;
            if (!focusInPic && (typing || gridFocused))
            {
                _picViewer.CenterOnLabel(item);
            }
        }
    }

    private bool IsTypingContext(object? source)
    {
        if (source is TextBox) return true;
        var top = TopLevel.GetTopLevel(this);
        var focused = top?.FocusManager?.GetFocusedElement();
        if (focused is TextBox) return true;
        if (focused is Control ctrl)
        {
            var tb = ctrl.FindAncestorOfType<TextBox>();
            if (tb is not null) return true;
        }
        if (source is Control sc)
        {
            var tb2 = sc.FindAncestorOfType<TextBox>();
            if (tb2 is not null) return true;
        }
        return false;
    }

    private void AttachShortcutHost()
    {
        if (_shortcutHost is not null) DetachShortcutHost();
        _shortcutHost = TopLevel.GetTopLevel(this) as InputElement;
        _shortcutHost?.AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    private void DetachShortcutHost()
    {
        _shortcutHost?.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
        _shortcutHost = null;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || Vm is null) return;
        if ((e.KeyModifiers & KeyModifiers.Control) == 0 || e.Key != Key.F) return;

        if (_quickInputPopup?.IsOpen == true) CloseQuickInput();
        OpenSearch();
        e.Handled = true;
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;

        if (e.Key == Key.A && (e.KeyModifiers & KeyModifiers.Alt) != 0 &&
            (_labelTextBox?.IsKeyboardFocusWithin == true || _quickInputPopup?.IsOpen == true))
        {
            ToggleQuickInput();
            e.Handled = true;
            return;
        }

        if (_quickInputPopup?.IsOpen == true && HandleQuickInputKey(e))
        {
            e.Handled = true;
            return;
        }

        var vm = Vm;
        var pic = _picViewer ?? this.FindControl<PicViewer>("Pic");
        var gridFocused = _labelsGrid?.IsKeyboardFocusWithin == true;

        if (IsTypingContext(e.Source))
        {
            if ((e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                switch (e.Key)
                {
                    case Key.S:
                        // Allow Ctrl+S / Ctrl+Shift+S to save even when typing in TextBox
                        if ((e.KeyModifiers & KeyModifiers.Shift) != 0) Vm?.SaveAsCommandCommand?.Execute(null);
                        else Vm?.SaveCurrentCommandCommand?.Execute(null);
                        e.Handled = true;
                        return;
                    case Key.Up:
                        MoveSelection(-1);
                        e.Handled = true;
                        return;
                    case Key.Down:
                        MoveSelection(1);
                        e.Handled = true;
                        return;
                    case Key.Left:
                        MoveImageSelection(-1);
                        e.Handled = true;
                        return;
                    case Key.Right:
                        MoveImageSelection(1);
                        e.Handled = true;
                        return;
                }
            }
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None)
        {
            switch (e.Key)
            {
                case Key.Q:
                    if (pic != null)
                    {
                        pic.Mode = ViewerMode.Browse;
                        e.Handled = true;
                    }
                    return;
                case Key.W:
                    if (pic != null)
                    {
                        pic.Mode = ViewerMode.Label;
                        e.Handled = true;
                    }
                    return;
                case Key.E:
                    if (pic != null)
                    {
                        pic.Mode = ViewerMode.Input;
                        e.Handled = true;
                    }
                    return;
                case Key.R:
                    if (pic != null)
                    {
                        pic.Mode = ViewerMode.Check;
                        e.Handled = true;
                    }
                    return;
                case Key.D1:
                case Key.NumPad1:
                    if (gridFocused)
                    {
                        vm?.SetSelectedCategory(1);
                        e.Handled = true;
                    }
                    return;
                case Key.D2:
                case Key.NumPad2:
                    if (gridFocused)
                    {
                        vm?.SetSelectedCategory(2);
                        e.Handled = true;
                    }
                    return;
            }
        }
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            switch (e.Key)
            {
                case Key.Up:
                    MoveSelection(-1);
                    e.Handled = true;
                    return;
                case Key.Down:
                    MoveSelection(1);
                    e.Handled = true;
                    return;
                case Key.Left:
                    MoveImageSelection(-1);
                    e.Handled = true;
                    return;
                case Key.Right:
                    MoveImageSelection(1);
                    e.Handled = true;
                    return;
                case Key.N:
                    vm?.NewTranslationCommandCommand?.Execute(null);
                    e.Handled = true;
                    return;
                case Key.Z:
                    vm?.UndoRemoveLabelCommandCommand?.Execute(null);
                    e.Handled = true;
                    return;
                case Key.O:
                    vm?.OpenTranslationFileCommandCommand?.Execute(null);
                    e.Handled = true;
                    return;
                case Key.S:
                    if ((e.KeyModifiers & KeyModifiers.Shift) != 0) vm?.SaveAsCommandCommand?.Execute(null);
                    else vm?.SaveCurrentCommandCommand?.Execute(null);
                    e.Handled = true;
                    return;
            }
        }
        else if (e.Key == Key.Delete)
        {
            _ = ConfirmDeleteAsync();
            e.Handled = true;
        }
    }

    private async Task ConfirmDeleteAsync()
    {
        var vm = Vm;
        if (vm?.SelectedLabel == null || string.IsNullOrEmpty(vm.SelectedImageFile)) return;
        var owner = TopLevel.GetTopLevel(this) as Window;
        var res = await ConfirmDialog.ShowAsync(owner, new ConfirmDialogOptions
        {
            Title = UiText.T(LangKeys.dialogDeleteLabelTitle, "删除标签"),
            Message = UiText.T(LangKeys.ui0203, "确认删除当前标签?"),
            Severity = ConfirmDialogSeverity.Danger,
            BodyIcon = ConfirmBodyIcon.Delete,
            YesText = UiText.T(LangKeys.ui0204, "删除"),
            NoText = UiText.T(LangKeys.buttonCancel, "取消"),
            ShowNo = true,
            YesIsDanger = true,
        });
        if (res == ConfirmDialogResult.Yes) vm.RemoveLabelCommandCommand?.Execute(null);
    }

    private void MoveSelection(int delta)
    {
        var vm = Vm;
        if (vm == null || vm.CurrentLabels.Count == 0) return;
        var idx = vm.SelectedLabel != null ? vm.CurrentLabels.IndexOf(vm.SelectedLabel) : -1;
        var newIdx = idx < 0 ? 0 : Math.Clamp(idx + delta, 0, vm.CurrentLabels.Count - 1);
        vm.SelectedLabel = vm.CurrentLabels[newIdx];
        vm.CurrentText = vm.SelectedLabel?.Text;
        ScrollSelectedIntoView();
    }
    private void MoveImageSelection(int delta)
    {
        var vm = Vm;
        if (vm == null || vm.ImageFileNames.Count == 0) return;
        var idx = vm.SelectedImageFile != null ? vm.ImageFileNames.IndexOf(vm.SelectedImageFile) : -1;
        var newIdx = idx < 0 ? 0 : Math.Clamp(idx + delta, 0, vm.ImageFileNames.Count - 1);
        vm.SelectedImageFile = vm.ImageFileNames[newIdx];
    }
    private void ScrollSelectedIntoView()
    {
        if (_labelsGrid?.SelectedItem is { } item)
        {
            try { _labelsGrid.ScrollIntoView(item, null); }
            catch
            {
                // ignored
            }
        }
    }
    private async void OnAddLabelRequested(object? sender, PicViewer.AddLabelRequestedEventArgs e)
    {
        if (Vm is null) return;
        await Vm.AddLabelAtAsync((float)e.XPercent, (float)e.YPercent, e.Category);
    }

    // 拖拽排序：按下时只记录候选项，移动超过阈值后才真正启动拖拽，
    // 避免普通点选标签时误触发拖动并改变顺序。
    private void OnGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ClearDragCandidate();
        if (_labelsGrid is null) return;
        var props = e.GetCurrentPoint(_labelsGrid).Properties;
        if (!props.IsLeftButtonPressed) return;

        if (e.Source is Visual v)
        {
            // 找到包含该项的可视容器（Control），其 DataContext 为 LabelItem
            Control? container = v.FindAncestorOfType<Control>();
            while (container is not null && container.DataContext is not LabelItem)
            {
                container = container.FindAncestorOfType<Control>();
            }
            if (container?.DataContext is LabelItem item)
            {
                _dragCandidate = item;
                _dragStartArgs = e;
                _dragStartPosition = e.GetPosition(_labelsGrid);
            }
        }
    }

    private async void OnGridPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_labelsGrid is null || _dragCandidate is null || _dragStartArgs is null) return;
        if (!e.GetCurrentPoint(_labelsGrid).Properties.IsLeftButtonPressed)
        {
            ClearDragCandidate();
            return;
        }

        var position = e.GetPosition(_labelsGrid);
        if (Math.Abs(position.X - _dragStartPosition.X) < DragThreshold &&
            Math.Abs(position.Y - _dragStartPosition.Y) < DragThreshold)
        {
            return;
        }

        var item = _dragCandidate;
        var startArgs = _dragStartArgs;
        ClearDragCandidate();

        var data = new DataTransfer();
        var dragItem = new DataTransferItem();
        dragItem.Set(DragDataFormat, item);
        data.Add(dragItem);
        await DragDrop.DoDragDropAsync(startArgs, data, DragDropEffects.Move);
    }

    private void OnGridPointerReleased(object? sender, PointerReleasedEventArgs e) => ClearDragCandidate();

    private void ClearDragCandidate()
    {
        _dragCandidate = null;
        _dragStartArgs = null;
    }

    // 工具：找到 DataGridRow 控件（无需引用其类型，按名称匹配）
    private static Control? FindRowControl(Visual source)
    {
        Control? c = source.FindAncestorOfType<Control>();
        while (c is not null)
        {
            if (c.GetType().Name == "DataGridRow") return c;
            c = c.FindAncestorOfType<Control>();
        }
        return null;
    }

    private void ClearDropHint()
    {
        if (_lastDropHintRow is null) return;
        _lastDropHintRow.Classes.Remove("drop-before");
        _lastDropHintRow.Classes.Remove("drop-after");
        _lastDropHintRow = null;
    }

    private void ApplyDropHint(Control row, bool after)
    {
        if (!ReferenceEquals(_lastDropHintRow, row))
        {
            ClearDropHint();
            _lastDropHintRow = row;
        }
        row.Classes.Set("drop-before", !after);
        row.Classes.Set("drop-after", after);
    }

    private void OnGridDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DragDataFormat)) return;
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;

        if (_labelsGrid is null) return;
        if (e.Source is Visual v)
        {
            var row = FindRowControl(v);
            if (row is not null)
            {
                var pos = e.GetPosition(row);
                var after = pos.Y > row.Bounds.Height / 2;
                ApplyDropHint(row, after);
            }
            else
            {
                // 悬停在空白区域：清理行高亮
                ClearDropHint();
            }
        }
    }

    private void OnGridDragLeave(object? sender, RoutedEventArgs e)
    {
        ClearDropHint();
    }

    private void OnGridDrop(object? sender, DragEventArgs e)
    {
        var vm = Vm;
        if (vm is null || _labelsGrid is null) return;
        if (!e.DataTransfer.Contains(DragDataFormat)) return;
        if (e.DataTransfer.TryGetValue(DragDataFormat) is not LabelItem dragged) return;

        var oldIndex = vm.CurrentLabels.IndexOf(dragged);
        if (oldIndex < 0) return;

        int newIndex;
        bool after = false;
        // 获取目标行：向上寻找 DataContext 为 LabelItem 的行控件
        if (e.Source is Visual v)
        {
            var row = FindRowControl(v);
            if (row is not null)
            {
                if (row.DataContext is LabelItem targetItem)
                {
                    newIndex = vm.CurrentLabels.IndexOf(targetItem);
                    var pos = e.GetPosition(row);
                    after = pos.Y > row.Bounds.Height / 2;
                    if (after) newIndex++;
                }
                else
                {
                    newIndex = vm.CurrentLabels.Count;
                }
            }
            else
            {
                newIndex = vm.CurrentLabels.Count;
            }
        }
        else
        {
            newIndex = vm.CurrentLabels.Count;
        }

        ClearDropHint();

        if (newIndex == oldIndex || newIndex == oldIndex + 1) { e.Handled = true; return; }

        vm.MoveLabelWithinCurrentImage(oldIndex, newIndex);
        _labelsGrid.SelectedItem = vm.SelectedLabel;
        ScrollSelectedIntoView();
        e.Handled = true;
    }

    // XAML 事件处理
    private void Imagine_manager_OnClick(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        var dlg = new ImageManager { DataContext = new ImageManagerViewModel(), Host = Vm };
        if (owner != null) dlg.Show(owner);
        else dlg.Show();
    }
    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var dlg = new AboutWindow();
        if (TopLevel.GetTopLevel(this) is Window win) await dlg.ShowDialog(win);
    }

    private void CategoryInner_OnClick(object? sender, RoutedEventArgs e) { Vm?.SetSelectedCategory(1); }
    private void CategoryOuter_OnClick(object? sender, RoutedEventArgs e) { Vm?.SetSelectedCategory(2); }
    private void PrevImage_OnClick(object? sender, RoutedEventArgs e) { MoveImageSelection(-1); }
    private void NextImage_OnClick(object? sender, RoutedEventArgs e) { MoveImageSelection(1); }
    private void ZoomIn_OnClick(object? sender, RoutedEventArgs e) => _picViewer?.ZoomIn();
    private void ZoomOut_OnClick(object? sender, RoutedEventArgs e) => _picViewer?.ZoomOut();
    private void ZoomReset_OnClick(object? sender, RoutedEventArgs e) => _picViewer?.ResetZoom();
    private async void FileSetting_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var (groups, notes) = vm.GetFileSettings();
        var dlg = new FileSettings { DataContext = new FileSettingsViewModel { GroupList = groups, Notes = notes ?? string.Empty }, Width = 640, Height = 494 };
        if (TopLevel.GetTopLevel(this) is Window win)
        {
            var result = await dlg.ShowDialog<bool?>(win);
            if (result == true && dlg.DataContext is FileSettingsViewModel fvm) { await vm.SaveFileSettingsAsync(fvm.GroupList, fvm.Notes); }
        }
    }

    // ---------------------------------------------------------- find & replace

    private void SearchButton_OnClick(object? sender, RoutedEventArgs e) => OpenSearch();

    private void OpenSearch()
    {
        if (Vm is not { } vm) return;

        if (_searchWindow is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            existing.FocusSearch();
            return;
        }

        var window = new FindReplaceWindow(vm);
        _searchWindow = window;
        window.Closed += OnSearchWindowClosed;
        vm.IsSearchOpen = true;

        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            var scale = owner.RenderScaling;
            var x = owner.Position.X + (int)((owner.Bounds.Width - window.Width) / 2 * scale);
            var y = owner.Position.Y + (int)((owner.Bounds.Height - window.Height) / 2 * scale);
            window.Position = new PixelPoint(x, y);
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }

    private void OnSearchWindowClosed(object? sender, EventArgs e)
    {
        if (sender is FindReplaceWindow window) window.Closed -= OnSearchWindowClosed;
        _searchWindow = null;
    }

    private void OnSearchPreviewRequested(object? sender, EventArgs e)
    {
        if (Vm is not { } vm) return;
        Dispatcher.UIThread.Post(() =>
        {
            _labelTextBox ??= this.FindControl<TextBox>("LabelTextBox");
            if (_labelTextBox is null) return;
            if (vm.SearchPreviewStart < 0 || vm.SearchPreviewEnd <= vm.SearchPreviewStart)
            {
                _labelTextBox.SelectionStart = 0;
                _labelTextBox.SelectionEnd = 0;
                return;
            }

            var length = _labelTextBox.Text?.Length ?? 0;
            var start = Math.Clamp(vm.SearchPreviewStart, 0, length);
            var end = Math.Clamp(vm.SearchPreviewEnd, start, length);
            _labelTextBox.SelectionStart = start;
            _labelTextBox.SelectionEnd = end;
        }, DispatcherPriority.Background);
    }

    // ------------------------------------------------------------- quick input

    private static readonly IReadOnlyDictionary<Key, string> QuickInputKeys = new Dictionary<Key, string>
    {
        [Key.A] = "啊",
        [Key.E] = "嗯",
        [Key.W] = "呜",
        [Key.G] = "咕",
        [Key.Y] = "咿",
        [Key.J] = "啾",
        [Key.P] = "噗",
        [Key.D1] = "♥",
        [Key.NumPad1] = "♥",
        [Key.D2] = "♡",
        [Key.NumPad2] = "♡",
        [Key.D3] = "♪",
        [Key.NumPad3] = "♪"
    };

    private void LabelTextBox_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        // Replace the default cut/copy/paste menu with the quick-input list.
        e.Handled = true;
        OpenQuickInput();
    }

    private void LabelTextBox_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_labelTextBox is null) return;
        var properties = e.GetCurrentPoint(_labelTextBox).Properties;
        if (properties.IsRightButtonPressed) return;
        if (_quickInputPopup?.IsOpen == true) CloseQuickInput();
    }

    private void ToggleQuickInput()
    {
        if (_quickInputPopup?.IsOpen == true) CloseQuickInput();
        else OpenQuickInput();
    }

    private void OpenQuickInput()
    {
        if (Vm?.SelectedLabel is null) return;
        _quickInputPopup ??= this.FindControl<Popup>("QuickInputPopup");
        if (_quickInputPopup is null) return;

        _quickInputPopup.DataContext = Vm;
        if (_quickInputPopup.Child is Control child) child.DataContext = Vm;

        _quickInputList ??= _quickInputPopup.Child?.GetVisualDescendants().OfType<SelectionList>().FirstOrDefault();
        _quickInputList ??= this.FindControl<SelectionList>("QuickInputList");
        if (_quickInputList is not null && Vm is { } vm)
        {
            _quickInputList.ItemsSource = vm.QuickInputItems;
            if (vm.QuickInputItems.Count > 0)
            {
                _suppressQuickInsert = true;
                _quickInputList.SelectedItem = vm.QuickInputItems[0];
                _suppressQuickInsert = false;
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (_quickInputList is not { ItemCount: > 0 }) return;
                _suppressQuickInsert = true;
                _quickInputList.SelectedIndex = 0;
                _suppressQuickInsert = false;
            }, DispatcherPriority.Loaded);
        }

        _quickInputPopup.IsOpen = true;
    }

    private void CloseQuickInput()
    {
        if (_quickInputPopup is not null) _quickInputPopup.IsOpen = false;
        _labelTextBox?.Focus();
    }

    private void QuickInputPopup_OnClosed(object? sender, EventArgs e)
    {
        _suppressQuickInsert = false;
    }

    private void QuickInputList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressQuickInsert) return;
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is not QuickInputItem item) return;
        InsertQuickText(item.Text);
        CloseQuickInput();
    }

    private bool HandleQuickInputKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseQuickInput();
                return true;
            case Key.Up:
                MoveQuickSelection(-1);
                return true;
            case Key.Down:
                MoveQuickSelection(1);
                return true;
            case Key.Enter:
                InsertSelectedQuickInput();
                return true;
        }

        if (e.KeyModifiers == KeyModifiers.None && QuickInputKeys.TryGetValue(e.Key, out var text))
        {
            InsertQuickText(text);
            CloseQuickInput();
            return true;
        }

        return false;
    }

    private void MoveQuickSelection(int delta)
    {
        if (_quickInputList is not { ItemCount: > 0 }) return;
        var next = Math.Clamp(_quickInputList.SelectedIndex + delta, 0, _quickInputList.ItemCount - 1);
        _suppressQuickInsert = true;
        _quickInputList.SelectedIndex = next;
        _suppressQuickInsert = false;
    }

    private void InsertSelectedQuickInput()
    {
        if (_quickInputList?.SelectedItem is not QuickInputItem item) return;
        InsertQuickText(item.Text);
        CloseQuickInput();
    }

    private void InsertQuickText(string text)
    {
        if (_labelTextBox is null) return;
        var current = _labelTextBox.Text ?? string.Empty;
        var start = Math.Clamp(_labelTextBox.SelectionStart, 0, current.Length);
        var end = Math.Clamp(_labelTextBox.SelectionEnd, start, current.Length);
        _labelTextBox.Text = current[..start] + text + current[end..];
        _labelTextBox.CaretIndex = start + text.Length;
        _labelTextBox.SelectionStart = _labelTextBox.CaretIndex;
        _labelTextBox.SelectionEnd = _labelTextBox.CaretIndex;
        _labelTextBox.Focus();
    }
}
