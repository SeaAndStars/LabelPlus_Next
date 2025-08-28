using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LabelPlus_Next.Models;

public class ProjectJson
{
    [JsonPropertyName("episodes")]
    public Dictionary<string, EpisodeInfo> Episodes { get; set; } = new();
}

public class EpisodeInfo
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}
