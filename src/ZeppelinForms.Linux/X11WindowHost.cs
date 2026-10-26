using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Linux;

/// <summary>
/// An X11 window among the form's elements: a video player's output, a GStreamer sink, a
/// toolkit's view that renders into an XID. By default the host makes a plain child
/// window — hand <see cref="Window"/> to the code that draws into it, in
/// <see cref="NativeControlHost.NativeControlCreated"/>; override
/// <see cref="CreateWindowCore"/> to make one of one's own.
/// </summary>
/// <remarks>
/// <para>
/// X11 only: under Wayland the form's window has no X11 windows inside, and the host
/// draws its placeholder (Wayland subsurfaces come in 0.19).
/// </para>
/// <para>
/// See <see cref="NativeControlHost"/> for what any native control can't do in a form:
/// the form's popups open under it, and Tab leads into it but not out of it.
/// </para>
/// </remarks>
public class X11WindowHost : NativeControlHost
{
    /// <summary>The hosted window; 0 before it is made and after it is destroyed.</summary>
    public nuint Window { get; private set; }

    /// <summary>The form's connection to the X server, the window was made with; 0 before.</summary>
    public nint Display { get; private set; }

    /// <summary>Make the window, a child of <paramref name="parent"/>. The default is a
    /// plain child window for something else to draw into: the server fills it with the
    /// host's <see cref="ZeppelinForms.Forms.Controls.Base.UIElement.Background"/> on a
    /// true-color screen, and with nothing where that is transparent.</summary>
    protected virtual nuint CreateWindowCore(nint display, nuint parent)
    {
        Color background = Background;
        nuint pixel = (nuint)((background.R << 16) | (background.G << 8) | background.B);

        nuint window = X11.XCreateSimpleWindow(display, parent, 0, 0, 1, 1, 0, 0, pixel);

        if (background.A == 0)
            X11.XSetWindowBackgroundPixmap(display, window, X11.None);

        return window;
    }

    /// <summary>Destroy the window <see cref="CreateWindowCore"/> made. The default is
    /// XDestroyWindow.</summary>
    protected virtual void DestroyWindowCore(nint display, nuint window)
    {
        X11.XDestroyWindow(display, window);
        X11.XFlush(display);
    }

    protected sealed override PlatformHandle CreateNativeControlCore(PlatformHandle parent)
    {
        if (parent.Kind != PlatformHandle.Xid || X11NativeSlot.FromHandle((nuint)parent.Handle) is not { } slot)
            throw new PlatformNotSupportedException($"An X11WindowHost lives in an X11 window, not in a {parent.Kind} one.");

        Display = slot.Display;

        nuint window = CreateWindowCore(Display, (nuint)parent.Handle);

        if (window == 0)
            throw new InvalidOperationException($"{GetType().Name}.CreateWindowCore returned no window.");

        Window = window;
        return new PlatformHandle((nint)window, PlatformHandle.Xid);
    }

    protected sealed override void DestroyNativeControlCore(PlatformHandle control)
    {
        try
        {
            DestroyWindowCore(Display, (nuint)control.Handle);
        }
        finally
        {
            Window = 0;
        }
    }
}