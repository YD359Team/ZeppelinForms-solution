using System.Reflection;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Forms;
using ZeppelinForms.Linux.DBus;

namespace ZeppelinForms.Linux.Tray;

/// <summary>
/// A <see cref="TrayIcon"/> on a Linux panel: a StatusNotifierItem on the session bus,
/// with its menu as a dbusmenu next to it (<see cref="DBusMenu"/>).
/// </summary>
/// <remarks>
/// <para>
/// Every icon has a connection of its own. It owns the name
/// <c>org.kde.StatusNotifierItem-{pid}-{n}</c> and exports the item at
/// <c>/StatusNotifierItem</c>, as the specification asks, and the menu at
/// <c>/MenuBar</c>; it registers the name with the <c>org.kde.StatusNotifierWatcher</c>.
/// The panel — the host — reads the properties and calls Activate on a click. Disposing
/// closes the connection: the bus drops the name, and the watcher takes the icon off
/// every panel. There is no other way to unregister one.
/// </para>
/// <para>
/// A watcher that comes later — the panel started after the application, or restarted —
/// is noticed by NameOwnerChanged, and the item registers again.
/// </para>
/// <para>
/// The connection is read by the platform's loop next to the display, as the portal's
/// is: a call from the panel is answered on the UI thread, where the icon's events run.
/// </para>
/// </remarks>
internal sealed class StatusNotifierItem : ITrayIconImpl, IDBusObject
{
    internal const string WatcherName = "org.kde.StatusNotifierWatcher";
    private const string WatcherPath = "/StatusNotifierWatcher";
    private const string ItemInterface = "org.kde.StatusNotifierItem";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    private const string ItemPath = "/StatusNotifierItem";
    private const string MenuPath = "/MenuBar";

    /// <summary>The panel answers at once; the margin is for a bus under load.</summary>
    internal static readonly TimeSpan CallTimeout = TimeSpan.FromMilliseconds(1000);

    // the sizes a panel picks from: 16 to 24 on most, 32 and 48 on large panels and HiDPI
    private static readonly int[] PixmapSizes = [16, 22, 24, 32, 48, 64];

    private static int s_count;

    private readonly TrayIcon _owner;
    private readonly DBusConnection _bus;
    private readonly DBusMenu _menu;
    private readonly string _service;
    private readonly Action<StatusNotifierItem> _disposed;
    private List<object?[]>? _pixmaps;

    private StatusNotifierItem(TrayIcon owner, DBusConnection bus, string service, Action<StatusNotifierItem> disposed)
    {
        _owner = owner;
        _bus = bus;
        _service = service;
        _disposed = disposed;
        _menu = new DBusMenu(owner, bus, MenuPath);

        bus.Register(ItemPath, this);
        bus.Register(MenuPath, _menu);
    }

    /// <summary>Make the item and register it; null without a session bus.
    /// <paramref name="disposed"/> is told when it goes.</summary>
    public static StatusNotifierItem? TryCreate(TrayIcon owner, Action<StatusNotifierItem> disposed)
    {
        DBusConnection? bus = DBusConnection.ConnectSession(CallTimeout);

        if (bus is null) return null;

        string name = $"org.kde.StatusNotifierItem-{Environment.ProcessId}-{++s_count}";

        // without the name the unique one serves: the watcher takes either
        string service = bus.RequestName(name) ? name : bus.UniqueName ?? name;

        var item = new StatusNotifierItem(owner, bus, service, disposed);

        // a watcher that appears later: the panel started after the application
        bus.AddMatch(
            $"type='signal',sender='org.freedesktop.DBus',interface='org.freedesktop.DBus'," +
            $"member='NameOwnerChanged',arg0='{WatcherName}'");

        bus.Signal += item.OnSignal;

        item.RegisterWithWatcher();

        return item;
    }

    /// <summary>Some panel collects the icons on this session bus now. Looked at once,
    /// for <see cref="PlatformCapabilities.SystemTray"/>: an icon made without one still
    /// appears when a panel comes.</summary>
    public static bool IsHostPresent()
    {
        using DBusConnection? bus = DBusConnection.ConnectSession(CallTimeout);

        return bus is not null && bus.NameHasOwner(WatcherName);
    }

    /// <summary>The connection's socket, for the loop's poll; −1 once closed.</summary>
    public int FileDescriptor => _bus.FileDescriptor;

