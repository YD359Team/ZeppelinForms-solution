using System.Globalization;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Linux.DBus;
using ZeppelinForms.Theming;

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
/// <para>
/// The standard namespace has no text size and no contrast colors. Both come from
/// the namespaces the desktops' own portals add next to it: GNOME's gives its
/// settings schemas — org.gnome.desktop.interface with text-scaling-factor —, KDE's
/// gives kdeglobals — the font of its General group, and the colors of its scheme.
/// A desktop with neither leaves the text at 1 and the contrast palette built-in.
/// </para>
/// </remarks>
internal sealed class PortalAppearance : ISystemAppearance, IDisposable
{
    private const string Destination = "org.freedesktop.portal.Desktop";
    private const string ObjectPath = "/org/freedesktop/portal/desktop";
    private const string SettingsInterface = "org.freedesktop.portal.Settings";
    private const string AppearanceNamespace = "org.freedesktop.appearance";

    /// <summary>GNOME's interface settings: text-scaling-factor, a double.</summary>
    private const string GnomeInterfaceNamespace = "org.gnome.desktop.interface";

    /// <summary>KDE's General group of kdeglobals: font, as QFont writes it —
    /// "Noto Sans,10,-1,5,400,0,0,0,0,0,0,0,0,0,0,1", the second field in points.</summary>
    private const string KdeGeneralNamespace = "org.kde.kdeglobals.General";

    /// <summary>KDE's color groups: "org.kde.kdeglobals.Colors:View" and the like,
    /// each color "r,g,b".</summary>
    private const string KdeColorsPrefix = "org.kde.kdeglobals.Colors:";

    /// <summary>The point size KDE ships its font in: the size the user didn't change.</summary>
    private const float KdeDefaultPointSize = 10f;

    private const string UnknownMethod = "org.freedesktop.DBus.Error.UnknownMethod";

    /// <summary>The bus answers in microseconds, and so does a running portal.
    /// The margin is for a portal the bus has to start first — on a cold session it
    /// brings up its desktop backend as well. A desktop without a portal at all
    /// costs nothing: the bus refuses at once that it knows no such service.</summary>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromMilliseconds(1000);

    /// <summary>The colors of KDE's scheme a contrast palette is made of, by role, in
    /// the order of <see cref="Theming.HighContrastPalette"/>: (group, key).</summary>
    private static readonly (string Group, string Key)[] s_kdePaletteColors =
    [
        ("View", "BackgroundNormal"),
        ("View", "ForegroundNormal"),
        ("Button", "BackgroundNormal"),
        ("Button", "ForegroundNormal"),
        ("Selection", "BackgroundNormal"),
        ("Selection", "ForegroundNormal"),
        ("View", "ForegroundInactive"),
        ("View", "ForegroundLink"),
    ];

    private readonly DBusConnection _bus;
    private bool _useReadOne = true;

    /// <summary>KDE's scheme colors, by (group, key): read once and kept up to date by
    /// SettingChanged, so a palette is made without calling the portal again — a call
    /// from inside the signal's handler would wait on the socket being read.</summary>
    private readonly Dictionary<(string Group, string Key), Color> _kdeColors = [];

    /// <summary>GNOME's text-scaling-factor; 1 elsewhere.</summary>
    private float _gnomeTextScaling = 1f;

    /// <summary>KDE's font size against the size it ships with; 1 elsewhere.</summary>
    private float _kdeFontScale = 1f;

    public bool IsDark { get; private set; }

    public Color? AccentColor { get; private set; }

    /// <summary>"contrast" of the appearance namespace: 1 — the user asked for
    /// higher contrast. The portal gives no colors for it: KDE's scheme gives them
    /// where it is the desktop, the built-in contrast palette is used elsewhere,
    /// light or dark by the color scheme.</summary>
    public bool IsHighContrast { get; private set; }

    /// <summary>KDE's scheme colors while contrast is on: the scheme the user picked is
    /// the one to show. Null — not KDE, or a color missing: a half palette would mix
    /// pairs, and the built-in one is used instead.</summary>
    public HighContrastPalette? HighContrastPalette { get; private set; }

    /// <summary>The text size the user asked for: GNOME's "Large Text" and text
    /// scaling factor, KDE's font size against its default of 10 points.</summary>
    public float TextScale => _gnomeTextScaling != 1f ? _gnomeTextScaling : _kdeFontScale;

