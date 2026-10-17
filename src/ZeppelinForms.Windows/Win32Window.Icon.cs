using System.ComponentModel;
using System.Diagnostics;
using ZeppelinForms.Drawing.Imaging;

namespace ZeppelinForms.Windows;

/// <summary>The window's icon: the title bar's, the taskbar button's, Alt+Tab's.</summary>
/// <remarks>
/// <para>
/// The icon used to be given once, at 32 and 16 px, and only from the form's own
/// <see cref="Forms.Form.Icon"/>: <see cref="App.Icon"/> went nowhere, an icon set
/// after the window opened was never seen, and the window class had none.
/// </para>
/// <para>
/// The class icon is what the taskbar falls back to when the window doesn't answer
/// WM_GETICON in time — an application busy filling its lists in <c>Shown</c> keeps
/// its UI thread from answering just when the taskbar button is made. With no class
/// icon the button took the executable's, the default one of .NET, and kept it.
/// </para>
/// </remarks>
internal sealed partial class Win32Window
{
    private const int SM_CXICON = 11;
    private const int SM_CXSMICON = 49;

    /// <summary>The icon the window shows, for the sizes of a new DPI.</summary>
    private Icon? _icon;

    private static (nint Large, nint Small)? s_classIcons;

    /// <summary>The application's icon at the system's sizes, for the window class:
    /// made once and kept for the process — every window registers the class again,
    /// and a new pair per window would leak.</summary>
    private static (nint Large, nint Small) ClassIcons() => s_classIcons ??= CreateClassIcons();

    private static (nint Large, nint Small) CreateClassIcons()
    {
        Icon icon = App.DefaultIcon ?? Assets.Logo;

        try
        {
            return (
                Win32Icon.Create(icon, NativeMethods.GetSystemMetrics(SM_CXICON), NativeMethods.GetSystemMetrics(SM_CXICON)),
                Win32Icon.Create(icon, NativeMethods.GetSystemMetrics(SM_CXSMICON), NativeMethods.GetSystemMetrics(SM_CXSMICON)));
        }
        catch (Win32Exception exception)
        {
            // without a class icon the window still opens: its own icon stays
            Debug.WriteLine($"ZeppelinForms: the class icon could not be made: {exception.Message}");
            return (0, 0);
        }
    }

    public void SetIcon(Icon? icon) => ApplyIcon(icon);

    /// <summary>Give the window its icons at the sizes of its DPI: the large one —
    /// the taskbar's and Alt+Tab's — and the small one of the title bar. Null — the
    /// class icon, the application's.</summary>
    private void ApplyIcon(Icon? icon)
    {
        _icon = icon;

        if (_handle == 0) return;

        nint oldLarge = _largeIcon;
        nint oldSmall = _smallIcon;

        _largeIcon = 0;
        _smallIcon = 0;

        if (icon is not null)
        {
            uint dpi = NativeMethods.GetDpiForWindow(_handle);

            int large = NativeMethods.GetSystemMetricsForDpi(SM_CXICON, dpi);
            int small = NativeMethods.GetSystemMetricsForDpi(SM_CXSMICON, dpi);

            _largeIcon = Win32Icon.Create(icon, large, large);
            _smallIcon = Win32Icon.Create(icon, small, small);
        }

        NativeMethods.SendMessage(_handle, NativeConstants.WM_SETICON, NativeConstants.ICON_BIG, _largeIcon);
        NativeMethods.SendMessage(_handle, NativeConstants.WM_SETICON, NativeConstants.ICON_SMALL, _smallIcon);

        // the window holds the new ones now: the old handles can go
        if (oldLarge != 0) NativeMethods.DestroyIcon(oldLarge);
        if (oldSmall != 0) NativeMethods.DestroyIcon(oldSmall);
    }
}