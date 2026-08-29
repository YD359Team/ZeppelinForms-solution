namespace ZeppelinForms.Forms.Controls;

public sealed class MenuItem
{
    public string Text { get; set; } = string.Empty;
    public string? PathData { get; set; }
    public bool IsSeparator { get; set; }
    public bool IsEnabled { get; set; } = true;
    public List<MenuItem> Items { get; } = [];

    public event EventHandler? Click;

    internal void RaiseClick() => Click?.Invoke(this, EventArgs.Empty);

    public static MenuItem Separator => new() { IsSeparator = true };
}
