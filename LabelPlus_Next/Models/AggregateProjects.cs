using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LabelPlus_Next.Models;

public sealed class AggregateProjects
{
    [JsonPropertyName("projects")] public Dictionary<string, string> Projects { get; set; } = new();
}
