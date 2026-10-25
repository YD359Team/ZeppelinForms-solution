using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Browser;

/// <summary>
/// The place of one element of the page among a form's elements: a <c>div</c> over the
/// canvas, as large as the part of the host the form shows, holding the element at the
/// host's size and offset by what is cut off (see zf.js, "native elements").
/// </summary>
/// <remarks>The slot's handle is its number: the element itself is a JavaScript object,
/// kept on the page and reached through <see cref="HtmlElementHost.Element"/>.</remarks>
internal sealed class BrowserNativeSlot : INativeControlSlot
{
    private static readonly Dictionary<int, BrowserNativeSlot> s_slots = [];

    private readonly BrowserWindow _owner;
    private readonly int _id;
    private bool _disposed;

    public BrowserNativeSlot(BrowserWindow owner)
    {
        _owner = owner;
        _id = Interop.CreateSlot();
        s_slots[_id] = this;
    }

    public PlatformHandle Parent => new(_id, PlatformHandle.HtmlElement);

    public event EventHandler? ControlFocused;

    internal static void OnFocused(int id)
    {
        if (s_slots.TryGetValue(id, out BrowserNativeSlot? slot))
            slot.ControlFocused?.Invoke(slot, EventArgs.Empty);
    }

    // the element is made inside the slot by the host: there is nothing to move into it
    public void Attach(PlatformHandle control) { }

    public void Place(Rectangle bounds, Rectangle visible)
    {
        if (_disposed) return;

        if (visible.Width <= 0 || visible.Height <= 0)
        {
            Interop.HideSlot(_id);
            return;
        }

        // a dialog is a layer of the canvas at its own origin
        Point origin = _owner.Origin;

        Interop.PlaceSlot(_id,
            bounds.X + origin.X, bounds.Y + origin.Y, bounds.Width, bounds.Height,
            visible.X + origin.X, visible.Y + origin.Y, visible.Width, visible.Height);
    }

    public void Focus()
    {
        if (!_disposed) Interop.FocusSlot(_id);
    }

    public void DestroyControl(PlatformHandle control)
    {
        if (!_disposed) Interop.DestroySlotElement(_id);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        s_slots.Remove(_id);
        Interop.DestroySlot(_id);
    }
}