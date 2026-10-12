using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Linux;

/// <summary>
/// Input methods on X11 (XIM), over the spot: ibus, fcitx and the others draw the
/// composition and the candidate list themselves, at the spot the window names — the
/// focused field's caret. Before 0.15 the context asked for no composition at all
/// (XIMPreeditNothing), and the input method's window opened wherever it chose.
/// </summary>
/// <remarks>
/// The composition is not drawn in the field as on Windows: that needs the XIM preedit
/// callbacks, which few input methods still support. The committed text arrives through
/// Xutf8LookupString as before.
/// </remarks>
internal sealed partial class X11Window : ITextInputMethodWindow
{
    public void SetTextInputArea(Rectangle? caret)
    {
        if (caret is not { } area || _inputContext == 0) return;

        // the spot is where the composition's baseline starts: the caret's foot,
        // in the window's physical pixels
        short x = (short)Math.Clamp(area.X * _scale, short.MinValue, short.MaxValue);
        short y = (short)Math.Clamp((area.Y + area.Height * 0.8f) * _scale, short.MinValue, short.MaxValue);

        nint spot = Marshal.AllocHGlobal(4);

        try
        {
            Marshal.WriteInt16(spot, 0, x);
            Marshal.WriteInt16(spot, 2, y);

            nint attributes = X11.XVaCreateNestedList(0, "spotLocation", spot, 0);
            if (attributes == 0) return;

            // a context made without a spot — the input method couldn't draw over
            // it — refuses the value, which is all right
            X11.XSetICValues(_inputContext, "preeditAttributes", attributes, 0);
            X11.XFree(attributes);
        }
        finally
        {
            Marshal.FreeHGlobal(spot);
        }
    }
}
