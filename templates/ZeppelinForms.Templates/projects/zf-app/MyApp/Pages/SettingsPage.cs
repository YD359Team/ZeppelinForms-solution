using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Navigation;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Theming;

// ZfApp, not App: in a project named Contoso.App the namespace Contoso.App would be found
// first, and App.Theme would be looked for in it
using ZfApp = ZeppelinForms.App;

namespace MyApp.Pages;

/// <summary>The second destination: the theme.</summary>
public class SettingsPage : Page
{
    public SettingsPage()
    {
        Title = "Settings";

        var theme = new ComboBox { SelectedIndex = -1 };
        theme.Items.AddRange(["As the system", "Light", "Dark"]);
        theme.SelectedIndex = ZfApp.IsFollowingSystemTheme ? 0 : ZfApp.RequestedTheme.Name == Themes.FluentDark.Name ? 2 : 1;

        theme.SelectionChanged += (_, _) =>
        {
            switch (theme.SelectedIndex)
            {
                case 0: ZfApp.UseSystemTheme(Themes.FluentLight, Themes.FluentDark); break;
                case 1: ZfApp.Theme = Themes.FluentLight; break;
                case 2: ZfApp.Theme = Themes.FluentDark; break;
            }
        };

        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Padding = new Thickness(16), CrossAxisAlignment = CrossAxisAlignment.Stretch };
        column.Children.Add(new Label { Text = "Theme", TextStyle = TextStyle.BodyStrong });
        column.Children.Add(theme);

        Child = column;
    }
}
