using System;
using Irihi.Avalonia.Shared.Contracts;
using LabelPlus_Next.Models;

namespace LabelPlus_Next.Views.Windows;

/// <summary>ViewModel for the custom content hosted by Ursa Dialog.</summary>
public sealed class ConfirmDialogViewModel : IDialogContext
{
    public ConfirmDialogViewModel(ConfirmDialogOptions options)
    {
        Title = options.Title;
        Message = options.Message;
        YesText = options.YesText;
        NoText = options.NoText;
        CancelText = options.CancelText;
        ShowNo = options.ShowNo;
        ShowCancel = options.ShowCancel;
        Severity = options.Severity;
        BodyIcon = options.BodyIcon == ConfirmBodyIcon.Auto
            ? options.Severity switch
            {
                ConfirmDialogSeverity.Danger => ConfirmBodyIcon.Delete,
                ConfirmDialogSeverity.Info => ConfirmBodyIcon.Info,
                _ => ConfirmBodyIcon.Warning
            }
            : options.BodyIcon;
        YesIsDanger = options.YesIsDanger;
        NoIsDanger = options.NoIsDanger;
    }

    public string Title { get; }
    public string Message { get; }
    public string YesText { get; }
    public string NoText { get; }
    public string CancelText { get; }
    public bool ShowNo { get; }
    public bool ShowCancel { get; }
    public ConfirmDialogSeverity Severity { get; }
    public ConfirmBodyIcon BodyIcon { get; }
    public bool ShowDeleteIcon => BodyIcon == ConfirmBodyIcon.Delete;
    public bool ShowSaveIcon => BodyIcon == ConfirmBodyIcon.Save;
    public bool ShowUploadIcon => BodyIcon == ConfirmBodyIcon.Upload;
    public bool ShowWarningIcon => BodyIcon == ConfirmBodyIcon.Warning;
    public bool ShowInfoIcon => BodyIcon == ConfirmBodyIcon.Info;
    public bool IsDanger => Severity == ConfirmDialogSeverity.Danger;
    public bool IsWarning => Severity == ConfirmDialogSeverity.Warning;
    public bool IsInfo => Severity == ConfirmDialogSeverity.Info;
    public bool YesIsDanger { get; }
    public bool NoIsDanger { get; }
    public bool YesIsPrimary => !YesIsDanger;



    public event EventHandler<object?>? RequestClose;

    public void Confirm() => Close(ConfirmDialogResult.Yes);
    public void Refuse() => Close(ConfirmDialogResult.No);
    public void Cancel() => Close(ConfirmDialogResult.Cancel);

    public void Close() => Close(ConfirmDialogResult.Cancel);

    public void Close(object? result) => RequestClose?.Invoke(this, result);
}
