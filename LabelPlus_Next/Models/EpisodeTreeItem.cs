using System.Collections.ObjectModel;

namespace LabelPlus_Next.Models;

public class EpisodeTreeItem
{
    public bool IsFile { get; set; }
    public bool IsEpisode => !IsFile;
    public string? Name { get; set; }

    // Episode-only fields
    public bool Include { get; set; }
    public int Number { get; set; }
    public string Status { get; set; } = "БўПо";
    public int LocalFileCount { get; set; }

    public ObservableCollection<EpisodeTreeItem> Children { get; } = new();
}
