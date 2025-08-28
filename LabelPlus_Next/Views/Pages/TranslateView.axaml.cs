using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions; // for I18NExtension
using Avalonia.VisualTree;
using LabelPlus_Next.CustomControls;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;
using Ursa.Controls;
using LabelPlus_Next.Models;

namespace LabelPlus_Next.Views.Pages;

public partial class TranslateView : UserControl
{
    private PicViewer? _picViewer; private DataGrid? _labelsGrid; private bool _initialized;
    private LabelItem? _lastCentered;

    public TranslateView(){ InitializeComponent(); }
    private void InitializeComponent(){ AvaloniaXamlLoader.Load(this); }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_initialized) return; _initialized = true;
        _picViewer = this.FindControl<PicViewer>("Pic");
        _labelsGrid = this.FindControl<DataGrid>("LabelsGrid");
        this.AddHandler(InputElement.KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
        if (DataContext is TranslateViewModel vm) InitServices(vm);
        if (_labelsGrid is not null)
        {
            _labelsGrid.SelectionChanged += OnGridSelectionChanged;
            _labelsGrid.GotFocus += OnGridGotFocus;
        }
        if (_picViewer is not null) _picViewer.AddLabelRequested += OnAddLabelRequested;
    }

    private void OnGridGotFocus(object? sender, GotFocusEventArgs e)
    {
        _lastCentered = null;
    }

    private void OnGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_labelsGrid is null || _picViewer is null) return;
        if (!_labelsGrid.IsKeyboardFocusWithin) return;
        if (_labelsGrid.SelectedItem is LabelItem item)
        {
            if (!ReferenceEquals(item, _lastCentered))
            {
                _picViewer.CenterOnLabel(item);
                _lastCentered = item;
            }
        }
    }

    private void InitServices(TranslateViewModel vm)
    {
        if (TopLevel.GetTopLevel(this) is not Window win) return;
        vm.InitializeServices(new AvaloniaFileDialogService(win));
        if (vm.NotificationManager is null)
        {
            vm.NotificationManager = WindowNotificationManager.TryGetNotificationManager(win, out var mgr) ? mgr : new WindowNotificationManager(win)
            { Position = Avalonia.Controls.Notifications.NotificationPosition.TopRight };
        }
    }

    private TranslateViewModel? Vm => DataContext as TranslateViewModel;

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

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        var vm = Vm; var pic = _picViewer ?? this.FindControl<PicViewer>("Pic");
        bool gridFocused = _labelsGrid?.IsKeyboardFocusWithin == true;

        if (IsTypingContext(e.Source))
        {
            if ((e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                switch (e.Key)
                {
                    case Key.Up: MoveSelection(-1); e.Handled = true; return;
                    case Key.Down: MoveSelection(1); e.Handled = true; return;
                    case Key.Left: MoveImageSelection(-1); e.Handled = true; return;
                    case Key.Right: MoveImageSelection(1); e.Handled = true; return;
                }
            }
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None)
        {
            switch (e.Key)
            {
                case Key.Q: if (pic!=null) { pic.Mode = ViewerMode.Browse; e.Handled = true; } return;
                case Key.W: if (pic!=null) { pic.Mode = ViewerMode.Label; e.Handled = true; } return;
                case Key.E: if (pic!=null) { pic.Mode = ViewerMode.Input; e.Handled = true; } return;
                case Key.R: if (pic!=null) { pic.Mode = ViewerMode.Check; e.Handled = true; } return;
                case Key.D1:
                case Key.NumPad1:
                    if (gridFocused) { vm?.SetSelectedCategory(1); e.Handled = true; }
                    return;
                case Key.D2:
                case Key.NumPad2:
                    if (gridFocused) { vm?.SetSelectedCategory(2); e.Handled = true; }
                    return;
            }
        }
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            switch (e.Key)
            {
                case Key.Up: MoveSelection(-1); e.Handled = true; return;
                case Key.Down: MoveSelection(1); e.Handled = true; return;
                case Key.Left: MoveImageSelection(-1); e.Handled = true; return;
                case Key.Right: MoveImageSelection(1); e.Handled = true; return;
                case Key.N: (vm?.NewTranslationCommandCommand)?.Execute(null); e.Handled = true; return;
                case Key.Z: (vm?.UndoRemoveLabelCommandCommand)?.Execute(null); e.Handled = true; return;
                case Key.O: (vm?.OpenTranslationFileCommandCommand)?.Execute(null); e.Handled = true; return;
                case Key.S:
                    if ((e.KeyModifiers & KeyModifiers.Shift) != 0) (vm?.SaveAsCommandCommand)?.Execute(null);
                    else (vm?.SaveCurrentCommandCommand)?.Execute(null);
                    e.Handled = true; return;
            }
        }
        else if (e.Key == Key.Delete)
        {
            _ = ConfirmDeleteAsync(); e.Handled = true; return;
        }
    }

    private async Task ConfirmDeleteAsync()
    { var vm = Vm; if (vm?.SelectedLabel == null || string.IsNullOrEmpty(vm.SelectedImageFile)) return; var res = await MessageBox.ShowAsync("确认删除当前标签?", "删除", MessageBoxIcon.Warning, MessageBoxButton.YesNo); if (res == MessageBoxResult.Yes) (vm.RemoveLabelCommandCommand)?.Execute(null); }

    private void MoveSelection(int delta)
    { var vm = Vm; if (vm == null || vm.CurrentLabels.Count==0) return; var idx = vm.SelectedLabel!=null? vm.CurrentLabels.IndexOf(vm.SelectedLabel): -1; var newIdx = idx < 0 ? 0 : Math.Clamp(idx + delta, 0, vm.CurrentLabels.Count - 1); vm.SelectedLabel = vm.CurrentLabels[newIdx]; vm.CurrentText = vm.SelectedLabel?.Text; ScrollSelectedIntoView(); }
    private void MoveImageSelection(int delta)
    { var vm = Vm; if (vm == null || vm.ImageFileNames.Count==0) return; var idx = vm.SelectedImageFile!=null? vm.ImageFileNames.IndexOf(vm.SelectedImageFile): -1; var newIdx = idx < 0 ? 0 : Math.Clamp(idx + delta, 0, vm.ImageFileNames.Count -1); vm.SelectedImageFile = vm.ImageFileNames[newIdx]; }
    private void ScrollSelectedIntoView(){ if (_labelsGrid?.SelectedItem is { } item){ try{ _labelsGrid.ScrollIntoView(item,null);} catch { } } }
    private async void OnAddLabelRequested(object? sender, PicViewer.AddLabelRequestedEventArgs e){ if (Vm is null) return; await Vm.AddLabelAtAsync((float)e.XPercent,(float)e.YPercent,e.Category); }

    // XAML 事件处理
    private void Imagine_manager_OnClick(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        var dlg = new ImageManager { DataContext = new ImageManagerViewModel() };
        if (owner != null) dlg.Show(owner); else dlg.Show();
    }
    private async void OnAboutClick(object? sender, RoutedEventArgs e){ var dlg = new AboutWindow(); if (TopLevel.GetTopLevel(this) is Window win) await dlg.ShowDialog(win); }
    private void LangComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (sender is not ComboBox cb || cb.SelectedItem is not string s) return;
            I18NExtension.Culture = new CultureInfo(s);
        }
        catch (CultureNotFoundException)
        {
            I18NExtension.Culture = CultureInfo.CurrentUICulture;
        }
    }
    private void BrowseMode_OnClick(object? sender, RoutedEventArgs e){ var pic=_picViewer ?? this.FindControl<PicViewer>("Pic"); if (pic!=null) pic.Mode=ViewerMode.Browse; }
    private void LabelMode_OnClick(object? sender, RoutedEventArgs e){ var pic=_picViewer ?? this.FindControl<PicViewer>("Pic"); if (pic!=null) pic.Mode=ViewerMode.Label; }
    private void CategoryInner_OnClick(object? sender, RoutedEventArgs e){ Vm?.SetSelectedCategory(1); }
    private void CategoryOuter_OnClick(object? sender, RoutedEventArgs e){ Vm?.SetSelectedCategory(2); }
    private void PrevImage_OnClick(object? sender, RoutedEventArgs e){ MoveImageSelection(-1); }
    private void NextImage_OnClick(object? sender, RoutedEventArgs e){ MoveImageSelection(1); }
    private async void FileSetting_OnClick(object? sender, RoutedEventArgs e){ if (Vm is not TranslateViewModel vm) return; var (groups, notes) = vm.GetFileSettings(); var dlg = new FileSettings{ DataContext = new FileSettingsViewModel { GroupList = groups, Notes = notes }, Width=600, Height=350}; if (TopLevel.GetTopLevel(this) is Window win){ var result = await dlg.ShowDialog<bool?>(win); if (result==true && dlg.DataContext is FileSettingsViewModel fvm){ await vm.SaveFileSettingsAsync(fvm.GroupList, fvm.Notes); } } }
}
