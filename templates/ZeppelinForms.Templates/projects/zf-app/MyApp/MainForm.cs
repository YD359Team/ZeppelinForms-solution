using MyApp.Pages;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Navigation;

namespace MyApp;

/// <summary>
/// The application's form: an app bar on top, a stack of pages in the middle, a bar of
/// destinations at the bottom. The same on a desktop, in a browser and on a phone — on a
/// phone and in a browser it fills the screen, on a desktop it is a window of this size.
/// </summary>
/// <remarks>
/// Back goes back through the stack: the arrow in the app bar, Android's back, the browser's
/// back button, Alt+Left, a swipe from the left edge. In a browser the address follows the
/// stack — <c>#/items/details?id=3</c> — and opens the page it names.
/// </remarks>
public class MainForm : Form
{
    public MainForm()
    {
        Title = "MyApp";
        Size = new Size(420, 760);

        // on a phone the form goes under the status bar and the gesture bar, and the bars of
        // the scaffold color the space under them and keep their content out of it
        EdgeToEdge = true;

        // the routes the address and GoToAsync open pages by
        var home = new NavigationHost { RootPage = new HomePage() };
        home.RegisterRoute("details", parameters => new DetailsPage(parameters.GetValueOrDefault("id", "1")));

        var add = new Button { Icon = Icons.Add, Text = "Add", CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 14) };
        add.Classes.Add("accent");
        add.Click += async (_, _) => await ShowSnackbarAsync("Nothing to add yet");

        Content = new Scaffold
        {
            AppBar = new AppBar(),
            FloatingActionButton = add,
            NavigationBar = new NavigationBar
            {
                Items =
                {
                    new NavigationBarItem("Home", Icons.Home) { Content = home },

                    // made the first time it is shown
                    new NavigationBarItem("Settings", Icons.Settings, () => new NavigationHost { RootPage = new SettingsPage() }),
                },
            },
        };
    }
}
