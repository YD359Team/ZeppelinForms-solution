using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace ZeppelinForms.VisualStudio;

/// <summary>View &gt; Other Windows &gt; ZeppelinForms Preview.</summary>
[Guid("af4eb00e-0d34-49a9-b970-8753b6f65957")]
public sealed class PreviewToolWindow : ToolWindowPane
{
    public PreviewToolWindow() : base(null)
    {
        Caption = "ZeppelinForms Preview";
        Content = new PreviewControl();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Content is PreviewControl control)
            control.Detach();

        base.Dispose(disposing);
    }
}