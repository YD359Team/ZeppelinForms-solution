using System.Runtime.InteropServices;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>One argument of a request or an event, as libwayland passes them: a union
/// of a pointer's size.</summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
internal struct WlArgument
{
    /// <summary>int, fixed (24.8), fd.</summary>
    [FieldOffset(0)] public int I;

    /// <summary>uint, new_id in a request.</summary>
    [FieldOffset(0)] public uint U;

    /// <summary>string, object, array; new_id in an event — the new proxy.</summary>
    [FieldOffset(0)] public nint P;
}

/// <summary>struct wl_array: a size, the room allocated, the data.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WlArray
{
    public nuint Size;
    public nuint Alloc;
    public nint Data;
}

/// <summary>libwayland-client: the connection, the queue, and the proxies — the
/// requests are marshalled and the events delivered by it, the protocol itself is the
/// generated classes'.</summary>
/// <remarks>
/// The requests go through <c>wl_proxy_marshal_array_constructor_versioned</c> rather
/// than <c>wl_proxy_marshal_array_flags</c>: the second came in 1.20, the first is there
/// since 1.10 — every distribution with a Wayland session has it.
/// </remarks>
internal static unsafe class WaylandNative
{
    internal const string Lib = "libwayland-client.so.0";

    [DllImport(Lib)] public static extern nint wl_display_connect(nint name);
    [DllImport(Lib)] public static extern void wl_display_disconnect(nint display);
    [DllImport(Lib)] public static extern int wl_display_get_fd(nint display);
    [DllImport(Lib)] public static extern int wl_display_dispatch(nint display);
    [DllImport(Lib)] public static extern int wl_display_dispatch_pending(nint display);
    [DllImport(Lib)] public static extern int wl_display_roundtrip(nint display);
    [DllImport(Lib)] public static extern int wl_display_flush(nint display);
    [DllImport(Lib)] public static extern int wl_display_prepare_read(nint display);
    [DllImport(Lib)] public static extern int wl_display_read_events(nint display);
    [DllImport(Lib)] public static extern void wl_display_cancel_read(nint display);
    [DllImport(Lib)] public static extern int wl_display_get_error(nint display);

    [DllImport(Lib)]
    public static extern nint wl_proxy_marshal_array_constructor_versioned(
        nint proxy, uint opcode, WlArgument* args, nint @interface, uint version);

    [DllImport(Lib)] public static extern void wl_proxy_marshal_array(nint proxy, uint opcode, WlArgument* args);

    [DllImport(Lib)]
    public static extern int wl_proxy_add_dispatcher(
        nint proxy,
        delegate* unmanaged<nint, nint, uint, nint, WlArgument*, int> dispatcher,
        nint implementation,
        nint data);

    [DllImport(Lib)] public static extern void wl_proxy_destroy(nint proxy);
    [DllImport(Lib)] public static extern uint wl_proxy_get_version(nint proxy);
    [DllImport(Lib)] public static extern uint wl_proxy_get_id(nint proxy);
    [DllImport(Lib)] public static extern nint wl_proxy_get_user_data(nint proxy);
}