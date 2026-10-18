using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Update.Services;
using NLog;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LabelPlus_Next.Update.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly TimeSpan WaitPidTimeout = TimeSpan.FromSeconds(30);

    private readonly IUpdaterService _updater;
    private readonly IMessageService _messages;

    private string? _overrideAppDir;
    private int? _waitPid;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private double bytesReceivedInMB;
    [ObservableProperty] private double progressPercentage;
    [ObservableProperty] private TimeSpan remaining;
    [ObservableProperty] private double speed; // MB/s

    [ObservableProperty] private DownloadStatus status = DownloadStatus.Idle;
    [ObservableProperty] private string? statusText = "准备更新...";
    [ObservableProperty] private double totalBytesToReceiveInMB;
    [ObservableProperty] private string? version;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(StopCommand), nameof(StartCommand), nameof(RestartCommand))]
    private bool isRunning;

    public MainWindowViewModel(IUpdaterService updater, IMessageService messages)
    {
        _updater = updater;
        _messages = messages;
        _updater.ProgressChanged += OnServiceProgressChanged;
        StartCommand = new AsyncRelayCommand(StartAsync, () => !IsRunning);
        StopCommand = new RelayCommand(() => _cts?.Cancel(), () => IsRunning);
        RestartCommand = new AsyncRelayCommand(StartAsync, () => !IsRunning);
    }

    public IRelayCommand StartCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand RestartCommand { get; }

    public bool IsIndeterminate => Status is DownloadStatus.Checking or DownloadStatus.Idle;

    partial void OnStatusChanged(DownloadStatus value) => OnPropertyChanged(nameof(IsIndeterminate));

    public void OverrideAppDir(string dir) => _overrideAppDir = dir;
    public void SetWaitPid(int pid) => _waitPid = pid;

    public Task RunUpdateAsyncPublic() => StartAsync();

    private async Task StartAsync()
    {
        if (IsRunning) return;
        IsRunning = true;
        using var cts = new CancellationTokenSource();
        _cts = cts;
        var completed = false;
        try
        {
            await WaitForCallerExitAsync(cts.Token);
            await _updater.RunUpdateAsync(_overrideAppDir, cts.Token);
            completed = _updater.Status is not (DownloadStatus.Error or DownloadStatus.Paused);
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消";
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "StartAsync failed");
            await _messages.ShowAsync($"更新失败: {ex.Message}", "错误");
        }
        finally
        {
            _cts = null;
            IsRunning = false;
        }
        // 成功或已是最新时，更新服务已重启主程序，退出更新器；失败时留在窗口以便重试
        if (completed)
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private async Task WaitForCallerExitAsync(CancellationToken ct)
    {
        if (_waitPid is not int pid || pid <= 0) return;
        try
        {
            using var proc = Process.GetProcessById(pid);
            StatusText = "等待主程序退出...";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(WaitPidTimeout);
            await proc.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException)
        {
            // 进程已退出
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Logger.Warn("PID {pid} did not exit within {timeout}, continuing", pid, WaitPidTimeout);
        }
        catch (InvalidOperationException ex)
        {
            Logger.Debug(ex, "Unable to wait for PID {pid}", pid);
        }
        finally
        {
            _waitPid = null; // 重试时无需再次等待
        }
    }

    private void OnServiceProgressChanged()
    {
        // 服务在后台线程报告进度，这里统一切回 UI 线程
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnServiceProgressChanged);
            return;
        }
        BytesReceivedInMB = _updater.BytesReceivedInMB;
        ProgressPercentage = _updater.ProgressPercentage;
        Remaining = _updater.Remaining;
        Speed = _updater.Speed;
        Status = _updater.Status;
        StatusText = _updater.StatusText;
        TotalBytesToReceiveInMB = _updater.TotalBytesToReceiveInMB;
        Version = _updater.LatestVersion;
    }
}
