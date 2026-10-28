using ZeppelinForms.Theming;

// ZfApp, not App: in a project named Contoso.App the namespace Contoso.App would be found
// first, and App.Theme would be looked for in it
using ZfApp = ZeppelinForms.App;

namespace MyApp;

/// <summary>What every start of the application does first, wherever it runs: the projects
/// next to this one call <see cref="Configure"/> before they show <see cref="MainForm"/>.</summary>
public static class Startup
{
    public static void Configure()
    {
        // light or dark as the system is, and its accent; Settings lets the user choose
        ZfApp.UseSystemTheme(Themes.FluentLight, Themes.FluentDark);
    }
}
