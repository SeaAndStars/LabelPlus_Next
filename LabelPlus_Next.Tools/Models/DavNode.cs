using System.Collections.ObjectModel;

namespace LabelPlus_Next.Tools.Models;

public class DavNode
{
    public string? Name { get; set; }
    public string? Uri { get; set; }
    public bool IsCollection { get; set; }
    public ObservableCollection<DavNode> Children { get; } = new();
}
