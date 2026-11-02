using System.Globalization;
using System.Runtime.CompilerServices;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Linux.DBus;

namespace ZeppelinForms.Linux.Accessibility;

/// <summary>Where the windows of the forms are, for the Component interface: the client
/// area's corner on the screen and the window's scale.</summary>
internal interface IAtSpiWindows
{
    /// <summary>Device pixels per logical unit of the form's window; 1 without one.</summary>
    float Scale(Form form);

    /// <summary>The client area's top-left corner on the screen, in device pixels. False —
    /// the platform can't know it: Wayland tells no window where it is, and coordinates
    /// on the screen are then given as coordinates in the window.</summary>
    bool TryGetScreenOrigin(Form form, out int x, out int y);
}

/// <summary>The application's accessibility tree on the AT-SPI bus: the application at the
/// root path, its forms under it, and every peer at a path of its own — answered from the
/// peers as they are now, and their changes sent as AT-SPI events.</summary>
/// <remarks>
/// <para>
/// One subtree on the connection serves them all: a peer gets its path the first time
/// it is handed out, by a number, and is found by it while it lives. The path is kept
/// as weakly as the peer is — a peer the core lets go takes its path with it, and a
/// client asking for it gets UnknownObject, which AT-SPI clients read as "defunct".
/// </para>
/// <para>
/// The core's events are listened to only once a client has asked something: until a
/// screen reader comes, no peer is made and nothing is sent. Which state changed, which
/// text was inserted, which child came are not told by the core: the bridge remembers
/// what it last told a client and sends the difference.
/// </para>
/// <para>
/// Everything runs on the UI thread: the platform's loop reads the bus there.
/// </para>
/// </remarks>
internal sealed class AtSpiTree : IDBusObject
{
    private static readonly Dictionary<string, object?> NoProperties = [];

    private readonly string _busName;
    private readonly Action<string, string, string, string, IReadOnlyList<object?>> _emit;
    private readonly IAtSpiWindows _windows;

    private readonly ConditionalWeakTable<AccessibilityPeer, string> _paths = new();
    private readonly Dictionary<string, WeakReference<AccessibilityPeer>> _byPath = new(StringComparer.Ordinal);
    private int _nextId;
    private int _sweepAt = 1024;

    /// <summary>What the clients were last told of a peer: its states, its text, its
    /// children — what a change is told against.</summary>
    private readonly ConditionalWeakTable<AccessibilityPeer, Told> _told = new();

    private bool _listening;
    private AccessibilityPeer? _focused;

    // the forms the clients were last told of, and the active one
    private readonly List<Form> _forms = [];
    private Form? _activeForm;

    /// <summary>The desktop the application is embedded in: the registry's root. The
    /// application's parent.</summary>
    public (string Name, string Path) Desktop { get; set; } = (string.Empty, AtSpi.NullPath);

    /// <summary>The id the registry gives the application, through the Application
    /// interface's Id property.</summary>
    public int ApplicationId { get; private set; }

    public AtSpiTree(
        string busName,
        Action<string, string, string, string, IReadOnlyList<object?>> emit,
        IAtSpiWindows windows)
    {
        _busName = busName;
        _emit = emit;
        _windows = windows;
    }

    /// <summary>Whether a client has asked something, and the core's events are told.</summary>
    public bool IsListening => _listening;

    // ===== paths =====

    /// <summary>A peer's reference: the bus name and its path. Null is the null object.</summary>
    public object?[] Reference(AccessibilityPeer? peer) =>
        peer is null ? [string.Empty, AtSpi.NullPath] : [_busName, PathOf(peer)];

    private object?[] RootReference => [_busName, AtSpi.RootPath];

    public string PathOf(AccessibilityPeer peer) =>
        _paths.GetValue(peer, p =>
        {
            string path = AtSpi.AccessiblePrefix + "/" + (++_nextId).ToString(CultureInfo.InvariantCulture);
            _byPath[path] = new WeakReference<AccessibilityPeer>(p);

            Sweep();

            return path;
        });

    /// <summary>Drop the paths of the peers that are gone, now and then: the table
    /// would otherwise grow by every row a list ever showed.</summary>
    private void Sweep()
    {
        if (_byPath.Count < _sweepAt) return;

        foreach ((string path, WeakReference<AccessibilityPeer> reference) in _byPath.ToArray())
            if (!reference.TryGetTarget(out _))
                _byPath.Remove(path);

        _sweepAt = Math.Max(1024, _byPath.Count * 2);
    }

    private AccessibilityPeer? PeerAt(string path) =>
        _byPath.TryGetValue(path, out WeakReference<AccessibilityPeer>? reference) &&
        reference.TryGetTarget(out AccessibilityPeer? peer)
            ? peer
            : null;

