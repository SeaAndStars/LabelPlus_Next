using System.Collections.Generic;

namespace LabelPlus_Next.Models;

public class EpisodeEntry
{
    public bool Include { get; set; } = true;
    public int Number { get; set; }
    public string Status { get; set; } = "БўПо";
    public List<string> LocalFiles { get; set; } = new();
}
