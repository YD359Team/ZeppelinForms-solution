using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Navigation;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace MyApp.Pages;

/// <summary>The first page: a list, each row opening its details. A row swiped to the left
/// is removed, with Undo in a snackbar.</summary>
public class HomePage : Page
{
    private readonly StackPanel _list;

    public HomePage()
    {
        Title = "MyApp";
        Route = "items";

        _list = new StackPanel
        {
            Orientation = Orientation.Vertical,
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
            OverflowY = Overflow.Auto,
        };

        for (int i = 1; i <= 20; i++)
            _list.Children.Add(ItemRow(i));

        Child = _list;
    }

    private SwipeView ItemRow(int id)
    {
        var text = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, Padding = new Thickness(16, 10) };
        text.Children.Add(new Label { Text = $"Item {id}", TextStyle = TextStyle.BodyStrong });
        text.Children.Add(new Label { Text = "Tap to open, swipe to the left to remove", TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });

        // GoToAsync by the route: the address in a browser becomes #/items/details?id=…
        text.Click += (_, _) => _ = NavigationHost?.GoToAsync($"details?id={id}");

        var row = new SwipeView(text) { RightMode = SwipeMode.Execute };

        row.RightItems.Add(new SwipeItem("Remove", null, new Color(255, 0xDC, 0x35, 0x45), async (_, _) =>
        {
            int index = _list.Children.IndexOf(row);
            _list.Children.Remove(row);

            if (FindForm() is { } form && await form.ShowSnackbarAsync($"Item {id} removed", "Undo"))
                _list.Children.Insert(Math.Min(index, _list.Children.Count), ItemRow(id));
        }));

        return row;
    }
}
