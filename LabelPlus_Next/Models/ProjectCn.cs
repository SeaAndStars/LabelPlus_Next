using System.Text.Json.Serialization;

namespace LabelPlus_Next.Models;

public sealed class ProjectCn
{
    [JsonPropertyName("项目")]
    public Dictionary<string, EpisodeCn> Items { get; set; } = new();
}

public sealed class EpisodeCn
{
    // 基础状态与各类文件路径（向后兼容）
    [JsonPropertyName("状态")] public string? Status { get; set; }
    [JsonPropertyName("图源文件路径")] public string? SourcePath { get; set; }
    [JsonPropertyName("翻译文件路径")] public string? TranslatePath { get; set; }
    [JsonPropertyName("嵌字文件路径")] public string? TypesetPath { get; set; }

    // 新增：校对文件路径
    [JsonPropertyName("校对文件路径")] public string? ProofPath { get; set; }

    // 新增：类型/编号/范围/显示名（支持 卷、番外、单行本、区间等）
    [JsonPropertyName("类型")] public string? Kind { get; set; } // 话/卷/番外/单行本/杂项
    [JsonPropertyName("编号")] public int? Number { get; set; }
    [JsonPropertyName("范围起")] public int? RangeStart { get; set; }
    [JsonPropertyName("范围止")] public int? RangeEnd { get; set; }
    [JsonPropertyName("范围显示")] public string? RangeDisplay { get; set; }
    [JsonPropertyName("显示名")] public string? Display { get; set; }

    // 新增：全局负责人/标签/备注（可选，向后兼容）
    [JsonPropertyName("负责人")] public string? Owner { get; set; }
    [JsonPropertyName("标签")] public List<string>? Tags { get; set; }
    [JsonPropertyName("备注")] public string? Notes { get; set; }

    // 新增：阶段负责人与时间戳（图源/翻译/校对/发布）
    [JsonPropertyName("图源负责人")] public string? SourceOwner { get; set; }
    [JsonPropertyName("图源创建时间")] public DateTimeOffset? SourceCreatedAt { get; set; }
    [JsonPropertyName("图源更新时间")] public DateTimeOffset? SourceUpdatedAt { get; set; }

    [JsonPropertyName("翻译负责人")] public string? TranslateOwner { get; set; }
    [JsonPropertyName("翻译创建时间")] public DateTimeOffset? TranslateCreatedAt { get; set; }
    [JsonPropertyName("翻译更新时间")] public DateTimeOffset? TranslateUpdatedAt { get; set; }

    [JsonPropertyName("校对负责人")] public string? ProofOwner { get; set; }
    [JsonPropertyName("校对创建时间")] public DateTimeOffset? ProofCreatedAt { get; set; }
    [JsonPropertyName("校对更新时间")] public DateTimeOffset? ProofUpdatedAt { get; set; }

    [JsonPropertyName("发布负责人")] public string? PublishOwner { get; set; }
    [JsonPropertyName("发布创建时间")] public DateTimeOffset? PublishCreatedAt { get; set; }
    [JsonPropertyName("发布更新时间")] public DateTimeOffset? PublishUpdatedAt { get; set; }

    // 新增：统计与追踪（全局）
    [JsonPropertyName("图像数量")] public int? ImagesCount { get; set; }
    [JsonPropertyName("源类型")] public string? SourceType { get; set; } // dir/archive/mixed
    [JsonPropertyName("创建时间")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("更新时间")] public DateTimeOffset? UpdatedAt { get; set; }

    // 新增：文件路径列表（每话/卷/番外/杂项的所有远端文件路径）
    [JsonPropertyName("文件路径列表")] public List<string>? FilePaths { get; set; }
}