    // ===== calls =====

    public DBusReply Call(DBusMessage call)
    {
        string path = call.Path ?? string.Empty;

        if (path == AtSpi.CachePath)
            return CacheCall(call);

        if (path == AtSpi.RootPath)
            return RootCall(call);

        // a client is here: from now on the core's changes are told
        StartListening();

        if (PeerAt(path) is not { } peer)
            throw new DBusErrorException("org.freedesktop.DBus.Error.UnknownObject", $"No accessible object at {path}.");

        return call.Interface switch
        {
            AtSpi.AccessibleInterface => AccessibleCall(call, peer),
            AtSpi.ComponentInterface => ComponentCall(call, peer),
            AtSpi.ActionInterface => ActionCall(call, peer),
            AtSpi.ValueInterface => ValueCall(call, peer),
            AtSpi.TextInterface => TextCall(call, peer),
            AtSpi.PropertiesInterface => PropertiesCall(call, peer),
            _ => throw DBusErrorException.UnknownMethod(call),
        };
    }

    /// <summary>The cache of AT-SPI 2.0's clients: they ask it for the whole tree at once.
    /// Nothing is cached — the clients ask the objects themselves.</summary>
    private static DBusReply CacheCall(DBusMessage call) => call.Member switch
    {
        "GetItems" => DBusReply.Of("a((so)(so)(so)iiassusau)", Array.Empty<object>()),
        _ => throw DBusErrorException.UnknownMethod(call),
    };

    // ===== the application =====

    private IReadOnlyList<Form> Forms() =>
        [.. Form.OpenForms.Where(form => form.PlatformWindow is not null && form.Visible)];

    private DBusReply RootCall(DBusMessage call)
    {
        if (call.Interface == AtSpi.PropertiesInterface)
        {
            if (call.Member == "Set" && call.Body is [AtSpi.ApplicationInterface, "Id", DBusVariant { Value: int id }, ..])
            {
                ApplicationId = id;
                return DBusReply.Empty;
            }

            return PropertiesReply(call, RootProperty);
        }

        if (call.Interface == AtSpi.ApplicationInterface)
        {
            return call.Member switch
            {
                "GetLocale" => DBusReply.Of("s", Locale()),
                _ => throw DBusErrorException.UnknownMethod(call),
            };
        }

        if (call.Interface != AtSpi.AccessibleInterface)
            throw DBusErrorException.UnknownMethod(call);

        // a client reads the tree: from now on the core's changes are told. Not on the
        // registry's own calls above — they come for every application, read or not
        StartListening();

        IReadOnlyList<Form> forms = Forms();

        return call.Member switch
        {
            "GetChildAtIndex" when call.Body is [int index, ..] =>
                DBusReply.Of("(so)", index >= 0 && index < forms.Count
                    ? Reference(forms[index].GetAccessibilityPeer())
                    : Reference(null)),
            "GetChildren" => DBusReply.Of("a(so)", forms.Select(form => Reference(form.GetAccessibilityPeer())).ToList()),
            "GetIndexInParent" => DBusReply.Of("i", -1),
            "GetRelationSet" => DBusReply.Of("a(ua(so))", Array.Empty<object>()),
            "GetRole" => DBusReply.Of("u", AtSpi.RoleApplication),
            "GetRoleName" or "GetLocalizedRoleName" => DBusReply.Of("s", AtSpi.RoleName(AtSpi.RoleApplication)),
            "GetState" => DBusReply.Of("au", AtSpi.Words(0)),
            "GetAttributes" => DBusReply.Of("a{ss}", new Dictionary<string, string> { ["toolkit"] = "ZeppelinForms" }),
            "GetApplication" => DBusReply.Of("(so)", RootReference),
            "GetInterfaces" => DBusReply.Of("as", new[] { AtSpi.AccessibleInterface, AtSpi.ApplicationInterface }),
            _ => throw DBusErrorException.UnknownMethod(call),
        };
    }

    private DBusVariant? RootProperty(string @interface, string name) => (@interface, name) switch
    {
        (AtSpi.AccessibleInterface, "Name") => new("s", App.DisplayName),
        (AtSpi.AccessibleInterface, "Description") => new("s", string.Empty),
        (AtSpi.AccessibleInterface, "Parent") => new("(so)", new object?[] { Desktop.Name, Desktop.Path }),
        (AtSpi.AccessibleInterface, "ChildCount") => new("i", Forms().Count),
        (AtSpi.AccessibleInterface, "Locale") => new("s", Locale()),
        (AtSpi.AccessibleInterface, "AccessibleId") => new("s", string.Empty),
        (AtSpi.ApplicationInterface, "ToolkitName") => new("s", "ZeppelinForms"),
        (AtSpi.ApplicationInterface, "Version") => new("s", typeof(Form).Assembly.GetName().Version?.ToString() ?? string.Empty),
        (AtSpi.ApplicationInterface, "AtspiVersion") => new("s", "2.1"),
        (AtSpi.ApplicationInterface, "Id") => new("i", ApplicationId),
        _ => null,
    };

