using System;
using System.Threading.Tasks;
using LabelPlus_Next.Update.ViewModels;

namespace LabelPlus_Next.Update.Services;

public interface IUpdaterService
{
    Task RunUpdateAsync(string? overrideAppDir = null);

    // Progress properties/events for binding
    double BytesReceivedInMB { get; }
    double ProgressPercentage { get; }
    TimeSpan Remaining { get; }
    double Speed { get; }
    DownloadStatus Status { get; }
    double TotalBytesToReceiveInMB { get; }
    string? LatestVersion { get; }

    // Notify when any progress-related property changes
    event Action? ProgressChanged;
}
