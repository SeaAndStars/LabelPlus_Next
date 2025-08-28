namespace LabelPlus_Next.Models;

public class EpisodeEntry
{
    public bool Include { get; set; } = true;
    public int Number { get; set; }
    public string Status { get; set; } = "立项";
    public List<string> LocalFiles { get; set; } = new();
}
