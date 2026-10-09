using Xunit;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class ImageGalleryTests
{
    private const float Width = 400;
    private const float Height = 300;

    private static Image Picture(int width = 4, int height = 4) => new(width, height, new byte[width * height * 4]);

    /// <summary>Three columns of 128 at this width: (400 + 8) / (100 + 8) fits 3,
    /// and the three widen to (400 − 2 × 8) / 3.</summary>
    private const float Cell = 128f;
    private const float Pitch = Cell + 8f;

    private static (Form Form, ImageGallery Gallery, HeadlessWindow Window) Create(int count = 30, Func<int, GalleryItem>? make = null)
    {
        var gallery = new ImageGallery
        {
            ThumbnailWidth = 100,
            ThumbnailHeight = 100,
            ItemSpacing = 8,
        };

        for (int i = 0; i < count; i++)
            gallery.Items.Add(make?.Invoke(i) ?? new GalleryItem(Picture(), $"Photo {i}"));

        var form = new Form { Size = new Size(Width, Height), Content = gallery };

        var window = (HeadlessWindow)new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, gallery, window);
    }

    private static Point Center(ImageGallery gallery, int index)
    {
        Rectangle cell = gallery.ThumbnailBounds(index);
        Point origin = gallery.GetAbsolutePosition();

        return new Point(origin.X + cell.X + cell.Width / 2f, origin.Y + cell.Y + cell.Height / 2f);
    }

    // ===== the grid =====

    [Fact]
    public void TheColumnsFollowTheWidthAndWidenToFillTheRow()
    {
        (_, ImageGallery gallery, _) = Create();

        Assert.Equal(3, gallery.Columns);

        Rectangle second = gallery.ThumbnailBounds(1);
        Assert.Equal(Pitch, second.X);
        Assert.Equal(Cell, second.Width);

        // the height keeps the proportion of the thumbnail size
        Assert.Equal(Cell, second.Height);
        Assert.Equal(Pitch, gallery.ThumbnailBounds(3).Y);
    }

    [Fact]
    public void ANarrowerWindowHasFewerColumns()
    {
        (Form form, ImageGallery gallery, HeadlessWindow window) = Create();

        window.Resize(250, 300);

        Assert.Equal(2, gallery.Columns);
        Assert.Equal((250f - 8f) / 2f, gallery.ThumbnailBounds(0).Width);
    }

    [Fact]
    public void RightToLeftStartsAtTheRight()
    {
        (Form form, ImageGallery gallery, _) = Create();

        gallery.FlowDirection = FlowDirection.RightToLeft;
        form.UpdateLayout();

        Assert.Equal(2 * Pitch, gallery.ThumbnailBounds(0).X);
        Assert.Equal(0f, gallery.ThumbnailBounds(2).X);
    }

    [Fact]
    public void AnItemIsFoundByPointAndAGapIsNoItem()
    {
        (_, ImageGallery gallery, _) = Create();

        Assert.Equal(4, gallery.IndexAt(new Point(Pitch + 10, Pitch + 10)));
        Assert.Equal(-1, gallery.IndexAt(new Point(Cell + 4, 10)));
    }

    [Fact]
    public void ScrollIntoViewBringsTheLastRowInWhole()
    {
        (Form form, ImageGallery gallery, _) = Create();

        gallery.ScrollIntoView(29);
        form.UpdateLayout();

        Assert.True(gallery.ScrollY > 0);

        Rectangle last = gallery.ThumbnailBounds(29);
        Assert.InRange(last.Y + last.Height, Height - 1f, Height + 0.5f);
    }

    // ===== selection =====

    [Fact]
    public void AClickSelectsAndExtendedSelectionTakesModifiers()
    {
        (Form form, ImageGallery gallery, _) = Create();

        gallery.SelectionMode = SelectionMode.Extended;

        Point first = Center(gallery, 1);
        HeadlessInput.Click(form, first.X, first.Y);

        Assert.Equal(1, gallery.SelectedIndex);

        Point fourth = Center(gallery, 4);
        form.OnPointerDown(fourth, modifiers: KeyModifiers.Shift);
        form.OnPointerUp(fourth, modifiers: KeyModifiers.Shift);

        Assert.Equal([1, 2, 3, 4], gallery.SelectedIndices);

        Point third = Center(gallery, 3);
        form.OnPointerDown(third, modifiers: KeyModifiers.Control);
        form.OnPointerUp(third, modifiers: KeyModifiers.Control);

        Assert.Equal([1, 2, 4], gallery.SelectedIndices);
    }

    [Fact]
    public void TheArrowsMoveThroughTheGrid()
    {
        (Form form, ImageGallery gallery, _) = Create(count: 8);

        Assert.True(form.FocusForAccessibility(gallery));
        gallery.SelectedIndex = 1;

        HeadlessInput.PressKey(form, Key.Down);
        Assert.Equal(4, gallery.SelectedIndex);

        HeadlessInput.PressKey(form, Key.Right);
        Assert.Equal(5, gallery.SelectedIndex);

        // the last row is short: Down from a column it lacks lands on the last item
        HeadlessInput.PressKey(form, Key.Down);
        Assert.Equal(7, gallery.SelectedIndex);

        HeadlessInput.PressKey(form, Key.Up);
        Assert.Equal(4, gallery.SelectedIndex);

        // at the top Up stays in its column
        HeadlessInput.PressKey(form, Key.Up);
        HeadlessInput.PressKey(form, Key.Up);
        Assert.Equal(1, gallery.SelectedIndex);

        HeadlessInput.PressKey(form, Key.End);
        Assert.Equal(7, gallery.SelectedIndex);
    }

    [Fact]
    public void RightToLeftSwapsLeftAndRight()
    {
        (Form form, ImageGallery gallery, _) = Create(count: 8);

        gallery.FlowDirection = FlowDirection.RightToLeft;
        form.UpdateLayout();

        Assert.True(form.FocusForAccessibility(gallery));
        gallery.SelectedIndex = 1;

        HeadlessInput.PressKey(form, Key.Left);
        Assert.Equal(2, gallery.SelectedIndex);
    }

    [Fact]
    public void TheSelectionFollowsItsItemsWhenTheListChanges()
    {
        (_, ImageGallery gallery, _) = Create(count: 6);

        gallery.SelectionMode = SelectionMode.Multiple;
        gallery.SetSelected(2, true);
        gallery.SetSelected(4, true);

        gallery.Items.Insert(0, new GalleryItem(Picture()));
        Assert.Equal([3, 5], gallery.SelectedIndices);

        gallery.Items.RemoveAt(3);
        Assert.Equal([4], gallery.SelectedIndices);
        Assert.Equal(4, gallery.SelectedIndex);
    }

    [Fact]
    public void EmptyFollowsTheItems()
    {
        (Form form, ImageGallery gallery, _) = Create(count: 0);

        Assert.True(gallery.HasPseudoClass(PseudoClass.Empty));

        gallery.Items.Add(new GalleryItem(Picture()));

        Assert.False(gallery.HasPseudoClass(PseudoClass.Empty));
    }

    // ===== loading =====

    [Fact]
    public void OnlyThumbnailsInViewLoadAFewAtATime()
    {
        var requests = new List<(int Index, TaskCompletionSource<Image?> Source)>();

        (Form form, ImageGallery gallery, _) = Create(count: 300, make: i => new GalleryItem
        {
            ThumbnailLoader = _ =>
            {
                var source = new TaskCompletionSource<Image?>();
                requests.Add((i, source));
                return source.Task;
            },
        });

        // four at once, the first ones
        Assert.Equal([0, 1, 2, 3], requests.Select(r => r.Index));

        // each one done lets the next one in
        while (requests.Any(r => !r.Source.Task.IsCompleted))
            requests.First(r => !r.Source.Task.IsCompleted).Source.SetResult(Picture());

        // the rows in view and one more: three rows of 136 in 300, plus one, by three
        Assert.Equal(12, requests.Count);
        Assert.Equal(GalleryImageState.Loaded, gallery.Items[11].ThumbnailState);
        Assert.Equal(GalleryImageState.Empty, gallery.Items[12].ThumbnailState);

        // scrolling far brings in those it reaches, not those it passed
        gallery.ScrollIntoView(299);
        form.UpdateLayout();

        Assert.All(requests.Skip(12), r => Assert.True(r.Index >= 280));
    }

    [Fact]
    public void AFailedThumbnailIsMarkedAndCanBeAskedAgain()
    {
        int calls = 0;

        (Form form, ImageGallery gallery, _) = Create(count: 1, make: _ => new GalleryItem
        {
            ThumbnailLoader = _ =>
            {
                calls++;
                throw new IOException("no such file");
            },
        });

        Assert.Equal(1, calls);
        Assert.Equal(GalleryImageState.Failed, gallery.Items[0].ThumbnailState);

        gallery.Items[0].Reload();
        form.UpdateLayout();

        Assert.Equal(GalleryImageState.Empty, gallery.Items[0].ThumbnailState);

        gallery.ScrollTo(0, 0);
        form.UpdateLayout();

        Assert.Equal(2, calls);
    }

    [Fact]
    public void LeavingTheFormCancelsTheLoads()
    {
        CancellationToken token = default;

        (Form form, ImageGallery gallery, _) = Create(count: 1, make: _ => new GalleryItem
        {
            ThumbnailLoader = t =>
            {
                token = t;
                return new TaskCompletionSource<Image?>().Task;
            },
        });

        Assert.False(token.IsCancellationRequested);

        form.Content = null;

        Assert.True(token.IsCancellationRequested);
    }

    // ===== the viewer =====

    [Fact]
    public void ADoubleClickOpensTheViewerOverTheWindowAndEscapeClosesIt()
    {
        (Form form, ImageGallery gallery, _) = Create();

        int activated = -1;
        gallery.ItemActivated += (_, e) => activated = e.Index;

        Point fifth = Center(gallery, 4);
        HeadlessInput.DoubleClick(form, fifth.X, fifth.Y);

        Assert.Equal(4, activated);
        Assert.True(gallery.IsViewerOpen);

        ImageViewer viewer = gallery.Viewer!;
        form.UpdateLayout();

        Assert.Contains(viewer, form.Overlays);
        Assert.Equal(new Size(Width, Height), viewer.ActualSize);
        Assert.True(viewer.IsFocused);
        Assert.Equal(4, viewer.Index);

        HeadlessInput.PressKey(form, Key.Right);
        HeadlessInput.PressKey(form, Key.Escape);

        Assert.False(gallery.IsViewerOpen);
        Assert.DoesNotContain(viewer, form.Overlays);

        // the gallery is where the viewer left off
        Assert.Equal(5, gallery.SelectedIndex);
        Assert.True(gallery.IsFocused);
    }

    [Fact]
    public void TheViewerFollowsTheWindowSize()
    {
        (Form form, ImageGallery gallery, HeadlessWindow window) = Create();

        gallery.ShowViewer(0);
        window.Resize(500, 350);

        Assert.Equal(new Size(500, 350), gallery.Viewer!.ActualSize);
    }

    [Fact]
    public void EnterOpensTheLeadItem()
    {
        (Form form, ImageGallery gallery, _) = Create();

        Assert.True(form.FocusForAccessibility(gallery));
        gallery.SelectedIndex = 2;

        HeadlessInput.PressKey(form, Key.Enter);

        Assert.Equal(2, gallery.Viewer!.Index);
    }

    [Fact]
    public void TheViewerStopsAtTheEndsUnlessItLoops()
    {
        var viewer = new ImageViewer { Items = [new GalleryItem(Picture()), new GalleryItem(Picture())] };

        Assert.Equal(0, viewer.Index);
        Assert.False(viewer.CanGoPrevious);

        viewer.Next();
        viewer.Next();
        Assert.Equal(1, viewer.Index);

        viewer.IsLooping = true;
        viewer.Next();
        Assert.Equal(0, viewer.Index);
    }

    [Fact]
    public void TheViewerButtonsGoBothWays()
    {
        var viewer = new ImageViewer
        {
            Items = [new GalleryItem(Picture()), new GalleryItem(Picture()), new GalleryItem(Picture())],
        };

        var form = new Form { Size = new Size(Width, Height), Content = viewer };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        Rectangle next = viewer.NextButton;
        HeadlessInput.Click(form, next.X + next.Width / 2f, next.Y + next.Height / 2f);
        Assert.Equal(1, viewer.Index);

        Rectangle previous = viewer.PreviousButton;
        HeadlessInput.Click(form, previous.X + previous.Width / 2f, previous.Y + previous.Height / 2f);
        Assert.Equal(0, viewer.Index);

        // right to left, the next picture is on the left
        viewer.FlowDirection = FlowDirection.RightToLeft;
        form.UpdateLayout();

        Assert.True(viewer.NextButton.X < viewer.PreviousButton.X);
    }

    [Fact]
    public void TheViewerLoadsThePictureAndItsNeighbours()
    {
        var loaded = new List<int>();

        GalleryItem Make(int i) => new()
        {
            Thumbnail = Picture(),
            ImageLoader = _ =>
            {
                loaded.Add(i);
                return Task.FromResult<Image?>(Picture(8, 8));
            },
        };

        var viewer = new ImageViewer { Items = [Make(0), Make(1), Make(2), Make(3)], Index = 2 };

        var form = new Form { Size = new Size(Width, Height), Content = viewer };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        Assert.Equal([2, 3, 1], loaded);
        Assert.Equal(8, viewer.CurrentItem!.Image!.Width);

        viewer.Previous();

        Assert.Equal([2, 3, 1, 0], loaded);
    }

    // ===== accessibility =====

    [Fact]
    public void ThePeerIsAListOfNamedPictures()
    {
        (Form form, ImageGallery gallery, _) = Create(count: 3);

        gallery.Items[1].Title = null;
        gallery.Items[1].Description = "A cat on a fence";
        gallery.Items[2].Title = null;

        AccessibilityPeer peer = gallery.GetAccessibilityPeer()!;

        Assert.Equal(AccessibilityRole.List, peer.Role);
        Assert.Equal(3, peer.Children.Count);

        Assert.Equal("Photo 0", peer.Children[0].Name);
        Assert.Equal("A cat on a fence", peer.Children[1].Name);
        Assert.Equal(Localization.Get(ZfText.ImageNumber, 3), peer.Children[2].Name);

        Assert.True(peer.Children[1].Select());
        Assert.True(peer.Children[1].States.HasFlag(AccessibilityStates.Selected));

        Assert.True(peer.Children[2].Invoke());
        Assert.Equal(2, gallery.Viewer!.Index);

        AccessibilityPeer viewerPeer = gallery.Viewer.GetAccessibilityPeer()!;
        Assert.Equal(AccessibilityRole.Dialog, viewerPeer.Role);
        Assert.Equal(Localization.Get(ZfText.ImageCounter, 3, 3), viewerPeer.Value);
    }
}