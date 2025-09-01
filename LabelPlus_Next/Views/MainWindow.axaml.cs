using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using LabelPlus_Next.ViewModels;
using LabelPlus_Next.Services;
using LabelPlus_Next.Views.Pages;
using LabelPlus_Next.Views.Windows;
using System.Collections;
using Ursa.Controls;
using Notification = Ursa.Controls.Notification;
using WindowNotificationManager = Ursa.Controls.WindowNotificationManager;
using LabelPlus_Next.Services.Api;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;

namespace LabelPlus_Next.Views;

public partial class MainWindow : UrsaWindow
{
    public static MainWindow? Instance { get; private set; }
    private readonly NavMenu? _menuFooter;
    private readonly NavMenu? _menuMain;
    private readonly ContentControl? _navHost;
    private readonly SettingsViewModel _settingsVm = new();
    private bool _didStartupCheck;
    private bool _navCollapsed;
    private TranslateView? _translateView;
    private TeamWorkPage? _teamWorkPage;
    private UploadPage? _uploadPage;
    private bool _uploadMenuInserted;
    // Prevent re-entrancy/infinite loop when user chose No on save prompt
    private bool _closeConfirmed;

    // TTL for upload permission cache (24 hours)
    private static readonly TimeSpan UploadPermCacheTtl = TimeSpan.FromHours(24);

    private sealed class ApiAuthConfig
    {
        [JsonProperty("baseUrl")] public string? BaseUrl { get; set; }
        [JsonProperty("token")] public string? Token { get; set; }
        [JsonProperty("username")] public string? Username { get; set; }
        [JsonProperty("password")] public string? Password { get; set; }
    }

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;
        _navHost = this.FindControl<ContentControl>("NavContent");
        _menuMain = this.FindControl<NavMenu>("NavMenuMain");
        _menuFooter = this.FindControl<NavMenu>("NavMenuFooter");

        _navCollapsed = _menuMain?.IsHorizontalCollapsed == true;
        UpdateToggleItemVisual();

        SetContent("translate");
        SelectMenuItemByTag(_menuMain, "translate");
        ClearMenuSelection(_menuFooter);

