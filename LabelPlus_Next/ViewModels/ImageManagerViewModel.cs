using System.Collections.ObjectModel;

namespace LabelPlus_Next.ViewModels;

public class ImageManagerViewModel : ViewModelBase
{
    public string? FolderPath { get; set; }
    public string? CreatedFilePath { get; set; }

    public ObservableCollection<string> FileFolderList { get; set; } = new(); // Ignored list (left)
    public ObservableCollection<string> FileList { get; set; } = new(); // Included list (right)

    public string? SelectedFolerFile { get; set; }
    public string? SelectedFile { get; set; }
}
