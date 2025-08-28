using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Serialization;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace LabelPlus_Next.ViewModels;

public partial class UploadSettingsViewModel : ObservableObject
{
    [ObservableProperty] private string? username;
    [ObservableProperty] private string? password;
    [ObservableProperty] private string? baseUrl = "https://alist1.seastarss.cn";

    public IAsyncRelayCommand SaveCommand { get; }

    public event EventHandler? RefreshRequested;

    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "upload.json");

    public UploadSettingsViewModel()
    {
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        _ = LoadAsync();
    }

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
            var s = new Models.UploadSettings { BaseUrl = BaseUrl, Username = Username, Password = Password };
            await using var fs = File.Create(SettingsPath);
            await JsonSerializer.SerializeAsync(fs, s, AppJsonContext.Default.UploadSettings);
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch { }
    }
}
