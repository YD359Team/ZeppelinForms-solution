using System.Runtime.InteropServices;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>A request or an event as libwayland describes it: its name, its signature
/// ("since" version, then a letter per argument, '?' before one that may be null), and
/// the interface of every object argument — null for the others.</summary>
internal readonly record struct WlMessage(string Name, string Signature, string?[] Types);

/// <summary>
/// The native <c>struct wl_interface</c> of every interface the generated classes speak,
/// kept for the life of the process: libwayland holds pointers to them in every proxy.
/// </summary>
/// <remarks>
/// A struct is allocated the first time its name is asked for and filled when its
/// interface is defined: the types of one message point at other interfaces — of this
/// protocol or another — which may not be defined yet. <see cref="Define"/> of every
/// protocol runs before the connection is used, so all are filled by then.
/// </remarks>
internal static unsafe class WlInterface
{
    // struct wl_interface { const char *name; int version; int method_count;
    //   const struct wl_message *methods; int event_count; const struct wl_message *events; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Native
    {
        public nint Name;
        public int Version;
        public int MethodCount;
        public nint Methods;
        public int EventCount;
        public nint Events;
    }

    // struct wl_message { const char *name; const char *signature; const struct wl_interface **types; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Name;
        public nint Signature;
        public nint Types;
    }

    private static readonly Dictionary<string, nint> s_interfaces = new(StringComparer.Ordinal);
    private static readonly Lock s_lock = new();

    /// <summary>The native struct of an interface, allocated on first request.</summary>
    public static nint Get(string name)
    {
        lock (s_lock)
        {
            if (!s_interfaces.TryGetValue(name, out nint pointer))
            {
                pointer = (nint)NativeMemory.AllocZeroed((nuint)sizeof(Native));
                s_interfaces[name] = pointer;
            }

            return pointer;
        }
    }

    /// <summary>Fill the struct of an interface with its name, version and messages.</summary>
    public static void Define(string name, int version, WlMessage[] requests, WlMessage[] events)
    {
        var native = (Native*)Get(name);

        lock (s_lock)
        {
            if (native->Name != 0) return;

            native->Version = version;
            native->MethodCount = requests.Length;
            native->Methods = Messages(requests);
            native->EventCount = events.Length;
            native->Events = Messages(events);

            // the name last: it marks the struct as filled
            native->Name = Marshal.StringToCoTaskMemUTF8(name);
        }
    }

    private static nint Messages(WlMessage[] messages)
    {
        if (messages.Length == 0) return 0;

        var native = (NativeMessage*)NativeMemory.AllocZeroed((nuint)(sizeof(NativeMessage) * messages.Length));

        for (int i = 0; i < messages.Length; i++)
        {
            WlMessage message = messages[i];

            native[i].Name = Marshal.StringToCoTaskMemUTF8(message.Name);
            native[i].Signature = Marshal.StringToCoTaskMemUTF8(message.Signature);

            // one slot per argument, even an argument with no interface: libwayland
            // walks the types along with the signature
            int count = Math.Max(1, message.Types.Length);
            var types = (nint*)NativeMemory.AllocZeroed((nuint)(sizeof(nint) * count));

            for (int t = 0; t < message.Types.Length; t++)
                types[t] = message.Types[t] is { } type ? Get(type) : 0;

            native[i].Types = (nint)types;
        }

        return (nint)native;
    }
}