using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using ReactiveUI;

namespace LabelPlus_Next.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{


    public List<string> LangList { get; }
    public string CurrentLang { get; }


    // ... 其他命令定义 ...

    public MainWindowViewModel()
    {
       
    }


}