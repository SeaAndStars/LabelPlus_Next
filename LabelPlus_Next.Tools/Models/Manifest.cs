using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LabelPlus_Next.Tools.Models;

public sealed class Manifest
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = "v1";
    [JsonPropertyName("generatedAt")] public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("projects")] public Dictionary<string, Project> Projects { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class Project
{
    [JsonPropertyName("latest")] public string? Latest { get; set; }
    [JsonPropertyName("releases")] public List<ProjectRelease> Releases { get; set; } = new();
}

public sealed class ProjectRelease
{
    [JsonPropertyName("version")] public string Version { get; set; } = "0.0.0";
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("files")] public List<ProjectReleaseFile> Files { get; set; } = new();
}

public sealed class ProjectReleaseFile
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("entry")] public string? Entry { get; set; }
    [JsonPropertyName("entry_windows")] public string? EntryWindows { get; set; }
    [JsonPropertyName("entry_linux")] public string? EntryLinux { get; set; }
    [JsonPropertyName("entry_macos")] public string? EntryMacos { get; set; }
}
