using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;

namespace LabelPlus_Next.Tools.ViewModels;

/// <summary>
/// The settings dialog edits the main window's view model directly. That keeps a
/// single source of truth: a change made here shows up in the main window immediately
/// and is persisted by the same auto-save path (and flushed when the app exits).
/// </summary>
public class ServerSettingsViewModel : ObservableObject
{
    private readonly MainWindowViewModel main;
    private string? savedMessage;

    public ServerSettingsViewModel(MainWindowViewModel main)
    {
        this.main = main;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
    }

    public IAsyncRelayCommand SaveCommand { get; }

    public string? BaseUrl
    {
        get => main.BaseUrl;
        set
        {
            if (main.BaseUrl == value) return;
            main.BaseUrl = value;
            OnPropertyChanged();
        }
    }

    public string? ApiBaseUrl
    {
        get => main.ApiBaseUrl;
        set
        {
            if (main.ApiBaseUrl == value) return;
            main.ApiBaseUrl = value;
            OnPropertyChanged();
        }
    }

    public string? Username
    {
        get => main.Username;
        set
        {
            if (main.Username == value) return;
            main.Username = value;
            OnPropertyChanged();
        }
    }

    public string? Password
    {
        get => main.Password;
        set
        {
            if (main.Password == value) return;
            main.Password = value;
            OnPropertyChanged();
        }
    }

    public string? TargetPath
    {
        get => main.TargetPath;
        set
        {
            if (main.TargetPath == value) return;
            main.TargetPath = value;
            OnPropertyChanged();
        }
    }

    public string? SavedMessage
    {
        get => savedMessage;
        private set => SetProperty(ref savedMessage, value);
    }

    private async Task SaveAsync()
    {
        var ok = await main.SaveSettingsNowAsync();
        SavedMessage = ok
            ? $"已保存 {DateTime.Now:HH:mm:ss}"
            : "保存失败，请查看日志";
    }
}
