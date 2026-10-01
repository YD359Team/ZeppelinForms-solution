using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Linux.DBus;

namespace ZeppelinForms.Linux;

/// <summary>The appearance settings of the Linux desktop, through the Settings
/// interface of the XDG Desktop Portal.</summary>
/// <remarks>
/// <para>
/// The portal is the one place GNOME, KDE and the others agree on:
/// org.freedesktop.appearance holds color-scheme — 0 no preference, 1 prefer dark,
/// 2 prefer light — and accent-color, three doubles from 0 to 1 that are out of
/// range when the user picked none. SettingChanged reports a change.
/// </para>
/// <para>
/// ReadOne is the current method; portals older than version 2 have only Read,
/// which wraps the value in one variant more. A portal or a desktop that doesn't
/// know a key answers with an error, and that key simply has no value.
/// </para>
/// </remarks>
internal sealed class PortalAppearance : ISystemAppearance, IDisposable
{
    private const string Destination = "org.freedesktop.portal.Desktop";
    private const string ObjectPath = "/org/freedesktop/portal/desktop";
    private const string SettingsInterface = "org.freedesktop.portal.Settings";
    private const string AppearanceNamespace = "org.freedesktop.appearance";

    private const string UnknownMethod = "org.freedesktop.DBus.Error.UnknownMethod";

    /// <summary>The bus answers in microseconds, and so does a running portal.
    /// The margin is for a portal the bus has to start first — on a cold session it
    /// brings up its desktop backend as well. A desktop without a portal at all
    /// costs nothing: the bus refuses at once that it knows no such service.</summary>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromMilliseconds(1000);

    private readonly DBusConnection _bus;
    private bool _useReadOne = true;

    public bool IsDark { get; private set; }

    public Color? AccentColor { get; private set; }

    public event EventHandler? Changed;

    /// <summary>The bus socket, for the select of the X11 loop; −1 once closed.</summary>
    public int FileDescriptor => _bus.FileDescriptor;

    private PortalAppearance(DBusConnection bus) => _bus = bus;

    /// <summary>Connect and read the settings; null when there is no session bus
    /// or no portal on it.</summary>
    public static PortalAppearance? TryCreate()
    {
        DBusConnection? bus = DBusConnection.ConnectSession(CallTimeout);

        if (bus is null) return null;

        var portal = new PortalAppearance(bus);

        // a signal subscription first: a change between reading and subscribing
        // would otherwise be lost
        bus.Call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "AddMatch",
            $"type='signal',interface='{SettingsInterface}',member='SettingChanged'," +
            $"path='{ObjectPath}',arg0='{AppearanceNamespace}'");

        bus.Signal += portal.OnSignal;

        object? scheme = portal.Read("color-scheme", out bool reachable);

        if (!reachable)
        {
            // no portal on this bus: the application gets no system appearance
            bus.Dispose();
            return null;
        }

        portal.IsDark = IsDarkScheme(scheme);
        portal.AccentColor = ToAccent(portal.Read("accent-color", out _));

        return portal;
    }

    /// <summary>Read data the event loop found on the socket.</summary>
    public void Poll() => _bus.ProcessIncoming();

    /// <summary>One setting of the appearance namespace. <paramref name="reachable"/> —
    /// whether the portal answered at all, even with an error about the key.</summary>
    private object? Read(string key, out bool reachable)
    {
        reachable = false;

        if (_useReadOne)
        {
            DBusMessage? reply = _bus.Call(Destination, ObjectPath, SettingsInterface, "ReadOne",
                AppearanceNamespace, key);

            if (reply is { Type: DBusMessageType.Error, ErrorName: UnknownMethod })
                _useReadOne = false;
            else
                return Value(reply, out reachable);
        }

        return Value(
            _bus.Call(Destination, ObjectPath, SettingsInterface, "Read", AppearanceNamespace, key),
            out reachable);
    }

    private static object? Value(DBusMessage? reply, out bool reachable)
    {
        // an error from the portal itself — "no such key" — still means a portal;
        // an error from the bus — "no such service" — doesn't
        reachable = reply is not null &&
            !(reply.Type == DBusMessageType.Error &&
              reply.ErrorName is "org.freedesktop.DBus.Error.ServiceUnknown"
                  or "org.freedesktop.DBus.Error.NameHasNoOwner");

        if (reply is not { Type: DBusMessageType.MethodReturn, Body: [DBusVariant variant, ..] })
            return null;

        return variant.Unwrap();
    }

    private void OnSignal(DBusMessage message)
    {
        if (message is not
            {
                Interface: SettingsInterface,
                Member: "SettingChanged",
                Body: [AppearanceNamespace, string key, DBusVariant value, ..],
            })
        {
            return;
        }

        bool dark = IsDark;
        Color? accent = AccentColor;

        switch (key)
        {
            case "color-scheme": dark = IsDarkScheme(value.Unwrap()); break;
            case "accent-color": accent = ToAccent(value.Unwrap()); break;
            default: return;
        }

        if (dark == IsDark && accent == AccentColor) return;

        IsDark = dark;
        AccentColor = accent;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>1 — prefer dark. "No preference" is light: that is what the
    /// desktops show an application that asks nothing.</summary>
    private static bool IsDarkScheme(object? value) => value is 1u;

    /// <summary>(ddd) in 0…1; out of range — the user picked no accent.</summary>
    private static Color? ToAccent(object? value)
    {
        if (value is not object?[] { Length: 3 } rgb ||
            rgb[0] is not double r || rgb[1] is not double g || rgb[2] is not double b)
        {
            return null;
        }

        static bool InRange(double v) => v is >= 0d and <= 1d;

        if (!InRange(r) || !InRange(g) || !InRange(b)) return null;

        static byte Channel(double v) => (byte)Math.Round(v * 255d);

        return new Color(Channel(r), Channel(g), Channel(b));
    }

    public void Dispose() => _bus.Dispose();
}