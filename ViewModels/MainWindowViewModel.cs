using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LabelPlus_Next.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public List<string> LangList { get; } = new List<string> { "default", "en", "zh-hant-tw" };

    [ObservableProperty]
    private string? currentLang;

    public MainWindowViewModel()
    {
        currentLang = LangList[0]; // 初始化 currentLang 避免空引用
    }
}