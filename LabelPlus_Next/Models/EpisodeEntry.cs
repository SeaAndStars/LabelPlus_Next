namespace LabelPlus_Next.Models;

public class EpisodeEntry
{
    public bool Include { get; set; } = true;
    public int Number { get; set; }
    // 标记是否为“番外”章节（非数字话数）
    public bool IsSpecial { get; set; }
    // 标记是否为卷（Vol/Volume/卷）
    public bool IsVolume { get; set; }
    public string Status { get; set; } = "立项";
    public List<string> LocalFiles { get; set; } = new();
}
