using System.Text.Json.Serialization;

namespace LabelPlus_Next.Tools.Models;

public class ToolsSettings
{
    [JsonPropertyName("baseUrl")] public string? BaseUrl { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("password")] public string? Password { get; set; }
}
