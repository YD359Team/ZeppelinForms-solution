using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms;

/// <summary>A form as a dialog: shown over its owner and awaited, closed with
/// <see cref="Accept"/> and a value or with <see cref="Cancel"/>.</summary>
public partial class Form
{
    private bool _dialogAccepted;
    private object? _dialogValue;

    /// <summary>Show the dialog and wait for the result. Requires a platform
    /// with a nested loop; where there is none, use ShowDialogAsync.</summary>
    public DialogResult<T> ShowDialog<T>(Form owner)
    {
        IPlatform platform = owner.Platform
            ?? throw new InvalidOperationException("The dialog owner is not bound to a platform yet.");

        if (platform is not INestedLoopSupport loop)
            throw new NotSupportedException(
                $"Platform {platform.GetType().Name} does not support a nested loop. " +
                "Use ShowDialogAsync.");

        BeginDialog(owner, platform);

        try
        {
            loop.RunNestedLoop(PlatformWindow!);
        }
        finally
        {
            EndDialog(owner);
        }

        return Result<T>();
    }

    /// <summary>Show the dialog without blocking the calling code. Works
    /// on any platform, including those without a nested loop.</summary>
    public async Task<DialogResult<T>> ShowDialogAsync<T>(Form owner)
    {
        IPlatform platform = owner.Platform
            ?? throw new InvalidOperationException("The dialog owner is not bound to a platform yet.");

        BeginDialog(owner, platform);

        try
        {
            await _dialogClosed!.Task;
        }
        finally
        {
            EndDialog(owner);
        }

        return Result<T>();
    }

    private TaskCompletionSource? _dialogClosed;

    private void BeginDialog(Form owner, IPlatform platform)
    {
        IsDialog = true;
        Platform = platform;

        _dialogAccepted = false;
        _dialogValue = null;

        // completion is awaited both synchronously and asynchronously: the source
        // is one — the window closing, wherever it came from
        _dialogClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // before the window: Win32 makes the dialog owned at creation, X11 marks it
        // transient for the owner and a dialog before mapping. Without an owner the
        // dialog was a window of its own and could fall behind the one it blocked
        SetOwner(owner);

        // and sized to its content, if it asks to be: the window is created at Size
        ApplySizeToContent();

        platform.CreateWindow(this);

        // modality is a disabled owner, not a nested loop:
        // the first is needed everywhere, the second only on desktop platforms
        owner.PlatformWindow?.SetEnabled(false);

        Show();
    }

    private void EndDialog(Form owner)
    {
        if (owner.PlatformWindow is { } ownerWindow)
        {
            ownerWindow.SetEnabled(true);
            ownerWindow.Activate();
        }

        _dialogClosed = null;
    }

    internal DialogResult<T> Result<T>() =>
        _dialogAccepted && _dialogValue is T typed
            ? new DialogResult<T>(true, typed)
            : DialogResult<T>.Cancelled();

    /// <summary>Close the dialog with a result.</summary>
    public void Accept(object? value = null) => CloseWithResult(accepted: true, value);

    public void Cancel() => CloseWithResult(accepted: false, value: null);

    /// <summary>Close with a result. The result is set before <see cref="Closing"/>,
    /// so a handler sees what the dialog is closing with; if the handler keeps the
    /// dialog open, the result is taken back — the dialog is not closed, and a later
    /// close from the title bar must not return what was refused.</summary>
    private void CloseWithResult(bool accepted, object? value)
    {
        bool previousAccepted = _dialogAccepted;
        object? previousValue = _dialogValue;

        _dialogAccepted = accepted;
        _dialogValue = value;

        if (RequestClose(CloseReason.Code))
        {
            PlatformWindow?.Close();
            return;
        }

        _dialogAccepted = previousAccepted;
        _dialogValue = previousValue;
    }
}