    private static string Locale() => CultureInfo.CurrentUICulture.Name.Replace('-', '_');

    // ===== Accessible =====

    private DBusReply AccessibleCall(DBusMessage call, AccessibilityPeer peer)
    {
        switch (call.Member)
        {
            case "GetChildAtIndex" when call.Body is [int index, ..]:
                {
                    IReadOnlyList<AccessibilityPeer> children = ChildrenOf(peer);

                    return DBusReply.Of("(so)", Reference(index >= 0 && index < children.Count ? children[index] : null));
                }

            case "GetChildren":
                return DBusReply.Of("a(so)", ChildrenOf(peer).Select(Reference).ToList());

            case "GetIndexInParent":
                return DBusReply.Of("i", IndexInParent(peer));

            case "GetRelationSet":
                return DBusReply.Of("a(ua(so))", Array.Empty<object>());

            case "GetRole":
                return DBusReply.Of("u", RoleOf(peer));

            case "GetRoleName" or "GetLocalizedRoleName":
                return DBusReply.Of("s", AtSpi.RoleName(RoleOf(peer)));

            case "GetState":
                return DBusReply.Of("au", AtSpi.Words(StatesOf(peer, remember: true)));

            case "GetAttributes":
                return DBusReply.Of("a{ss}", Attributes(peer));

            case "GetApplication":
                return DBusReply.Of("(so)", RootReference);

            case "GetInterfaces":
                return DBusReply.Of("as", Interfaces(peer));

            default:
                throw DBusErrorException.UnknownMethod(call);
        }
    }

    private DBusVariant? AccessibleProperty(AccessibilityPeer peer, string name) => name switch
    {
        "Name" => new("s", peer.Name),
        "Description" => new("s", peer.Description ?? string.Empty),
        "HelpText" => new("s", peer.Description ?? string.Empty),
        "Parent" => new("(so)", ParentReference(peer)),
        "ChildCount" => new("i", ChildrenOf(peer).Count),
        "Locale" => new("s", Locale()),
        "AccessibleId" => new("s", peer.Element?.Name ?? string.Empty),
        _ => null,
    };

    /// <summary>The children, remembered as told: a later change of them is told
    /// as the children added and removed since.</summary>
    private IReadOnlyList<AccessibilityPeer> ChildrenOf(AccessibilityPeer peer)
    {
        IReadOnlyList<AccessibilityPeer> children = peer.Children;

        TellOf(peer).Children = [.. children.Select(PathOf)];

        return children;
    }

    private object?[] ParentReference(AccessibilityPeer peer) =>
        peer.Parent is { } parent ? Reference(parent) : RootReference;

    private int IndexInParent(AccessibilityPeer peer)
    {
        if (peer.Parent is { } parent)
        {
            IReadOnlyList<AccessibilityPeer> siblings = parent.Children;

            for (int i = 0; i < siblings.Count; i++)
                if (ReferenceEquals(siblings[i], peer))
                    return i;

            return -1;
        }

        IReadOnlyList<Form> forms = Forms();

        for (int i = 0; i < forms.Count; i++)
            if (ReferenceEquals(forms[i].GetAccessibilityPeer(), peer))
                return i;

        return -1;
    }

    private static uint RoleOf(AccessibilityPeer peer) => AtSpi.Role(peer.Role, peer.States);

    /// <summary>The state set; a form's window is "active" while it is. Remembered when
    /// <paramref name="remember"/>: a later change is told against what a client saw.</summary>
    private ulong StatesOf(AccessibilityPeer peer, bool remember)
    {
        ulong states = AtSpi.States(peer.Role, peer.States);

        if (peer is FormPeer && peer.Form is { IsActive: true })
            states |= AtSpi.Bit(AtSpi.StateActive);

        if (remember) TellOf(peer).States = states;

        return states;
    }

