using System.Text.Json.Serialization;

namespace LabelPlus_Next.Models;

public class UpdateManifest
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    [JsonPropertyName("files")] public UpdateFile[] Files { get; set; } = []; // optional
}

public class UpdateFile
{
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
}
