using System.Text.Json.Serialization;

namespace LabelPlus_Next.Update.Models;

public class AppSettings
{
    [JsonPropertyName("update")] public UpdateSettings Update { get; set; } = new();
}

public class UpdateSettings
{
    [JsonPropertyName("baseUrl")] public string? BaseUrl { get; set; }
    [JsonPropertyName("manifestPath")] public string? ManifestPath { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("password")] public string? Password { get; set; }
}
