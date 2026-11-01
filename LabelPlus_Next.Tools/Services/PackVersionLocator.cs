using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LabelPlus_Next.Tools.Services;

/// <summary>
/// Locates the version metadata used by the publish tool.
/// <para>
/// A selected folder can either be a prepared package directory (which contains
/// Client.version.json/Update.version.json) or a source project directory. In the
/// latter case the version files live in sibling project folders, so the tool also
/// searches the solution/repository root instead of failing with an empty Project.
/// </para>
/// </summary>
public static class PackVersionLocator
{
    private static readonly string[] VersionFileNames =
    {
        "Client.version.json",
        "Update.version.json",
        "Clietn.version.json" // legacy misspelling kept for compatibility
    };

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".idea", "node_modules", "packages", "release", "TestResults"
    };

    /// <summary>Version files directly inside a package folder.</summary>
    public static IReadOnlyList<string> FindVersionFilesInFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return Array.Empty<string>();
        return FindDirect(folder);
    }

    /// <summary>True when the folder looks like a source project/repository that can be built.</summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public static bool LooksLikeSourceProject(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return false;

        try
        {
            return Directory.EnumerateFiles(folder, "*.csproj", SearchOption.TopDirectoryOnly).Any()
                   || Directory.EnumerateFiles(folder, "*.sln", SearchOption.TopDirectoryOnly).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Finds the repository root from the selected folder and/or the solution file.
    /// Both a top-level .sln and the presence of the main/update projects are accepted.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public static string? FindRepoRoot(string? selectedFolder, string? solutionPath, bool allowAppDirectoryFallback = true)
    {
        foreach (var seed in EnumerateSeeds(selectedFolder, solutionPath, allowAppDirectoryFallback))
        {
            DirectoryInfo? directory;
            try
            {
                directory = new DirectoryInfo(seed);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                continue;
            }

            for (var depth = 0; depth < 8 && directory is not null; depth++)
            {
                if (LooksLikeRepoRoot(directory.FullName)) return directory.FullName;
                directory = directory.Parent;
            }
        }

        return null;
    }

    /// <summary>
    /// Version files in the repository: the known project locations first, then a
    /// shallow scan of the root's subdirectories.
    /// </summary>
    public static IReadOnlyList<string> FindVersionFilesInRepo(string repoRoot)
    {
        if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot)) return Array.Empty<string>();

        var result = new List<string>();
        void Add(string path)
        {
            if (!File.Exists(path)) return;
            var fullPath = Path.GetFullPath(path);
            if (result.Any(existing => string.Equals(existing, fullPath, StringComparison.OrdinalIgnoreCase))) return;
            result.Add(fullPath);
        }

        // Preferred layout of this repository: the version belongs to the main app
        // project even when the publish tool is pointed at the desktop source folder.
        Add(Path.Combine(repoRoot, "LabelPlus_Next", "Client.version.json"));
        Add(Path.Combine(repoRoot, "LabelPlus_Next.Update", "Update.version.json"));

        // Generic layouts: files at the root and one level below.
        foreach (var fileName in VersionFileNames)
            Add(Path.Combine(repoRoot, fileName));

        foreach (var directory in SafeEnumerateDirectories(repoRoot))
        {
            var name = Path.GetFileName(directory);
            if (IgnoredDirectoryNames.Contains(name)) continue;
            foreach (var fileName in VersionFileNames)
                Add(Path.Combine(directory, fileName));
        }

        // Prefer the correctly spelled file over the legacy typo in the same folder.
        return result
            .Where(path => !string.Equals(Path.GetFileName(path), "Clietn.version.json", StringComparison.OrdinalIgnoreCase)
                           || !File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "Client.version.json")))
            .ToList();
    }

    private static List<string> FindDirect(string folder)
    {
        var files = new List<string>();
        var update = Path.Combine(folder, "Update.version.json");
        var client = Path.Combine(folder, "Client.version.json");
        var typo = Path.Combine(folder, "Clietn.version.json");

        if (File.Exists(update)) files.Add(Path.GetFullPath(update));
        if (File.Exists(client)) files.Add(Path.GetFullPath(client));
        else if (File.Exists(typo)) files.Add(Path.GetFullPath(typo));

        return files;
    }

    private static IEnumerable<string> EnumerateSeeds(string? selectedFolder, string? solutionPath, bool allowAppDirectoryFallback)
    {
        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            var solutionDir = Directory.Exists(solutionPath) ? solutionPath : Path.GetDirectoryName(solutionPath);
            if (!string.IsNullOrWhiteSpace(solutionDir) && Directory.Exists(solutionDir))
                yield return solutionDir;
        }

        if (!string.IsNullOrWhiteSpace(selectedFolder))
        {
            var selectedDir = Directory.Exists(selectedFolder) ? selectedFolder : Path.GetDirectoryName(selectedFolder);
            if (!string.IsNullOrWhiteSpace(selectedDir) && Directory.Exists(selectedDir))
                yield return selectedDir;
        }

        if (allowAppDirectoryFallback && Directory.Exists(AppContext.BaseDirectory))
            yield return AppContext.BaseDirectory;
    }

    private static bool LooksLikeRepoRoot(string directory)
    {
        try
        {
            if (Directory.EnumerateFiles(directory, "*.sln", SearchOption.TopDirectoryOnly).Any())
                return true;

            return Directory.Exists(Path.Combine(directory, "LabelPlus_Next"))
                   && Directory.Exists(Path.Combine(directory, "LabelPlus_Next.Update"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string directory)
    {
        try
        {
            // Materialize inside the try: enumeration is lazy and otherwise
            // throws later at the caller, outside this helper's catch.
            return Directory.EnumerateDirectories(directory).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
