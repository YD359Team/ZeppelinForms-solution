namespace ZeppelinForms.Android;

public sealed class BackRequestedEventArgs : EventArgs
{
    /// <summary>The application handled the back request itself — the activity
    /// doesn't need to be closed.</summary>
    public bool Handled { get; set; }
}