        Opened += OnOpened;
    }

    private static string GetPermCachePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LabelPlus_Next");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "upload.perm");
    }

    private static bool TryReadUploadPermCache(out bool hasPerm)
    {
        hasPerm = false;
        try
        {
            var path = GetPermCachePath();
            if (!File.Exists(path)) return false;

            string txt;
            if (OperatingSystem.IsWindows())
            {
                var enc = File.ReadAllBytes(path);
                var data = System.Security.Cryptography.ProtectedData.Unprotect(enc, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                txt = Encoding.UTF8.GetString(data);
            }
            else
            {
                txt = File.ReadAllText(path).Trim();
            }

            // Legacy format: just "1" means allowed, without timestamp
            if (!txt.Contains('|'))
            {
                hasPerm = txt == "1";
                if (hasPerm)
                {
                    // upgrade legacy to timestamped format
                    SaveUploadPermCache(true);
                }
                return hasPerm;
            }

            var parts = txt.Split('|');
            if (parts.Length < 2) return false;
            hasPerm = parts[0] == "1";
            if (!long.TryParse(parts[1], out var ts)) return false;
            var written = DateTimeOffset.FromUnixTimeSeconds(ts);
            if (DateTimeOffset.UtcNow - written <= UploadPermCacheTtl)
            {
                return hasPerm; // valid and within TTL
            }
            // expired -> remove cache and force reauth
            try { File.Delete(path); } catch { }
            hasPerm = false;
            return false;
        }
        catch { return false; }
    }

    private static void SaveUploadPermCache(bool hasPerm)
    {
        try
        {
            var path = GetPermCachePath();
            if (!hasPerm)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            var payload = $"1|{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            if (OperatingSystem.IsWindows())
            {
                var data = Encoding.UTF8.GetBytes(payload);
                var enc = System.Security.Cryptography.ProtectedData.Protect(data, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                File.WriteAllBytes(path, enc);
            }
            else
            {
                File.WriteAllText(path, payload);
                try { File.SetAttributes(path, FileAttributes.ReadOnly); } catch { }
            }
        }
        catch { /* ignore */ }
    }

    private void InsertUploadMenuIfAbsent()
    {
        if (_uploadMenuInserted || _menuMain is null) return;
        var uploadItem = new NavMenuItem { Header = "上传", Icon = "⤴", Tag = "upload" };
        var items = _menuMain.Items;
        var insertIndex = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if ((items[i] as Control)?.Tag as string == "deliver")
            {
                insertIndex = i;
                break;
            }
        }
        if (insertIndex >= 0)
            items.Insert(insertIndex, uploadItem);
        else
            items.Add(uploadItem);
        _uploadMenuInserted = true;
    }

    private async Task TryInsertUploadMenuAsync()
    {
        if (_uploadMenuInserted || _menuMain is null) return;

        // Prefer cache first; if valid and not expired
        if (TryReadUploadPermCache(out var cached) && cached)
        {
            InsertUploadMenuIfAbsent();
            return;
        }

        try
        {
            var cfgPath = Path.Combine(AppContext.BaseDirectory, "upload.json");
            if (!File.Exists(cfgPath)) return;
            var cfg = JsonConvert.DeserializeObject<ApiAuthConfig>(await File.ReadAllTextAsync(cfgPath));
            if (cfg is null || string.IsNullOrWhiteSpace(cfg.BaseUrl)) return;

            var auth = new AuthApi(cfg.BaseUrl);
            ApiResponse<MeData>? me = null;
            if (!string.IsNullOrWhiteSpace(cfg.Token))
                me = await auth.GetMeAsync(cfg.Token);
            else if (!string.IsNullOrWhiteSpace(cfg.Username) && !string.IsNullOrWhiteSpace(cfg.Password))
                me = await auth.GetMeAsync(cfg.Username, cfg.Password);
            else
                return;

            if (me is { Code: 200, Data: not null } && me.Data.Permission > 265)
            {
                SaveUploadPermCache(true);
                InsertUploadMenuIfAbsent();
            }
            else
            {
                SaveUploadPermCache(false);
            }
        }
        catch
        {
            // Ignore errors; do not insert menu nor cache
        }
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
            nmi.Icon = _navCollapsed ? "?" : "?";
        }
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (_didStartupCheck) return;
        _didStartupCheck = true;

        var manager = WindowNotificationManager.TryGetNotificationManager(this, out var existing) && existing is not null
            ? existing
            : new WindowNotificationManager(this) { Position = NotificationPosition.TopRight };

        try
        {
            await _settingsVm.LoadAsync();
            await _settingsVm.CheckAndUpdateOnStartupAsync();
            var message = string.IsNullOrWhiteSpace(_settingsVm.Status) ? "更新检查完成" : _settingsVm.Status;
            await Dispatcher.UIThread.InvokeAsync(() => manager.Show(new Notification("更新", message), showIcon: true, showClose: true, type: NotificationType.Information, classes: ["Light"]));
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => manager.Show(new Notification("更新检查失败", ex.Message), showIcon: true, showClose: true, type: NotificationType.Warning, classes: ["Light"]));
        }

        _ = TryInsertUploadMenuAsync();
    }

    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems is not { Count: > 0 }) return;
        string? tag = null;
        if (e.AddedItems[0] is Control ctrl)
            tag = ctrl.Tag as string;

        if (string.Equals(tag, "toggle", StringComparison.Ordinal))
        {
            ToggleNavCollapse();
            if (sender is NavMenu menu)
            {
                var previous = e.RemovedItems is { Count: > 0 } ? e.RemovedItems[0] : null;
                menu.SelectedItem = previous;
            }
            return;
        }

        if (string.Equals(tag, "settings", StringComparison.Ordinal))
        {
            var win = new SettingsWindow { DataContext = _settingsVm };
            if (IsVisible) win.ShowDialog(this);
            else win.Show();

            if (sender is NavMenu menu)
            {
                var previous = e.RemovedItems is { Count: > 0 } ? e.RemovedItems[0] : null;
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

    private static void SelectMenuItemByTag(NavMenu? menu, string tag
    )
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
                _translateView ??= new TranslateView { DataContext = new TranslateViewModel() };
                host.Content = _translateView;
                break;
            case "teamwork":
                _teamWorkPage ??= new TeamWorkPage { DataContext = new TeamWorkViewModel() };
                host.Content = _teamWorkPage;
                break;
            case "proof":
                host.Content = new SimpleTextPage("校对页面");
                break;
            case "upload":
                _uploadPage ??= new UploadPage { DataContext = new UploadViewModel() };
                host.Content = _uploadPage;
                break;
            case "deliver":
                host.Content = new SimpleTextPage("交付页面");
                break;
            case "settings":
                var win = new SettingsWindow { DataContext = _settingsVm };
                if (IsVisible)
                    win.ShowDialog(this);
                else
                    win.Show();
                break;
            default:
                host.Content = new SimpleTextPage("欢迎");
                break;
        }
    }

    public void OpenTranslateWithFile(string path)
    {
        if (_navHost is null) return;
        SetContent("translate");
        if (_translateView?.DataContext is TranslateViewModel tvm)
        {
            _ = tvm.LoadTranslationFile(path);
        }
    }

    public void OpenTranslateWithFile(string path, CollaborationSession? session)
    {
        if (_navHost is null) return;
        SetContent("translate");
        if (_translateView?.DataContext is TranslateViewModel tvm)
        {
            if (session is not null) tvm.Collab = session;
            _ = tvm.LoadTranslationFile(path);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // If already confirmed once, proceed
        if (_closeConfirmed)
        {
            base.OnClosing(e);
            return;
        }

        // If translate tab is open and has unsaved changes, defer close decision to an async flow
        if (_translateView?.DataContext is TranslateViewModel tvm && tvm.HasUnsavedChanges)
        {
            e.Cancel = true; // prevent immediate close
            _ = PromptSaveAndCloseAsync(tvm); // run async workflow without blocking UI thread
            return;
        }
        base.OnClosing(e);
    }

    private async Task PromptSaveAndCloseAsync(TranslateViewModel tvm)
    {
        var res = await MessageBox.ShowAsync("当前译文有未保存的更改，是否保存？", "关闭确认", MessageBoxIcon.Warning, MessageBoxButton.YesNoCancel);
        if (res == MessageBoxResult.Cancel)
        {
            // user cancelled: do nothing, keep window open
            return;
        }
        if (res == MessageBoxResult.Yes)
        {
            if (!string.IsNullOrEmpty(tvm.OpenTranslationFilePath))
                await tvm.FileSave(tvm.OpenTranslationFilePath);
            else
                await tvm.SaveAsCommand();
        }
        // For No or after saving: close for real on UI thread, and mark confirmed to avoid re-prompt
        _closeConfirmed = true;
        await Dispatcher.UIThread.InvokeAsync(Close);
    }
}
