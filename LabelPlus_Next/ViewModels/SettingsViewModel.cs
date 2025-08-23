using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using System;
using System.Net;
using System.Threading.Tasks;
using WebDav;

namespace LabelPlus_Next.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    // Shared WebDAV client for the app lifetime
    private static IWebDavClient? _client;

    private readonly ISettingsService _settingsService;
    private readonly IUpdateService _updateService;

    [ObservableProperty] private string? baseUrl;
    [ObservableProperty] private string? manifestPath = "manifest.json";
    [ObservableProperty] private string? username;
    [ObservableProperty] private string? password;
    [ObservableProperty] private string? currentVersion;
    [ObservableProperty] private string? latestVersion;
    [ObservableProperty] private string? updateNotes;
    [ObservableProperty] private string? status;

    public IAsyncRelayCommand VerifyWebDavCommand { get; }

    public SettingsViewModel() : this(new JsonSettingsService(), new WebDavUpdateService()) { }

    public SettingsViewModel(ISettingsService settingsService, IUpdateService updateService)
    {
        _settingsService = settingsService;
        _updateService = updateService;
        VerifyWebDavCommand = new AsyncRelayCommand(VerifyWebDavAsync);
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        try
        {
            var s = await _settingsService.LoadAsync();
            BaseUrl = s.Update.BaseUrl;
            ManifestPath = s.Update.ManifestPath ?? "manifest.json";
            Username = s.Update.Username;
            Password = s.Update.Password;
            Status = "设置已加载";
        }
        catch (Exception ex)
        {
            Status = $"加载失败: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        try
        {
            var s = new AppSettings
            {
                Update = new UpdateSettings
                {
                    BaseUrl = BaseUrl,
                    ManifestPath = ManifestPath,
                    Username = Username,
                    Password = Password
                }
            };
            await _settingsService.SaveAsync(s);
            Status = "保存成功";
        }
        catch (Exception ex)
        {
            Status = $"保存失败: {ex.Message}";
        }
    }

    private bool EnsureWebDavClient()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            Status = "请先填写 BaseUrl";
            return false;
        }
        try
        {
            var uri = new Uri(BaseUrl!, UriKind.Absolute);
            var @params = new WebDavClientParams { BaseAddress = uri };
            if (!string.IsNullOrEmpty(Username))
            {
                @params.Credentials = new NetworkCredential(Username, Password ?? string.Empty);
            }
            // Dispose previous instance if it supports IDisposable
            if (_client is IDisposable d) d.Dispose();
            _client = new WebDavClient(@params);
            return true;
        }
        catch (Exception ex)
        {
            Status = $"WebDAV配置错误: {ex.Message}";
            return false;
        }
    }

    public async Task VerifyWebDavAsync()
    {
        try
        {
            if (!EnsureWebDavClient()) return;
            var path = string.IsNullOrWhiteSpace(ManifestPath) ? string.Empty : ManifestPath!.TrimStart('/');
            var result = await _client!.Propfind(path);
            if (result.IsSuccessful)
            {
                Status = $"WebDAV验证成功，资源数: {result.Resources.Count}";
            }
            else
            {
                Status = $"WebDAV验证失败: {(int)result.StatusCode} {result.Description}";
            }
        }
        catch (Exception ex)
        {
            Status = $"验证异常: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task CheckUpdateAsync()
    {
        try
        {
            Status = "检查更新中...";
            var upd = new UpdateSettings
            {
                BaseUrl = BaseUrl,
                ManifestPath = ManifestPath,
                Username = Username,
                Password = Password
            };
            var m = await _updateService.FetchManifestAsync(upd);
            if (m == null)
            {
                Status = "未获取到清单";
                return;
            }
            LatestVersion = m.Version;
            UpdateNotes = m.Notes;
            Status = string.IsNullOrEmpty(m.Version) ? "清单无版本信息" : $"最新版本: {m.Version}";
        }
        catch (Exception ex)
        {
            Status = $"检查失败: {ex.Message}";
        }
    }
}
