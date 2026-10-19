using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LabelPlus_Next.Tools.Models;

namespace LabelPlus_Next.Tools.Services;

/// <summary>
/// Reads and writes <c>tools.settings.json</c>.
/// Writes go to a temporary file first so a crash or a full disk can never leave
/// the configuration truncated/corrupted.
/// </summary>
public static class ToolsSettingsStore
{
    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>Default location: next to the executable, like the rest of the tool's files.</summary>
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "tools.settings.json");

    public static async Task<ToolsSettings?> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;

        await using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        return await JsonSerializer
            .DeserializeAsync<ToolsSettings>(fs, ReadOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Synchronous variant used during startup, before the UI binds.</summary>
    public static ToolsSettings? Load(string path)
    {
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path, Encoding.UTF8);
        return JsonSerializer.Deserialize<ToolsSettings>(json, ReadOptions);
    }

    public static async Task SaveAsync(string path, ToolsSettings settings, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(settings, WriteOptions);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tempPath = BuildTempPath(path);
        try
        {
            await using (var fs = new FileStream(
                             tempPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             useAsync: true))
            await using (var writer = new StreamWriter(fs, Utf8NoBom))
            {
                await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>Synchronous variant used when the application is shutting down.</summary>
    public static void Save(string path, ToolsSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, WriteOptions);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tempPath = BuildTempPath(path);
        try
        {
            File.WriteAllText(tempPath, json, Utf8NoBom);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    // Unique per writer so an exiting application's flush cannot collide with an
    // in-flight debounced save.
    private static string BuildTempPath(string path) => $"{path}.{Guid.NewGuid():N}.tmp";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // best effort only
        }
    }
}
