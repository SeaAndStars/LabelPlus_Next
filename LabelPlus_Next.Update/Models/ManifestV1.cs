using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LabelPlus_Next.Update.Models;

public class ManifestV1
{
    [JsonPropertyName("schema")] public string? Schema { get; set; }
    [JsonPropertyName("generatedAt")] public DateTimeOffset GeneratedAt { get; set; }
    [JsonPropertyName("projects")] public Dictionary<string, ProjectReleases> Projects { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class ProjectReleases
{
    [JsonPropertyName("latest")] public string? Latest { get; set; }
    [JsonPropertyName("releases")] public List<ReleaseItem> Releases { get; set; } = new();
}

public class ReleaseItem
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("files")] public List<ReleaseFile>? Files { get; set; }
}

public class ReleaseFile
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
}
