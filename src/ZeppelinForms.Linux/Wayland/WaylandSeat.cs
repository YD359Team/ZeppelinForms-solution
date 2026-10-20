using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>
/// A seat — the user's keyboard and pointer as one —, and its devices as they come and go:
/// a keyboard plugged in, a tablet's pointer, a virtual keyboard of a remote desktop.
/// </summary>
/// <remarks>
/// The first seat the compositor announces is taken: desktops have one, and a second is a
/// multi-user setup with windows of its own. Touch comes later.
/// </remarks>
internal sealed class WaylandSeat : IDisposable
{
    private readonly WaylandPlatform _platform;
    private readonly WlSeat _seat;

    public WaylandSeat(WaylandPlatform platform, WlSeat seat)
    {
        _platform = platform;
        _seat = seat;

        seat.Capabilities += OnCapabilities;
    }

    public WlSeat Proxy => _seat;

    public WaylandPointer? Pointer { get; private set; }

    public WaylandKeyboard? Keyboard { get; private set; }

    /// <summary>Shift, Control and Alt as the keyboard holds them; none without a keyboard.</summary>
    public KeyModifiers Modifiers => Keyboard?.Modifiers ?? KeyModifiers.None;

    private void OnCapabilities(uint capabilities)
    {
        bool hasPointer = (capabilities & (uint)WlSeat.Capability.Pointer) != 0;
        bool hasKeyboard = (capabilities & (uint)WlSeat.Capability.Keyboard) != 0;

        if (hasPointer && Pointer is null)
        {
            Pointer = new WaylandPointer(_platform, _seat.GetPointer(), this);
        }
        else if (!hasPointer && Pointer is not null)
        {
            Pointer.Dispose();
            Pointer = null;
        }

        // no libxkbcommon: no keyboard — the text can't be told from bare key codes
        if (hasKeyboard && Keyboard is null && NativeProbe.Has(Xkb.Lib))
        {
            Keyboard = new WaylandKeyboard(_platform, _seat.GetKeyboard());
        }
        else if (!hasKeyboard && Keyboard is not null)
        {
            Keyboard.Dispose();
            Keyboard = null;
        }
    }

    /// <summary>The window closed: no device holds it any longer.</summary>
    public void Forget(WaylandWindow window)
    {
        Pointer?.Forget(window);
        Keyboard?.Forget(window);
    }

    public void Dispose()
    {
        Pointer?.Dispose();
        Keyboard?.Dispose();

        Pointer = null;
        Keyboard = null;

        if (!_seat.IsAlive) return;

        // wl_seat.release is version 5; an older one is only forgotten
        if (_seat.Version >= 5) _seat.Release();
        else _seat.Abandon();
    }
}