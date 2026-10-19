using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Linux;

public sealed class X11DisplayProvider : IDisplayProvider
{
    private readonly nint _display;
    private readonly float _scale;
    private readonly float _dpi;

    internal X11DisplayProvider(nint display, float scale, float dpi)
    {
        _display = display;
        _scale = scale;
        _dpi = dpi;
    }

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        int screen = X11.XDefaultScreen(_display);
        nuint root = X11.XRootWindow(_display, screen);

        List<DisplayInfo> displays = [];

        // the area without the panels, of the current desktop: one rectangle over the
        // whole root, so each screen gets its part of it
        Rectangle? workArea = ReadWorkArea(root);

        try
        {
            nint resourcesPtr = X11.XRRGetScreenResourcesCurrent(_display, root);

            if (resourcesPtr != 0)
            {
                try
                {
                    var resources = Marshal.PtrToStructure<X11.XRRScreenResources>(resourcesPtr);

                    for (int i = 0; i < resources.ncrtc; i++)
                    {
                        nuint crtc = (nuint)Marshal.ReadIntPtr(resources.crtcs, i * nint.Size);
                        nint infoPtr = X11.XRRGetCrtcInfo(_display, resourcesPtr, crtc);

                        if (infoPtr == 0) continue;

                        try
                        {
                            var info = Marshal.PtrToStructure<X11.XRRCrtcInfo>(infoPtr);

                            // a CRTC without a mode is a disabled output
                            if (info.mode == 0 || info.width == 0) continue;

                            var bounds = new Rectangle(
                                new Point(info.x, info.y),
                                new Size(info.width, info.height));

                            displays.Add(new DisplayInfo
                            {
                                Bounds = bounds,
                                // _NET_WORKAREA: until 0.17 it wasn't read, and the
                                // working area was the full screen — a centered window
                                // stood under the panel
                                WorkingArea = WorkingAreaOf(bounds, workArea),
                                Scale = _scale,
                                Dpi = _dpi,
                                IsPrimary = displays.Count == 0,
                                Name = $"CRTC{i}",
                            });
                        }
                        finally
                        {
                            X11.XRRFreeCrtcInfo(infoPtr);
                        }
                    }
                }
                finally
                {
                    X11.XRRFreeScreenResources(resourcesPtr);
                }
            }
        }
        catch (DllNotFoundException)
        {
            // no libXrandr — we make do with one screen
        }

        if (displays.Count > 0)
            return displays;

        var single = new Rectangle(
            Point.Empty,
            new Size(X11.XDisplayWidth(_display, screen), X11.XDisplayHeight(_display, screen)));

        return
        [
            new DisplayInfo
            {
                Bounds = single,
                WorkingArea = WorkingAreaOf(single, workArea),
                Scale = _scale,
                Dpi = _dpi,
                IsPrimary = true,
            },
        ];
    }


    /// <summary>The part of a screen inside the work area; the whole screen when there is
    /// no work area or it misses the screen.</summary>
    /// <remarks>_NET_WORKAREA is one rectangle for all the screens together: a panel on
    /// an inner edge between two monitors isn't taken out of either. EWMH has nothing
    /// finer that every manager publishes.</remarks>
    private static Rectangle WorkingAreaOf(Rectangle screen, Rectangle? workArea)
    {
        if (workArea is not { } area) return screen;

        float left = Math.Max(screen.X, area.X);
        float top = Math.Max(screen.Y, area.Y);
        float right = Math.Min(screen.X + screen.Width, area.X + area.Width);
        float bottom = Math.Min(screen.Y + screen.Height, area.Y + area.Height);

        return right > left && bottom > top
            ? new Rectangle(new Point(left, top), new Size(right - left, bottom - top))
            : screen;
    }

    /// <summary>The work area of the current desktop, in pixels; null — the window
    /// manager publishes none, or there is no manager at all.</summary>
    private Rectangle? ReadWorkArea(nuint root)
    {
        nuint workArea = X11.XInternAtom(_display, "_NET_WORKAREA", true);
        if (workArea == X11.None) return null;

        // four numbers per desktop: x, y, width, height
        long desktop = ReadCardinals(root, "_NET_CURRENT_DESKTOP", 0, 1) is [long current] ? current : 0;

        return ReadCardinals(root, workArea, desktop * 4, 4) is [long x, long y, long width, long height] && width > 0 && height > 0
            ? new Rectangle(new Point(x, y), new Size(width, height))
            : null;
    }

    private long[]? ReadCardinals(nuint window, string property, long offset, long length)
    {
        nuint atom = X11.XInternAtom(_display, property, true);
        return atom == X11.None ? null : ReadCardinals(window, atom, offset, length);
    }

    /// <summary>A CARDINAL property, <paramref name="length"/> items from
    /// <paramref name="offset"/>. Xlib hands 32-bit items over as C longs, as
    /// ChangeLongs writes them; null — no such property or not that many items.</summary>
    private long[]? ReadCardinals(nuint window, nuint property, long offset, long length)
    {
        int status = X11.XGetWindowProperty(
            _display, window, property, offset, length, false, X11.XA_CARDINAL,
            out _, out int format, out nuint count, out _, out nint data);

        if (status != 0 || data == 0) return null;

        try
        {
            if (format != 32 || (long)count < length) return null;

            var values = new long[length];

            for (int i = 0; i < length; i++)
                values[i] = Marshal.ReadIntPtr(data, i * nint.Size);

            return values;
        }
        finally
        {
            X11.XFree(data);
        }
    }
}