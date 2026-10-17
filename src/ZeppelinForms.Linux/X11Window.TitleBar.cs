using System.Text;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Linux;

/// <summary>The title bar dark or light: <c>_GTK_THEME_VARIANT</c>, the hint GNOME's
/// window manager reads to draw a window's frame in the dark or the light variant of
/// its theme. A manager that doesn't know it ignores it. X has no way to ask for a
/// caption's color, so the color is not passed on.</summary>
internal sealed partial class X11Window
{
    public void SetTitleBarAppearance(bool isDark, Color? background, Color? foreground)
    {
        if (_window == 0) return;

        nuint property = X11.XInternAtom(_display, "_GTK_THEME_VARIANT", false);
        nuint utf8 = X11.XInternAtom(_display, "UTF8_STRING", false);

        byte[] value = Encoding.UTF8.GetBytes(isDark ? "dark" : "light");

        X11.XChangeProperty(_display, _window, property, utf8, 8, X11.PropModeReplace, value, value.Length);
        X11.XFlush(_display);
    }
}