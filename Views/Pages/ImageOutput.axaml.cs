using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.ViewModels;

namespace LabelPlus_Next.Views.Pages;

public partial class ImageOutput : Window
{
    public ImageOutput()
    {
        InitializeComponent();
        ProcessChange(0);
    }
    
    private void ProcessChange(int process)
    {
       
        ProgressBar.Value=process;
    }
    
}