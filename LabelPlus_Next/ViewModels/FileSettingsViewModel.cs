namespace LabelPlus_Next.ViewModels;

public class FileSettingsViewModel : ViewModelBase
{
    public List<string> GroupList { get; set; } = new();
    public string Notes { get; set; } = string.Empty;
}
