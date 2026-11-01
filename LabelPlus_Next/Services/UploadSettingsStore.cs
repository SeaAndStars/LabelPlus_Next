using LabelPlus_Next.Models;
using LabelPlus_Next.Serialization;
using LabelPlus_Next.Shared;
using System.Text.Json;

namespace LabelPlus_Next.Services;

/// <summary>
/// Reads/writes upload.json under LocalApplicationData. Legacy upload.json next
/// to the executable is read once and migrated, then never written again.
/// </summary>
public static class UploadSettingsStore
{
    public static string Path => AppConfigPaths.UploadSettings;

    public static string ResolveReadPath()
        => AppConfigPaths.ResolveExisting(Path, AppConfigPaths.UploadSettingsCandidates);

    public static async Task<UploadSettings?> LoadAsync(string? path = null, CancellationToken ct = default)
    {
        var target = string.IsNullOrWhiteSpace(path) ? Path : path;
        var usesAppData = string.Equals(target, Path, StringComparison.Ordinal);
        var readPath = usesAppData ? ResolveReadPath() : target;
        if (!File.Exists(readPath)) return null;

        await using var fs = File.OpenRead(readPath);
        var settings = await JsonSerializer.DeserializeAsync(fs, AppJsonContext.Default.UploadSettings, ct);
        if (settings is null) return null;

        if (usesAppData && !string.Equals(readPath, target, StringComparison.Ordinal))
        {
            try
            {
                await SaveAsync(settings, target, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Best effort: the legacy file remains readable next time.
            }
        }
        return settings;
    }

    public static async Task SaveAsync(UploadSettings settings, string? path = null, CancellationToken ct = default)
    {
        var target = string.IsNullOrWhiteSpace(path) ? Path : path;
        AppConfigPaths.EnsureParent(target);
        await using var fs = File.Create(target);
        await JsonSerializer.SerializeAsync(fs, settings, AppJsonContext.Default.UploadSettings, ct);
        AppConfigPaths.SecureFile(target);
    }
}
