using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms;

/// <summary>Where the window stands and how big it may be: its size limits, its
/// place above other windows, its place on the screen. The browser and Android have
/// none of these and ignore them, as they do the frame.</summary>
public partial class Form
{
    /// <summary>The smallest size the window can be given, by the user dragging its
    /// edges and by the application alike. An axis of NaN has no limit: Size.Auto —
    /// none at all, the default. Measured as <see cref="Size"/> is.</summary>
    /// <remarks>
    /// <para>
    /// Until 0.17 the limits bounded only <see cref="SizeToContent"/>: a window could
    /// be dragged down to a strip of its title bar whatever MinimumSize said.
    /// </para>
    /// <para>
    /// As in WinForms, a minimum above the maximum moves the maximum up to it, and a
    /// maximum below the minimum moves the minimum down — the two never contradict.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">An axis negative or infinite.</exception>
    public Size MinimumSize
    {
        get;
        set
        {
            CheckLimit(value, nameof(MinimumSize));

            if (field == value) return;

            field = value;

            MaximumSize = new Size(
                RaiseTo(MaximumSize.Width, value.Width),
                RaiseTo(MaximumSize.Height, value.Height));

            ApplySizeLimits();
        }
    } = Size.Auto;

    /// <summary>The largest size the window can be given. An axis of NaN has no
    /// limit: Size.Auto — none at all, the default. MaximumSize.Width is also where
    /// the text of a form sized to its content wraps.</summary>
    /// <exception cref="ArgumentOutOfRangeException">An axis negative or infinite.</exception>
    public Size MaximumSize
    {
        get;
        set
        {
            CheckLimit(value, nameof(MaximumSize));

            if (field == value) return;

            field = value;

            MinimumSize = new Size(
                LowerTo(MinimumSize.Width, value.Width),
                LowerTo(MinimumSize.Height, value.Height));

            ApplySizeLimits();
        }
    } = Size.Auto;

    /// <summary>Whether the window stays above every window that isn't topmost, as
    /// a tool palette or a timer does. Can be changed while the window is open.</summary>
    public bool TopMost
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            DesktopWindow?.SetTopMost(value);
        }
    }

    /// <summary>Put the window in the middle of the working area of the screen it is
    /// on — the taskbar left out. Before the window is open it opens there:
    /// <see cref="WindowStartupLocation"/> becomes <see cref="WindowStartupLocation.CenterScreen"/>.</summary>
    public void CenterToScreen()
    {
        if (DesktopWindow is { } window)
        {
            if (!window.SupportsPositioning)
                throw new PlatformNotSupportedException(
                    "This platform doesn't let a window place itself: on Wayland the compositor " +
                    "decides where windows go. Check App.Capabilities for WindowPositioning first.");

            window.CenterOnScreen();
            return;
        }

        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    /// <summary><see cref="Position"/> changed: by the application, or by the user
    /// moving the window.</summary>
    public event EventHandler? LocationChanged;

    /// <summary><see cref="Size"/> changed: by the application, or by the user resizing
    /// the window. <see cref="ClientSizeChanged"/> follows the area inside the frame.</summary>
    public event EventHandler? SizeChanged;

    protected virtual void OnLocationChanged(EventArgs e) => LocationChanged?.Invoke(this, e);

    protected virtual void OnSizeChanged(EventArgs e) => SizeChanged?.Invoke(this, e);

    /// <summary>The window moved, by the user or the system: the platform tells the form
    /// where it is now, in screen pixels.</summary>
    /// <remarks>Not through the setter: that would send the window to where it already
    /// is, and a drag would fight itself.</remarks>
    internal void OnWindowMoved(Point position)
    {
        if (_position == position) return;

        _position = position;
        OnLocationChanged(EventArgs.Empty);
    }

    /// <summary>The window was resized: the platform tells the form its size now, in
    /// logical units and measured as <see cref="Size"/> is.</summary>
    internal void OnWindowResized(Size size)
    {
        if (_size == size) return;

        _size = size;
        OnSizeChanged(EventArgs.Empty);
    }

    /// <summary>An open window goes where <see cref="Position"/> and <see cref="Size"/>
    /// say. Before it is open there is nothing to move: the platform reads them when
    /// it makes the window.</summary>
    private void ApplyBounds() => DesktopWindow?.SetBounds(new Rectangle(Position, Size));
    internal Size ClampToSizeLimits(Size size) => new(
        Clamp(size.Width, MinimumSize.Width, MaximumSize.Width),
        Clamp(size.Height, MinimumSize.Height, MaximumSize.Height));

    /// <summary>An open window follows its limits at once: the platform keeps the user's
    /// dragging within them and brings the window inside if it is out.</summary>
    private void ApplySizeLimits() => DesktopWindow?.SetSizeLimits(MinimumSize, MaximumSize);

    private static void CheckLimit(Size limit, string name)
    {
        if (!IsLength(limit.Width) || !IsLength(limit.Height))
            throw new ArgumentOutOfRangeException(name, limit,
                "Expected finite non-negative lengths, or NaN for no limit.");

        static bool IsLength(float value) => float.IsNaN(value) || (float.IsFinite(value) && value >= 0f);
    }

    /// <summary>The maximum moved up to a new minimum on one axis.</summary>
    private static float RaiseTo(float maximum, float minimum) =>
        !float.IsNaN(maximum) && !float.IsNaN(minimum) && maximum < minimum ? minimum : maximum;

    /// <summary>The minimum moved down to a new maximum on one axis.</summary>
    private static float LowerTo(float minimum, float maximum) =>
        !float.IsNaN(minimum) && !float.IsNaN(maximum) && minimum > maximum ? maximum : minimum;

    private static float Clamp(float value, float minimum, float maximum)
    {
        // an auto size has nothing to clamp: the layout decides it
        if (float.IsNaN(value)) return value;

        if (!float.IsNaN(maximum) && value > maximum) value = maximum;
        if (!float.IsNaN(minimum) && value < minimum) value = minimum;

        return value;
    }
}