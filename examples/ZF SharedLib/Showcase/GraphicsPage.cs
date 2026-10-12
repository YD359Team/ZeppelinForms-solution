using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Shapes;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>Pictures and drawn shapes: vector icons, shapes with fills and outlines,
/// a picture in every layout.</summary>
internal static class GraphicsPage
{
    private static readonly Color Blue = new(255, 0x0D, 0x6E, 0xFD);
    private static readonly Color Pink = new(255, 0xD6, 0x33, 0x84);
    private static readonly Color Orange = new(255, 0xFD, 0x7E, 0x14);
    private static readonly Color Green = new(255, 0x19, 0x87, 0x54);

    public static UIElement Create() =>
        Demo.Page("Icons and shapes", "Vector icons, shapes and pictures.",
            Demo.Section("Vector icons", Icons(), "SvgIcon draws SVG path data at any size; a stroke width turns it into a line icon"),
            Demo.Section("Shapes", Shapes(), "Points are fractions of the shape's size: it scales with its box"),
            Demo.Section("Pictures", Pictures(), "ImageLayout says how the picture fills its box; Flip mirrors it"));

    private static UIElement Icons()
    {
        // a heart and a star, filled; the showcase's own icons, stroked
        const string heart = "M12 21 L4.5 13.5 C2.5 11.5 2.5 8 4.5 6 C6.5 4 9.5 4 12 7 C14.5 4 17.5 4 19.5 6 C21.5 8 21.5 11.5 19.5 13.5 Z";
        const string star = "M12 2 L15 9 L22 9.5 L16.5 14 L18.5 21 L12 17 L5.5 21 L7.5 14 L2 9.5 L9 9 Z";

        return Demo.Row(
            new SvgIcon { PathData = heart, Color = Pink, IconSize = 24 },
            new SvgIcon { PathData = heart, Color = Pink, IconSize = 48 },
            new SvgIcon { PathData = star, Color = Orange, IconSize = 24 },
            new SvgIcon { PathData = star, Color = Orange, IconSize = 48 },
            new SvgIcon { PathData = star, Color = Blue, IconSize = 48, StrokeWidth = 1.5f },
            new SvgIcon { PathData = "M3 11 L12 3 L21 11 M5 9 V21 H19 V9 M10 21 V15 H14 V21", Color = Green, IconSize = 48, StrokeWidth = 1.5f });
    }

    private static UIElement Shapes()
    {
        Size box = new(90, 90);

        return Demo.Row(
            new LineShape { From = new(0, 1), To = new(1, 0), Stroke = Blue, StrokeThickness = 3, Size = box },
            new RectangleShape { Fill = new Color(60, 0x0D, 0x6E, 0xFD), Stroke = Blue, StrokeThickness = 2, Size = box },
            new RectangleShape { Fill = Green, Radius = new(16), Size = box },
            new EllipseShape { Fill = Orange, Size = box },
            new EllipseShape { Stroke = Pink, StrokeThickness = 4, Size = new(130, 80) },
            new PolygonShape
            {
                Fill = new Color(80, 0xD6, 0x33, 0x84),
                Stroke = Pink,
                StrokeThickness = 2,
                Size = box,
                Points = [new(0.5f, 0f), new(0.62f, 0.38f), new(1f, 0.38f), new(0.69f, 0.6f), new(0.81f, 1f), new(0.5f, 0.75f), new(0.19f, 1f), new(0.31f, 0.6f), new(0f, 0.38f), new(0.38f, 0.38f)],
            },
            new PolygonShape { Fill = Blue, Size = box, Points = [new(0, 1), new(0.5f, 0), new(1, 1)] });
    }

    private static UIElement Pictures()
    {
        var picture = new PictureBox
        {
            Size = new(260, 180),
            Layout = ImageLayout.Zoom,
            BorderWidth = 1,
            BorderColor = new Color(80, 128, 128, 128),
        };
        picture.LoadAsset("Laughing.png");

        var layout = new ComboBox { SelectedIndex = -1 };
        layout.Items.AddRange(Enum.GetNames<ImageLayout>());
        layout.SelectedIndex = (int)ImageLayout.Zoom;
        layout.SelectionChanged += (_, _) => picture.Layout = (ImageLayout)layout.SelectedIndex;

        var flip = new ComboBox { SelectedIndex = -1 };
        flip.Items.AddRange(Enum.GetNames<ImageFlip>());
        flip.SelectedIndex = 0;
        flip.SelectionChanged += (_, _) => picture.Flip = (ImageFlip)flip.SelectedIndex;

        return Demo.Column(Demo.Row(Demo.Labeled("Layout", layout), Demo.Labeled("Flip", flip)), picture);
    }
}
