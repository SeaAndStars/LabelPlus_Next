using System.Text.Json.Serialization;

namespace LabelPlus_Next.Models;

public class AppSettings
{
    [JsonPropertyName("update")] public UpdateSettings Update { get; set; } = new();
}

public class UpdateSettings
{
    // Default, hard-coded configuration for update checking (used by main app when needed)
    public const string DefaultBaseUrl = "https://alist.seastarss.cn";
    public const string DefaultManifestPath = "/OneDrive/Update/manifest.json";

    // e.g. https://webdav.example.com/updates/
    [JsonPropertyName("baseUrl")] public string? BaseUrl { get; set; } = DefaultBaseUrl;

    // e.g. app/manifest.json (relative to base)
    [JsonPropertyName("manifestPath")] public string? ManifestPath { get; set; } = DefaultManifestPath;

    [JsonPropertyName("username")] public string? Username { get; set; }

    [JsonPropertyName("password")] public string? Password { get; set; }
}
