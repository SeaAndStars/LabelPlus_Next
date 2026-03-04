using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using LabelPlus_Next.Services;
using LabelPlus_Next.Services.Api;
using LabelPlus_Next.ViewModels;
using LabelPlus_Next.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using NLog;
using Ursa.Controls;

namespace LabelPlus_Next.Views;

public partial class MainView : UserControl
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly NavMenu? _menuFooter;
    private readonly NavMenu? _menuMain;
    private readonly ContentControl? _navHost;
    private readonly SettingsViewModel _settingsVm;
    private bool _didStartupInit;
    private bool _navCollapsed;
    private TranslateView? _translateView;
    private TeamWorkPage? _teamWorkPage;
    private UploadPage? _uploadPage;
    private SettingsPage? _settingsPage;
    private bool _uploadMenuInserted;
    private string? _lastContentTag;

    private static readonly TimeSpan UploadPermCacheTtl = TimeSpan.FromHours(24);

    private sealed class ApiAuthConfig
    {
        [JsonProperty("baseUrl")] public string? BaseUrl { get; set; }
        [JsonProperty("token")] public string? Token { get; set; }
        [JsonProperty("username")] public string? Username { get; set; }
        [JsonProperty("password")] public string? Password { get; set; }
    }

    public MainView()
    {
        InitializeComponent();
        _navHost = this.FindControl<ContentControl>("NavContent");
        _menuMain = this.FindControl<NavMenu>("NavMenuMain");
        _menuFooter = this.FindControl<NavMenu>("NavMenuFooter");
        _settingsVm = App.Services.GetRequiredService<SettingsViewModel>();
        _navCollapsed = _menuMain?.IsHorizontalCollapsed == true;
        UpdateToggleItemVisual();
        SetContent("translate");
        SelectMenuItemByTag(_menuMain, "translate");
        ClearMenuSelection(_menuFooter);
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_didStartupInit) return;
        _didStartupInit = true;
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is not null) App.Services.GetRequiredService<ITopLevelProvider>().TopLevel = top;
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Warn(ex, "Service provider disposed before setting TopLevel during MainView startup");
        }
        catch (InvalidOperationException ex)
        {
            Logger.Warn(ex, "ITopLevelProvider not available during MainView startup");
        }
        _ = TryInsertUploadMenuAsync();
    }

    #region Upload menu permission cache helpers
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
            else txt = File.ReadAllText(path).Trim();
            if (!txt.Contains('|')) { hasPerm = txt == "1"; if (hasPerm) SaveUploadPermCache(true); return hasPerm; }
            var parts = txt.Split('|');
            if (parts.Length < 2) return false;
            hasPerm = parts[0] == "1";
            if (!long.TryParse(parts[1], out var ts)) return false;
            var written = DateTimeOffset.FromUnixTimeSeconds(ts);
            if (DateTimeOffset.UtcNow - written <= UploadPermCacheTtl) return hasPerm;
            try { File.Delete(path); }
            catch (IOException ex)
            {
                Logger.Warn(ex, "Failed to delete expired upload permission cache {path}", path);
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Warn(ex, "Access denied deleting upload permission cache {path}", path);
            }
            hasPerm = false;
            return false;
        }
        catch (IOException ex)
        {
            Logger.Warn(ex, "Failed to read upload permission cache");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warn(ex, "Access denied reading upload permission cache");
            return false;
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            Logger.Warn(ex, "Upload permission cache decryption failed");
            return false;
        }
        catch (FormatException ex)
        {
            Logger.Warn(ex, "Upload permission cache had invalid format");
            return false;
        }
    }

    private static void SaveUploadPermCache(bool hasPerm)
    {
        try
        {
            var path = GetPermCachePath();
            if (!hasPerm) { if (File.Exists(path)) File.Delete(path); return; }
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
                try { File.SetAttributes(path, FileAttributes.ReadOnly); }
                catch (IOException ex)
                {
                    Logger.Warn(ex, "Failed to mark upload permission cache as read-only {path}", path);
                }
                catch (UnauthorizedAccessException ex)
                {
                    Logger.Warn(ex, "Access denied marking upload permission cache as read-only {path}", path);
                }
            }
        }
        catch (IOException ex)
        {
            Logger.Warn(ex, "Failed to persist upload permission cache");
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warn(ex, "Access denied persisting upload permission cache");
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            Logger.Warn(ex, "Encryption error when saving upload permission cache");
        }
    }
    #endregion

    #region Menu / Navigation
    private void InsertUploadMenuIfAbsent()
    {
        if (_uploadMenuInserted || _menuMain is null) return;
        var uploadItem = new NavMenuItem { Header = "上传", Icon = "⤴", Tag = "upload" };
        var items = _menuMain.Items;
        var insertIndex = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if ((items[i] as Control)?.Tag as string == "deliver") { insertIndex = i; break; }
        }
        if (insertIndex >= 0) items.Insert(insertIndex, uploadItem); else items.Add(uploadItem);
        _uploadMenuInserted = true;
    }

    private async Task TryInsertUploadMenuAsync()
    {
        if (_uploadMenuInserted || _menuMain is null) return;
        if (OperatingSystem.IsBrowser()) return;
        if (TryReadUploadPermCache(out var cached) && cached) { InsertUploadMenuIfAbsent(); return; }
        try
        {
            var cfgPath = Path.Combine(AppContext.BaseDirectory, "upload.json");
            if (!File.Exists(cfgPath)) return;
            var cfg = JsonConvert.DeserializeObject<ApiAuthConfig>(await File.ReadAllTextAsync(cfgPath));
            if (cfg is null || string.IsNullOrWhiteSpace(cfg.BaseUrl)) return;
            var auth = new AuthApi(cfg.BaseUrl);
            ApiResponse<MeData>? me = null;
            if (!string.IsNullOrWhiteSpace(cfg.Token)) me = await auth.GetMeAsync(cfg.Token);
            else if (!string.IsNullOrWhiteSpace(cfg.Username) && !string.IsNullOrWhiteSpace(cfg.Password)) me = await auth.GetMeAsync(cfg.Username, cfg.Password);
            else return;
            if (me is { Code: 200, Data: not null } && me.Data.Permission > 265) { SaveUploadPermCache(true); InsertUploadMenuIfAbsent(); }
            else SaveUploadPermCache(false);
        }
        catch (IOException ex)
        {
            Logger.Warn(ex, "Failed to read upload auth config");
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warn(ex, "Access denied reading upload auth config");
        }
        catch (JsonException ex)
        {
            Logger.Warn(ex, "Upload auth config contained invalid JSON");
        }
        catch (HttpRequestException ex)
        {
            Logger.Warn(ex, "Failed to query upload permission via API");
        }
        catch (TaskCanceledException ex)
        {
            Logger.Warn(ex, "Upload permission query timed out");
        }
    }

    private void ToggleNavCollapse()
    {
        _navCollapsed = !_navCollapsed;
        if (_menuMain is not null) _menuMain.IsHorizontalCollapsed = _navCollapsed;
        UpdateToggleItemVisual();
    }

    private void UpdateToggleItemVisual()
    {
        if (_menuMain?.Items is not IEnumerable items) return;
        var first = items.Cast<object>().OfType<NavMenuItem>().FirstOrDefault();
        if (first is NavMenuItem nmi)
        {
            nmi.Header = _navCollapsed ? "展开" : "收起";
            nmi.Icon = _navCollapsed ? "◀" : "▶";
        }
    }

    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems is not { Count: > 0 }) return;
        if (e.AddedItems[0] is not Control ctrl) return;
        var tag = NormalizeTag(ctrl.Tag as string);
        if (tag == "toggle")
        {
            ToggleNavCollapse();
            if (sender is NavMenu menu)
            {
                var previous = e.RemovedItems is { Count: > 0 } ? e.RemovedItems[0] : null;
                menu.SelectedItem = previous;
            }
            return;
        }
        if (tag == "settings")
        {
            SetContent(tag);
            if (sender == _menuMain) ClearMenuSelection(_menuFooter); else ClearMenuSelection(_menuMain);
            return;
        }
        if (sender == _menuMain) ClearMenuSelection(_menuFooter); else ClearMenuSelection(_menuMain);
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
        var match = items.Cast<object>().OfType<Control>().FirstOrDefault(c => string.Equals((c.Tag as string)?.Trim(), tag, StringComparison.OrdinalIgnoreCase));
        if (match != null) menu.SelectedItem = match;
    }

    private static string? NormalizeTag(string? tag) => string.IsNullOrWhiteSpace(tag) ? null : tag.Trim().ToLowerInvariant();

    private void SetContent(string? tag)
    {
        var host = _navHost;
        if (host is null) return;
        var t = NormalizeTag(tag);
        if (t is null) return;
        switch (t)
        {
            case "translate":
                _translateView ??= new TranslateView { DataContext = App.Services.GetRequiredService<TranslateViewModel>() };
                host.Content = _translateView;
                break;
            case "teamwork":
                _teamWorkPage ??= new TeamWorkPage { DataContext = App.Services.GetRequiredService<TeamWorkViewModel>() };
                host.Content = _teamWorkPage;
                break;
            case "proof":
                host.Content = new SimpleTextPage("校对页面");
                break;
            case "upload":
                _uploadPage ??= new UploadPage { DataContext = App.Services.GetRequiredService<UploadViewModel>() };
                host.Content = _uploadPage;
                break;
            case "deliver":
                host.Content = new SimpleTextPage("交付页面");
                break;
            case "settings":
                _settingsPage ??= new SettingsPage { DataContext = _settingsVm };
                host.Content = _settingsPage;
                break;
            default:
                if (string.IsNullOrEmpty(_lastContentTag)) host.Content = new SimpleTextPage("欢迎");
                break;
        }
        _lastContentTag = t;
    }
    #endregion

    #region External APIs for host window/app
    public bool HasUnsavedTranslationChanges => _translateView?.DataContext is TranslateViewModel tvm && tvm.HasUnsavedChanges;

    public TranslateViewModel? GetTranslateViewModel() => _translateView?.DataContext as TranslateViewModel;

    public async Task<bool> TryAutoSaveBeforeUpdateAsync()
    {
        TranslateViewModel? tvm = null;
        string? path = null;
        bool hasUnsaved = false;

        if (Dispatcher.UIThread.CheckAccess())
        {
            tvm = _translateView?.DataContext as TranslateViewModel;
            path = tvm?.OpenTranslationFilePath;
            hasUnsaved = tvm?.HasUnsavedChanges ?? false;
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                tvm = _translateView?.DataContext as TranslateViewModel;
                path = tvm?.OpenTranslationFilePath;
                hasUnsaved = tvm?.HasUnsavedChanges ?? false;
            });
        }

        if (tvm is null || string.IsNullOrWhiteSpace(path) || !hasUnsaved)
        {
            Logger.Debug("Auto-save before update skipped: no active translation with unsaved changes.");
            return false;
        }

        try
        {
            await tvm.FileSave(path).ConfigureAwait(false);
            Logger.Info("Auto-saved translation file before update: {file}", path);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Auto-save before update failed for {file}", path);
            return false;
        }
    }

    public void OpenTranslateWithFile(string path)
    {
        if (_navHost is null) return;
        SetContent("translate");
        if (_translateView?.DataContext is TranslateViewModel tvm) _ = tvm.LoadTranslationFile(path).ConfigureAwait(false);
    }

    public void OpenTranslateWithFile(string path, CollaborationSession? session)
    {
        if (_navHost is null) return;
        SetContent("translate");
        if (_translateView?.DataContext is TranslateViewModel tvm)
        {
            if (session is not null) tvm.Collab = session;
            _ = tvm.LoadTranslationFile(path).ConfigureAwait(false);
        }
    }

    public async Task OpenTranslateWithFileAsync(string path)
    {
        if (_navHost is null) return;
        SetContent("translate");
        if (_translateView?.DataContext is TranslateViewModel tvm) await tvm.LoadTranslationFile(path).ConfigureAwait(false);
    }

    public async Task OpenTranslateWithFileAsync(string path, CollaborationSession? session)
    {
        if (_navHost is null) return;
        SetContent("translate");
        if (_translateView?.DataContext is TranslateViewModel tvm)
        {
            if (session is not null) tvm.Collab = session;
            await tvm.LoadTranslationFile(path).ConfigureAwait(false);
        }
    }
    #endregion
}