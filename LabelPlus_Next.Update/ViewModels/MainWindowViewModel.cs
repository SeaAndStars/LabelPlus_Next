using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Update.Models;
using LabelPlus_Next.Update.Services;
using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace LabelPlus_Next.Update.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly IUpdaterService _updater;
    private readonly IMessageService _messages;

    private string? _overrideAppDir;
    private int? _waitPid;
    [ObservableProperty] private double bytesReceivedInMB;
    [ObservableProperty] private double progressPercentage;
    [ObservableProperty] private TimeSpan remaining;
    [ObservableProperty] private double speed; // MB/s

    [ObservableProperty] private DownloadStatus status = DownloadStatus.Idle;
    [ObservableProperty] private double totalBytesToReceiveInMB;
    [ObservableProperty] private string? version;

    public MainWindowViewModel(IUpdaterService updater, IMessageService messages)
    {
        _updater = updater;
        _messages = messages;
        _updater.ProgressChanged += OnServiceProgressChanged;
        StartCommand = new AsyncRelayCommand(StartAsync);
        StopCommand = new RelayCommand(() => { /* no-op */ });
        RestartCommand = new AsyncRelayCommand(StartAsync);
    }

    public IRelayCommand StartCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand RestartCommand { get; }

    public void OverrideAppDir(string dir) => _overrideAppDir = dir;
    public void SetWaitPid(int pid) => _waitPid = pid;

    public Task RunUpdateAsyncPublic() => StartAsync();

    private static string GetCurrentRid()
    {
        var arch = RuntimeInformation.ProcessArchitecture;
        if (OperatingSystem.IsWindows())
            return arch == Architecture.Arm64 ? "win-arm64" : "win-x64";
        if (OperatingSystem.IsLinux())
            return arch == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
        if (OperatingSystem.IsMacOS())
            return arch == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        return arch == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
    }
    private async Task StartAsync()
    {
        try
        {
            if (_waitPid is int pid && pid > 0)
            {
                try { var proc = Process.GetProcessById(pid); proc.WaitForExit(); }
                catch { }
            }
            await _updater.RunUpdateAsync(_overrideAppDir);
            // After updater completes normally, the updater service will attempt to relaunch main app.
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "StartAsync failed");
            await _messages.ShowAsync($"更新失败: {ex.Message}", "错误");
        }
    }

    private static string? ResolveMainExe(string appDir)
    {
        // Prefer using Client.version.json's project field
        var clientVerPath = Path.Combine(appDir, "Client.version.json");
        try
        {
            if (File.Exists(clientVerPath))
            {
                using var fs = File.OpenRead(clientVerPath);
                using var doc = JsonDocument.Parse(fs);
                if (doc.RootElement.TryGetProperty("project", out var p) && p.ValueKind == JsonValueKind.String)
                {
                    var proj = p.GetString();
                    if (!string.IsNullOrWhiteSpace(proj))
                    {
                        var exe = Path.Combine(appDir, proj + ".exe");
                        if (File.Exists(exe)) return exe;
                    }
                }
            }
        }
        catch { }

        // Fallback search: *.Desktop*.exe in root
        try
        {
            var candidates = Directory.GetFiles(appDir, "*.exe", SearchOption.TopDirectoryOnly);
            foreach (var f in candidates)
            {
                var name = Path.GetFileName(f);
                if (name.Contains(".Desktop", StringComparison.OrdinalIgnoreCase) && !name.Contains("Update", StringComparison.OrdinalIgnoreCase))
                    return f;
            }
            // fallback to any exe except updater
            foreach (var f in candidates)
            {
                if (!Path.GetFileName(f).Contains("Update", StringComparison.OrdinalIgnoreCase)) return f;
            }
        }
        catch { }
        return null;
    }


    private void OnServiceProgressChanged()
    {
        // Pull values from service and update observable properties
        BytesReceivedInMB = _updater.BytesReceivedInMB;
        ProgressPercentage = _updater.ProgressPercentage;
        Remaining = _updater.Remaining;
        Speed = _updater.Speed;
        Status = _updater.Status;
        TotalBytesToReceiveInMB = _updater.TotalBytesToReceiveInMB;
    Version = _updater.LatestVersion;
    }
}
