namespace LabelPlus_Next.Models;

public enum ConfirmDialogSeverity
{
    Info,
    Warning,
    Danger
}

public enum ConfirmBodyIcon
{
    Auto,
    Delete,
    Save,
    Upload,
    Warning,
    Info
}

public enum ConfirmDialogResult
{
    Cancel = 0,
    Yes = 1,
    No = 2
}

/// <summary>Display options for the app confirm dialog; button danger is action-specific.</summary>
public sealed class ConfirmDialogOptions
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public ConfirmDialogSeverity Severity { get; set; } = ConfirmDialogSeverity.Warning;
    public ConfirmBodyIcon BodyIcon { get; set; } = ConfirmBodyIcon.Auto;

    public string YesText { get; set; } = "是";
    public string NoText { get; set; } = "否";
    public string CancelText { get; set; } = "取消";

    public bool ShowNo { get; set; } = true;
    public bool ShowCancel { get; set; }

    /// <summary>True for destructive actions (delete / discard): the Yes button turns red.</summary>
    public bool YesIsDanger { get; set; }

    /// <summary>True when the No choice itself is destructive (e.g. discard unsaved changes).</summary>
    public bool NoIsDanger { get; set; }
}
