using System.ComponentModel;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms;

/// <summary>The arguments of <see cref="Form.Closing"/>: set <see cref="CancelEventArgs.Cancel"/>
/// to keep the form open — unsaved changes, a running operation.</summary>
public sealed class FormClosingEventArgs(CloseReason reason) : CancelEventArgs
{
    /// <summary>Who asked the form to close.</summary>
    public CloseReason Reason { get; } = reason;
}