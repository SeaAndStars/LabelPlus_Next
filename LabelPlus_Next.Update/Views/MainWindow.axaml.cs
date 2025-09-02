using LabelPlus_Next.Update.ViewModels;
using System;
using Ursa.Controls;

namespace LabelPlus_Next.Update.Views;

public partial class MainWindow : UrsaWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel vm)
        {
            _ = vm.RunUpdateAsyncPublic();
        }
    }
}
