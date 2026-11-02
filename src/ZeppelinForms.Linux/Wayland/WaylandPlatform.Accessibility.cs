using ZeppelinForms.Forms;
using ZeppelinForms.Linux.Accessibility;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>Screen readers: the forms' accessibility trees on the AT-SPI bus, as on X11
/// (<see cref="AtSpiBridge"/>). The bus socket is polled with the display's.</summary>
public sealed partial class WaylandPlatform : IAtSpiWindows
{
    /// <summary>Null without AT-SPI on the desktop.</summary>
    private AtSpiBridge? _accessibility;

    private void InitAccessibility() => _accessibility = AtSpiBridge.TryCreate(this);

    float IAtSpiWindows.Scale(Form form)
    {
        foreach (WaylandWindow window in _windows)
            if (ReferenceEquals(window.Form, form))
                return window.Scale;

        return 1f;
    }

    /// <summary>Wayland tells no client where its windows are: coordinates on the screen
    /// are given in the window, as GTK gives them there.</summary>
    bool IAtSpiWindows.TryGetScreenOrigin(Form form, out int x, out int y)
    {
        x = y = 0;
        return false;
    }
}
