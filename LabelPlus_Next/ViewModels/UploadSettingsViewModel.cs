using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Models;
using LabelPlus_Next.Serialization;
using System.Text.Json;

namespace LabelPlus_Next.ViewModels;

public partial class UploadSettingsViewModel : ObservableObject
{
    [ObservableProperty] private string? baseUrl = "https://alist1.seastarss.cn";
    [ObservableProperty] private string? password;
    [ObservableProperty] private string? username;

    public UploadSettingsViewModel()
    {
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        _ = LoadAsync();
    }

    public IAsyncRelayCommand SaveCommand { get; }

    private static string SettingsPath
    {
        get => Path.Combine(AppContext.BaseDirectory, "upload.json");
    }

    public event EventHandler? RefreshRequested;

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                await using var fs = File.OpenRead(SettingsPath);
                var s = await JsonSerializer.DeserializeAsync(fs, AppJsonContext.Default.UploadSettings);
                if (s != null)
                {
                    BaseUrl = string.IsNullOrWhiteSpace(s.BaseUrl) ? BaseUrl : s.BaseUrl;
                    Username = s.Username;
                    Password = s.Password;
                }
            }
        }
        catch { }
    }

    private async Task SaveAsync()
    {
        try
        {
            var s = new UploadSettings { BaseUrl = BaseUrl, Username = Username, Password = Password };
            await using var fs = File.Create(SettingsPath);
            await JsonSerializer.SerializeAsync(fs, s, AppJsonContext.Default.UploadSettings);
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch { }
    }
}
