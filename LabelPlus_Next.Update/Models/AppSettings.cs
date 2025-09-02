using System.Text.Json.Serialization;

namespace LabelPlus_Next.Update.Models;

public class AppSettings
{
    [JsonPropertyName("update")] public UpdateSettings Update { get; set; } = new();
}

public class UpdateSettings
{
    // Default, hard-coded configuration for update checking
    public const string DefaultBaseUrl = "https://alist.seastarss.cn";
    public const string DefaultManifestPath = "/OneDrive2/Update/manifest.json";

    [JsonPropertyName("baseUrl")] public string? BaseUrl { get; set; } = DefaultBaseUrl;
    [JsonPropertyName("manifestPath")] public string? ManifestPath { get; set; } = DefaultManifestPath;
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("password")] public string? Password { get; set; }
}
