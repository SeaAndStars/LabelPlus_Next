using System.Collections.Generic;

namespace LabelPlus_Next.ViewModels;

public class FileSettingsViewModel : ViewModelBase
{
    public List<string> GroupList { get; set; }
    public string Notes { get; set; }
}