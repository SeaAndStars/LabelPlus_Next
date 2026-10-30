using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Antelcat.I18N.Avalonia;
using LabelPlus_Next.Lang;
using LabelPlus_Next.Models;
using LabelPlus_Next.Serialization;
using LabelPlus_Next.Services;
using LabelPlus_Next.Services.Api;
using LabelPlus_Next.Shared;
using LabelPlus_Next.Views;
using NLog;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Net.Http;

namespace LabelPlus_Next.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly ISettingsService _settingsService;
    private AppSettings? _settings;

    [ObservableProperty] private string? baseUrl = "https://alist.seastarss.cn";

    // New detailed fields
    [ObservableProperty] private string? currentClientVersion;
    [ObservableProperty] private string? currentUpdaterVersion;

    // Old single-field display (kept for compatibility)
    [ObservableProperty] private string? currentVersion;
    [ObservableProperty] private bool isProgressIndeterminate;
    [ObservableProperty] private string? latestClientVersion;
    [ObservableProperty] private string? latestUpdaterVersion;
    [ObservableProperty] private string? latestVersion;
    [ObservableProperty] private string? manifestPath = "/OneDrive2/Update/manifest.json";
    [ObservableProperty] private string? password = "91f158c48d6ab9c5373c992eb07426cad91da2befd437101b1c90797d8c9daf1";
    [ObservableProperty] private string? deeplinkCallbackUrl = "https://localhost:5175/api/deeplink/ack";
    [ObservableProperty] private bool allowLocalHttpFallback = false;
    [ObservableProperty] private string? deeplinkCallbackAuth;
    [ObservableProperty] private string? status;

    [ObservableProperty] private double labelIndexFontScale = LabelOverlaySettings.DefaultLabelIndexFontScale;
    [ObservableProperty] private double labelTipFontScale = LabelOverlaySettings.DefaultLabelTipFontScale;
    [ObservableProperty] private double labelTipBackgroundOpacity = LabelOverlaySettings.DefaultLabelTipBackgroundOpacity;

    [ObservableProperty] private LanguageOption? selectedLanguage;
    public IReadOnlyList<LanguageOption> Languages => LocalizationService.Options;

    [ObservableProperty] private ThemeVariant? selectedTheme;
    [ObservableProperty] private string? themeDisplayName;
    private bool _suppressThemePersist;

    public event EventHandler? LabelOverlaySettingsChanged;

    [ObservableProperty] private string? updateNotes;
    [ObservableProperty] private double updateProgress; // 0-100

    // Progress UI
    [ObservableProperty] private string? updateTask;
    [ObservableProperty] private string? username = "Upgrade";

    public SettingsViewModel() : this(new JsonSettingsService()) { }

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        VerifyHttpCommand = new AsyncRelayCommand(VerifyHttpAsync);
        CheckUpdateCommand = new AsyncRelayCommand(CheckAndUpdateOnStartupAsync);
        SelectedLanguage = LocalizationService.Options[0];
        _ = LoadAsync();
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
        => LocalizationService.Apply(value?.Code);

    partial void OnSelectedThemeChanged(ThemeVariant? value)
    {
        ThemeDisplayName = value == ThemeVariant.Light
            ? I18NExtension.Translate(LangKeys.settingsThemeLight)
            : value == ThemeVariant.Dark
                ? I18NExtension.Translate(LangKeys.settingsThemeDark)
                : I18NExtension.Translate(LangKeys.settingsThemeSystem);

        if (_suppressThemePersist || _settings is null) return;
        _settings.Theme = ThemePreference.Format(value);
        _ = PersistThemeAsync(_settings);
    }

    private async Task PersistThemeAsync(AppSettings settings)
    {
        try
        {
            await _settingsService.SaveAsync(settings);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Persist theme preference failed");
        }
    }

    public IAsyncRelayCommand VerifyHttpCommand { get; }
    public IAsyncRelayCommand CheckUpdateCommand { get; }

    public async Task LoadAsync()
    {
        try
        {
            Logger.Info("Loading update settings...");
            var s = await _settingsService.LoadAsync();
            _settings = s;
            LabelIndexFontScale = s.LabelOverlay.LabelIndexFontScale;
            LabelTipFontScale = s.LabelOverlay.LabelTipFontScale;
            LabelTipBackgroundOpacity = s.LabelOverlay.LabelTipBackgroundOpacity;
            SelectedLanguage = LocalizationService.Resolve(s.Language);

            _suppressThemePersist = true;
            try
            {
                SelectedTheme = ThemePreference.Parse(s.Theme);
                // Ensure the display text is localized even when the parsed value did not change.
                OnSelectedThemeChanged(SelectedTheme);
            }
            finally
            {
                _suppressThemePersist = false;
            }

            // Keep VM defaults as priority; only override when settings.json has non-empty values
            var defBase = BaseUrl;
            var defPath = ManifestPath;
            var defUser = Username;
            var defPwd = Password;
            var effBase = string.IsNullOrWhiteSpace(s.Update.BaseUrl) ? defBase : s.Update.BaseUrl;
            var effPath = string.IsNullOrWhiteSpace(s.Update.ManifestPath) ? defPath : s.Update.ManifestPath;
            var effUser = string.IsNullOrWhiteSpace(s.Update.Username) ? defUser : s.Update.Username;
            var effPwd = string.IsNullOrWhiteSpace(s.Update.Password) ? defPwd : s.Update.Password;
            var effCallback = string.IsNullOrWhiteSpace(s.Update.DeeplinkCallbackUrl) ? DeeplinkCallbackUrl : s.Update.DeeplinkCallbackUrl;
            var effAllowFallback = s.Update.AllowLocalHttpFallback;
            var effAuth = string.IsNullOrWhiteSpace(s.Update.DeeplinkCallbackAuth) ? DeeplinkCallbackAuth : s.Update.DeeplinkCallbackAuth;

            BaseUrl = effBase;
            ManifestPath = effPath;
            Username = effUser;
            Password = effPwd;
            DeeplinkCallbackUrl = effCallback;
            AllowLocalHttpFallback = effAllowFallback;
            DeeplinkCallbackAuth = effAuth;

            // Persist back if we had to fill missing values using defaults
            var needSave = false;
            if (!string.Equals(s.Update.BaseUrl, effBase, StringComparison.Ordinal))
            {
                s.Update.BaseUrl = effBase;
                needSave = true;
            }
            if (!string.Equals(s.Update.ManifestPath, effPath, StringComparison.Ordinal))
            {
                s.Update.ManifestPath = effPath;
                needSave = true;
            }
            if (!string.Equals(s.Update.Username, effUser, StringComparison.Ordinal))
            {
                s.Update.Username = effUser;
                needSave = true;
            }
            if (!string.Equals(s.Update.Password, effPwd, StringComparison.Ordinal))
            {
                s.Update.Password = effPwd;
                needSave = true;
            }
            if (!string.Equals(s.Update.DeeplinkCallbackUrl, effCallback, StringComparison.Ordinal))
            {
                s.Update.DeeplinkCallbackUrl = effCallback;
                needSave = true;
            }
            if (s.Update.AllowLocalHttpFallback != effAllowFallback)
            {
                s.Update.AllowLocalHttpFallback = effAllowFallback;
                needSave = true;
            }
            if (!string.Equals(s.Update.DeeplinkCallbackAuth, effAuth, StringComparison.Ordinal))
            {
                s.Update.DeeplinkCallbackAuth = effAuth;
                needSave = true;
            }
            if (needSave)
            {
                Logger.Info("Settings missing values filled from VM defaults. Saving back to settings.json");
                await _settingsService.SaveAsync(s);
            }

            Status = I18NExtension.Translate(LangKeys.ui0157);
            Logger.Info("Settings effective: baseUrl={baseUrl}, manifestPath={manifest}", BaseUrl, ManifestPath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Load settings failed");
            Status = UiText.Tr("加载失败: {0}", ex.Message);
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        try
        {
            Logger.Info("Saving update settings... baseUrl={baseUrl}, manifestPath={manifest}", BaseUrl, ManifestPath);
            var s = new AppSettings
            {
                Language = SelectedLanguage?.Code ?? "default",
                Theme = ThemePreference.Format(SelectedTheme),
                LabelOverlay = new LabelOverlaySettings
                {
                    LabelIndexFontScale = LabelIndexFontScale,
                    LabelTipFontScale = LabelTipFontScale,
                    LabelTipBackgroundOpacity = LabelTipBackgroundOpacity
                },
                Update = new UpdateSettings
                {
                    BaseUrl = BaseUrl,
                    ManifestPath = ManifestPath,
                    Username = Username,
                    Password = Password,
                    DeeplinkCallbackUrl = DeeplinkCallbackUrl,
                    DeeplinkCallbackAuth = DeeplinkCallbackAuth,
                    AllowLocalHttpFallback = AllowLocalHttpFallback
                }
            };
            await _settingsService.SaveAsync(s);
            _settings = s;
            LabelOverlaySettingsChanged?.Invoke(this, EventArgs.Empty);
            Status = I18NExtension.Translate(LangKeys.ui0158);
            Logger.Info("Settings saved");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Save settings failed");
            Status = UiText.Tr("保存失败: {0}", ex.Message);
        }
    }

    public async Task VerifyHttpAsync()
    {
        try
        {
            Logger.Info("Verify via API download... baseUrl={baseUrl}, manifestPath={manifest}", BaseUrl, ManifestPath);
            if (string.IsNullOrWhiteSpace(BaseUrl))
            {
                Status = I18NExtension.Translate(LangKeys.ui0159);
                Logger.Warn("BaseUrl is empty");
                return;
            }
            if (string.IsNullOrWhiteSpace(ManifestPath))
            {
                Status = I18NExtension.Translate(LangKeys.ui0160);
                Logger.Warn("ManifestPath is empty");
                return;
            }

            // 1) 使用用户名/密码获取 token（如有配置）
            string? token = null;
            if (!string.IsNullOrWhiteSpace(Username))
            {
                Logger.Info("Attempt login via AuthApi with username={user}", Username);
                var auth = new AuthApi(BaseUrl!);
                var login = await auth.LoginAsync(Username!, Password ?? string.Empty);
                if (login.Code == 200 && login.Data is not null && !string.IsNullOrWhiteSpace(login.Data.Token))
                {
                    token = login.Data.Token;
                    Logger.Info("Login success");
                }
                else
                {
                    Logger.Warn("Login failed: code={code}, message={msg}", login.Code, login.Message);
                    Status = UiText.Tr("登录失败: {0} {1}", login.Code, login.Message);
                    return;
                }
            }
            else
            {
                Status = I18NExtension.Translate(LangKeys.ui0161);
                Logger.Warn("Username/Password not configured");
                return;
            }

            // 2) 解析 manifest 远端路径
            string filePath;
            if (Uri.TryCreate(ManifestPath, UriKind.Absolute, out var abs))
                filePath = abs.AbsolutePath;
            else
            {
                var p = ManifestPath!.Replace('\\', '/');
                filePath = p.StartsWith('/') ? p : "/" + p;
            }
            Logger.Debug("Resolved manifest api filePath={path}", filePath);

            // 3) 使用 FileSystemApi.DownloadAsync 获取 raw_url 并下载
            Logger.Info("Begin API download of manifest...");
            var fs = new FileSystemApi(BaseUrl!);
            var result = await fs.DownloadAsync(token!, filePath);
            if (result.Code != 200 || result.Content is null)
            {
                Logger.Warn("Download failed: code={code}, message={msg}", result.Code, result.Message);
                Status = UiText.Tr("下载失败: {0} {1}", result.Code, result.Message);
                return;
            }
            Logger.Info("Download success: bytes={len}", result.Content.Length);

            // 4) 验证清单 JSON 结构（移除 UTF-8 BOM 并裁剪前后空白）
            var bytes = result.Content;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                Logger.Debug("Detected UTF-8 BOM in manifest, stripping");
                bytes = bytes[3..];
            }
            var json = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Trim();

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("projects", out _))
                {
                    Status = I18NExtension.Translate(LangKeys.ui0162);
                    Logger.Info("Manifest schema valid (projects found)");
                }
                else
                {
                    Status = I18NExtension.Translate(LangKeys.ui0163);
                    Logger.Warn("Manifest schema invalid: projects missing");
                }
            }
            catch (JsonException jx)
            {
                // 内容可能不是 JSON（例如 HTML 或压缩包），记录前 64 字节十六进制以便诊断
                var previewLen = Math.Min(64, bytes.Length);
                var hex = BitConverter.ToString(bytes, 0, previewLen).Replace("-", string.Empty);
                Logger.Error(jx, "Manifest JSON parse failed. FirstBytesHex={hex}", hex);
                Status = UiText.Tr("验证失败：清单不是有效 JSON（{0}）", jx.Message);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Verify via API failed");
            Status = UiText.Tr("验证异常: {0}", ex.Message);
        }
    }

    // 通过 IUpdateService 验证清单，便于测试不同失败路径（超时/JSON非法/网络错误/空清单等）
    public async Task VerifyViaUpdateServiceAsync(IUpdateService updateService, UpdateSettings upd, CancellationToken ct = default)
    {
        try
        {
            Status = I18NExtension.Translate(LangKeys.ui0164);
            Logger.Info("Verify via IUpdateService...");
            var manifest = await updateService.FetchManifestAsync(upd, ct);
            if (manifest is null)
            {
                Status = I18NExtension.Translate(LangKeys.ui0165);
                return;
            }
            if (manifest.Files is null)
            {
                Status = I18NExtension.Translate(LangKeys.ui0166);
                return;
            }
            Status = I18NExtension.Translate(LangKeys.ui0167);
        }
        catch (TaskCanceledException)
        {
            Status = I18NExtension.Translate(LangKeys.ui0168);
        }
        catch (TimeoutException)
        {
            Status = I18NExtension.Translate(LangKeys.ui0168);
        }
        catch (JsonException jx)
        {
            Status = UiText.Tr("验证失败：清单不是有效 JSON（{0}）", jx.Message);
        }
        catch (HttpRequestException hx)
        {
            Status = UiText.Tr("验证失败：网络错误（{0}）", hx.Message);
        }
        catch (Exception ex)
        {
            Status = UiText.Tr("验证异常: {0}", ex.Message);
        }
    }

    private const string ClientProjectKey = "LabelPlus_Next.Desktop";
    private const string UpdaterProjectKey = "LabelPlus_Next.Update";

    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private CancellationTokenSource? _updateCts;

    [ObservableProperty] private bool isUpdating;

    [RelayCommand]
    public void CancelUpdate() => _updateCts?.Cancel();

    // 检查更新：优先更新 Updater（分阶段安装，失败回滚），若 Client 有更新则交给 Updater 处理（主程序不下载 Client）
    public async Task CheckAndUpdateOnStartupAsync()
    {
        if (!await _updateGate.WaitAsync(0))
        {
            Status = I18NExtension.Translate(LangKeys.ui0169);
            return;
        }

        using var cts = new CancellationTokenSource();
        _updateCts = cts;
        IsUpdating = true;
        try
        {
            Status = I18NExtension.Translate(LangKeys.ui0170);
            Logger.Info("Update check started");
            if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(ManifestPath))
            {
                Status = I18NExtension.Translate(LangKeys.ui0171);
                return;
            }

            // 本地版本先展示，即使服务器不可达也能看到当前版本
            var appDir = AppContext.BaseDirectory;
            var updateDir = Path.Combine(appDir, "update");
            var clientLocal = ReadLocalVersion(Path.Combine(appDir, "Client.version.json"));
            var updaterLocal = ReadLocalVersion(Path.Combine(updateDir, "Update.version.json"))
                               ?? ReadLocalVersion(Path.Combine(appDir, "Update.version.json"));
            CurrentClientVersion = clientLocal;
            CurrentUpdaterVersion = updaterLocal;
            CurrentVersion = clientLocal;

            var resolver = new AlistResolver(BaseUrl!, Username, Password);
            var manifest = await FetchManifestAsync(resolver, cts.Token);
            if (manifest is null) return;
            Directory.CreateDirectory(updateDir);

            manifest.Projects.TryGetValue(ClientProjectKey, out var cproj);
            manifest.Projects.TryGetValue(UpdaterProjectKey, out var uproj);
            var clientLatest = LatestOf(cproj);
            var updaterLatest = LatestOf(uproj);

            LatestClientVersion = clientLatest;
            LatestUpdaterVersion = updaterLatest;
            LatestVersion = clientLatest;
            UpdateNotes = FindRelease(cproj, clientLatest)?.Notes;
            Logger.Info("Local client={c} updater={u}; remote client={rc} updater={ru}", clientLocal, updaterLocal, clientLatest, updaterLatest);

            var needUpdater = UpdateVersion.IsNewer(updaterLatest, updaterLocal);
            var needClient = UpdateVersion.IsNewer(clientLatest, clientLocal);
            if (!needUpdater && !needClient)
            {
                Status = string.Format(I18NExtension.Translate(LangKeys.ui0156) ?? "Already up to date ({0})", clientLocal);
                return;
            }

            if (needUpdater && uproj is not null && !await UpdateUpdaterAsync(resolver, uproj, updaterLatest!, updateDir, cts.Token))
                return;

            if (needClient)
                await LaunchUpdaterForClientAsync(appDir, updateDir);
        }
        catch (OperationCanceledException)
        {
            Status = I18NExtension.Translate(LangKeys.ui0172);
            Logger.Info("Update check cancelled");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Update check failed");
            Status = string.Format(I18NExtension.Translate(LangKeys.ui0179) ?? "Update check failed: {0}", ex.Message);
        }
        finally
        {
            _updateCts = null;
            IsUpdating = false;
            UpdateTask = null;
            IsProgressIndeterminate = false;
            _updateGate.Release();
        }
    }

    private async Task<ManifestV1?> FetchManifestAsync(AlistResolver resolver, CancellationToken ct)
    {
        var token = await resolver.GetTokenAsync(ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            Status = I18NExtension.Translate(LangKeys.ui0173);
            return null;
        }
        var filePath = Uri.TryCreate(ManifestPath, UriKind.Absolute, out var abs)
            ? abs.AbsolutePath
            : "/" + ManifestPath!.Replace('\\', '/').TrimStart('/');
        var result = await new FileSystemApi(BaseUrl!).DownloadAsync(token, filePath, ct);
        if (result.Code != 200 || result.Content is null)
        {
            Status = I18NExtension.Translate(LangKeys.ui0174);
            Logger.Warn("Manifest download failed: {code} {msg}", result.Code, result.Message);
            return null;
        }
        var bytes = result.Content.AsSpan();
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) bytes = bytes[3..];
        try
        {
            var manifest = JsonSerializer.Deserialize(bytes, AppJsonContext.Default.ManifestV1);
            if (manifest?.Projects is { Count: > 0 }) return manifest;
        }
        catch (JsonException ex)
        {
            Logger.Warn(ex, "Manifest JSON invalid");
        }
        Status = I18NExtension.Translate(LangKeys.ui0166);
        return null;
    }

    private async Task<bool> UpdateUpdaterAsync(AlistResolver resolver, ProjectReleases uproj, string version, string updateDir, CancellationToken ct)
    {
        var file = PickFile(uproj, version);
        if (file is null)
        {
            Logger.Warn("Manifest has no updater package for {ver}", version);
            return true; // 无包可用时不阻塞客户端更新
        }

        var zipPath = UpdateFiles.NewTempFile();
        try
        {
            SetTask(UiText.Tr("下载更新程序 {0}...", version), indeterminate: false);
            var progress = new Progress<TransferProgress>(p => OnUi(() =>
            {
                IsProgressIndeterminate = p.IsIndeterminate;
                UpdateProgress = p.Percentage;
            }));
            if (!await UpdateDownloader.DownloadVerifiedAsync(resolver, file.Value.Url, zipPath, file.Value.Sha256, chunks: 8, progress, ct: ct))
            {
                Status = I18NExtension.Translate(LangKeys.ui0175);
                return false;
            }

            SetTask(UiText.Tr("安装更新程序..."), indeterminate: false);
            var installProgress = new Progress<double>(v => OnUi(() => UpdateProgress = v));
            await StagedInstaller.ApplyZipAsync(zipPath, updateDir, progress: installProgress, ct: ct);
            UpdatePlatform.CleanupForeignArchives(updateDir, Logger);
            CurrentUpdaterVersion = version;
            Status = UiText.Tr("更新程序已更新到 {0}", version);
            Logger.Info("Updater updated to {ver}", version);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Logger.Error(ex, "Installing updater failed; previous files restored");
            Status = UiText.Tr("安装更新程序失败，已回滚: {0}", ex.Message);
            return false;
        }
        finally
        {
            UpdateFiles.TryDelete(zipPath);
        }
    }

    private async Task LaunchUpdaterForClientAsync(string appDir, string updateDir)
    {
        await AutoSaveBeforeClientUpdateAsync();
        var entry = ResolveUpdaterEntry(updateDir) ?? ResolveUpdaterEntry(appDir);
        if (entry is null)
        {
            Status = I18NExtension.Translate(LangKeys.ui0176);
            Logger.Warn("Updater entry not found");
            return;
        }

        try
        {
            Status = I18NExtension.Translate(LangKeys.ui0177);
            var pid = Environment.ProcessId.ToString();
            ProcessStartInfo psi;
            if (OperatingSystem.IsMacOS() && entry.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                // macOS .app 通过 open 启动，参数经 --args 传递
                psi = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
                psi.ArgumentList.Add(entry);
                psi.ArgumentList.Add("--args");
            }
            else
            {
                UpdatePlatform.EnsureExecutable(entry, Logger);
                psi = new ProcessStartInfo(entry) { UseShellExecute = OperatingSystem.IsWindows() };
            }
            psi.WorkingDirectory = Path.GetDirectoryName(entry) ?? updateDir;
            psi.ArgumentList.Add("--waitpid");
            psi.ArgumentList.Add(pid);
            psi.ArgumentList.Add("--targetpath");
            psi.ArgumentList.Add(appDir);
            Process.Start(psi);

            OnUi(() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException or PlatformNotSupportedException)
        {
            Status = I18NExtension.Translate(LangKeys.ui0178);
            Logger.Error(ex, "Failed to start updater");
        }
    }

    private async Task AutoSaveBeforeClientUpdateAsync()
    {
        try
        {
            if (MainWindow.Instance is { } mainWindow) await mainWindow.TryAutoSaveBeforeUpdateAsync();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Auto-save before update encountered an error.");
        }
    }

    private void SetTask(string task, bool indeterminate)
    {
        UpdateTask = task;
        Status = task;
        UpdateProgress = 0;
        IsProgressIndeterminate = indeterminate;
    }

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private static string? LatestOf(ProjectReleases? p)
        => p is null ? null : p.Latest ?? p.Releases.Select(r => r.Version).Where(v => !string.IsNullOrWhiteSpace(v)).MaxBy(v => v, Comparer<string?>.Create(UpdateVersion.Compare));

    private static ReleaseItemV1? FindRelease(ProjectReleases? p, string? version)
        => p?.Releases.FirstOrDefault(r => string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase));

    private static (string Url, string? Sha256)? PickFile(ProjectReleases prj, string version)
    {
        var release = FindRelease(prj, version);
        if (release is null) return null;
        var file = UpdatePlatform.PickForCurrentPlatform(release.Files, f => f.Name, f => f.Url);
        if (file is not null) return (file.Url!, file.Sha256);
        return string.IsNullOrWhiteSpace(release.Url) ? null : (release.Url!, null);
    }

    private static string? ReadLocalVersion(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            using var doc = JsonDocument.Parse(fs);
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.Warn(ex, "读取版本文件失败 {path}", path);
            return null;
        }
    }

    [ExcludeFromCodeCoverage]
    private static string? ResolveUpdaterEntry(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return null;
            if (OperatingSystem.IsWindows())
            {
                var exe = Path.Combine(dir, "LabelPlus_Next.Update.exe");
                if (File.Exists(exe)) return exe;
                var exes = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
                return exes.FirstOrDefault(p => Path.GetFileName(p).Contains("Update", StringComparison.OrdinalIgnoreCase));
            }
            if (OperatingSystem.IsMacOS())
            {
                var app = Path.Combine(dir, "LabelPlus_Next.Update.app");
                if (Directory.Exists(app)) return app;
                var apps = Directory.GetDirectories(dir, "*.app", SearchOption.TopDirectoryOnly);
                var named = apps.FirstOrDefault(p => Path.GetFileName(p).Contains("Update", StringComparison.OrdinalIgnoreCase));
                if (named is not null) return named;
            }
            var bin = Path.Combine(dir, "LabelPlus_Next.Update");
            if (File.Exists(bin)) return bin;
            return Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(p => string.IsNullOrEmpty(Path.GetExtension(p)) && Path.GetFileName(p).Contains("Update", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warn(ex, "Failed to resolve updater entry in {dir}", dir);
            return null;
        }
    }
}
