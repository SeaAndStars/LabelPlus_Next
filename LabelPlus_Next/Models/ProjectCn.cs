using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LabelPlus_Next.Models;

public sealed class ProjectCn
{
    [JsonPropertyName("项目")]
    public Dictionary<string, EpisodeCn> Items { get; set; } = new();
}

public sealed class EpisodeCn
{
    [JsonPropertyName("状态")] public string? Status { get; set; }
    [JsonPropertyName("图源文件路径")] public string? SourcePath { get; set; }
    [JsonPropertyName("翻译文件路径")] public string? TranslatePath { get; set; }
    [JsonPropertyName("嵌字文件路径")] public string? TypesetPath { get; set; }
}
