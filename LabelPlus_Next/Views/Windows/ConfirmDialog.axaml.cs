using Avalonia.Controls;
using LabelPlus_Next.Models;
using Ursa.Controls;

namespace LabelPlus_Next.Views.Windows;

/// <summary>
/// Opens the app confirm dialog through Ursa's window-based Dialog host.
/// The custom view only supplies message/buttons; title bar, drag, close and
/// the severity icon come from Ursa.
/// </summary>
public static class ConfirmDialog
{
    public static async Task<ConfirmDialogResult> ShowAsync(Window? owner, ConfirmDialogOptions options)
    {
        owner ??= MainWindow.Instance;
        if (owner is null) return ConfirmDialogResult.Cancel;

        var mode = options.Severity switch
        {
            ConfirmDialogSeverity.Danger => DialogMode.Error,
            ConfirmDialogSeverity.Info => DialogMode.Info,
            _ => DialogMode.Warning
        };
        var vm = new ConfirmDialogViewModel(options);

        var result = await Dialog.ShowCustomAsync<ConfirmDialogView, ConfirmDialogViewModel, ConfirmDialogResult>(
            vm,
            owner,
            new DialogOptions
            {
                Title = options.Title,
                Mode = mode,
                Button = DialogButton.None,
                CanDragMove = true,
                CanResize = false,
                IsCloseButtonVisible = true,
                ShowInTaskBar = false,
                StartupLocation = WindowStartupLocation.CenterOwner
            });

        return result;
    }
}
