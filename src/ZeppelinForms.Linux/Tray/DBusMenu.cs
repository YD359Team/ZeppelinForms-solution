using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Input.Commands;
using ZeppelinForms.Linux.DBus;

namespace ZeppelinForms.Linux.Tray;

/// <summary>
/// The menu of a tray icon as a dbusmenu (<c>com.canonical.dbusmenu</c>, version 3): the
/// panel draws it as its own menu from the layout the application gives, and reports a
/// click on an item back as an event.
/// </summary>
/// <remarks>
/// <para>
/// The items are the owner's <see cref="TrayIcon.Menu"/> as it is when the panel asks:
/// the layout is read from them on every GetLayout. Each item keeps the id it was given
/// first, so the panel's events find it after the menu changed around it. The root is 0.
/// </para>
/// <para>
/// The panel calls AboutToShow before it opens a menu: for the root that raises
/// <see cref="TrayIcon.MenuOpening"/>, for a submenu the item's
/// <see cref="MenuItem.Opening"/> — and the answer is always "update", since the handlers
/// may have changed anything. The panel reads the layout again and shows that.
/// </para>
/// </remarks>
internal sealed class DBusMenu(TrayIcon owner, DBusConnection bus, string path) : IDBusObject
{
    private const string MenuInterface = "com.canonical.dbusmenu";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    private const string LayoutSignature = "(ia{sv}av)";

    private readonly Dictionary<MenuItem, int> _ids = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, MenuItem> _items = [];
    private int _nextId;
    private uint _revision = 1;

    public DBusReply Call(DBusMessage call)
    {
        switch (call.Interface, call.Member)
        {
            case (MenuInterface, "GetLayout") when call.Body is [int parent, int depth, List<object?> names]:
                return new DBusReply("u" + LayoutSignature, [_revision, Layout(parent, depth, Names(names))]);

            case (MenuInterface, "GetGroupProperties") when call.Body is [List<object?> ids, List<object?> names]:
                {
                    var groups = new List<object?[]>();

                    foreach (int id in ids.OfType<int>())
                    {
                        if (id == 0 || _items.ContainsKey(id))
                            groups.Add([id, Properties(id, Names(names))]);
                    }

                    return DBusReply.Of("a(ia{sv})", groups);
                }

            case (MenuInterface, "GetProperty") when call.Body is [int id, string name]:
                return Properties(id, [name]).TryGetValue(name, out DBusVariant? value)
                    ? DBusReply.Of("v", value)
                    : throw new DBusErrorException("org.freedesktop.DBus.Error.InvalidArgs", $"No property {name} on item {id}.");

            case (MenuInterface, "Event") when call.Body is [int id, string eventId, ..]:
                Event(id, eventId);
                return DBusReply.Empty;

            case (MenuInterface, "EventGroup") when call.Body is [List<object?> events]:
                {
                    var errors = new List<int>();

                    foreach (object?[] e in events.OfType<object?[]>())
                    {
                        if (e is [int id, string eventId, ..] && !Event(id, eventId))
                            errors.Add(id);
                    }

                    return DBusReply.Of("ai", errors);
                }

            case (MenuInterface, "AboutToShow") when call.Body is [int id]:
                return DBusReply.Of("b", AboutToShow(id));

            case (MenuInterface, "AboutToShowGroup") when call.Body is [List<object?> ids]:
                {
                    var updates = new List<int>();
                    var errors = new List<int>();

                    foreach (int id in ids.OfType<int>())
                    {
                        if (id != 0 && !_items.ContainsKey(id)) errors.Add(id);
                        else if (AboutToShow(id)) updates.Add(id);
                    }

                    return new DBusReply("aiai", [updates, errors]);
                }

            case (PropertiesInterface, "Get") when call.Body is [MenuInterface, string name]:
                return DBusReply.Of("v", MenuProperty(name));

            case (PropertiesInterface, "Get") when call.Body is [string other, string name]:
                throw DBusErrorException.UnknownProperty(other, name);

            case (PropertiesInterface, "GetAll") when call.Body is [string @interface]:
                return DBusReply.Of("a{sv}", @interface == MenuInterface
                    ? new Dictionary<string, DBusVariant>
                    {
                        ["Version"] = MenuProperty("Version"),
                        ["TextDirection"] = MenuProperty("TextDirection"),
                        ["Status"] = MenuProperty("Status"),
                        ["IconThemePath"] = MenuProperty("IconThemePath"),
                    }
                    : []);

            case (PropertiesInterface, "Set") when call.Body is [_, string name, ..]:
                throw DBusErrorException.ReadOnly(name);

            case ("org.freedesktop.DBus.Introspectable" or null, "Introspect"):
                return DBusReply.Of("s", Introspection);

            default:
                throw DBusErrorException.UnknownMethod(call);
        }
    }