    /// <summary>Answer what the panel asked. Without data it returns at once.</summary>
    public void Poll() => _bus.ProcessIncoming();

    private void RegisterWithWatcher()
    {
        _bus.CallMethod(WatcherName, WatcherPath, WatcherName, "RegisterStatusNotifierItem", "s", [_service]);
    }

    private void OnSignal(DBusMessage signal)
    {
        // NameOwnerChanged(name, old owner, new owner): a new watcher — register with it
        if (signal is { Member: "NameOwnerChanged", Body: [WatcherName, _, string owner, ..] } &&
            owner.Length > 0)
        {
            RegisterWithWatcher();
        }
    }

    // ===== the owner's changes =====

    public void Update()
    {
        _pixmaps = null;

        _bus.Emit(ItemPath, ItemInterface, "NewIcon", string.Empty, []);
        _bus.Emit(ItemPath, ItemInterface, "NewToolTip", string.Empty, []);
        _bus.Emit(ItemPath, ItemInterface, "NewTitle", string.Empty, []);

        // the items may have changed with the rest: the panel reads the menu again
        _menu.Invalidate();
    }

    public void Dispose()
    {
        _bus.Unregister(ItemPath);
        _bus.Unregister(MenuPath);

        // the bus drops the name with the connection, and the watcher the icon
        _bus.Dispose();
        _disposed(this);
    }

    // ===== the calls =====

    public DBusReply Call(DBusMessage call)
    {
        switch (call.Interface, call.Member)
        {
            case (ItemInterface, "Activate"):
                _owner.RaiseClick();
                return DBusReply.Empty;

            // a middle click, the wheel: nothing for them; the menu is the panel's own
            case (ItemInterface, "SecondaryActivate"):
            case (ItemInterface, "Scroll"):
            case (ItemInterface, "ContextMenu"):
            case (ItemInterface, "ProvideXdgActivationToken"):
                return DBusReply.Empty;

            case (PropertiesInterface, "Get") when call.Body is [ItemInterface, string name]:
                return DBusReply.Of("v", Property(name));

            case (PropertiesInterface, "Get") when call.Body is [string other, string name]:
                throw DBusErrorException.UnknownProperty(other, name);

            case (PropertiesInterface, "GetAll") when call.Body is [string @interface]:
                return DBusReply.Of("a{sv}", @interface == ItemInterface ? AllProperties() : []);

            case (PropertiesInterface, "Set") when call.Body is [_, string name, ..]:
                throw DBusErrorException.ReadOnly(name);

            case ("org.freedesktop.DBus.Introspectable" or null, "Introspect"):
                return DBusReply.Of("s", Introspection);

            default:
                throw DBusErrorException.UnknownMethod(call);
        }
    }

    private static readonly string[] PropertyNames =
    [
        "Category", "Id", "Title", "Status", "WindowId", "IconThemePath", "Menu", "ItemIsMenu",
        "IconName", "IconPixmap", "OverlayIconName", "OverlayIconPixmap",
        "AttentionIconName", "AttentionIconPixmap", "AttentionMovieName", "ToolTip",
    ];

    private Dictionary<string, DBusVariant> AllProperties()
    {
        var all = new Dictionary<string, DBusVariant>(StringComparer.Ordinal);

        foreach (string name in PropertyNames)
            all[name] = Property(name);

        return all;
    }

    private static string AppName => Assembly.GetEntryAssembly()?.GetName().Name ?? "ZeppelinForms";

    private DBusVariant Property(string name) => name switch
    {
        "Category" => new("s", "ApplicationStatus"),
        "Id" => new("s", AppName),
        "Title" => new("s", _owner.ToolTip ?? AppName),
        "Status" => new("s", "Active"),
        "WindowId" => new("i", 0),
        "IconThemePath" => new("s", string.Empty),
        "Menu" => new("o", MenuPath),

        // false: a click activates — Click — and the menu opens on the right button
        "ItemIsMenu" => new("b", false),

        "IconName" => new("s", string.Empty),
        "IconPixmap" => new("a(iiay)", Pixmaps()),
        "OverlayIconName" => new("s", string.Empty),
        "OverlayIconPixmap" => new("a(iiay)", Array.Empty<object?[]>()),
        "AttentionIconName" => new("s", string.Empty),
        "AttentionIconPixmap" => new("a(iiay)", Array.Empty<object?[]>()),
        "AttentionMovieName" => new("s", string.Empty),

        // (icon name, icon pixmaps, title, description)
        "ToolTip" => new("(sa(iiay)ss)",
            new object?[] { string.Empty, Array.Empty<object?[]>(), _owner.ToolTip ?? AppName, string.Empty }),

        _ => throw DBusErrorException.UnknownProperty(ItemInterface, name),
    };

