using System.Windows.Input;
using Avalonia;
using ReactiveUI;

namespace LabelPlus_Next.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    // public static readonly StyledProperty<string> CurrentLabelTextProperty =
        // AvaloniaProperty.Register<MainWindowViewModel, string>("CurrentLabelText");

    
    // 添加菜单命令      
    public ICommand NewCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ExitCommand { get; }

    public object Labels { get; }

    public string Languages { get; }

    public ICommand BrowseModeCommand { get; }

    public ICommand EditLabelCommand { get; }

    public ICommand InputModeCommand { get; }

    public ICommand CheckModeCommand { get; }

    public ICommand HideWindowCommand { get; }
    public string CurrentLabelText { get; }
    public ICommand ZoomPlusCommand { get; }
    public ICommand ZoomMinusCommand{ get; }
    public ICommand ZoomLevels { get; }
    public object CurrentImage { get; }
    public string SelectedLanguage { get; }
    public object SelectedZoom { get; }


    // ... 其他命令定义 ...

    public MainWindowViewModel()
    {
        NewCommand = ReactiveCommand.Create(ExecuteNew);
        OpenCommand = ReactiveCommand.Create(ExecuteOpen);
        SaveCommand = ReactiveCommand.Create(ExecuteSave);
        ExitCommand = ReactiveCommand.Create(ExecuteExit);
    }

    private void ExecuteNew() { /* 新建逻辑 */ }
    private void ExecuteOpen() { /* 打开逻辑 */ }
    private void ExecuteSave() { /* 保存逻辑 */ }
    private void ExecuteExit() { /* 退出逻辑 */ }
    // ... 其他命令实现 ...
}