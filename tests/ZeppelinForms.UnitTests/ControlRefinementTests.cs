using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Navigation;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>The refinements of existing controls in 0.14.0.</summary>
[Collection("Platform")]
public class ControlRefinementTests
{
    /// <summary>Records what a control draws, by shape and color.</summary>
    private sealed class RecordingGraphics : HeadlessGraphics
    {
        public List<(Rectangle Rect, Color Color)> RoundFills { get; } = [];
        public List<(Rectangle Rect, Color Color)> Ellipses { get; } = [];
        public List<(Point From, Point To, Color Color)> Lines { get; } = [];
        public List<Color> Polylines { get; } = [];

        public override void FillRoundRectangle(Rectangle rect, CornerRadius radius, Color color) =>
            RoundFills.Add((rect, color));

        public override void FillEllipse(Rectangle rect, Color color) => Ellipses.Add((rect, color));

        public override void DrawLine(Point from, Point to, Color color, float width) => Lines.Add((from, to, color));

        public override void DrawPolyline(ReadOnlySpan<Point> points, Color color, float width) => Polylines.Add(color);
    }

    private static Form CreateForm(UIElement content)
    {
        var form = new Form { Size = new Size(400, 300), Content = content };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    private static RecordingGraphics Record(UnitControl control)
    {
        var g = new RecordingGraphics();
        control.Draw(g);
        return g;
    }

    // ===== CheckBox: the glyph of the third state =====

    private static CheckBox ThirdState(IndeterminateGlyph glyph)
    {
        var box = new CheckBox
        {
            Text = "Some",
            IsThreeState = true,
            CheckColor = new Color(255, 1, 2, 3),
            CheckGlyphColor = new Color(255, 4, 5, 6),
            BoxBackground = new Color(255, 7, 8, 9),
            IndeterminateGlyph = glyph,
        };

        box.CheckedState = CheckedState.Intermediate;
        CreateForm(box);

        return box;
    }

    [Fact]
    public void TheDashLiesOnAFilledBox()
    {
        CheckBox box = ThirdState(IndeterminateGlyph.Dash);
        RecordingGraphics g = Record(box);

        Assert.Contains(g.RoundFills, f => f.Color == box.CheckColor);
        Assert.Contains(g.Lines, l => l.Color == box.CheckGlyphColor && l.From.Y == l.To.Y);
        Assert.Empty(g.Polylines);
    }

    [Fact]
    public void TheClassicSquareSitsInAnEmptyBox()
    {
        CheckBox box = ThirdState(IndeterminateGlyph.Square);
        RecordingGraphics g = Record(box);

        // the box keeps its background; the accent fills a smaller square inside it
        (Rectangle outer, _) = Assert.Single(g.RoundFills, f => f.Color == box.BoxBackground);
        (Rectangle inner, _) = Assert.Single(g.RoundFills, f => f.Color == box.CheckColor);

        Assert.True(inner.Width < outer.Width);
        Assert.True(inner.X > outer.X && inner.X + inner.Width < outer.X + outer.Width);
        Assert.Empty(g.Lines);
    }

    [Fact]
    public void TheDotLiesOnAFilledBox()
    {
        CheckBox box = ThirdState(IndeterminateGlyph.Dot);
        RecordingGraphics g = Record(box);

        Assert.Contains(g.RoundFills, f => f.Color == box.CheckColor);
        Assert.Contains(g.Ellipses, e => e.Color == box.CheckGlyphColor);
        Assert.Empty(g.Lines);
    }

    [Fact]
    public void TheGlyphComesFromAStyleSheet()
    {
        var box = new CheckBox { Text = "Some", IsThreeState = true };
        Form form = CreateForm(box);

        StyleSheet sheet = StyleSheet.Parse("CheckBox:indeterminate { IndeterminateGlyph: Square; }", "test.zss");
        Assert.Empty(sheet.Diagnostics);
        form.Styles.Add(sheet);

        Assert.Equal(IndeterminateGlyph.Dash, box.IndeterminateGlyph);

        box.CheckedState = CheckedState.Intermediate;

        Assert.Equal(IndeterminateGlyph.Square, box.IndeterminateGlyph);
    }

    // ===== PageIndicator: page titles on hover =====

    private static (Form Form, HeadlessPlatform Platform, PageIndicator Indicator) CreatePages(bool interactive = true)
    {
        var pages = new PageControl { Size = new Size(300, 200) };
        pages.Children.Add(new Page { Name = "one", Title = "One" });
        pages.Children.Add(new Page { Name = "two", Title = "Two" });
        pages.Children.Add(new Page { Name = "three" });

        PageIndicator indicator = pages.CreateIndicator();
        indicator.IsInteractive = interactive;
        indicator.ToolTip = "Pages";

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(pages);
        panel.Children.Add(indicator);

        var platform = new HeadlessPlatform();
        var form = new Form { Size = new Size(400, 300), Content = panel, ToolTipDelay = 1 };

        platform.CreateWindow(form);
        form.UpdateLayout();

        return (form, platform, indicator);
    }

    /// <summary>The tooltip's delay runs on a timer that comes back through the UI
    /// queue: pump the queue, as the UI thread would, until a tip is shown.</summary>
    private static string? WaitForToolTip(Form form, HeadlessPlatform platform)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);

