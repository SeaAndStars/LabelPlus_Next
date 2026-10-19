using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Models;
using LabelPlus_Next.Serialization;
using System.Text.Json;
using NLog;

namespace LabelPlus_Next.ViewModels;

public partial class UploadSettingsViewModel : ObservableObject
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    [ObservableProperty] private string? baseUrl = "https://alist1.seastarss.cn";
    [ObservableProperty] private string? password;
    [ObservableProperty] private string? username;

    private readonly string _settingsPath;

    public UploadSettingsViewModel() : this(null) { }

    public UploadSettingsViewModel(string? settingsPath)
    {
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? Path.Combine(AppContext.BaseDirectory, "upload.json")
            : settingsPath;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        _ = LoadAsync();
    }

    public IAsyncRelayCommand SaveCommand { get; }

    public event EventHandler? RefreshRequested;

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                await using var fs = File.OpenRead(_settingsPath);
                var s = await JsonSerializer.DeserializeAsync(fs, AppJsonContext.Default.UploadSettings);
                if (s != null)
                {
                    BaseUrl = string.IsNullOrWhiteSpace(s.BaseUrl) ? BaseUrl : s.BaseUrl;
                    Username = s.Username;
                    Password = s.Password;
                }
            }
        }
        catch (IOException ex)
        {
            Logger.Warn(ex, "Failed to read upload settings from {path}", _settingsPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warn(ex, "Access denied reading upload settings from {path}", _settingsPath);
        }
        catch (JsonException ex)
        {
            Logger.Warn(ex, "Upload settings file contains invalid JSON: {path}", _settingsPath);
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            var s = new UploadSettings { BaseUrl = BaseUrl, Username = Username, Password = Password };
            await using var fs = File.Create(_settingsPath);
            await JsonSerializer.SerializeAsync(fs, s, AppJsonContext.Default.UploadSettings);
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (IOException ex)
        {
            Logger.Error(ex, "Failed to write upload settings to {path}", _settingsPath);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Error(ex, "Access denied writing upload settings to {path}", _settingsPath);
            throw;
        }
        catch (JsonException ex)
        {
            Logger.Error(ex, "Failed to serialize upload settings to {path}", _settingsPath);
            throw;
        }
    }
}
