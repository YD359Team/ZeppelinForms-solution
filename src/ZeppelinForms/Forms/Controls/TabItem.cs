using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

public readonly partial record struct GridLength
{
    public sealed class TabItem
    {
        public string Header { get; set; } = string.Empty;
        public string? PathData { get; set; }
        public UIElement? Content { get; set; }
        public bool IsEnabled { get; set; } = true;
    }
}