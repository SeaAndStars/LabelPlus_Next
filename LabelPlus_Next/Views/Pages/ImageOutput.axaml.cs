using Ursa.Controls;

namespace LabelPlus_Next.Views.Pages;

public partial class ImageOutput : UrsaWindow
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
