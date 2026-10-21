using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>
/// The seat's pointer: the window it is over, its moves, buttons and wheel, and the cursor
/// shown over the window.
/// </summary>
/// <remarks>
/// <para>
/// The coordinates are the surface's own, logical: the window gives them to the frame it
/// draws or to the form, see <see cref="WaylandWindow.OnPointerMotion"/>. While a button
/// is held the compositor keeps sending the pointer to the window it was pressed in, even
/// outside it: Wayland's capture is implicit.
/// </para>
/// <para>
/// The wheel is gathered over a pointer frame: from version 8 the compositor gives it in
/// 120ths of a notch, as Windows does; from 5 to 7 in notches; a touchpad's continuous
/// scroll in surface units, of which 10 make a notch.
/// </para>
/// </remarks>
internal sealed class WaylandPointer : IDisposable
{
    // linux/input-event-codes.h
    private const uint BtnLeft = 0x110;
    private const uint BtnRight = 0x111;
    private const uint BtnMiddle = 0x112;

    private const uint VerticalAxis = 0;
    private const uint HorizontalAxis = 1;

    /// <summary>Surface units of continuous scroll in a notch of 120.</summary>
    private const double UnitsPerNotch = 10;

    private readonly WaylandPlatform _platform;
    private readonly WlPointer _pointer;
    private readonly WaylandSeat _seat;

    /// <summary>The surface the cursor is drawn on; made with the first cursor shown — the
    /// seat may come before the compositor's globals are all bound.</summary>
    private WlSurface? _cursorSurface;

    /// <summary>cursor-shape-v1: the compositor draws the cursor of a kind itself, from the
    /// desktop's own theme at the screen's scale. Made with the first cursor shown; null
    /// where the compositor lacks it, and the Xcursor theme is drawn instead.</summary>
    private WpCursorShapeDeviceV1? _shapeDevice;

    private WaylandWindow? _focus;
    private uint _enterSerial;
    private Point _position;

    // the wheel of the current frame
    private int _value120Vertical;
    private int _value120Horizontal;
    private double _continuousVertical;
    private double _continuousHorizontal;
    private bool _hasDiscrete;

    // what continuous scroll left over, less than one step of the form's
    private double _remainderVertical;
    private double _remainderHorizontal;

    public WaylandPointer(WaylandPlatform platform, WlPointer pointer, WaylandSeat seat)
    {
        _platform = platform;
        _pointer = pointer;
        _seat = seat;

        pointer.Enter += OnEnter;
        pointer.Leave += OnLeave;
        pointer.Motion += OnMotion;
        pointer.Button += OnButton;
        pointer.Axis += OnAxis;
        pointer.AxisDiscrete += OnAxisDiscrete;
        pointer.AxisValue120 += OnAxisValue120;
        pointer.Frame += OnFrame;
    }

    /// <summary>The pointer's events come in frames from version 5; before, each event is
    /// a frame of its own.</summary>
    private bool HasFrames => _pointer.Version >= 5;

    private void OnEnter(uint serial, WlSurface? surface, double x, double y)
    {
        _focus = _platform.FindWindow(surface);
        _enterSerial = serial;
        _position = new Point((float)x, (float)y);

        // the cursor is set anew on every enter: over another client's surface the
        // pointer showed that client's cursor
        if (_focus is not null)
        {
            ApplyCursor(_focus);
            _focus.OnPointerMotion(_position, _seat.Modifiers);
        }
    }

    private void OnLeave(uint serial, WlSurface? surface)
    {
        _focus?.OnPointerLeave();
        _focus = null;
    }

    private void OnMotion(uint time, double x, double y)
    {
        _position = new Point((float)x, (float)y);
        _focus?.OnPointerMotion(_position, _seat.Modifiers);
    }

    private void OnButton(uint serial, uint time, uint button, uint state)
    {
        if (_focus is not { IsAlive: true } window) return;

        MouseButton? which = button switch
        {
            BtnLeft => MouseButton.Left,
            BtnRight => MouseButton.Right,
            BtnMiddle => MouseButton.Middle,
            _ => null,
        };

        if (which is not { } mouseButton) return;

        // a menu's Copy takes the clipboard with the click's serial
        if (state == (uint)WlPointer.ButtonState.Pressed) _seat.NoteInput(serial);

        // the serial goes along: a press on the frame moves or resizes the window by it
        window.OnPointerButton(mouseButton, state == (uint)WlPointer.ButtonState.Pressed, serial, _seat.Modifiers);
    }

    private void OnAxis(uint time, uint axis, double value)
    {
        if (axis == VerticalAxis) _continuousVertical += value;
        else if (axis == HorizontalAxis) _continuousHorizontal += value;

        if (!HasFrames) OnFrame();
    }

    private void OnAxisDiscrete(uint axis, int discrete)
    {
        // version 5 to 7: notches; 8 sends value120 instead
        if (_pointer.Version >= 8) return;

        _hasDiscrete = true;

        if (axis == VerticalAxis) _value120Vertical += discrete * 120;
        else if (axis == HorizontalAxis) _value120Horizontal += discrete * 120;
    }

