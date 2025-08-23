using System.Text.Json.Serialization;

namespace LabelPlus_Next.Models;

public class AppSettings
{
    [JsonPropertyName("update")] public UpdateSettings Update { get; set; } = new();
}

public class UpdateSettings
{
    // e.g. https://webdav.example.com/updates/
    [JsonPropertyName("baseUrl")] public string? BaseUrl { get; set; }

    // e.g. app/manifest.json (relative to base)
    [JsonPropertyName("manifestPath")] public string? ManifestPath { get; set; }

    [JsonPropertyName("username")] public string? Username { get; set; }

    [JsonPropertyName("password")] public string? Password { get; set; }
}
