using System.Collections.Generic;

namespace LabelPlus_Next.ViewModels;

public class ImageManagerViewModel : ViewModelBase
{
    public List<string> SelectedFolerFile { get; set; }
    public List<string> FileFolderList { get; set; }
    public List<string> FileList { get; set; }
    public List<string> SelectedFile { get; set; }
}