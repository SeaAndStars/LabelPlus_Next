using Avalonia.Controls;

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
        ProgressBar.Value = process;
    }
}