using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LabelPlus_Next.UpdateClient.ViewModels;

public partial class MainViewViewModel : ViewModelBase
{
    private readonly object _sync = new();

    [ObservableProperty]
    private double progressPercentage;

    [ObservableProperty]
    private double speedMBps;

    [ObservableProperty]
    private double bytesReceivedMB;

    [ObservableProperty]
    private double totalBytesMB = 100;

    [ObservableProperty]
    private TimeSpan remaining;

    [ObservableProperty]
    private string version = "v1.0.0";

    [ObservableProperty]
    private bool isDownloading;

    public double LogoOpacity => Math.Clamp(1.0 - (ProgressPercentage / 100.0), 0, 1);

    partial void OnProgressPercentageChanged(double value)
    {
        OnPropertyChanged(nameof(LogoOpacity));
    }

    [ObservableProperty]
    private string statusText = "就绪";

    private CancellationTokenSource? _cts;

    [RelayCommand]
    private void ToggleStartPause()
    {
        if (!IsDownloading)
        {
            StartDownload();
        }
        else
        {
            PauseDownload();
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _cts?.Cancel();
        IsDownloading = false;
        StatusText = "已停止";
        SpeedMBps = 0;
    }

    [RelayCommand]
    private void Restart()
    {
        Stop();
        ProgressPercentage = 0;
        BytesReceivedMB = 0;
        StatusText = "重新开始";
        StartDownload();
    }

    private void StartDownload()
    {
        if (_cts != null)
        {
            // already running
            return;
        }

        _cts = new CancellationTokenSource();
        IsDownloading = true;
        StatusText = "下载中";

        _ = Task.Run(async () =>
        {
            var rnd = new Random();
            var lastTick = DateTime.UtcNow;
            try
            {
                while (!_cts.IsCancellationRequested && BytesReceivedMB < TotalBytesMB)
                {
                    await Task.Delay(200, _cts.Token);

                    var now = DateTime.UtcNow;
                    var dt = (now - lastTick).TotalSeconds;
                    lastTick = now;
                    var speed = 2 + rnd.NextDouble() * 3; // MB/s
                    var added = speed * dt;

                    BytesReceivedMB = Math.Min(TotalBytesMB, BytesReceivedMB + added);
                    ProgressPercentage = (BytesReceivedMB / TotalBytesMB) * 100.0;
                    SpeedMBps = speed;

                    var remainingMB = TotalBytesMB - BytesReceivedMB;
                    Remaining = TimeSpan.FromSeconds(Math.Max(0, remainingMB / Math.Max(0.1, SpeedMBps)));
                }

                if (BytesReceivedMB >= TotalBytesMB)
                {
                    StatusText = "已完成";
                    IsDownloading = false;
                }
            }
            catch (TaskCanceledException) { }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                SpeedMBps = 0;
            }
        });
    }

    private void PauseDownload()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        IsDownloading = false;
        StatusText = "已暂停";
        SpeedMBps = 0;
    }
}
