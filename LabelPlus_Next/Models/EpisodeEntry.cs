namespace LabelPlus_Next.Models;

public class EpisodeEntry
{
    public bool Include { get; set; } = true;
    public int Number { get; set; }
    // 标记是否为“番外”章节（非数字话数）
    public bool IsSpecial { get; set; }
    // 标记是否为卷（Vol/Volume/卷）
    public bool IsVolume { get; set; }
    // 新增：标记是否为“杂项”
    public bool IsMisc { get; set; }
    public string Status { get; set; } = "立项";
    public List<string> LocalFiles { get; set; } = new();

    // 新增：可编辑元数据（用于生成 ProjectCn.EpisodeCn 对应字段）
    // 类型：话/卷/番外/单行本/杂项
    public string? Kind { get; set; }
    // 显示名（UI展示/用户编辑，如 卷03、番外02、07、杂项）
    public string? Display { get; set; }
    // 区间（如 15-51），及显示
    public int? RangeStart { get; set; }
    public int? RangeEnd { get; set; }
    public string? RangeDisplay { get; set; }

    // 其他可编辑元数据
    public string? Owner { get; set; }
    public List<string>? Tags { get; set; }
    public string? Notes { get; set; }
}
