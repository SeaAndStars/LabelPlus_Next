using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.Services;
using LabelPlus_Next.ViewModels;
using LabelPlus_Next.Views.Windows;
using NLog;
using Ursa.Controls;

namespace LabelPlus_Next.Views.Pages;

public partial class UploadPage : UserControl
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private bool _servicesInitialized;

    private UploadViewModel? _vm;

    public UploadPage()
    {
        try
        {
            InitializeComponent();
            if (Design.IsDesignMode)
            {
                DataContext = new UploadViewModel();
            }
            DataContextChanged += OnDataContextChanged;
            TryHookVm(DataContext as UploadViewModel);
            Logger.Info("UploadPage constructed.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "UploadPage constructor failed.");
            throw;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Logger.Debug("UploadPage attached to visual tree.");
        EnsureServices();
    }

    private void EnsureServices()
    {
        if (_servicesInitialized) return;
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null)
            {
                Logger.Warn("EnsureServices: TopLevel is null, will retry later.");
                return;
            }
            if (_vm is not null)
            {
                _vm.InitializeServices(new AvaloniaFileDialogService(top));
                _servicesInitialized = true;
                Logger.Info("UploadPage services initialized.");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "EnsureServices failed.");
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Logger.Debug("DataContext changed: {type}", DataContext?.GetType().FullName);
        TryHookVm(DataContext as UploadViewModel);
        EnsureServices();
    }

    private void TryHookVm(UploadViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm)) return;
        try
        {
            if (_vm is not null)
            {
                _vm.MetadataReady -= VmOnMetadataReadyAsync;
                _vm.OpenSettingsRequested -= VmOnOpenSettingsRequestedAsync;
                _vm.MultiMetadataReady -= VmOnMultiMetadataReady;
                Logger.Debug("Unhooked previous UploadViewModel events.");
            }
            _vm = vm;
            if (_vm is not null)
            {
                _vm.MetadataReady += VmOnMetadataReadyAsync;
                _vm.OpenSettingsRequested += VmOnOpenSettingsRequestedAsync;
                _vm.MultiMetadataReady += VmOnMultiMetadataReady;
                Logger.Debug("Hooked UploadViewModel events.");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "TryHookVm failed.");
        }
    }

    private async void VmOnOpenSettingsRequestedAsync(object? sender, EventArgs e)
    {
        try
        {
            if (VisualRoot is not Window win)
            {
                Logger.Warn("OpenSettingsRequested: VisualRoot window is null.");
                return;
            }
            var dlg = new UploadSettingsWindow();
            await dlg.ShowDialog(win);
            if (_vm is not null)
            {
                await _vm.RefreshCommand.ExecuteAsync(null);
                Logger.Info("Settings dialog closed, refresh triggered.");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error while opening settings dialog.");
        }
    }

    private async void VmOnMetadataReadyAsync(object? sender, EventArgs e)
    {
        try
        {
            if (_vm is null)
            {
                Logger.Warn("MetadataReady: ViewModel is null.");
                return;
            }
            if (_vm.HasDuplicates)
            {
                Logger.Info("Duplicate episodes detected, asking for confirmation.");
                var res = await MessageBox.ShowAsync("��⵽��Զ�˴�����ͬ�������Ƿ����?", "��ʾ", MessageBoxIcon.Warning, MessageBoxButton.YesNo);
                if (res != MessageBoxResult.Yes)
                {
                    Logger.Info("User cancelled due to duplicates.");
                    return;
                }
            }
            var dlg = new ProjectMetaDataWindow { DataContext = _vm };
            if (VisualRoot is Window owner)
            {
                Logger.Debug("Opening ProjectMetaDataWindow as dialog.");
                await dlg.ShowDialog(owner);
            }
            else
            {
                Logger.Debug("Opening ProjectMetaDataWindow as window.");
                dlg.Show();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error while handling MetadataReady.");
        }
    }

    private void VmOnMultiMetadataReady(object? sender, IReadOnlyList<UploadViewModel> vms)
    {
        try
        {
            var owner = VisualRoot as Window;
            foreach (var vm in vms)
            {
                var win = new ProjectMetaDataWindow { DataContext = vm };
                if (owner is not null) win.Show(owner);
                else win.Show();
            }
            Logger.Info("Opened {count} metadata windows for multi-folder selection.", vms.Count);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error while opening multiple metadata windows.");
        }
    }
}