    private static Dictionary<string, string> Attributes(AccessibilityPeer peer)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["toolkit"] = "ZeppelinForms" };

        if (peer.HeadingLevel > 0) attributes["level"] = peer.HeadingLevel.ToString(CultureInfo.InvariantCulture);
        else if (peer.Level > 0) attributes["level"] = peer.Level.ToString(CultureInfo.InvariantCulture);

        if (peer.Position is { } position)
        {
            attributes["posinset"] = position.Index.ToString(CultureInfo.InvariantCulture);
            attributes["setsize"] = position.Count.ToString(CultureInfo.InvariantCulture);
        }

        if (peer.LiveSetting != AccessibilityLiveSetting.Off)
        {
            string live = peer.LiveSetting == AccessibilityLiveSetting.Assertive ? "assertive" : "polite";
            attributes["live"] = live;
            attributes["container-live"] = live;
        }

        if (peer.Element?.Name is { Length: > 0 } id) attributes["id"] = id;

        return attributes;
    }

    private static List<string> Interfaces(AccessibilityPeer peer)
    {
        var interfaces = new List<string> { AtSpi.AccessibleInterface, AtSpi.ComponentInterface };

        if (AtSpi.Actions(peer.Actions).Count > 0) interfaces.Add(AtSpi.ActionInterface);
        if (peer.Range is not null) interfaces.Add(AtSpi.ValueInterface);
        if (TextOf(peer) is not null) interfaces.Add(AtSpi.TextInterface);

        return interfaces;
    }

    // ===== Component =====

    private DBusReply ComponentCall(DBusMessage call, AccessibilityPeer peer)
    {
        switch (call.Member)
        {
            case "GetExtents" when call.Body is [uint coords, ..]:
                {
                    (int x, int y, int width, int height) = Extents(peer, coords);
                    return DBusReply.Of("(iiii)", new object[] { x, y, width, height });
                }

            case "GetPosition" when call.Body is [uint coords, ..]:
                {
                    (int x, int y, _, _) = Extents(peer, coords);
                    return new DBusReply("ii", [x, y]);
                }

            case "GetSize":
                {
                    (_, _, int width, int height) = Extents(peer, AtSpi.CoordWindow);
                    return new DBusReply("ii", [width, height]);
                }

            case "Contains" when call.Body is [int x, int y, uint coords, ..]:
                {
                    (int left, int top, int width, int height) = Extents(peer, coords);
                    return DBusReply.Of("b", x >= left && y >= top && x < left + width && y < top + height);
                }

            case "GetAccessibleAtPoint" when call.Body is [int x, int y, uint coords, ..]:
                return DBusReply.Of("(so)", Reference(AtPoint(peer, x, y, coords)));

            case "GetLayer":
                return DBusReply.Of("u", peer is FormPeer ? AtSpi.LayerWindow : AtSpi.LayerWidget);

            case "GetMDIZOrder":
                return DBusReply.Of("n", (short)0);

            case "GrabFocus":
                return DBusReply.Of("b", peer.Focus());

            case "GetAlpha":
                return DBusReply.Of("d", 1d);

            case "ScrollTo":
                return DBusReply.Of("b", peer.ScrollIntoView());

            case "ScrollToPoint" or "SetExtents" or "SetPosition" or "SetSize":
                return DBusReply.Of("b", false);

            default:
                throw DBusErrorException.UnknownMethod(call);
        }
    }

    /// <summary>A peer's bounds in device pixels: on the screen, in its window, or
    /// against its parent's corner.</summary>
    private (int X, int Y, int Width, int Height) Extents(AccessibilityPeer peer, uint coords)
    {
        Rectangle bounds = peer.Bounds;

        if (peer.Form is not { } form) return (0, 0, 0, 0);

        float scale = _windows.Scale(form);
        float x = bounds.X * scale;
        float y = bounds.Y * scale;

        if (coords == AtSpi.CoordScreen && _windows.TryGetScreenOrigin(form, out int originX, out int originY))
        {
            x += originX;
            y += originY;
        }
        else if (coords == AtSpi.CoordParent && peer.Parent is { } parent)
        {
            x -= parent.Bounds.X * scale;
            y -= parent.Bounds.Y * scale;
        }

        return ((int)MathF.Round(x), (int)MathF.Round(y),
            (int)MathF.Round(bounds.Width * scale), (int)MathF.Round(bounds.Height * scale));
    }

    /// <summary>The deepest peer under a point, from this one down; null when the
    /// point is outside it.</summary>
    private AccessibilityPeer? AtPoint(AccessibilityPeer peer, int x, int y, uint coords)
    {
        (int left, int top, int width, int height) = Extents(peer, coords);

        if (x < left || y < top || x >= left + width || y >= top + height) return null;

        // the last child drawn is on top: overlays over the content
        IReadOnlyList<AccessibilityPeer> children = peer.Children;

        for (int i = children.Count - 1; i >= 0; i--)
            if (AtPoint(children[i], x, y, coords) is { } hit)
                return hit;

        return peer;
    }

    // ===== Action =====

    private static DBusReply ActionCall(DBusMessage call, AccessibilityPeer peer)
    {
        IReadOnlyList<(AccessibilityActions Action, string Name)> actions = AtSpi.Actions(peer.Actions);
        string keys = AtSpi.KeyBinding(peer.AccessKey);

        bool Valid(int index) => index >= 0 && index < actions.Count;

        switch (call.Member)
        {
            case "GetName" or "GetLocalizedName" when call.Body is [int index, ..]:
                return DBusReply.Of("s", Valid(index) ? actions[index].Name : string.Empty);

            case "GetDescription" when call.Body is [int _, ..]:
                return DBusReply.Of("s", string.Empty);

            case "GetKeyBinding" when call.Body is [int index, ..]:
                return DBusReply.Of("s", index == 0 ? keys : string.Empty);

            case "GetActions":
                return DBusReply.Of("a(sss)", actions
                    .Select((action, i) => new object[] { action.Name, string.Empty, i == 0 ? keys : string.Empty })
                    .ToList());

            case "DoAction" when call.Body is [int index, ..]:
                {
                    if (!Valid(index)) return DBusReply.Of("b", false);

                    bool done = actions[index].Action switch
                    {
                        AccessibilityActions.Invoke => peer.Invoke(),
                        AccessibilityActions.Toggle => peer.Toggle(),
                        AccessibilityActions.Select => peer.Select(),
                        AccessibilityActions.Expand => peer.Expand(),
                        AccessibilityActions.Collapse => peer.Collapse(),
                        _ => false,
                    };

                    return DBusReply.Of("b", done);
                }

            default:
                throw DBusErrorException.UnknownMethod(call);
        }
    }

    private static DBusVariant? ActionProperty(AccessibilityPeer peer, string name) => name switch
    {
        "NActions" => new("i", AtSpi.Actions(peer.Actions).Count),
        _ => null,
    };

    // ===== Value =====

    private static DBusReply ValueCall(DBusMessage call, AccessibilityPeer peer) =>
        throw DBusErrorException.UnknownMethod(call);

    private static DBusVariant? ValueProperty(AccessibilityPeer peer, string name)
    {
        if (peer.Range is not { } range) return null;

        return name switch
        {
            "MinimumValue" => new("d", range.Minimum),
            "MaximumValue" => new("d", range.Maximum),
            "MinimumIncrement" => new("d", range.SmallChange),
            "CurrentValue" => new("d", range.Value),
            "Text" => new("s", peer.Value ?? range.Value.ToString(CultureInfo.CurrentCulture)),
            _ => null,
        };
    }

    // ===== Text =====

    /// <summary>The text the Text interface reads: a field's value, the words of static
    /// text, a heading, a link. Null — no text to read by pieces.</summary>
    private static string? TextOf(AccessibilityPeer peer) => peer.Role switch
    {
        AccessibilityRole.TextBox or AccessibilityRole.SpinButton => peer.Value ?? string.Empty,
        AccessibilityRole.Text or AccessibilityRole.Heading or AccessibilityRole.Link => peer.Name,
        _ => null,
    };

    private DBusReply TextCall(DBusMessage call, AccessibilityPeer peer)
    {
        string text = TextOf(peer) ?? string.Empty;

        static object[] Piece((string Text, int Start, int End) piece) => [piece.Text, piece.Start, piece.End];

        switch (call.Member)
        {
            case "GetText" when call.Body is [int start, int end, ..]:
                return DBusReply.Of("s", AtSpiText.Slice(text, start, end));

            case "GetStringAtOffset" when call.Body is [int offset, uint granularity, ..]:
                return new DBusReply("sii", Piece(AtSpiText.At(text, offset, granularity)));

            case "GetTextAtOffset" when call.Body is [int offset, uint boundary, ..]:
                return new DBusReply("sii", Piece(AtSpiText.AtBoundary(text, offset, boundary)));

            case "GetTextBeforeOffset" when call.Body is [int offset, uint boundary, ..]:
                return new DBusReply("sii", Piece(AtSpiText.Before(text, offset, boundary)));

            case "GetTextAfterOffset" when call.Body is [int offset, uint boundary, ..]:
                return new DBusReply("sii", Piece(AtSpiText.After(text, offset, boundary)));

            case "GetCharacterAtOffset" when call.Body is [int offset, ..]:
                return DBusReply.Of("i", offset >= 0 && offset < text.Length ? (int)text[offset] : 0);

            case "GetNSelections":
                return DBusReply.Of("i", peer.SelectionLength > 0 && peer.SelectionStart >= 0 ? 1 : 0);

            case "GetSelection" when call.Body is [int _, ..]:
                return peer.SelectionLength > 0 && peer.SelectionStart >= 0
                    ? new DBusReply("ii", [peer.SelectionStart, peer.SelectionStart + peer.SelectionLength])
                    : new DBusReply("ii", [0, 0]);

            case "GetCharacterExtents" or "GetRangeExtents":
                {
                    uint coords = call.Body.Count > 0 && call.Body[^1] is uint type ? type : AtSpi.CoordScreen;
                    (int x, int y, int width, int height) = Extents(peer, coords);

                    return new DBusReply("iiii", [x, y, width, height]);
                }

            case "GetOffsetAtPoint":
                return DBusReply.Of("i", -1);

            case "GetBoundedRanges":
                return DBusReply.Of("a(iisv)", Array.Empty<object>());

            case "GetAttributeRun" or "GetAttributes" when call.Body is [int offset, ..]:
                return new DBusReply("a{ss}ii", [NoTextAttributes, 0, text.Length]);

            case "GetDefaultAttributes" or "GetDefaultAttributeSet":
                return DBusReply.Of("a{ss}", NoTextAttributes);

            case "GetAttributeValue":
                return DBusReply.Of("s", string.Empty);

            case "SetCaretOffset" or "AddSelection" or "RemoveSelection" or "SetSelection" or "ScrollSubstringTo" or "ScrollSubstringToPoint":
                return DBusReply.Of("b", false);

            default:
                throw DBusErrorException.UnknownMethod(call);
        }
    }

    private static readonly Dictionary<string, string> NoTextAttributes = [];

    private static DBusVariant? TextProperty(AccessibilityPeer peer, string name) => name switch
    {
        "CharacterCount" => new("i", (TextOf(peer) ?? string.Empty).Length),
        "CaretOffset" => new("i", peer.CaretIndex),
        _ => null,
    };

    // ===== Properties =====

    private DBusReply PropertiesCall(DBusMessage call, AccessibilityPeer peer)
    {
        if (call.Member == "Set" &&
            call.Body is [AtSpi.ValueInterface, "CurrentValue", DBusVariant { Value: double value }, ..])
        {
            peer.SetRangeValue(value);
            return DBusReply.Empty;
        }

        return PropertiesReply(call, (@interface, name) => @interface switch
        {
            AtSpi.AccessibleInterface => AccessibleProperty(peer, name),
            AtSpi.ActionInterface => ActionProperty(peer, name),
            AtSpi.ValueInterface => ValueProperty(peer, name),
            AtSpi.TextInterface => TextProperty(peer, name),
            _ => null,
        });
    }

    private static readonly Dictionary<string, string[]> PropertyNames = new(StringComparer.Ordinal)
    {
        [AtSpi.AccessibleInterface] = ["Name", "Description", "Parent", "ChildCount", "Locale", "AccessibleId", "HelpText"],
        [AtSpi.ActionInterface] = ["NActions"],
        [AtSpi.ValueInterface] = ["MinimumValue", "MaximumValue", "MinimumIncrement", "CurrentValue", "Text"],
        [AtSpi.TextInterface] = ["CharacterCount", "CaretOffset"],
        [AtSpi.ApplicationInterface] = ["ToolkitName", "Version", "AtspiVersion", "Id"],
    };

    /// <summary>Get and GetAll of org.freedesktop.DBus.Properties over a lookup.</summary>
    private static DBusReply PropertiesReply(DBusMessage call, Func<string, string, DBusVariant?> property)
    {
        switch (call.Member)
        {
            case "Get" when call.Body is [string @interface, string name, ..]:
                return property(@interface, name) is { } value
                    ? DBusReply.Of("v", value)
                    : throw DBusErrorException.UnknownProperty(@interface, name);

            case "GetAll" when call.Body is [string @interface, ..]:
                {
                    var all = new Dictionary<string, DBusVariant>(StringComparer.Ordinal);

                    if (PropertyNames.TryGetValue(@interface, out string[]? names))
                        foreach (string name in names)
                            if (property(@interface, name) is { } found)
                                all[name] = found;

                    return DBusReply.Of("a{sv}", all);
                }

            case "Set" when call.Body is [string @interface, string name, ..]:
                throw DBusErrorException.ReadOnly(@interface + "." + name);

            default:
                throw DBusErrorException.UnknownMethod(call);
        }
    }

    // ===== events =====

    private sealed class Told
    {
        public ulong? States;
        public string? Text;
        public List<string>? Children;
    }

    private Told TellOf(AccessibilityPeer peer) => _told.GetValue(peer, _ => new Told());

    private void StartListening()
    {
        if (_listening) return;

        _listening = true;

        AccessibilityEvents.FocusChanged += OnFocusChanged;
        AccessibilityEvents.PropertyChanged += OnPropertyChanged;
        AccessibilityEvents.StructureChanged += OnStructureChanged;
        AccessibilityEvents.Announcement += OnAnnouncement;

        _forms.Clear();
        _forms.AddRange(Forms());
        _activeForm = _forms.FirstOrDefault(form => form.IsActive);
    }

    /// <summary>The bridge is going: the core's events are let go.</summary>
    public void StopListening()
    {
        if (!_listening) return;

        _listening = false;

        AccessibilityEvents.FocusChanged -= OnFocusChanged;
        AccessibilityEvents.PropertyChanged -= OnPropertyChanged;
        AccessibilityEvents.StructureChanged -= OnStructureChanged;
        AccessibilityEvents.Announcement -= OnAnnouncement;

        _focused = null;
        _forms.Clear();
        _activeForm = null;
    }

    /// <summary>An Event.Object signal: (detail, detail1, detail2, any_data, properties).</summary>
    private void Emit(AccessibilityPeer? peer, string @interface, string member, string detail, int detail1, int detail2, DBusVariant data) =>
        _emit(peer is null ? AtSpi.RootPath : PathOf(peer), @interface, member, "siiva{sv}",
            [detail, detail1, detail2, data, NoProperties]);

    private static DBusVariant NoData => new("i", 0);

    private void EmitState(AccessibilityPeer peer, int state, bool on) =>
        Emit(peer, AtSpi.ObjectEvents, "StateChanged", AtSpi.StateName(state) ?? string.Empty, on ? 1 : 0, 0, NoData);

    private void OnFocusChanged(AccessibilityPeer peer)
    {
        if (ReferenceEquals(peer, _focused)) return;

        if (_focused is { } previous)
        {
            EmitState(previous, AtSpi.StateFocused, false);

            if (_told.TryGetValue(previous, out Told? told) && told.States is { } states)
                told.States = states & ~AtSpi.Bit(AtSpi.StateFocused);
        }

        _focused = peer;

        EmitState(peer, AtSpi.StateFocused, true);
        StatesOf(peer, remember: true);
    }

    private void OnPropertyChanged(AccessibilityPeer peer, AccessibilityProperty property)
    {
        switch (property)
        {
            case AccessibilityProperty.Name:
                Emit(peer, AtSpi.ObjectEvents, "PropertyChange", "accessible-name", 0, 0, new("s", peer.Name));
                break;

            case AccessibilityProperty.Description:
                Emit(peer, AtSpi.ObjectEvents, "PropertyChange", "accessible-description", 0, 0,
                    new("s", peer.Description ?? string.Empty));
                break;

            case AccessibilityProperty.Value when TextOf(peer) is { } text && peer.Role is AccessibilityRole.TextBox:
                TellTextChange(peer, text);
                break;

            case AccessibilityProperty.Value or AccessibilityProperty.Range when peer.Range is { } range:
                Emit(peer, AtSpi.ObjectEvents, "PropertyChange", "accessible-value", 0, 0, new("d", range.Value));
                break;

            case AccessibilityProperty.Value:
                Emit(peer, AtSpi.ObjectEvents, "PropertyChange", "accessible-value", 0, 0, new("s", peer.Value ?? string.Empty));
                break;

            case AccessibilityProperty.States:
                TellStateChanges(peer);
                break;

            case AccessibilityProperty.Caret when peer.CaretIndex >= 0:
                Emit(peer, AtSpi.ObjectEvents, "TextCaretMoved", string.Empty, peer.CaretIndex, 0, NoData);
                Emit(peer, AtSpi.ObjectEvents, "TextSelectionChanged", string.Empty, 0, 0, NoData);
                break;
        }

        // a live element is read out on its own when its name or value changes
        if (peer.LiveSetting != AccessibilityLiveSetting.Off &&
            property is AccessibilityProperty.Name or AccessibilityProperty.Value &&
            (property == AccessibilityProperty.Name ? peer.Name : peer.Value) is { Length: > 0 } spoken)
        {
            Announce(peer, spoken, peer.LiveSetting);
        }
    }

    /// <summary>A field's text changed: what was deleted and what was inserted, against
    /// the text last told — a screen reader echoes the typed characters from it.</summary>
    private void TellTextChange(AccessibilityPeer peer, string text)
    {
        Told told = TellOf(peer);
        string before = told.Text ?? string.Empty;
        told.Text = text;

        if (AtSpiText.Difference(before, text) is not { } change) return;

        if (change.Deleted.Length > 0)
        {
            Emit(peer, AtSpi.ObjectEvents, "TextChanged", "delete", change.Offset, change.Deleted.Length,
                new("s", change.Deleted));
        }

        if (change.Inserted.Length > 0)
        {
            Emit(peer, AtSpi.ObjectEvents, "TextChanged", "insert", change.Offset, change.Inserted.Length,
                new("s", change.Inserted));
        }
    }

    /// <summary>The states that differ from the ones last told, one event each. A peer
    /// never told of is told nothing: there is no "before" to tell against, and a client
    /// that asks now gets the states as they are.</summary>
    private void TellStateChanges(AccessibilityPeer peer)
    {
        Told told = TellOf(peer);
        ulong now = StatesOf(peer, remember: false);

        if (told.States is not { } before)
        {
            told.States = now;
            return;
        }

        told.States = now;

        ulong changed = before ^ now;

        for (int state = 0; state < 64 && changed != 0; state++)
        {
            ulong bit = AtSpi.Bit(state);

            if ((changed & bit) == 0) continue;

            changed &= ~bit;

            if (AtSpi.StateName(state) is not null)
                EmitState(peer, state, (now & bit) != 0);
        }
    }

    /// <summary>The children added and removed since a client was last told of them.
    /// Children never told of are not told of now: a client reads them when it comes.</summary>
    private void OnStructureChanged(AccessibilityPeer peer)
    {
        if (!_told.TryGetValue(peer, out Told? told) || told.Children is not { } before) return;

        IReadOnlyList<AccessibilityPeer> children = peer.Children;
        List<string> after = [.. children.Select(PathOf)];

        told.Children = after;

        for (int i = before.Count - 1; i >= 0; i--)
        {
            if (!after.Contains(before[i]))
            {
                Emit(peer, AtSpi.ObjectEvents, "ChildrenChanged", "remove", i, 0,
                    new("(so)", new object?[] { _busName, before[i] }));
            }
        }

        for (int i = 0; i < after.Count; i++)
        {
            if (!before.Contains(after[i]))
            {
                Emit(peer, AtSpi.ObjectEvents, "ChildrenChanged", "add", i, 0,
                    new("(so)", Reference(children[i])));
            }
        }
    }

    private void OnAnnouncement(Form form, string text, AccessibilityLiveSetting politeness) =>
        Announce(form.GetAccessibilityPeer(), text, politeness);

    /// <summary>Text read out without moving the focus: AT-SPI 2.52's Announcement,
    /// which Orca 46 and later speak.</summary>
    private void Announce(AccessibilityPeer peer, string text, AccessibilityLiveSetting politeness) =>
        Emit(peer, AtSpi.ObjectEvents, "Announcement", string.Empty,
            politeness == AccessibilityLiveSetting.Assertive ? AtSpi.LiveAssertive : AtSpi.LivePolite, 0,
            new("s", text));

    /// <summary>The windows that opened, closed, came forward or went back since the last
    /// look: a turn of the platform's loop. The core tells no such thing, and a window's
    /// activation is how a screen reader learns where the user is.</summary>
    public void ReviewWindows()
    {
        if (!_listening) return;

        IReadOnlyList<Form> forms = Forms();

        for (int i = _forms.Count - 1; i >= 0; i--)
        {
            Form form = _forms[i];

            if (forms.Contains(form)) continue;

            _forms.RemoveAt(i);

            AccessibilityPeer peer = form.GetAccessibilityPeer();
            Emit(peer, AtSpi.WindowEvents, "Destroy", string.Empty, 0, 0, new("s", peer.Name));
            Emit(null, AtSpi.ObjectEvents, "ChildrenChanged", "remove", i, 0, new("(so)", Reference(peer)));
        }

        foreach (Form form in forms)
        {
            if (_forms.Contains(form)) continue;

            _forms.Add(form);

            AccessibilityPeer peer = form.GetAccessibilityPeer();
            Emit(null, AtSpi.ObjectEvents, "ChildrenChanged", "add", _forms.Count - 1, 0, new("(so)", Reference(peer)));
            Emit(peer, AtSpi.WindowEvents, "Create", string.Empty, 0, 0, new("s", peer.Name));
        }

        Form? active = forms.FirstOrDefault(form => form.IsActive);

        if (ReferenceEquals(active, _activeForm)) return;

        if (_activeForm is { } previous && forms.Contains(previous))
        {
            AccessibilityPeer peer = previous.GetAccessibilityPeer();
            EmitState(peer, AtSpi.StateActive, false);
            Emit(peer, AtSpi.WindowEvents, "Deactivate", string.Empty, 0, 0, new("s", peer.Name));
        }

        _activeForm = active;

        if (active is not null)
        {
            AccessibilityPeer peer = active.GetAccessibilityPeer();
            EmitState(peer, AtSpi.StateActive, true);
            Emit(peer, AtSpi.WindowEvents, "Activate", string.Empty, 0, 0, new("s", peer.Name));
        }
    }
}
