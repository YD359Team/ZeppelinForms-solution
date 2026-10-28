using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Navigation;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace MyApp.Pages;

/// <summary>A page opened by a route: <c>details?id=3</c>. Its actions are shown in the app
/// bar while it is the current page.</summary>
public class DetailsPage : Page
{
    public DetailsPage(string id)
    {
        Title = $"Item {id}";

        var body = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12, Padding = new Thickness(16) };

        body.Children.Add(new Label { Text = $"Item {id}", TextStyle = TextStyle.Subtitle });
        body.Children.Add(new Label
        {
            Text = "Go back with the arrow above, the system's back, Alt+Left or a swipe from the left edge.",
            TextWrapping = TextWrapping.Wrap,
        });

        var more = new Button { Text = "More…", HorizontalAlignment = HorizontalAlignment.Left };
        more.Click += (_, _) => ShowMore();
        body.Children.Add(more);

        Child = body;

        Actions.Add(new AppBarButton(Icons.Star, "Star", async (_, _) =>
        {
            if (FindForm() is { } form) await form.ShowSnackbarAsync($"Item {id} starred");
        }));
    }

    /// <summary>A sheet from the bottom: drag it down, tap beside it or go back to close it.</summary>
    private void ShowMore()
    {
        if (FindForm() is not { } form) return;

        var options = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, CrossAxisAlignment = CrossAxisAlignment.Stretch };
        options.Children.Add(new Label { Text = Title, TextStyle = TextStyle.Subtitle });

        var sheet = new BottomSheet(options);

        foreach (string choice in new[] { "Share", "Duplicate", "Move to archive" })
        {
            var button = new Button { Text = choice, HorizontalContentAlign = HorizontalContentAlignment.Left };
            button.Click += (_, _) => sheet.Close();
            options.Children.Add(button);
        }

        sheet.Show(form);
    }
}
