using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using LabelPlus_Next.Models;

namespace LabelPlus_Next.ViewModels;

public sealed class MetaNode : ObservableObject
{
    public bool IsFile { get; init; }
    public string? Name { get; set; }
    public int Number { get; set; }
    public bool IsVolume { get; set; }
    public bool IsSpecial { get; set; }

    private bool _include;
    public bool Include
    {
        get => _include;
        set => SetProperty(ref _include, value);
    }

    public string Status { get; set; } = "立项";
    public int LocalFileCount { get; init; }
    public string EpisodeDisplay { get; init; } = string.Empty;
    public string LocalFileCountDisplay { get; init; } = string.Empty;
    public List<MetaNode> Children { get; } = new();
    public EpisodeEntry? Ref { get; init; }
    public string? Display { get; set; }
    public int? RangeStart { get; set; }
    public int? RangeEnd { get; set; }
    public string? RangeDisplay { get; set; }
    public string? Owner { get; set; }
    public string? Tags { get; set; }
    public string? Notes { get; set; }

    private int _depth;
    public int Depth
    {
        get => _depth;
        set
        {
            if (SetProperty(ref _depth, value)) OnPropertyChanged(nameof(IndentMargin));
        }
    }

    public Thickness IndentMargin => new(Depth * 18, 0, 0, 0);

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value)) OnPropertyChanged(nameof(ChevronGlyph));
        }
    }

    public bool HasChildren => Children.Count > 0;
    public string ChevronGlyph => IsExpanded ? "⌄" : "›";
    public System.Windows.Input.ICommand? ToggleCommand { get; set; }
}
