using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LabelPlus_Next.Models;

public sealed class ProjectCn
{
    [JsonPropertyName("��Ŀ")]
    public Dictionary<string, EpisodeCn> Items { get; set; } = new();
}

public sealed class EpisodeCn
{
    [JsonPropertyName("״̬")] public string? Status { get; set; }
    [JsonPropertyName("ͼԴ�ļ�·��")] public string? SourcePath { get; set; }
    [JsonPropertyName("�����ļ�·��")] public string? TranslatePath { get; set; }
    [JsonPropertyName("Ƕ���ļ�·��")] public string? TypesetPath { get; set; }
}