        while (ShownToolTip(form) is null && DateTime.UtcNow < deadline)
        {
            platform.PumpAll();
            Thread.Sleep(5);
        }

        return ShownToolTip(form);
    }

    /// <summary>The center of a dot: the dots are centered in the content.</summary>
    private static Point Dot(PageIndicator indicator, int index)
    {
        const int count = 3;

        float item = indicator.ActiveDotSize;
        float total = count * item + (count - 1) * indicator.Spacing;

        Point at = indicator.GetAbsolutePosition();
        float content = indicator.ActualSize.Width - indicator.Padding.Horizontal;
        float start = at.X + indicator.Padding.Left + (content - total) / 2f;

        return new Point(start + index * (item + indicator.Spacing) + item / 2f, at.Y + indicator.ActualSize.Height / 2f);
    }

    private static string? ShownToolTip(Form form) =>
        form.Overlays.OfType<Border>().Select(b => (b.Child as Label)?.Text).LastOrDefault(t => t is not null);

    [Fact]
    public void HoveringADotShowsItsPageTitle()
    {
        var (form, platform, indicator) = CreatePages();

        form.OnPointerMove(Dot(indicator, 0));
        Assert.Null(ShownToolTip(form));

        Assert.Equal("One", WaitForToolTip(form, platform));

        // the next dot: the shown tip moves on without waiting again
        form.OnPointerMove(Dot(indicator, 1));
        Assert.Equal("Two", ShownToolTip(form));

        // a page without a title: the indicator's own tooltip
        form.OnPointerMove(Dot(indicator, 2));
        Assert.Equal("Pages", ShownToolTip(form));
    }

    [Fact]
    public void TitlesShowOnAnIndicatorThatOnlyShowsThePosition()
    {
        var (form, platform, indicator) = CreatePages(interactive: false);

        form.OnPointerMove(Dot(indicator, 1));

        Assert.Equal("Two", WaitForToolTip(form, platform));
    }

    [Fact]
    public void TitlesCanBeTurnedOff()
    {
        var (form, platform, indicator) = CreatePages();
        indicator.ShowPageTitles = false;

        form.OnPointerMove(Dot(indicator, 0));

        Assert.Equal("Pages", WaitForToolTip(form, platform));
    }

    // ===== ColorPicker: alpha =====

    private static IEnumerable<UIElement> Tree(UIElement root)
    {
        yield return root;

        IEnumerable<UIElement> children = root switch
        {
            PanelControl panel => panel.Children,
            WrapControl { Child: { } child } => [child],
            _ => [],
        };

        foreach (UIElement child in children)
            foreach (UIElement element in Tree(child))
                yield return element;
    }

    private static List<TrackBar> OpenEditor(Form form, ColorPicker picker)
    {
        Point at = picker.GetAbsolutePosition();
        var center = new Point(at.X + picker.ActualSize.Width / 2f, at.Y + picker.ActualSize.Height / 2f);

        form.OnPointerDown(center);
        form.OnPointerUp(center);
        form.UpdateLayout();

        return [.. form.Overlays.SelectMany(Tree).OfType<TrackBar>()];
    }

    private sealed class FillRecorder : HeadlessGraphics
    {
        public int Squares { get; private set; }

        public override void FillRectangle(Rectangle rect, Color color) => Squares++;
    }

    [Fact]
    public void TheHexShowsAlphaOnlyWhenAllowedAndNeeded()
    {
        var picker = new ColorPicker { Value = new Color(128, 255, 0, 0) };

        Assert.Equal("#FF0000", picker.HexText);

        picker.AllowAlpha = true;
        Assert.Equal("#FF000080", picker.HexText);

        picker.Value = new Color(255, 255, 0, 0);
        Assert.Equal("#FF0000", picker.HexText);
    }

    [Fact]
    public void ATranslucentSwatchHasACheckerboard()
    {
        var picker = new ColorPicker { Value = new Color(255, 10, 20, 30) };
        CreateForm(picker);

        var opaque = new FillRecorder();
        picker.Draw(opaque);
        Assert.Equal(0, opaque.Squares);

        picker.Value = new Color(100, 10, 20, 30);

        var translucent = new FillRecorder();
        picker.Draw(translucent);
        Assert.True(translucent.Squares > 1);
    }

    [Fact]
    public void TheEditorHasAnAlphaSliderWhenAllowed()
    {
        var picker = new ColorPicker { Value = new Color(255, 10, 20, 30) };
        Form form = CreateForm(picker);

        Assert.Equal(3, OpenEditor(form, picker).Count);

        // closed by a second click, opened again with alpha
        OpenEditor(form, picker);
        picker.AllowAlpha = true;

        List<TrackBar> sliders = OpenEditor(form, picker);
        Assert.Equal(4, sliders.Count);

        sliders[3].Value = 64;

        Assert.Equal(new Color(64, 10, 20, 30), picker.Value);
    }

    // ===== text decorations and outline =====

    /// <summary>Records the text effects in force at each caption drawn.</summary>
    private sealed class TextRecorder : HeadlessGraphics
    {
        public List<(string Text, TextEffects Effects)> Texts { get; } = [];

        public override void DrawText(
            string text, Rectangle rect, Color color, Font font,
            HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
            VerticalContentAlignment vAlign = VerticalContentAlignment.Center) =>
            Texts.Add((text, TextEffects));

        public TextEffects EffectsOf(string text) => Texts.Single(t => t.Text == text).Effects;
    }

    [Fact]
    public void DecorationsAreInheritedLikeTextTransform()
    {
        var label = new Label { Text = "inside" };
        var panel = new StackPanel { TextDecorations = TextDecorations.Underline };
        panel.Children.Add(label);
        CreateForm(panel);

        Assert.Equal(TextDecorations.Underline, label.TextDecorations);

        // the label's own value wins
        label.TextDecorations = TextDecorations.None;
        Assert.Equal(TextDecorations.None, label.TextDecorations);
    }

    [Fact]
    public void TheRendererHandsEachElementItsEffects()
    {
        var plain = new Label { Text = "plain" };
        var struck = new Label
        {
            Text = "struck",
            TextDecorations = TextDecorations.Strikethrough,
            TextDecorationColor = new Color(255, 200, 0, 0),
            TextOutlineColor = new Color(255, 0, 0, 0),
            TextOutlineWidth = 2f,
        };
        var after = new Label { Text = "after" };

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(plain);
        panel.Children.Add(struck);
        panel.Children.Add(after);

        Form form = CreateForm(panel);

        var g = new TextRecorder();
        ElementTreeRenderer.Draw(form.Content!, g);

        Assert.True(g.EffectsOf("plain").IsEmpty);

        TextEffects effects = g.EffectsOf("struck");
        Assert.Equal(TextDecorations.Strikethrough, effects.Decorations);
        Assert.Equal(new Color(255, 200, 0, 0), effects.LineColor(Colors.Black));
        Assert.True(effects.HasOutline);
        Assert.Equal(2f, effects.OutlineWidth);

        // a sibling drawn after it is not touched by them
        Assert.True(g.EffectsOf("after").IsEmpty);
        Assert.True(g.TextEffects.IsEmpty);
    }

    [Fact]
    public void ASheetListsDecorationsTheCssWay()
    {
        var label = new Label { Text = "styled" };
        Form form = CreateForm(label);

        StyleSheet sheet = StyleSheet.Parse("""
            Label {
                TextDecorations: underline overline;
                TextOutlineColor: #000000c0;
                TextOutlineWidth: 1.5;
            }
            """, "test.zss");

        Assert.Empty(sheet.Diagnostics);
        form.Styles.Add(sheet);

        Assert.Equal(TextDecorations.Underline | TextDecorations.Overline, label.TextDecorations);
        Assert.Equal(new Color(0xc0, 0, 0, 0), label.TextOutlineColor);
        Assert.Equal(1.5f, label.TextOutlineWidth);

        StyleSheet wrong = StyleSheet.Parse("Label { TextDecorations: underline wavy; }", "wrong.zss");
        Assert.Contains("wavy", Assert.Single(wrong.Diagnostics).Message);
    }

    // ===== icons: Button and TextBox =====

    private const string Square = "M0 0H10V10H0Z";

    /// <summary>Records where icons and captions are drawn.</summary>
    private sealed class IconRecorder : HeadlessGraphics
    {
        public List<Rectangle> Paths { get; } = [];
        public List<Rectangle> Images { get; } = [];
        public List<(string Text, Rectangle Rect)> Texts { get; } = [];

        public override void DrawSvgPath(string pathData, Rectangle rect, Color color, float strokeWidth = 0) => Paths.Add(rect);

        public override void DrawImage(Rectangle rect, Image image, ImageFlip flip = ImageFlip.None, ImageLayout layout = ImageLayout.Stretch) =>
            Images.Add(rect);

        public override void DrawText(
            string text, Rectangle rect, Color color, Font font,
            HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
            VerticalContentAlignment vAlign = VerticalContentAlignment.Center) =>
            Texts.Add((text, rect));
    }

    private static IconRecorder Paint(UnitControl control)
    {
        var g = new IconRecorder();
        control.Draw(g);
        return g;
    }

    [Fact]
    public void AButtonPlacesItsIconOnEachSide()
    {
        var button = new Button { Text = "Save", Icon = IconSource.FromPath(Square), Size = new Size(200, 60) };
        CreateForm(button);

        IconRecorder start = Paint(button);
        Assert.True(start.Paths[0].X < start.Texts[0].Rect.X);

        button.IconPlacement = IconPlacement.End;
        IconRecorder end = Paint(button);
        Assert.True(end.Paths[0].X > end.Texts[0].Rect.X);

        button.IconPlacement = IconPlacement.Top;
        IconRecorder top = Paint(button);
        Assert.True(top.Paths[0].Y < top.Texts[0].Rect.Y);

        button.IconPlacement = IconPlacement.Bottom;
        IconRecorder bottom = Paint(button);
        Assert.True(bottom.Paths[0].Y > bottom.Texts[0].Rect.Y);
    }

    [Fact]
    public void ACenteredButtonCentersTheIconAndTextTogether()
    {
        var button = new Button { Text = "Save", Icon = IconSource.FromPath(Square), Size = new Size(300, 40) };
        CreateForm(button);

        IconRecorder g = Paint(button);

        float textWidth = TextMeasurer.Current.MeasureText("Save", button.EffectiveFont).Width;
        float rowLeft = g.Paths[0].X;
        float rowRight = g.Texts[0].Rect.X + textWidth;

        Rectangle content = button.ContentBounds;
        float middle = content.X + content.Width / 2f;

        Assert.Equal(middle, (rowLeft + rowRight) / 2f, 0.5f);
    }

    [Fact]
    public void TheStartIsOnTheRightInARightToLeftLayout()
    {
        var button = new Button
        {
            Text = "Save",
            Icon = IconSource.FromPath(Square),
            Size = new Size(200, 60),
            FlowDirection = ZeppelinForms.Core.Text.FlowDirection.RightToLeft,
        };
        CreateForm(button);

        IconRecorder g = Paint(button);
        Assert.True(g.Paths[0].X > g.Texts[0].Rect.X);
    }

    [Fact]
    public void AButtonMeasuresItsIconBesideOrAbove()
    {
        var button = new Button { Text = "Save", Icon = IconSource.FromPath(Square) };
        CreateForm(button);

        Size text = TextMeasurer.Current.MeasureText("Save", button.EffectiveFont);

        button.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));
        Assert.Equal(text.Width + button.IconSize + button.IconGap + button.Padding.Horizontal, button.DesiredSize.Width, 0.5f);

        button.IconPlacement = IconPlacement.Top;
        button.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));
        Assert.Equal(text.Height + button.IconSize + button.IconGap + button.Padding.Vertical, button.DesiredSize.Height, 0.5f);
    }

    [Fact]
    public void PathDataAndPicturesAreBothIcons()
    {
        var button = new Button { Text = "Open", IconPathData = Square, Size = new Size(200, 40) };
        CreateForm(button);

        Assert.IsType<PathIconSource>(button.Icon);
        Assert.Single(Paint(button).Paths);

        button.Icon = new Image(2, 2, new byte[16]);

        Assert.Null(button.IconPathData);
        Assert.Single(Paint(button).Images);
    }

    [Fact]
    public void TextBoxIconsNarrowTheTextArea()
    {
        var box = new TextBox
        {
            Text = "query",
            Size = new Size(240, 32),
            LeadingIcon = IconSource.FromPath(Square),
            TrailingIcon = IconSource.FromPath(Square),
        };
        CreateForm(box);

        IconRecorder g = Paint(box);

        Rectangle content = box.ContentBounds;
        Rectangle leading = g.Paths[0];
        Rectangle trailing = g.Paths[1];

        Assert.Equal(content.X, leading.X, 0.5f);
        Assert.Equal(content.X + content.Width - box.IconSize, trailing.X, 0.5f);

        // the text starts after the leading icon and its gap
        Assert.Equal(content.X + box.IconSize + box.IconGap, g.Texts.Single(t => t.Text == "query").Rect.X, 0.5f);
    }

    [Fact]
    public void ATrailingIconWithAHandlerIsAButton()
    {
        var box = new TextBox { Text = "query", Size = new Size(240, 32), TrailingIcon = IconSource.FromPath(Square) };
        Form form = CreateForm(box);

        int clicks = 0;
        box.TrailingIconClick += (_, _) => { clicks++; box.Text = string.Empty; };

        Point at = box.GetAbsolutePosition();
        Rectangle content = box.ContentBounds;
        var onIcon = new Point(at.X + content.X + content.Width - box.IconSize / 2f, at.Y + box.ActualSize.Height / 2f);

        form.OnPointerDown(onIcon);
        form.OnPointerUp(onIcon);

        Assert.Equal(1, clicks);
        Assert.Equal(string.Empty, box.Text);

        // the text itself still takes the caret
        box.Text = "query";
        var onText = new Point(at.X + content.X, at.Y + box.ActualSize.Height / 2f);

        form.OnPointerDown(onText);
        form.OnPointerUp(onText);

        Assert.Equal(1, clicks);
        Assert.Equal(0, box.CaretIndex);
    }

    // ===== TextBox: word wrap and whitespace =====

    /// <summary>A multi-line field whose text area is 12.5 characters of the headless
    /// measurer wide: twelve fit, thirteen don't.</summary>
    private static (Form Form, TextBox Box) Wrapped(string text, bool wrap = true)
    {
        float charWidth = TextMeasurer.Current.MeasureTextWidth("x", 1, Font.Default);

        var box = new TextBox
        {
            IsMultiline = true,
            WordWrap = wrap,
            Text = text,
            Padding = new Thickness(0),
            Size = new Size(charWidth * 12.5f, 200),
        };

        return (CreateForm(box), box);
    }

    private static List<string> RowsDrawn(TextBox box)
    {
        var g = new IconRecorder();
        box.Draw(g);
        return [.. g.Texts.Select(t => t.Text)];
    }

    private static void FocusAtStart(Form form, TextBox box)
    {
        Point at = box.GetAbsolutePosition();
        HeadlessInput.Click(form, at.X + 1, at.Y + 1);
        HeadlessInput.PressKey(form, Key.Home, KeyModifiers.Control);
    }

    [Fact]
    public void WrappingBreaksBetweenWordsAndKeepsTheSpacesOnTheRow()
    {
        var (_, box) = Wrapped("alpha beta gamma delta");

        Assert.Equal(["alpha beta ", "gamma delta"], RowsDrawn(box));

        // the text itself is untouched
        Assert.Equal("alpha beta gamma delta", box.Text);
    }

    [Fact]
    public void AWordWiderThanTheRowIsBrokenWhereItOverflows()
    {
        var (_, box) = Wrapped("abcdefghijklmnopqrstuvwxyz");

        Assert.Equal(["abcdefghijkl", "mnopqrstuvwx", "yz"], RowsDrawn(box));
    }

    [Fact]
    public void WithoutWrapALineStaysWhole()
    {
        var (_, box) = Wrapped("alpha beta gamma delta", wrap: false);

        Assert.Equal(["alpha beta gamma delta"], RowsDrawn(box));
    }

    [Fact]
    public void ArrowsAndEndMoveAlongTheRowsOnScreen()
    {
        var (form, box) = Wrapped("alpha beta gamma delta");
        FocusAtStart(form, box);

        HeadlessInput.PressKey(form, Key.Right);
        HeadlessInput.PressKey(form, Key.Right);
        Assert.Equal(2, box.CaretIndex);

        // down a row on screen, not a line of the text: under the same x
        HeadlessInput.PressKey(form, Key.Down);
        Assert.Equal(11 + 2, box.CaretIndex);

        HeadlessInput.PressKey(form, Key.Up);
        Assert.Equal(2, box.CaretIndex);

        // the end of the row is before its hanging space, so the caret stays on it
        HeadlessInput.PressKey(form, Key.End);
        Assert.Equal(10, box.CaretIndex);

        HeadlessInput.PressKey(form, Key.Home);
        Assert.Equal(0, box.CaretIndex);
    }

    [Fact]
    public void AClickPastAWrappedRowStaysOnIt()
    {
        var (form, box) = Wrapped("alpha beta gamma delta");

        Point at = box.GetAbsolutePosition();
        float lineHeight = TextMeasurer.Current.MeasureText("Wg", box.EffectiveFont).Height;

        HeadlessInput.Click(form, at.X + box.ActualSize.Width - 1, at.Y + lineHeight / 2f);

        Assert.Equal(10, box.CaretIndex);
    }

    [Fact]
    public void AWrappedFieldIsAsTallAsItsRows()
    {
        var (_, box) = Wrapped("one two three four five six seven eight nine ten eleven twelve");
        float lineHeight = TextMeasurer.Current.MeasureText("Wg", box.EffectiveFont).Height;

        box.Measure(new Size(box.Size.Width, float.PositiveInfinity));

        // more than the three lines a multi-line field asks for at least
        Assert.True(box.DesiredSize.Height > lineHeight * 4);
    }

    private sealed class WhitespaceRecorder : HeadlessGraphics
    {
        public int Dots { get; private set; }
        public int Arrows { get; private set; }
        public int Pilcrows { get; private set; }

        public override void FillEllipse(Rectangle rect, Color color) => Dots++;

        public override void DrawPolyline(ReadOnlySpan<Point> points, Color color, float width) => Arrows++;

        public override void DrawText(
            string text, Rectangle rect, Color color, Font font,
            HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
            VerticalContentAlignment vAlign = VerticalContentAlignment.Center)
        {
            if (text == "\u00B6") Pilcrows++;
        }
    }

    [Fact]
    public void WhitespaceShowsAsDotsArrowsAndPilcrows()
    {
        var box = new TextBox { IsMultiline = true, Text = "a b\tc\nd", ShowWhitespace = true, Size = new Size(300, 120) };
        CreateForm(box);

        var g = new WhitespaceRecorder();
        box.Draw(g);

        Assert.Equal(1, g.Dots);
        Assert.Equal(1, g.Arrows);

        // after the first line only: the last one has no line break
        Assert.Equal(1, g.Pilcrows);

        box.ShowWhitespace = false;

        var off = new WhitespaceRecorder();
        box.Draw(off);

        Assert.Equal(0, off.Dots + off.Arrows + off.Pilcrows);
    }

    [Fact]
    public void APasswordShowsNoWhitespace()
    {
        var box = new TextBox { Text = "a b", PasswordChar = '*', ShowWhitespace = true, Size = new Size(200, 30) };
        CreateForm(box);

        var g = new WhitespaceRecorder();
        box.Draw(g);

        Assert.Equal(0, g.Dots);
    }
}