    private static DBusVariant MenuProperty(string name) => name switch
    {
        "Version" => new("u", 3u),
        "TextDirection" => new("s", "ltr"),
        "Status" => new("s", "normal"),
        "IconThemePath" => new("as", Array.Empty<string>()),
        _ => throw DBusErrorException.UnknownProperty(MenuInterface, name),
    };

    private static HashSet<string> Names(List<object?> names) => [.. names.OfType<string>()];

    // ===== the items =====

    private int Id(MenuItem item)
    {
        if (!_ids.TryGetValue(item, out int id))
        {
            id = ++_nextId;
            _ids[item] = id;
        }

        _items[id] = item;

        return id;
    }

    private List<MenuItem> Children(int id)
    {
        if (id == 0) return owner.Menu;

        return _items.TryGetValue(id, out MenuItem? item)
            ? item.Items
            : throw new DBusErrorException("org.freedesktop.DBus.Error.InvalidArgs", $"No menu item {id}.");
    }

    /// <summary>The node of <paramref name="id"/>: (id, properties, children), the children
    /// down to <paramref name="depth"/> levels — −1, all of them.</summary>
    private object?[] Layout(int id, int depth, HashSet<string> names)
    {
        List<MenuItem> children = Children(id);
        var nodes = new List<DBusVariant>();

        if (depth != 0)
        {
            foreach (MenuItem child in children)
                nodes.Add(new DBusVariant(LayoutSignature, Layout(Id(child), depth - 1, names)));
        }

        return [id, Properties(id, names), nodes];
    }

    /// <summary>The item's properties, those of <paramref name="names"/> — all when empty.
    /// The defaults of the specification are left out, as it asks: a standard item,
    /// visible, enabled, no toggle.</summary>
    private Dictionary<string, DBusVariant> Properties(int id, ICollection<string> names)
    {
        var properties = new Dictionary<string, DBusVariant>(StringComparer.Ordinal);

        if (id == 0)
        {
            properties["children-display"] = new("s", "submenu");
        }
        else if (_items.TryGetValue(id, out MenuItem? item))
        {
            if (item.IsSeparator)
            {
                properties["type"] = new("s", "separator");
            }
            else
            {
                // '_' marks the access key in dbusmenu; a plain one is doubled
                properties["label"] = new("s", item.Text.Replace("_", "__", StringComparison.Ordinal));

                if (!item.IsEffectivelyEnabled)
                    properties["enabled"] = new("b", false);

                if (item.IsCheckable || item.Command is ToggleCommand || item.IsEffectivelyChecked)
                {
                    properties["toggle-type"] = new("s", "checkmark");
                    properties["toggle-state"] = new("i", item.IsEffectivelyChecked ? 1 : 0);
                }

                if (item.Items.Count > 0)
                    properties["children-display"] = new("s", "submenu");
            }
        }

        if (names.Count > 0)
        {
            foreach (string name in properties.Keys.ToArray())
            {
                if (!names.Contains(name)) properties.Remove(name);
            }
        }

        return properties;
    }

    // ===== the panel's events =====

