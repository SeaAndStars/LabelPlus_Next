using Avalonia.Markup.Xaml;
using Ursa.Controls;
using LabelPlus_Next.ViewModels;

namespace LabelPlus_Next.Views.Windows;

public partial class ProjectMetaDataWindow : UrsaWindow
{
    private UploadViewModel? _vm;

    public ProjectMetaDataWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.MetaWindowCloseRequested -= OnMetaWindowCloseRequested;
        }
        _vm = DataContext as UploadViewModel;
        if (_vm is not null)
        {
            _vm.MetaWindowCloseRequested += OnMetaWindowCloseRequested;
        }
    }

    private void OnMetaWindowCloseRequested(object? sender, bool e)
    {
        Close(e);
    }
}
