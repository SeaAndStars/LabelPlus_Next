using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Controls.Notifications;
using LabelPlus_Next.ViewModels;
using Ursa.Controls;
using UWindowNotificationManager = Ursa.Controls.WindowNotificationManager;
using LabelPlus_Next.Views.Windows;

namespace LabelPlus_Next.Views.Pages;

public partial class TeamWorkPage : UserControl
{
    public TeamWorkPage()
    {
        InitializeComponent();
    this.AttachedToVisualTree += (_, __) =>
        {
            if (DataContext is TeamWorkViewModel vm)
            {
        if (TopLevel.GetTopLevel(this) is Window win)
        {
            vm.NotificationManager = UWindowNotificationManager.TryGetNotificationManager(win, out var existing)
            ? existing
            : new UWindowNotificationManager(win) { Position = NotificationPosition.TopRight };
        }
                vm.OpenSettingsRequested -= VmOnOpenSettingsRequested;
                vm.OpenSettingsRequested += VmOnOpenSettingsRequested;
            }
        };
    }
    private async void VmOnOpenSettingsRequested(object? sender, EventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window win)
        {
            var dlg = new UploadSettingsWindow { DataContext = new UploadSettingsViewModel() };
            await dlg.ShowDialog(win);
        }
    }
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
