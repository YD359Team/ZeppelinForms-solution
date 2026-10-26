using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Forms;

/// <summary>
/// An icon of the application in the system tray — the notification area of the Windows
/// taskbar, the status area of a Linux panel — with a tooltip, a menu and a click, as
/// WinForms' <c>NotifyIcon</c> has.
/// </summary>
/// <remarks>
/// <para>
/// The menu is the system's own, built from <see cref="Menu"/> each time it opens: text,
/// enabled, a check mark, separators and submenus — not the form's menus, which can't open
/// over a panel the application doesn't draw, nor without a window shown. An item's
/// <see cref="MenuItem.Click"/> and command run as in a menu bar. <see cref="MenuOpening"/>
/// comes first: the place to bring the items up to date.
/// </para>
/// <para>
/// With <see cref="Form.Hide"/> it makes "close to the tray": cancel the main form's
/// <see cref="Form.Closing"/>, hide it, and show it again on <see cref="Click"/>. The
/// icon doesn't keep the application running by itself — the hidden form does.
/// </para>
/// <para>
/// Windows shows the icon in the notification area. On Linux the icon is a
/// StatusNotifierItem on the session bus: KDE, XFCE, Cinnamon, LXQt and waybar show it,
/// GNOME with the AppIndicator extension; a desktop without a tray shows nothing — check
/// <see cref="PlatformCapabilities.SystemTray"/>. The browser and Android have no tray.
/// </para>
/// </remarks>
public sealed class TrayIcon : IDisposable
{
    // the icons on the screen: what the application's end takes off the tray
    private static readonly List<TrayIcon> s_shown = [];

    // made visible before there was a platform to show them on
    private static readonly List<TrayIcon> s_waiting = [];

    private ITrayIconImpl? _impl;
    private bool _disposed;

    /// <summary>The picture; null — the application's icon (<see cref="App.Icon"/>, or the logo).</summary>
    public Icon? Icon
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            field = value;
            _impl?.Update();
        }
    }

    /// <summary>The text shown on hover: the application's name and its state, as a rule.
    /// Windows cuts it at 127 characters.</summary>
    public string? ToolTip
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            _impl?.Update();
        }
    }

    /// <summary>The icon is in the tray. Where the platform has no tray it stays false
    /// whatever is set — <see cref="IsShown"/> tells.</summary>
    public bool Visible
    {
        get;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (field == value) return;

            field = value;

            if (value) Realize();
            else Withdraw();
        }
    }

    /// <summary>The icon is in the tray now: <see cref="Visible"/> and a platform with one.</summary>
    public bool IsShown => _impl is not null;

    /// <summary>The menu the icon opens — a right click on Windows, the panel's own way on
    /// Linux. Empty — no menu.</summary>
    public List<MenuItem> Menu { get; } = [];

    /// <summary>The icon was clicked: a left click on Windows, the panel's activation on
    /// Linux — the moment to show the main form.</summary>
    public event EventHandler? Click;

    /// <summary>A double click on Windows; Linux panels activate on a single click and
    /// raise <see cref="Click"/> only.</summary>
    public event EventHandler? DoubleClick;

    /// <summary>The menu is about to open: the place to set the items' state from the
    /// application's, once, as <see cref="MenuItem.Opening"/> is for a submenu.</summary>
    public event EventHandler? MenuOpening;

    /// <summary>The picture the platform shows.</summary>
    internal Icon EffectiveIcon => Icon ?? App.DefaultIcon ?? Assets.Logo;

    internal void RaiseClick() => Click?.Invoke(this, EventArgs.Empty);

    internal void RaiseDoubleClick() => DoubleClick?.Invoke(this, EventArgs.Empty);

    internal void RaiseMenuOpening() => MenuOpening?.Invoke(this, EventArgs.Empty);

    /// <summary>Put the icon into the tray, if the platform is known and has one. One made
    /// visible before the application exists waits for it, see <see cref="OnAppStarted"/>.</summary>
    private void Realize()
    {
        if (_impl is not null) return;

        if (App.CurrentPlatform is null)
        {
            if (!s_waiting.Contains(this)) s_waiting.Add(this);
            return;
        }

        if (App.CurrentPlatform is not ITrayPlatform platform) return;

        _impl = platform.CreateTrayIcon(this);

        if (_impl is not null) s_shown.Add(this);
    }

    private void Withdraw()
    {
        s_waiting.Remove(this);
        s_shown.Remove(this);

        ITrayIconImpl? impl = _impl;
        _impl = null;
        impl?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;

        Withdraw();
        _disposed = true;
    }

    // ===== the application's life =====

    /// <summary>The application started: icons made visible before it — in the main
    /// form's constructor, say — go into the tray now, and those already there take the
    /// application's icon if they show it.</summary>
    internal static void OnAppStarted()
    {
        foreach (TrayIcon icon in s_waiting.ToArray())
        {
            s_waiting.Remove(icon);
            icon.Realize();
        }

        foreach (TrayIcon icon in s_shown.ToArray())
            icon._impl?.Update();
    }

    /// <summary>The application's loop ended: the icons leave the tray. On Windows an icon
    /// left behind stays in the notification area until the mouse passes over it.</summary>
    internal static void OnAppEnded()
    {
        foreach (TrayIcon icon in s_shown.ToArray())
            icon.Withdraw();
    }
}

/// <summary>A platform with a system tray: it makes the native icon of a
/// <see cref="TrayIcon"/>. Null — no tray after all, a Linux desktop without a host.</summary>
internal interface ITrayPlatform
{
    ITrayIconImpl? CreateTrayIcon(TrayIcon owner);
}

/// <summary>The native side of a tray icon. <see cref="Update"/> reads the owner's icon
/// and tooltip again; the menu is built from the owner's items when it opens.</summary>
internal interface ITrayIconImpl : IDisposable
{
    void Update();
}