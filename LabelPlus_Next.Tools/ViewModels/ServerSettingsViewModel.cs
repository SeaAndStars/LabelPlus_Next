using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Tools.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;

namespace LabelPlus_Next.Tools.ViewModels;

public partial class ServerSettingsViewModel : ObservableObject
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private string? baseUrl;
    private string? apiBaseUrl;
    private string? username;
    private string? password;
    private string? targetPath;

    public string? BaseUrl { get => baseUrl; set => SetProperty(ref baseUrl, value); }
    public string? ApiBaseUrl { get => apiBaseUrl; set => SetProperty(ref apiBaseUrl, value); }
    public string? Username { get => username; set => SetProperty(ref username, value); }
    public string? Password { get => password; set => SetProperty(ref password, value); }
    public string? TargetPath { get => targetPath; set => SetProperty(ref targetPath, value); }

    public IAsyncRelayCommand SaveCommand { get; }

    private string SettingsPath => Path.Combine(AppContext.BaseDirectory, "tools.settings.json");

    public ServerSettingsViewModel()
    {
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        _ = LoadAsync();
    }

    public ServerSettingsViewModel(MainWindowViewModel main)
        : this()
    {
        baseUrl = main.BaseUrl;
        username = main.Username;
        password = main.Password;
        targetPath = main.TargetPath;
    }

    private async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            await using var fs = File.OpenRead(SettingsPath);
            var s = await JsonSerializer.DeserializeAsync<ToolsSettings>(fs, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (s is null) return;
            baseUrl = s.BaseUrl;
            apiBaseUrl = s.ApiBaseUrl;
            username = s.Username;
            password = s.Password;
            targetPath = s.TargetPath;
            OnPropertyChanged(nameof(BaseUrl));
            OnPropertyChanged(nameof(ApiBaseUrl));
            OnPropertyChanged(nameof(Username));
            OnPropertyChanged(nameof(Password));
            OnPropertyChanged(nameof(TargetPath));
        }
        catch (IOException ex)
        {
            Logger.Warn(ex, "Failed to load tool settings from {SettingsPath}", SettingsPath);
        }
        catch (JsonException ex)
        {
            Logger.Warn(ex, "Tool settings JSON is invalid at {SettingsPath}", SettingsPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warn(ex, "Access denied loading tool settings from {SettingsPath}", SettingsPath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unexpected error loading tool settings from {SettingsPath}", SettingsPath);
            throw;
        }
    }

    private async Task SaveAsync()
    {
        var s = new ToolsSettings
        {
            BaseUrl = baseUrl,
            ApiBaseUrl = apiBaseUrl,
            Username = username,
            Password = password,
            TargetPath = targetPath
        };
        await using var fs = File.Create(SettingsPath);
        await JsonSerializer.SerializeAsync(fs, s, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    }
}