    /// <summary>The part of the text size GNOME folds into Xft.dpi on X11: its
    /// settings daemon writes 96 × text scaling × window scale there. The X11 display
    /// scale takes it out again — it is a size of text, not a density of the screen.</summary>
    public float TextScaleInXftDpi => _gnomeTextScaling;

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
        // would otherwise be lost. Every namespace: the text size and KDE's colors
        // come from namespaces of their own, and OnSignal picks what it reads
        bus.Call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "AddMatch",
            $"type='signal',interface='{SettingsInterface}',member='SettingChanged'," +
            $"path='{ObjectPath}'");

        bus.Signal += portal.OnSignal;

        object? scheme = portal.Read(AppearanceNamespace, "color-scheme", out bool reachable);

        if (!reachable)
        {
            // no portal on this bus: the application gets no system appearance
            bus.Dispose();
            return null;
        }

        portal.IsDark = IsDarkScheme(scheme);
        portal.AccentColor = ToAccent(portal.Read(AppearanceNamespace, "accent-color", out _));

        portal.IsHighContrast = IsHigherContrast(portal.Read(AppearanceNamespace, "contrast", out _));

        portal._gnomeTextScaling = ToGnomeTextScaling(
            portal.Read(GnomeInterfaceNamespace, "text-scaling-factor", out _));

        portal._kdeFontScale = ToKdeFontScale(portal.Read(KdeGeneralNamespace, "font", out _));

        foreach ((string group, string key) in s_kdePaletteColors)
            portal.TakeKdeColor(group, key, portal.Read(KdeColorsPrefix + group, key, out _));

        portal.HighContrastPalette = portal.MakeHighContrastPalette();

        return portal;
    }

    /// <summary>Read data the event loop found on the socket.</summary>
    public void Poll() => _bus.ProcessIncoming();

    /// <summary>One setting. <paramref name="reachable"/> — whether the portal
    /// answered at all, even with an error about the key.</summary>
    private object? Read(string @namespace, string key, out bool reachable)
    {
        reachable = false;

        if (_useReadOne)
        {
            DBusMessage? reply = _bus.Call(Destination, ObjectPath, SettingsInterface, "ReadOne",
                @namespace, key);

            if (reply is { Type: DBusMessageType.Error, ErrorName: UnknownMethod })
                _useReadOne = false;
            else
                return Value(reply, out reachable);
        }

        return Value(
            _bus.Call(Destination, ObjectPath, SettingsInterface, "Read", @namespace, key),
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
                Body: [string @namespace, string key, DBusVariant value, ..],
            })
        {
            return;
        }

        var before = (IsDark, AccentColor, IsHighContrast, TextScale, HighContrastPalette);

        switch (@namespace, key)
        {
            case (AppearanceNamespace, "color-scheme"): IsDark = IsDarkScheme(value.Unwrap()); break;
            case (AppearanceNamespace, "accent-color"): AccentColor = ToAccent(value.Unwrap()); break;
            case (AppearanceNamespace, "contrast"): IsHighContrast = IsHigherContrast(value.Unwrap()); break;
            case (GnomeInterfaceNamespace, "text-scaling-factor"): _gnomeTextScaling = ToGnomeTextScaling(value.Unwrap()); break;
            case (KdeGeneralNamespace, "font"): _kdeFontScale = ToKdeFontScale(value.Unwrap()); break;

            default:
                if (!@namespace.StartsWith(KdeColorsPrefix, StringComparison.Ordinal)) return;

                TakeKdeColor(@namespace[KdeColorsPrefix.Length..], key, value.Unwrap());
                break;
        }

        HighContrastPalette = MakeHighContrastPalette();

        // what the application sees, compared as a whole: a color of KDE's scheme
        // matters only while contrast is on, and KDE's font not while GNOME's factor wins
        if (before == (IsDark, AccentColor, IsHighContrast, TextScale, HighContrastPalette)) return;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Keep a color of KDE's scheme if the palette is made of it.</summary>
    private void TakeKdeColor(string group, string key, object? value)
    {
        if (Array.IndexOf(s_kdePaletteColors, (group, key)) < 0) return;

        if (ToKdeColor(value) is { } color)
            _kdeColors[(group, key)] = color;
        else
            _kdeColors.Remove((group, key));
    }

    /// <summary>KDE's scheme as a contrast palette while contrast is on; null
    /// otherwise, or with a color missing.</summary>
    private HighContrastPalette? MakeHighContrastPalette()
    {
        if (!IsHighContrast) return null;

        var colors = new Color[s_kdePaletteColors.Length];

        for (int i = 0; i < colors.Length; i++)
            if (!_kdeColors.TryGetValue(s_kdePaletteColors[i], out colors[i]))
                return null;

        return new HighContrastPalette(
            colors[0], colors[1], colors[2], colors[3],
            colors[4], colors[5], colors[6], colors[7]);
    }

    /// <summary>1 — prefer dark. "No preference" is light: that is what the
    /// desktops show an application that asks nothing.</summary>
    private static bool IsDarkScheme(object? value) => value is 1u;

    /// <summary>1 — higher contrast; 0 — no preference.</summary>
    private static bool IsHigherContrast(object? value) => value is 1u;

    /// <summary>GNOME's factor: 1.25 for "Large Text", whatever the user set with a
    /// tweak tool; anything unreadable is 1.</summary>
    private static float ToGnomeTextScaling(object? value) =>
        value is double factor && double.IsFinite(factor) && factor > 0d ? (float)factor : 1f;

    /// <summary>KDE's general font against its 10 points: "Noto Sans,12,…" is 1.2.
    /// A font given in pixels (a point size of −1), or one that can't be read, is 1.</summary>
    private static float ToKdeFontScale(object? value)
    {
        if (value is not string font) return 1f;

        string[] fields = font.Split(',');

        if (fields.Length < 2 ||
            !float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float points) ||
            points <= 0f)
        {
            return 1f;
        }

        return points / KdeDefaultPointSize;
    }

    /// <summary>A color of KDE's scheme: "r,g,b" or "r,g,b,a", channels from 0 to 255.</summary>
    private static Color? ToKdeColor(object? value)
    {
        if (value is not string text) return null;

        string[] fields = text.Split(',');

        if (fields.Length is not (3 or 4)) return null;

        Span<byte> channels = stackalloc byte[3];

        for (int i = 0; i < 3; i++)
        {
            if (!int.TryParse(fields[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int channel) ||
                channel is < 0 or > 255)
            {
                return null;
            }

            channels[i] = (byte)channel;
        }

        return new Color(channels[0], channels[1], channels[2]);
    }

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
