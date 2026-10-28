using ZeppelinForms;
using ZeppelinForms.Theming;

namespace MyApp;

/// <summary>What every start of the application does first, wherever it runs: the projects
/// next to this one call <see cref="Configure"/> before they show <see cref="MainForm"/>.</summary>
public static class Startup
{
    public static void Configure()
    {
        // light or dark as the system is, and its accent; Settings lets the user choose
        App.UseSystemTheme(Themes.FluentLight, Themes.FluentDark);
    }
}
