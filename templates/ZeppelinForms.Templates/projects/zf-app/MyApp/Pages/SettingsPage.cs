using ZeppelinForms;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Navigation;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Theming;

namespace MyApp.Pages;

/// <summary>The second destination: the theme.</summary>
public class SettingsPage : Page
{
    public SettingsPage()
    {
        Title = "Settings";

        var theme = new ComboBox { SelectedIndex = -1 };
        theme.Items.AddRange(["As the system", "Light", "Dark"]);
        theme.SelectedIndex = App.IsFollowingSystemTheme ? 0 : App.RequestedTheme.Name == Themes.FluentDark.Name ? 2 : 1;

        theme.SelectionChanged += (_, _) =>
        {
            switch (theme.SelectedIndex)
            {
                case 0: App.UseSystemTheme(Themes.FluentLight, Themes.FluentDark); break;
                case 1: App.Theme = Themes.FluentLight; break;
                case 2: App.Theme = Themes.FluentDark; break;
            }
        };

        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Padding = new Thickness(16), CrossAxisAlignment = CrossAxisAlignment.Stretch };
        column.Children.Add(new Label { Text = "Theme", TextStyle = TextStyle.BodyStrong });
        column.Children.Add(theme);

        Child = column;
    }
}
