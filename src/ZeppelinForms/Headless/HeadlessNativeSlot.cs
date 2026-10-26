using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Headless;

/// <summary>A headless window's place for a native control: it holds nothing and
/// remembers what it was asked — checked in tests.</summary>
public sealed class HeadlessNativeSlot : INativeControlSlot
{
    private static int s_next;

    internal HeadlessNativeSlot() => Parent = new PlatformHandle(Interlocked.Increment(ref s_next), "Headless");

    public PlatformHandle Parent { get; }

    /// <summary>The control the host made, once attached.</summary>
    public PlatformHandle? Control { get; private set; }

    /// <summary>Where the host is in the client area, as last placed.</summary>
    public Rectangle Bounds { get; private set; }

    /// <summary>The part of the host shown, as last placed; empty — hidden.</summary>
    public Rectangle Visible { get; private set; }

    public int PlaceCount { get; private set; }

    public int FocusCount { get; private set; }

    public bool IsDisposed { get; private set; }

    /// <summary>The controls destroyed by the slot — those of hosts without a destruction
    /// of their own.</summary>
    public List<PlatformHandle> Destroyed { get; } = [];

    public event EventHandler? ControlFocused;

    public void Attach(PlatformHandle control) => Control = control;

    public void Place(Rectangle bounds, Rectangle visible)
    {
        Bounds = bounds;
        Visible = visible;
        PlaceCount++;
    }

    public void Focus() => FocusCount++;

    public void DestroyControl(PlatformHandle control) => Destroyed.Add(control);

    public void Dispose() => IsDisposed = true;

    /// <summary>Play a click into the control: it takes the keyboard by itself.</summary>
    public void SimulateControlFocus() => ControlFocused?.Invoke(this, EventArgs.Empty);
}