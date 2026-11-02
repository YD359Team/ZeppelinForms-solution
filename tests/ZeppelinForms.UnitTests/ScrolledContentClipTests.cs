using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.20: what a scrolling panel draws under its children — a list's selection and
/// hover — is cut where the children are. The highlight of a row scrolled past the top of a
/// combo box's drop-down was drawn above the list, over the combo box.</summary>
[Collection("Platform")]
public class ScrolledContentClipTests
{
    /// <summary>The selection's fills, each with the clip it was drawn under.</summary>
    private sealed class ClipRecordingGraphics(Color watched) : HeadlessGraphics
    {
        private readonly Stack<Rectangle?> _saved = new();
        private Rectangle? _clip;

        public List<(Rectangle Fill, Rectangle? Clip)> Fills { get; } = [];

        public override void Save()
        {
            _saved.Push(_clip);
            base.Save();
        }

        public override void Restore()
        {
            _clip = _saved.Count > 0 ? _saved.Pop() : null;
            base.Restore();
        }

        public override void ClipRect(Rectangle bounds)
        {
            _clip = bounds;
            base.ClipRect(bounds);
        }

        public override void FillRectangle(Rectangle rect, Color color)
        {
            if (color == watched) Fills.Add((rect, _clip));

            base.FillRectangle(rect, color);
        }
    }

    [Fact]
    public void ASelectedRowScrolledPastTheTopIsCutAtTheListsEdge()
    {
        var list = new ListBox
        {
            Size = new Size(200, 100),
            OverflowY = Overflow.Auto,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        for (int i = 0; i < 30; i++) list.Items.Add($"Item {i}");

        var form = new Form { Size = new Size(300, 300), Content = new StackPanel { Padding = new Thickness(0, 80, 0, 0), Children = { list } } };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        list.SelectedIndex = 0;
        list.ScrollTo(0, 10);
        form.UpdateLayout();

        var g = new ClipRecordingGraphics(list.SelectionColor);
        ElementTreeRenderer.Draw(form.Content!, g);

        // the row is half out of sight: its highlight starts above the list, and the
        // list's own clip cuts it there
        (Rectangle fill, Rectangle? clip) = Assert.Single(g.Fills);

        Assert.True(fill.Y < 0, "the row is scrolled partly past the top");
        Assert.Equal(list.ClipBounds, clip);
    }
}
