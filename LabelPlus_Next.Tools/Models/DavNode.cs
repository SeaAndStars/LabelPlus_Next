using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace LabelPlus_Next.Tools.Models;

public class DavNode : ObservableObject
{
    private bool isExpanded;

    public string? Name { get; set; }
    public string? Uri { get; set; }
    public bool IsCollection { get; set; }
    public ObservableCollection<DavNode> Children { get; } = new();

    /// <summary>
    /// Bound to <see cref="Avalonia.Controls.TreeViewItem.IsExpanded"/> so the tool can
    /// expand the root automatically and let users see their files right after connecting.
    /// </summary>
    public bool IsExpanded
    {
        get => isExpanded;
        set => SetProperty(ref isExpanded, value);
    }
}
