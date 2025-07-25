using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace LabelPlus_Next.Views.Pages;

public partial class ImageOutput : Window
{
    public ImageOutput()
    {
        InitializeComponent();
    }



    private void ProcessChange(int process)
    {
        ProgressBar.Value = process;
    }
}