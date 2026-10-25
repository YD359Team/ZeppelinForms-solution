namespace ZeppelinForms.Windows;

/// <summary>Native controls among the form's elements: each in a slot of its own, a
/// child window of this one (see <see cref="Win32NativeSlot"/>).</summary>
internal sealed partial class Win32Window : INativeControlHostWindow
{
    public INativeControlSlot? CreateNativeSlot()
    {
        if (_handle == 0) return null;

        // the form is drawn round its children from now on: without it a frame painted
        // over the controls, and they showed only where they repainted themselves
        nint style = NativeMethods.GetWindowLongPtr(_handle, NativeConstants.GWL_STYLE);

        if ((style & (nint)NativeConstants.WS_CLIPCHILDREN) == 0)
            NativeMethods.SetWindowLongPtr(_handle, NativeConstants.GWL_STYLE, style | (nint)NativeConstants.WS_CLIPCHILDREN);

        return new Win32NativeSlot(this, _handle);
    }

    /// <summary>A press on the form's own picture while a native control has the
    /// keyboard: the keyboard comes back to the form, which a press in a window with a
    /// focused child doesn't do by itself — the typing went on into the control.</summary>
    private void TakeKeyboardBack()
    {
        nint focused = NativeMethods.GetFocus();

        if (focused != _handle && focused != 0 && NativeMethods.IsChild(_handle, focused))
            NativeMethods.SetFocus(_handle);
    }
}