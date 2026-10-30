using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace LabelPlus_Next.Shared;

/// <summary>
/// Per-user configuration locations. All writable configuration lives under
/// LocalApplicationData/LabelPlus_Next; legacy files next to the executable are
/// only read for migration.
/// </summary>
[ExcludeFromCodeCoverage]
public static class AppConfigPaths
{
    public const string ProductFolder = "LabelPlus_Next";

    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductFolder);

    public static string Settings => Path.Combine(Root, "settings.json");
    public static string UploadSettings => Path.Combine(Root, "upload.json");
    public static string RecentProjects => Path.Combine(Root, "recent_projects.json");
    public static string ToolsSettings => Path.Combine(Root, "tools.settings.json");

    public static string LegacySettings => Path.Combine(AppContext.BaseDirectory, "settings.json");
    public static string LegacyUpdateSettings => Path.Combine(AppContext.BaseDirectory, "update", "settings.json");
    public static string LegacyUploadSettings => Path.Combine(AppContext.BaseDirectory, "upload.json");
    public static string LegacyToolsSettings => Path.Combine(AppContext.BaseDirectory, "tools.settings.json");

    public static string[] SettingsCandidates =>
    [
        Settings,
        LegacySettings,
        LegacyUpdateSettings
    ];

    public static string[] UploadSettingsCandidates =>
    [
        UploadSettings,
        LegacyUploadSettings
    ];

    public static string[] ToolsSettingsCandidates =>
    [
        ToolsSettings,
        LegacyToolsSettings
    ];

    /// <summary>Returns the first existing path, or <paramref name="preferred"/> when none exist.</summary>
    public static string ResolveExisting(string preferred, params string[] fallbacks)
    {
        if (File.Exists(preferred)) return preferred;
        foreach (var path in fallbacks)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
        }
        return preferred;
    }

    public static void EnsureParent(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
    }

    public static void SecureFile(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path)) return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Best effort; the file still lives in the per-user config directory.
        }
    }
}
