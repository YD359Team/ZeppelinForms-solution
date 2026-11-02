using ZeppelinForms.Linux.DBus;

namespace ZeppelinForms.Linux.Accessibility;

/// <summary>The application on the accessibility bus of the Linux desktop: AT-SPI 2, which
/// Orca and the other assistive technologies of GNOME, KDE and the rest read.</summary>
/// <remarks>
/// <para>
/// The accessibility bus is a bus of its own; the session bus gives its address
/// (org.a11y.Bus.GetAddress). The application connects to it, exports its tree there
/// (<see cref="AtSpiTree"/>) and embeds its root in the registry's desktop — after which
/// a screen reader finds it among the applications, as it finds GTK's and Qt's.
/// </para>
/// <para>
/// Nothing more happens until a client reads the tree: no peer is made and no event is
/// sent. A desktop without AT-SPI — no org.a11y.Bus on the session bus — costs one call
/// the bus refuses at once. <c>NO_AT_BRIDGE=1</c>, which GTK and Qt honor, keeps the
/// application off the bus.
/// </para>
/// <para>
/// The connection is read by the platform's loop next to the display, as the portal's
/// and the tray's are: a call from a screen reader is answered on the UI thread, where
/// the peers may be read.
/// </para>
/// </remarks>
internal sealed class AtSpiBridge : IDisposable
{
    /// <summary>The registry and a screen reader answer at once; the margin is for an
    /// accessibility bus the session bus has to start first.</summary>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromMilliseconds(1000);

    private readonly DBusConnection _bus;
    private readonly AtSpiTree _tree;

    private AtSpiBridge(DBusConnection bus, string busName, IAtSpiWindows windows)
    {
        _bus = bus;
        _tree = new AtSpiTree(busName, bus.Emit, windows);

        bus.RegisterSubtree(AtSpi.AccessiblePrefix, _tree);
        bus.Register(AtSpi.CachePath, _tree);
    }

    /// <summary>Connect to the accessibility bus and embed the application; null where
    /// the desktop has no AT-SPI, or the application was asked to stay off it.</summary>
    public static AtSpiBridge? TryCreate(IAtSpiWindows windows)
    {
        if (Environment.GetEnvironmentVariable("NO_AT_BRIDGE") == "1") return null;

        if (AccessibilityBusAddress() is not { } address) return null;

        DBusConnection? bus = DBusConnection.Connect(address, CallTimeout);

        if (bus is null) return null;

        if (bus.UniqueName is not { Length: > 0 } name)
        {
            bus.Dispose();
            return null;
        }

        var bridge = new AtSpiBridge(bus, name, windows);
        bridge.Embed();

        return bridge;
    }

    /// <summary>The accessibility bus's address, from the session bus; null without one.</summary>
    private static string? AccessibilityBusAddress()
    {
        using DBusConnection? session = DBusConnection.ConnectSession(CallTimeout);

        DBusMessage? reply = session?.Call("org.a11y.Bus", "/org/a11y/bus", "org.a11y.Bus", "GetAddress");

        return reply is { Type: DBusMessageType.MethodReturn, Body: [string address, ..] } && address.Length > 0
            ? address
            : null;
    }

    /// <summary>Put the application's root into the registry's desktop: Socket.Embed
    /// with the root's reference, answered with the desktop's — the root's parent.</summary>
    private void Embed()
    {
        DBusMessage? reply = _bus.CallMethod(
            AtSpi.RegistryName, AtSpi.RootPath, AtSpi.SocketInterface, "Embed",
            "(so)", [new object?[] { _bus.UniqueName, AtSpi.RootPath }]);

        if (reply is { Type: DBusMessageType.MethodReturn, Body: [object?[] { Length: 2 } desktop, ..] } &&
            desktop[0] is string name && desktop[1] is string path)
        {
            _tree.Desktop = (name, path);
        }
    }

    /// <summary>The bus socket, for the loop's wait; −1 once closed.</summary>
    public int FileDescriptor => _bus.FileDescriptor;

    /// <summary>Answer what the screen readers asked, and tell them of the windows that
    /// came, went or were activated. Without data it returns at once.</summary>
    public void Poll()
    {
        _bus.ProcessIncoming();
        _tree.ReviewWindows();
    }

    public void Dispose()
    {
        _tree.StopListening();

        // out of the desktop: a screen reader drops the application at once, rather than
        // when the bus tells it the connection is gone
        _bus.CallMethod(
            AtSpi.RegistryName, AtSpi.RootPath, AtSpi.SocketInterface, "Unembed",
            "(so)", [new object?[] { _bus.UniqueName, AtSpi.RootPath }]);

        _bus.Dispose();
    }
}
