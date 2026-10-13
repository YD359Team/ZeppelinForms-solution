using Xunit;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class CursorImageTests
{
    private static readonly CursorImage Lens =
        new(new Image(16, 16, new byte[16 * 16 * 4]), new Point(7, 7), CursorKind.Cross);

    private static (Form Form, HeadlessWindow Window, Border Canvas, Button Button) Host()
    {
        var platform = new HeadlessPlatform();

        var button = new Button { Text = "кнопка", Cursor = CursorKind.Hand };

        var canvas = new Border
        {
            Size = new Size(300, 200),
            CursorImage = Lens,
            Child = new StackPanel { Children = { button } },
        };

        var form = new Form { Size = new Size(300, 200), Content = canvas };
        var window = (HeadlessWindow)platform.CreateWindow(form);
        form.UpdateLayout();

        return (form, window, canvas, button);
    }

    [Fact]
    public void PictureCursorReachesThePlatform()
    {
        var (form, window, _, _) = Host();

        form.OnPointerMove(new Point(250, 180));

        Assert.Same(Lens, window.CursorImage);
    }

    [Fact]
    public void ChildsOwnKindWinsOverItsAncestorsPicture()
    {
        var (form, window, _, button) = Host();

        form.OnPointerMove(new Point(250, 180));

        Point inside = button.GetAbsolutePosition();
        form.OnPointerMove(new Point(inside.X + 5, inside.Y + 5));

        Assert.Null(window.CursorImage);
        Assert.Equal(CursorKind.Hand, window.Cursor);
    }

    [Fact]
    public void HotspotStaysInsideThePicture()
    {
        var cursor = new CursorImage(new Image(8, 8, new byte[8 * 8 * 4]), new Point(20, -3));

        Assert.Equal(new Point(7, 0), cursor.Hotspot);
    }
}