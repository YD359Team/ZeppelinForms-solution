namespace ZeppelinForms.Linux;

/// <summary>Native controls among the form's elements: each in a slot of its own, a
/// child window of this one (see <see cref="X11NativeSlot"/>).</summary>
internal sealed partial class X11Window : INativeControlHostWindow
{
    public INativeControlSlot? CreateNativeSlot() =>
        _window == 0 ? null : new X11NativeSlot(_platform, this, _window);

    /// <summary>A press on the form's own picture while a native control has the
    /// keyboard: the keyboard comes back to the form. The manager gives the focus to a
    /// window it activates, and this one is active already.</summary>
    internal void TakeKeyboardBack()
    {
        if (_window == 0) return;

        X11.XGetInputFocus(_display, out nuint focused, out _);

        if (focused != _window && focused != 0 && focused != X11.PointerRoot && _platform.HasSlots)
            X11.XSetInputFocus(_display, _window, X11.RevertToParent, (nuint)X11.CurrentTime);
    }
}