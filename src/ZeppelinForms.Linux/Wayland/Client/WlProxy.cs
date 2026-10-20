using System.Runtime.InteropServices;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>What the generated class of an interface knows about itself without an
/// instance: its native struct, and how to wrap a proxy libwayland made.</summary>
internal interface IWlObject<TSelf> where TSelf : WlProxy
{
    static abstract string InterfaceName { get; }

    /// <summary>The highest version the class speaks: a bind asks for no more.</summary>
    static abstract uint InterfaceVersion { get; }

    static abstract TSelf Wrap(nint handle);
}

/// <summary>
/// A client-side object of the protocol — a <c>wl_proxy</c> — and the managed object over
/// it: requests go out through it, events come back into its <see cref="Dispatch"/>.
/// </summary>
/// <remarks>
/// <para>
/// The events arrive through one native dispatcher for every proxy: it is handed a
/// GCHandle of the managed object as the proxy's implementation, and the opcode and the
/// arguments of the event. The generated classes decode them into typed C# events.
/// </para>
/// <para>
/// An exception in a handler mustn't unwind into libwayland: it is kept and thrown again
/// once the dispatch returns, by <see cref="ThrowPendingException"/>.
/// </para>
/// </remarks>
internal abstract unsafe class WlProxy
{
    private GCHandle _self;

    [ThreadStatic] private static Exception? t_pending;

    protected WlProxy(nint handle, bool listen = true)
    {
        Handle = handle;

        if (handle == 0 || !listen) return;

        _self = GCHandle.Alloc(this);
        nint self = GCHandle.ToIntPtr(_self);

        WaylandNative.wl_proxy_add_dispatcher(handle, &OnEvent, self, self);
    }

    /// <summary>The native proxy; 0 once destroyed.</summary>
    public nint Handle { get; private set; }

    /// <summary>The version the object was made with: what it may be asked for.</summary>
    public uint Version => Handle != 0 ? WaylandNative.wl_proxy_get_version(Handle) : 0;

    public bool IsAlive => Handle != 0;

    /// <summary>An event of this object.</summary>
    protected abstract void Dispatch(uint opcode, WlArgument* args);

    /// <summary>A request that makes nothing.</summary>
    protected void Emit(uint opcode, WlArgument* args)
    {
        ThrowIfDestroyed();
        WaylandNative.wl_proxy_marshal_array(Handle, opcode, args);
    }

    /// <summary>A request that makes a new object: its proxy, of the given interface, at
    /// this object's version — or <paramref name="version"/> for a bind.</summary>
    protected nint EmitConstructor(uint opcode, WlArgument* args, string interfaceName, uint version)
    {
        ThrowIfDestroyed();

        nint created = WaylandNative.wl_proxy_marshal_array_constructor_versioned(
            Handle, opcode, args, WlInterface.Get(interfaceName), version);

        if (created == 0)
            throw new InvalidOperationException($"libwayland made no {interfaceName} (the connection may be lost).");

        return created;
    }

    /// <summary>Forget the native proxy: after a destructor request, or a destructor event
    /// of the server's. The object's id goes back to libwayland.</summary>
    protected void DestroyProxy()
    {
        if (Handle == 0) return;

        WaylandNative.wl_proxy_destroy(Handle);
        Handle = 0;

        if (_self.IsAllocated) _self.Free();
    }

    private void ThrowIfDestroyed()
    {
        if (Handle == 0)
            throw new ObjectDisposedException(GetType().Name, "The Wayland object is destroyed.");
    }

    /// <summary>The managed object of a proxy that came in an event's object argument:
    /// one this client made and listens to. Null for a null object.</summary>
    protected static T? FromHandle<T>(nint handle) where T : WlProxy
    {
        if (handle == 0) return null;

        nint data = WaylandNative.wl_proxy_get_user_data(handle);

        return data != 0 && GCHandle.FromIntPtr(data).Target is T known ? known : null;
    }

    /// <summary>A string argument; null for a null one.</summary>
    protected static string? Text(nint pointer) =>
        pointer == 0 ? null : Marshal.PtrToStringUTF8(pointer);

    /// <summary>An array argument, copied: libwayland frees it after the dispatch.</summary>
    protected static byte[] Bytes(nint array)
    {
        if (array == 0) return [];

        var native = (WlArray*)array;

        return new ReadOnlySpan<byte>((void*)native->Data, (int)native->Size).ToArray();
    }

    /// <summary>A string for a request, freed by <see cref="Free"/> after it.</summary>
    protected static nint Utf8(string? value) =>
        value is null ? 0 : Marshal.StringToCoTaskMemUTF8(value);

    protected static void Free(nint pointer)
    {
        if (pointer != 0) Marshal.FreeCoTaskMem(pointer);
    }

    /// <summary>Throw what a handler threw during the last dispatch, now that libwayland
    /// is out of the stack.</summary>
    public static void ThrowPendingException()
    {
        if (t_pending is not { } pending) return;

        t_pending = null;
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(pending);
    }

    // the platform's C convention: what libwayland's wl_dispatcher_func_t is
    [UnmanagedCallersOnly]
    private static int OnEvent(nint implementation, nint proxy, uint opcode, nint message, WlArgument* args)
    {
        try
        {
            if (GCHandle.FromIntPtr(implementation).Target is WlProxy target)
                target.Dispatch(opcode, args);
        }
        catch (Exception exception)
        {
            // the first one is what went wrong; the rest are its consequences
            t_pending ??= exception;
        }

        return 0;
    }
}