    /// <summary>"clicked" runs the item as a click in a menu bar does; "opened",
    /// "closed" and "hovered" mean nothing here. False — no such item.</summary>
    private bool Event(int id, string eventId)
    {
        if (id == 0) return true;

        if (!_items.TryGetValue(id, out MenuItem? item)) return false;

        if (eventId == "clicked" && item.Items.Count == 0)
            item.Invoke();

        return true;
    }

    /// <summary>A menu is about to open: its Opening, and the layout is new.</summary>
    private bool AboutToShow(int id)
    {
        if (id == 0) owner.RaiseMenuOpening();
        else if (_items.TryGetValue(id, out MenuItem? item)) item.RaiseOpening();
        else return false;

        _revision++;

        return true;
    }

    /// <summary>The layout changed without the panel asking: it reads it again.</summary>
    public void Invalidate() =>
        bus.Emit(path, MenuInterface, "LayoutUpdated", "ui", [++_revision, 0]);

    private static readonly string Introspection = DBusConnection.IntrospectHeader + """
        <node>
          <interface name="com.canonical.dbusmenu">
            <property name="Version" type="u" access="read"/>
            <property name="TextDirection" type="s" access="read"/>
            <property name="Status" type="s" access="read"/>
            <property name="IconThemePath" type="as" access="read"/>
            <method name="GetLayout">
              <arg type="i" name="parentId" direction="in"/>
              <arg type="i" name="recursionDepth" direction="in"/>
              <arg type="as" name="propertyNames" direction="in"/>
              <arg type="u" name="revision" direction="out"/>
              <arg type="(ia{sv}av)" name="layout" direction="out"/>
            </method>
            <method name="GetGroupProperties">
              <arg type="ai" name="ids" direction="in"/>
              <arg type="as" name="propertyNames" direction="in"/>
              <arg type="a(ia{sv})" name="properties" direction="out"/>
            </method>
            <method name="GetProperty">
              <arg type="i" name="id" direction="in"/>
              <arg type="s" name="name" direction="in"/>
              <arg type="v" name="value" direction="out"/>
            </method>
            <method name="Event">
              <arg type="i" name="id" direction="in"/>
              <arg type="s" name="eventId" direction="in"/>
              <arg type="v" name="data" direction="in"/>
              <arg type="u" name="timestamp" direction="in"/>
            </method>
            <method name="EventGroup">
              <arg type="a(isvu)" name="events" direction="in"/>
              <arg type="ai" name="idErrors" direction="out"/>
            </method>
            <method name="AboutToShow">
              <arg type="i" name="id" direction="in"/>
              <arg type="b" name="needUpdate" direction="out"/>
            </method>
            <method name="AboutToShowGroup">
              <arg type="ai" name="ids" direction="in"/>
              <arg type="ai" name="updatesNeeded" direction="out"/>
              <arg type="ai" name="idErrors" direction="out"/>
            </method>
            <signal name="ItemsPropertiesUpdated">
              <arg type="a(ia{sv})" name="updatedProps"/>
              <arg type="a(ias)" name="removedProps"/>
            </signal>
            <signal name="LayoutUpdated">
              <arg type="u" name="revision"/>
              <arg type="i" name="parent"/>
            </signal>
            <signal name="ItemActivationRequested">
              <arg type="i" name="id"/>
              <arg type="u" name="timestamp"/>
            </signal>
          </interface>
          <interface name="org.freedesktop.DBus.Properties">
            <method name="Get"><arg name="interface" type="s" direction="in"/><arg name="name" type="s" direction="in"/><arg name="value" type="v" direction="out"/></method>
            <method name="GetAll"><arg name="interface" type="s" direction="in"/><arg name="properties" type="a{sv}" direction="out"/></method>
          </interface>
          <interface name="org.freedesktop.DBus.Introspectable">
            <method name="Introspect"><arg name="xml" type="s" direction="out"/></method>
          </interface>
        </node>
        """;
}