    /// <summary>The icon at the sizes a panel picks from: (width, height, ARGB32 in network
    /// byte order, not premultiplied). Kept until the next <see cref="Update"/>.</summary>
    private List<object?[]> Pixmaps()
    {
        if (_pixmaps is not null) return _pixmaps;

        var pixmaps = new List<object?[]>();
        var seen = new HashSet<(int, int)>();

        try
        {
            Icon icon = _owner.EffectiveIcon;

            foreach (int size in PixmapSizes)
            {
                Image image = icon.ToImage(size, size);

                // an icon file without that size gives the nearest one: once is enough
                if (seen.Add((image.Width, image.Height)))
                    pixmaps.Add([image.Width, image.Height, ToArgb(image)]);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or ZfSetupException or ArgumentException)
        {
            // no decoder or a broken icon: the panel shows its placeholder
        }

        return _pixmaps = pixmaps;
    }

    /// <summary>Premultiplied RGBA → straight ARGB, big-endian: A, R, G, B per pixel.</summary>
    private static byte[] ToArgb(Image image)
    {
        byte[] rgba = image.Pixels;
        byte[] argb = new byte[image.Width * image.Height * 4];

        for (int i = 0; i < argb.Length; i += 4)
        {
            int a = rgba[i + 3];

            argb[i] = (byte)a;

            if (a == 0) continue;

            argb[i + 1] = (byte)Math.Min(255, rgba[i] * 255 / a);
            argb[i + 2] = (byte)Math.Min(255, rgba[i + 1] * 255 / a);
            argb[i + 3] = (byte)Math.Min(255, rgba[i + 2] * 255 / a);
        }

        return argb;
    }

    private static readonly string Introspection = DBusConnection.IntrospectHeader + """
        <node>
          <interface name="org.kde.StatusNotifierItem">
            <property name="Category" type="s" access="read"/>
            <property name="Id" type="s" access="read"/>
            <property name="Title" type="s" access="read"/>
            <property name="Status" type="s" access="read"/>
            <property name="WindowId" type="i" access="read"/>
            <property name="IconThemePath" type="s" access="read"/>
            <property name="Menu" type="o" access="read"/>
            <property name="ItemIsMenu" type="b" access="read"/>
            <property name="IconName" type="s" access="read"/>
            <property name="IconPixmap" type="a(iiay)" access="read"/>
            <property name="OverlayIconName" type="s" access="read"/>
            <property name="OverlayIconPixmap" type="a(iiay)" access="read"/>
            <property name="AttentionIconName" type="s" access="read"/>
            <property name="AttentionIconPixmap" type="a(iiay)" access="read"/>
            <property name="AttentionMovieName" type="s" access="read"/>
            <property name="ToolTip" type="(sa(iiay)ss)" access="read"/>
            <method name="ContextMenu"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
            <method name="Activate"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
            <method name="SecondaryActivate"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
            <method name="Scroll"><arg name="delta" type="i" direction="in"/><arg name="orientation" type="s" direction="in"/></method>
            <signal name="NewTitle"/>
            <signal name="NewIcon"/>
            <signal name="NewAttentionIcon"/>
            <signal name="NewOverlayIcon"/>
            <signal name="NewToolTip"/>
            <signal name="NewStatus"><arg name="status" type="s"/></signal>
          </interface>
          <interface name="org.freedesktop.DBus.Properties">
            <method name="Get"><arg name="interface" type="s" direction="in"/><arg name="name" type="s" direction="in"/><arg name="value" type="v" direction="out"/></method>
            <method name="GetAll"><arg name="interface" type="s" direction="in"/><arg name="properties" type="a{sv}" direction="out"/></method>
            <method name="Set"><arg name="interface" type="s" direction="in"/><arg name="name" type="s" direction="in"/><arg name="value" type="v" direction="in"/></method>
          </interface>
          <interface name="org.freedesktop.DBus.Introspectable">
            <method name="Introspect"><arg name="xml" type="s" direction="out"/></method>
          </interface>
        </node>
        """;
}