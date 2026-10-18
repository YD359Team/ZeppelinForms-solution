namespace ZeppelinForms.Linux;

/// <summary>Auto-repeat told from typing: a held key reports <c>isRepeat</c>, as
/// WM_KEYDOWN's bit 30 does on Windows.</summary>
/// <remarks>
/// <para>
/// By default X11 sends a held key as a release and a press, again and again, and
/// the form saw every repeat as the key let go and pressed anew: isRepeat was always
/// false, and a KeyUp came between the presses. Detectable auto-repeat (XKB) drops
/// the releases of a held key — the presses alone come, and a press of a key that is
/// already down is a repeat.
/// </para>
/// <para>
/// A server without XKB can't do it, and keeps the old pairs; such servers are long
/// gone from desktops, X.Org and XWayland both support it.
/// </para>
/// </remarks>
public sealed partial class X11Platform
{
    /// <summary>The server drops the releases of a held key.</summary>
    private bool _detectableAutoRepeat;

    /// <summary>The key codes down now: a press of one of them is a repeat.</summary>
    private readonly HashSet<uint> _heldKeys = [];

    private void EnableDetectableAutoRepeat()
    {
        try
        {
            _detectableAutoRepeat = X11.XkbSetDetectableAutoRepeat(Display, true, out bool supported) && supported;
        }
        catch (EntryPointNotFoundException)
        {
            // an Xlib without XKB: repeats stay release+press pairs, as before
            _detectableAutoRepeat = false;
        }
    }

    /// <summary>A key went down: whether it is a repeat of a key held.</summary>
    private bool OnKeyPressed(uint keycode) =>
        !_heldKeys.Add(keycode) && _detectableAutoRepeat;

    private void OnKeyReleased(uint keycode) => _heldKeys.Remove(keycode);

    /// <summary>The focus went elsewhere: the releases of the keys held now go to
    /// another window, and a key pressed on return must not count as a repeat.</summary>
    private void ForgetHeldKeys() => _heldKeys.Clear();
}