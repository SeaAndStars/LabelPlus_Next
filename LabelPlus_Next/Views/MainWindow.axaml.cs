using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using LabelPlus_Next.ViewModels;
using LabelPlus_Next.Views.Pages;
using LabelPlus_Next.Views.Windows;
using System.Collections;
using Ursa.Controls;
using Notification = Ursa.Controls.Notification;
using WindowNotificationManager = Ursa.Controls.WindowNotificationManager;

namespace LabelPlus_Next.Views;

public partial class MainWindow : UrsaWindow
{
    private readonly NavMenu? _menuFooter;
    private readonly NavMenu? _menuMain;
    private readonly ContentControl? _navHost;
    private readonly SettingsViewModel _settingsVm = new();
    private bool _didStartupCheck;
    private bool _navCollapsed;

    public MainWindow()
    {
        InitializeComponent();
        _navHost = this.FindControl<ContentControl>("NavContent");
        _menuMain = this.FindControl<NavMenu>("NavMenuMain");
        _menuFooter = this.FindControl<NavMenu>("NavMenuFooter");

        // Initialize collapse state from NavMenu property and sync toggle item text/icon
        _navCollapsed = _menuMain?.IsHorizontalCollapsed == true;
        UpdateToggleItemVisual();

        // Set default page and selection
        SetContent("translate");
        SelectMenuItemByTag(_menuMain, "translate");
        ClearMenuSelection(_menuFooter);

        Opened += OnOpened;
    }

    private void ToggleNavCollapse()
    {
        _navCollapsed = !_navCollapsed;
        if (_menuMain is not null)
        {
            _menuMain.IsHorizontalCollapsed = _navCollapsed;
        }
        UpdateToggleItemVisual();
    }

    private void UpdateToggleItemVisual()
    {
        if (_menuMain?.Items is not IEnumerable items) return;
        var first = items.Cast<object>().OfType<NavMenuItem>().FirstOrDefault();
        if (first is NavMenuItem nmi)
        {
            nmi.Header = _navCollapsed ? "展开" : "收起";
            nmi.Icon = _navCollapsed ? "?" : "?"; // use basic triangles to avoid missing glyphs
        }
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (_didStartupCheck) return;
        _didStartupCheck = true;

        // Build notification manager once
        var manager = WindowNotificationManager.TryGetNotificationManager(this, out var existing) && existing is not null
            ? existing
            : new WindowNotificationManager(this) { Position = NotificationPosition.TopRight };

        try
        {
            // Load settings and run startup update check using the same VM instance used by Settings page
            await _settingsVm.LoadAsync();
            await _settingsVm.CheckAndUpdateOnStartupAsync();

            // Only show update result status
            var message = string.IsNullOrWhiteSpace(_settingsVm.Status) ? "更新检查完成" : _settingsVm.Status;
            await Dispatcher.UIThread.InvokeAsync(() => manager.Show(new Notification("更新", message), showIcon: true, showClose: true, type: NotificationType.Information, classes: ["Light"]));
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => manager.Show(new Notification("更新检查失败", ex.Message), showIcon: true, showClose: true, type: NotificationType.Warning, classes: ["Light"]));
        }
    }

    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems is not { Count: > 0 }) return;
        string? tag = null;
        if (e.AddedItems[0] is Control ctrl)
            tag = ctrl.Tag as string;

        // Toggle expand/collapse
        if (string.Equals(tag, "toggle", StringComparison.Ordinal))
        {
            ToggleNavCollapse();
            // restore previous selection after toggling
            if (sender is NavMenu menu)
            {
                var previous = e.RemovedItems is { Count: > 0 } ? e.RemovedItems[0] : null;
                menu.SelectedItem = previous;
            }
            return;
        }

        // Intercept settings: open modal and restore previous selection
        if (string.Equals(tag, "settings", StringComparison.Ordinal))
        {
            var win = new SettingsWindow { DataContext = _settingsVm };
            if (IsVisible) win.ShowDialog(this);
            else win.Show();

            if (sender is NavMenu menu)
            {
                var previous = e.RemovedItems is { Count: > 0 } ? e.RemovedItems[0] : null;
                // Restore previous selected item; if none, select translate in main menu and clear footer
                if (previous != null)
                {
                    menu.SelectedItem = previous;
                }
                else
                {
                    SelectMenuItemByTag(_menuMain, "translate");
                    ClearMenuSelection(_menuFooter);
                }
            }
            return;
        }

        // Keep main/footer selections mutually exclusive and fully clear highlight
        if (sender == _menuMain)
        {
            ClearMenuSelection(_menuFooter);
        }
        else if (sender == _menuFooter)
        {
            ClearMenuSelection(_menuMain);
        }

        SetContent(tag);
    }

    private static void ClearMenuSelection(NavMenu? menu)
    {
        if (menu is null) return;
        menu.SelectedItem = null;
    }

    private static void SelectMenuItemByTag(NavMenu? menu, string tag)
    {
        if (menu?.Items is not IEnumerable items) return;
        var match = items.Cast<object>()
            .OfType<Control>()
            .FirstOrDefault(c => string.Equals(c.Tag as string, tag, StringComparison.Ordinal));
        if (match != null)
        {
            menu.SelectedItem = match;
        }
    }

    private void SetContent(string? tag)
    {
        var host = _navHost;
        if (host is null) return;
        switch (tag)
        {
            case "translate":
                host.Content = new TranslateView { DataContext = new TranslateViewModel() };
                break;
            case "proof":
                host.Content = new SimpleTextPage("校对页面");
                break;
            case "collab":
                host.Content = new SimpleTextPage("协作页面");
                break;
            case "upload":
                host.Content = new UploadPage { DataContext = new UploadViewModel() };
                break;
            case "deliver":
                host.Content = new SimpleTextPage("交付页面");
                break;
            case "settings":
                // Open as dialog window and keep current content
                var win = new SettingsWindow { DataContext = _settingsVm };
                if (IsVisible)
                    win.ShowDialog(this);
                else
                    win.Show();
                // Keep current page; do not change host.Content
                break;
            default:
                host.Content = new SimpleTextPage("欢迎");
                break;
        }
    }
}
