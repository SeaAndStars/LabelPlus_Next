using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;
using LabelPlus_Next.Views.Windows;
using System;
using Ursa.Controls;

namespace LabelPlus_Next.Views.Pages;

public partial class UploadPage : UserControl
{
    private UploadViewModel? _vm;

    public UploadPage()
    {
        InitializeComponent();
        if (Design.IsDesignMode)
        {
            DataContext = new UploadViewModel();
        }
        this.DataContextChanged += OnDataContextChanged;
        TryHookVm(DataContext as UploadViewModel);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        TryHookVm(DataContext as UploadViewModel);
    }

    private void TryHookVm(UploadViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm)) return;
        if (_vm is not null)
        {
            _vm.MetadataReady -= VmOnMetadataReadyAsync;
            _vm.OpenSettingsRequested -= VmOnOpenSettingsRequestedAsync;
        }
        _vm = vm;
        if (_vm is not null)
        {
            // provide services
            var top = TopLevel.GetTopLevel(this);
            if (top is not null)
            {
                _vm.InitializeServices(new AvaloniaFileDialogService(top));
            }
            // wire events
            _vm.MetadataReady += VmOnMetadataReadyAsync;
            _vm.OpenSettingsRequested += VmOnOpenSettingsRequestedAsync;
        }
    }

    private async void VmOnOpenSettingsRequestedAsync(object? sender, EventArgs e)
    {
        if (VisualRoot is not Window win) return;
        var dlg = new UploadSettingsWindow();
        await dlg.ShowDialog(win);
        if (_vm is not null)
        {
            await _vm.RefreshCommand.ExecuteAsync(null);
        }
    }

    private async void VmOnMetadataReadyAsync(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        if (_vm.HasDuplicates)
        {
            var res = await MessageBox.ShowAsync("检测到与远端存在相同话数，是否继续?", "提示", MessageBoxIcon.Warning, MessageBoxButton.YesNo);
            if (res != MessageBoxResult.Yes) return;
        }
        var dlg = new ProjectMetaDataWindow { DataContext = _vm };
        if (VisualRoot is Window owner)
            await dlg.ShowDialog(owner);
        else
            dlg.Show();
    }
}