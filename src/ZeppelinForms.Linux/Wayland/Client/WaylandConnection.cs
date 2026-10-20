using ZeppelinForms.Core;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>A connection to the compositor: the display, its socket and its queue.</summary>
/// <remarks>The one default queue, read on the platform's thread: every object is made
/// and listened to there, as the X11 platform does with its connection.</remarks>
internal sealed class WaylandConnection : IDisposable
{
    static WaylandConnection()
    {
        // every table before the first request: the types of one point at another's
        WaylandProtocol.Define();
        XdgShellProtocol.Define();
    }

    private WaylandConnection(nint handle)
    {
        Handle = handle;
        Display = WlDisplay.Wrap(handle);
    }

    /// <summary>The wl_display*: what EGL's Wayland platform takes as the native display.</summary>
    public nint Handle { get; private set; }

    public WlDisplay Display { get; }

    /// <summary>The socket, for a poll that waits on it with the platform's timers.</summary>
    public int FileDescriptor => WaylandNative.wl_display_get_fd(Handle);

    /// <summary>Whether a Wayland session is there to connect to: libwayland-client is
    /// installed and WAYLAND_DISPLAY names a socket (or the default one is used).</summary>
    public static bool IsSessionAvailable =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
        && NativeProbe.Has(WaylandNative.Lib);

    /// <summary>Connect to the compositor WAYLAND_DISPLAY names.</summary>
    /// <exception cref="ZfPlatformException">No compositor answered.</exception>
    public static WaylandConnection Connect()
    {
        nint handle = WaylandNative.wl_display_connect(0);

        if (handle == 0)
            throw new ZfPlatformException(
                "Could not connect to the Wayland compositor. Check the WAYLAND_DISPLAY variable.");

        return new WaylandConnection(handle);
    }

    /// <summary>Send everything and wait until the compositor has answered all of it:
    /// the events those requests bring are dispatched before it returns.</summary>
    public void Roundtrip() => Check(WaylandNative.wl_display_roundtrip(Handle));

    /// <summary>Wait for events and dispatch them.</summary>
    public void Dispatch() => Check(WaylandNative.wl_display_dispatch(Handle));

    /// <summary>Dispatch what is already queued, without waiting.</summary>
    public void DispatchPending() => Check(WaylandNative.wl_display_dispatch_pending(Handle));

    /// <summary>Send the requests made so far.</summary>
    public void Flush() => WaylandNative.wl_display_flush(Handle);

    private void Check(int result)
    {
        // a handler's exception first: it is what the user's code did wrong
        WlProxy.ThrowPendingException();

        if (result < 0)
            throw new ZfPlatformException(
                $"The connection to the Wayland compositor failed (error {WaylandNative.wl_display_get_error(Handle)}).");
    }

    public void Dispose()
    {
        if (Handle == 0) return;

        WaylandNative.wl_display_disconnect(Handle);
        Handle = 0;
    }
}