    private void OnAxisValue120(uint axis, int value120)
    {
        _hasDiscrete = true;

        if (axis == VerticalAxis) _value120Vertical += value120;
        else if (axis == HorizontalAxis) _value120Horizontal += value120;
    }

    /// <summary>The end of a frame: the wheel gathered over it goes to the form at once.</summary>
    private void OnFrame()
    {
        int vertical;
        int horizontal;

        if (_hasDiscrete)
        {
            // a mouse wheel: its notches, not the distance the compositor made of them
            vertical = _value120Vertical;
            horizontal = _value120Horizontal;
        }
        else
        {
            // a touchpad: the distance, with the part under one step kept for the next frame
            _remainderVertical += _continuousVertical * 120 / UnitsPerNotch;
            _remainderHorizontal += _continuousHorizontal * 120 / UnitsPerNotch;

            vertical = (int)_remainderVertical;
            horizontal = (int)_remainderHorizontal;

            _remainderVertical -= vertical;
            _remainderHorizontal -= horizontal;
        }

        _value120Vertical = 0;
        _value120Horizontal = 0;
        _continuousVertical = 0;
        _continuousHorizontal = 0;
        _hasDiscrete = false;

        if ((vertical != 0 || horizontal != 0) && _focus is { IsAlive: true } window)
        {
            // Wayland's positive is down and right; the form's wheel is Windows': up
            // is positive, and right is positive for the horizontal one, as on X11
            window.OnPointerWheel(-vertical, horizontal);
        }
    }

    /// <summary>The window's cursor changed: shown now if the pointer is over it.</summary>
    public void CursorChanged(WaylandWindow window)
    {
        if (ReferenceEquals(_focus, window)) ApplyCursor(window);
    }

    private void ApplyCursor(WaylandWindow window)
    {
        // a kind the compositor knows is its to draw: the desktop's theme, its size, its scale
        if (window.EffectiveCursorPicture is null && _platform.CursorShapeManager is { } shapes)
        {
            _shapeDevice ??= shapes.GetPointer(_pointer);
            _shapeDevice.SetShape(_enterSerial, (uint)ShapeOf(window.EffectiveCursorKind));

            return;
        }

        WaylandCursorImage? image = window.EffectiveCursorPicture is { } picture
            ? _platform.Cursors.Get(picture)
            : _platform.Cursors.Get(window.EffectiveCursorKind, window.BufferScale);

        // no theme at all: the compositor's own cursor, whatever it shows
        if (image is not { } cursor) return;

        WlSurface surface = _cursorSurface ??= _platform.Compositor.CreateSurface();

        // the surface's buffer scale makes a theme of scale 2 sharp on a HiDPI screen;
        // the hotspot is in the surface's logical units
        if (surface.Version >= 3) surface.SetBufferScale(cursor.Scale);

        surface.Attach(cursor.Buffer, 0, 0);
        surface.Damage(0, 0, int.MaxValue, int.MaxValue);
        surface.Commit();

        _pointer.SetCursor(_enterSerial, surface, cursor.HotspotX / cursor.Scale, cursor.HotspotY / cursor.Scale);
    }

    /// <summary>The shape of cursor-shape-v1 for a kind: the CSS cursors' names.</summary>
    private static WpCursorShapeDeviceV1.Shape ShapeOf(CursorKind kind) => kind switch
    {
        CursorKind.Hand => WpCursorShapeDeviceV1.Shape.Pointer,
        CursorKind.IBeam => WpCursorShapeDeviceV1.Shape.Text,
        CursorKind.Wait => WpCursorShapeDeviceV1.Shape.Wait,
        CursorKind.SizeWestEast => WpCursorShapeDeviceV1.Shape.EwResize,
        CursorKind.SizeNorthSouth => WpCursorShapeDeviceV1.Shape.NsResize,
        CursorKind.SizeNorthWestSouthEast => WpCursorShapeDeviceV1.Shape.NwseResize,
        CursorKind.SizeNorthEastSouthWest => WpCursorShapeDeviceV1.Shape.NeswResize,
        CursorKind.SizeAll => WpCursorShapeDeviceV1.Shape.Move,
        CursorKind.Cross => WpCursorShapeDeviceV1.Shape.Crosshair,
        CursorKind.No => WpCursorShapeDeviceV1.Shape.NotAllowed,
        _ => WpCursorShapeDeviceV1.Shape.Default,
    };

    /// <summary>The window closed: a pointer over it holds it no longer.</summary>
    public void Forget(WaylandWindow window)
    {
        if (ReferenceEquals(_focus, window)) _focus = null;
    }

    public void Dispose()
    {
        _shapeDevice?.Destroy();
        _shapeDevice = null;

        _cursorSurface?.Destroy();
        _cursorSurface = null;

        if (!_pointer.IsAlive) return;

        // wl_pointer.release is version 3; an older one is only forgotten
        if (_pointer.Version >= 3) _pointer.Release();
        else _pointer.Abandon();
    }
}