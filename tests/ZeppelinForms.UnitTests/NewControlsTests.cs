using Xunit;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.UnitTests;

/// <summary>The controls new in 0.14.0.</summary>
[Collection("Platform")]
public class NewControlsTests
{
    private static Form CreateForm(UIElement content, float width = 400, float height = 300)
    {
        var form = new Form { Size = new Size(width, height), Content = content };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    // ===== RangeSlider =====

    private const float Thumb = 14f;

    private static (Form Form, RangeSlider Slider) CreateRange(float lower, float upper)
    {
        var slider = new RangeSlider
        {
            Size = new Size(300, 30),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        slider.SetRange(lower, upper);

        return (CreateForm(slider), slider);
    }

    /// <summary>Where a value is on the track, in the form's coordinates.</summary>
    private static Point At(RangeSlider slider, float value)
    {
        Point origin = slider.GetAbsolutePosition();
        float travel = slider.ActualSize.Width - Thumb;
        float fraction = (value - slider.Minimum) / (slider.Maximum - slider.Minimum);

        return new Point(origin.X + Thumb / 2f + travel * fraction, origin.Y + slider.ActualSize.Height / 2f);
    }

    private static void Drag(Form form, Point from, Point to)
    {
        form.OnPointerDown(from);
        form.OnPointerMove(new Point((from.X + to.X) / 2f, (from.Y + to.Y) / 2f));
        form.OnPointerMove(to);
        form.OnPointerUp(to);
    }

    [Fact]
    public void TheValuesDontDependOnTheOrderOfAssignment()
    {
        var slider = new RangeSlider { LowerValue = 150, UpperValue = 180, Maximum = 200 };

        Assert.Equal(150f, slider.LowerValue);
        Assert.Equal(180f, slider.UpperValue);
    }

    [Fact]
    public void AThumbAssignedPastTheOtherPushesIt()
    {
        var slider = new RangeSlider();
        slider.SetRange(20, 40);

        slider.LowerValue = 60;
        Assert.Equal(60f, slider.LowerValue);
        Assert.Equal(60f, slider.UpperValue);

        slider.UpperValue = 10;
        Assert.Equal(10f, slider.LowerValue);
        Assert.Equal(10f, slider.UpperValue);

        // and a gap keeps them apart
        slider.MinimumGap = 5;
        Assert.Equal(15f, slider.UpperValue);

        slider.LowerValue = 98;
        Assert.Equal(95f, slider.LowerValue);
        Assert.Equal(100f, slider.UpperValue);
    }

    [Fact]
    public void SetRangeRaisesOneEvent()
    {
        var slider = new RangeSlider();
        int changes = 0;
        slider.RangeChanged += (_, _) => changes++;

        slider.SetRange(30, 70);
        Assert.Equal(1, changes);

        slider.SetRange(30, 70);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void DraggingMovesTheNearestThumb()
    {
        var (form, slider) = CreateRange(20, 80);

        Drag(form, At(slider, 20), At(slider, 35));
        Assert.Equal(35f, slider.LowerValue, 0.5f);
        Assert.Equal(80f, slider.UpperValue);
        Assert.Equal(RangeThumb.Lower, slider.ActiveThumb);

        // a press on the track beside the upper thumb moves it there
        Drag(form, At(slider, 70), At(slider, 70));
        Assert.Equal(70f, slider.UpperValue, 0.5f);
        Assert.Equal(RangeThumb.Upper, slider.ActiveThumb);
    }

    [Fact]
    public void AClosedRangeOpensTowardTheDrag()
    {
        var (form, slider) = CreateRange(0, 0);

        // both thumbs at the minimum: dragging right takes the upper one
        Drag(form, At(slider, 0), At(slider, 40));

        Assert.Equal(0f, slider.LowerValue);
        Assert.Equal(40f, slider.UpperValue, 0.5f);

        var (form2, slider2) = CreateRange(100, 100);

        // at the maximum: dragging left takes the lower one
        Drag(form2, At(slider2, 100), At(slider2, 60));

        Assert.Equal(60f, slider2.LowerValue, 0.5f);
        Assert.Equal(100f, slider2.UpperValue);
    }

    [Fact]
    public void ThumbsPushEachOtherWhenDragged()
    {
        var (form, slider) = CreateRange(20, 40);

        Drag(form, At(slider, 20), At(slider, 70));

        Assert.Equal(70f, slider.LowerValue, 0.5f);
        Assert.Equal(70f, slider.UpperValue, 0.5f);
    }

    [Fact]
    public void TabGoesThroughBothThumbs()
    {
        var (form, slider) = CreateRange(20, 80);

        HeadlessInput.Click(form, At(slider, 20).X, At(slider, 20).Y);
        Assert.True(slider.IsFocused);
        Assert.Equal(RangeThumb.Lower, slider.ActiveThumb);

        HeadlessInput.PressKey(form, Key.Right);
        Assert.Equal(21f, slider.LowerValue, 0.5f);

        // the second tab stop: the upper thumb, still in the slider
        HeadlessInput.PressKey(form, Key.Tab);
        Assert.True(slider.IsFocused);
        Assert.Equal(RangeThumb.Upper, slider.ActiveThumb);

        HeadlessInput.PressKey(form, Key.Left);
        Assert.Equal(79f, slider.UpperValue, 0.5f);

        HeadlessInput.PressKey(form, Key.End);
        Assert.Equal(100f, slider.UpperValue);

        HeadlessInput.PressKey(form, Key.Tab, KeyModifiers.Shift);
        Assert.Equal(RangeThumb.Lower, slider.ActiveThumb);

        HeadlessInput.PressKey(form, Key.Home);
        Assert.Equal(0f, slider.LowerValue);
    }

    [Fact]
    public void EachThumbIsASliderForAssistiveTechnology()
    {
        var (_, slider) = CreateRange(20, 80);

        AccessibilityPeer group = slider.GetAccessibilityPeer()!;
        Assert.Equal(AccessibilityRole.Group, group.Role);

        AccessibilityPeer lower = group.Children[0];
        AccessibilityPeer upper = group.Children[1];

        Assert.Equal(AccessibilityRole.Slider, lower.Role);

        // the names follow the language: the test may run under any culture
        Assert.Equal(Localization.Get(ZfText.RangeLower), lower.Name);
        Assert.Equal(Localization.Get(ZfText.RangeUpper), upper.Name);
        Assert.Equal(20d, lower.Range!.Value.Value);
        Assert.Equal(80d, upper.Range!.Value.Value);

        Assert.True(upper.SetRangeValue(50));
        Assert.Equal(50f, slider.UpperValue);
    }

    // ===== TabStrip =====

    private static (Form Form, TabStrip Strip) CreateStrip(int tabs, float width = 600)
    {
        var strip = new TabStrip { VerticalAlignment = VerticalAlignment.Top };

        for (int i = 0; i < tabs; i++)
            strip.Items.Add(new TabStripItem($"Tab {i}"));

        return (CreateForm(strip, width), strip);
    }

    private static Point TabCenter(TabStrip strip, int index)
    {
        Rectangle tab = strip.TabRect(index);
        Point origin = strip.GetAbsolutePosition();

        return new Point(origin.X + tab.X + tab.Width / 2f, origin.Y + tab.Y + tab.Height / 2f);
    }

    /// <summary>The middle of a tab's close button: at its right edge, inside the padding.</summary>
    private static Point CloseCenter(TabStrip strip, int index)
    {
        Rectangle tab = strip.TabRect(index);
        Point origin = strip.GetAbsolutePosition();

        return new Point(origin.X + tab.X + tab.Width - 6 - 8, origin.Y + tab.Y + tab.Height / 2f);
    }

    private static string[] Headers(TabStrip strip) => [.. strip.Items.Select(i => i.Header)];

    [Fact]
    public void TheSelectionFollowsTheTabAndPassesToTheRightNeighbour()
    {
        var (_, strip) = CreateStrip(3);

        // the first tab added is selected
        Assert.Equal(0, strip.SelectedIndex);

        strip.SelectedIndex = 1;
        strip.Items.Insert(0, new TabStripItem("New"));

        // the same tab stays selected at its new place
        Assert.Equal("Tab 1", strip.SelectedItem!.Header);

        strip.Items.Remove(strip.SelectedItem);
        Assert.Equal("Tab 2", strip.SelectedItem!.Header);

        // the last one closed: the left neighbour
        strip.Items.Remove(strip.SelectedItem);
        Assert.Equal("Tab 0", strip.SelectedItem!.Header);
    }

    [Fact]
    public void ClicksSelectAndCloseTabs()
    {
        var (form, strip) = CreateStrip(3);
        var closing = new List<string>();

        strip.TabCloseRequested += (_, e) =>
        {
            closing.Add(e.Item.Header);

            // the first tab asks to be kept
            e.Cancel = e.Item.Header == "Tab 0";
        };

        HeadlessInput.Click(form, TabCenter(strip, 2).X, TabCenter(strip, 2).Y);
        Assert.Equal(2, strip.SelectedIndex);

        HeadlessInput.Click(form, CloseCenter(strip, 1).X, CloseCenter(strip, 1).Y);
        Assert.Equal(["Tab 0", "Tab 2"], Headers(strip));

        HeadlessInput.Click(form, CloseCenter(strip, 0).X, CloseCenter(strip, 0).Y);
        Assert.Equal(["Tab 0", "Tab 2"], Headers(strip));
        Assert.Equal(["Tab 1", "Tab 0"], closing);

        // the middle button closes too
        HeadlessInput.Click(form, TabCenter(strip, 1).X, TabCenter(strip, 1).Y, MouseButton.Middle);
        Assert.Equal(["Tab 0"], Headers(strip));
    }

    [Fact]
    public void ATabThatCantCloseHasNoButton()
    {
        var (form, strip) = CreateStrip(2);
        strip.Items[1].IsClosable = false;

        HeadlessInput.Click(form, CloseCenter(strip, 1).X, CloseCenter(strip, 1).Y);

        // the click lands on the tab itself and selects it
        Assert.Equal(2, strip.Items.Count);
        Assert.Equal(1, strip.SelectedIndex);
        Assert.False(strip.RequestClose(strip.Items[1]));
    }

    [Fact]
    public void ThePlusButtonAsksForATab()
    {
        var (form, strip) = CreateStrip(2);
        strip.ShowAddButton = true;
        form.UpdateLayout();

        strip.AddTabRequested += (_, _) => strip.Items.Add(new TabStripItem("Added"));

        Rectangle last = strip.TabRect(1);
        Point origin = strip.GetAbsolutePosition();
        float height = last.Height;

        HeadlessInput.Click(form, origin.X + last.X + last.Width + height / 2f, origin.Y + height / 2f);

        Assert.Equal(["Tab 0", "Tab 1", "Added"], Headers(strip));
    }

    [Fact]
    public void DraggingATabReordersAndKeepsItSelected()
    {
        var (form, strip) = CreateStrip(3);
        var moves = new List<(int, int)>();
        strip.TabMoved += (_, e) => moves.Add((e.OldIndex, e.NewIndex));

        Point from = TabCenter(strip, 0);
        Point past = TabCenter(strip, 2);

        form.OnPointerDown(from);
        form.OnPointerMove(new Point(from.X + 10, from.Y));
        form.OnPointerMove(new Point(past.X + 5, past.Y));
        form.OnPointerUp(new Point(past.X + 5, past.Y));

        Assert.Equal(["Tab 1", "Tab 2", "Tab 0"], Headers(strip));
        Assert.Equal("Tab 0", strip.SelectedItem!.Header);
        Assert.Equal([(0, 1), (1, 2)], moves);
    }

    [Fact]
    public void TheKeyboardSelectsMovesAndCloses()
    {
        var (form, strip) = CreateStrip(3);

        HeadlessInput.Click(form, TabCenter(strip, 0).X, TabCenter(strip, 0).Y);
        Assert.True(strip.IsFocused);

        HeadlessInput.PressKey(form, Key.Right);
        Assert.Equal(1, strip.SelectedIndex);

        HeadlessInput.PressKey(form, Key.Right, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.Equal(["Tab 0", "Tab 2", "Tab 1"], Headers(strip));
        Assert.Equal(2, strip.SelectedIndex);

        HeadlessInput.PressKey(form, Key.W, KeyModifiers.Control);
        Assert.Equal(["Tab 0", "Tab 2"], Headers(strip));

        HeadlessInput.PressKey(form, Key.Home);
        Assert.Equal(0, strip.SelectedIndex);
    }

    [Fact]
    public void TooManyTabsNarrowThenScroll()
    {
        var (form, strip) = CreateStrip(20, width: 500);

        Rectangle first = strip.TabRect(0);
        Assert.Equal(strip.MinTabWidth, first.Width, 0.5f);

        // the last tab is out of sight, and selecting it scrolls it into view
        strip.SelectedIndex = 19;
        form.UpdateLayout();

        Rectangle last = strip.TabRect(19);
        Assert.True(last.X + last.Width <= strip.ContentBounds.X + strip.ContentBounds.Width + 0.5f);
        Assert.True(last.X >= strip.ContentBounds.X);
    }

    [Fact]
    public void EachTabIsATabForAssistiveTechnology()
    {
        var (_, strip) = CreateStrip(3);

        AccessibilityPeer list = strip.GetAccessibilityPeer()!;
        Assert.Equal(AccessibilityRole.TabList, list.Role);
        Assert.Equal(3, list.Children.Count);
        Assert.Equal("Tab 1", list.Children[1].Name);

        Assert.True(list.Children[2].Select());
        Assert.Equal(2, strip.SelectedIndex);
        Assert.True(list.Children[2].States.HasFlag(AccessibilityStates.Selected));
    }

    // ===== AutoCompleteBox =====

    private static readonly string[] Cities =
        ["Amsterdam", "Athens", "Berlin", "Bern", "New York", "Newcastle", "York", "Vienna"];

    private static (Form Form, AutoCompleteBox Box) CreateAutoComplete(AutoCompleteFilter filter = AutoCompleteFilter.StartsWith)
    {
        var box = new AutoCompleteBox
        {
            ItemsSource = Cities,
            FilterMode = filter,
            Size = new Size(200, 30),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        Form form = CreateForm(box);

        Point at = box.GetAbsolutePosition();
        HeadlessInput.Click(form, at.X + 5, at.Y + 5);

        return (form, box);
    }

    private static string[] Shown(AutoCompleteBox box) => [.. box.Suggestions.Select(s => s.ToString()!)];

    [Fact]
    public void TypingNarrowsTheSuggestions()
    {
        var (form, box) = CreateAutoComplete();

        HeadlessInput.TypeText(form, "b");
        Assert.True(box.IsDropDownOpen);
        Assert.Equal(["Berlin", "Bern"], Shown(box));

        HeadlessInput.TypeText(form, "erl");
        Assert.Equal(["Berlin"], Shown(box));

        // nothing matches: the list goes away
        HeadlessInput.TypeText(form, "x");
        Assert.False(box.IsDropDownOpen);
    }

    [Fact]
    public void FilterModesMatchDifferently()
    {
        var (form, box) = CreateAutoComplete(AutoCompleteFilter.WordStartsWith);

        HeadlessInput.TypeText(form, "york");
        Assert.Equal(["New York", "York"], Shown(box));

        var (form2, box2) = CreateAutoComplete(AutoCompleteFilter.Contains);

        HeadlessInput.TypeText(form2, "ew");
        Assert.Equal(["New York", "Newcastle"], Shown(box2));
    }

    [Fact]
    public void ArrowsAndEnterTakeASuggestion()
    {
        var (form, box) = CreateAutoComplete();
        object? chosen = null;
        box.SuggestionChosen += (_, e) => chosen = e.Item;

        HeadlessInput.TypeText(form, "a");
        Assert.Equal(-1, box.HighlightedIndex);

        HeadlessInput.PressKey(form, Key.Down);
        HeadlessInput.PressKey(form, Key.Down);
        HeadlessInput.PressKey(form, Key.Up);
        Assert.Equal(0, box.HighlightedIndex);

        HeadlessInput.PressKey(form, Key.Down);
        HeadlessInput.PressKey(form, Key.Enter);

        Assert.Equal("Athens", box.Text);
        Assert.Equal("Athens", chosen);
        Assert.Equal("Athens", box.SelectedItem);
        Assert.Equal(6, box.CaretIndex);
        Assert.False(box.IsDropDownOpen);

        // editing away from the item forgets it
        HeadlessInput.PressKey(form, Key.Backspace);
        Assert.Null(box.SelectedItem);
    }

    [Fact]
    public void EscapeClosesAndDownReopens()
    {
        var (form, box) = CreateAutoComplete();

        HeadlessInput.TypeText(form, "n");
        Assert.True(box.IsDropDownOpen);

        HeadlessInput.PressKey(form, Key.Escape);
        Assert.False(box.IsDropDownOpen);
        Assert.Equal("n", box.Text);

        HeadlessInput.PressKey(form, Key.Down);
        Assert.True(box.IsDropDownOpen);
    }

    [Fact]
    public void AClickTakesASuggestionAndTheFieldKeepsTheFocus()
    {
        var (form, box) = CreateAutoComplete();

        HeadlessInput.TypeText(form, "be");

        UIElement list = form.Overlays.Last();
        form.UpdateLayout();

        // the second row: Bern
        Point origin = list.GetAbsolutePosition();
        UIElement row = ((PanelControl)list).Children[1];
        Point rowAt = row.GetAbsolutePosition();

        HeadlessInput.Click(form, rowAt.X + 5, rowAt.Y + row.ActualSize.Height / 2f);

        Assert.Equal("Bern", box.Text);
        Assert.True(box.IsFocused);
        Assert.False(box.IsDropDownOpen);
        Assert.True(origin.Y >= box.GetAbsolutePosition().Y + box.ActualSize.Height - 0.5f);
    }

    [Fact]
    public void AProviderAnswersForTheWholeText()
    {
        var (form, box) = CreateAutoComplete();
        box.SuggestionProvider = text => [$"{text}@mail.example", $"{text}@post.example"];

        HeadlessInput.TypeText(form, "ann");

        Assert.Equal(["ann@mail.example", "ann@post.example"], Shown(box));
    }

    [Fact]
    public void TextFromCodeDoesntSuggest()
    {
        var (_, box) = CreateAutoComplete();

        box.Text = "Ber";

        Assert.False(box.IsDropDownOpen);
    }

    [Fact]
    public void APrefixMustBeLongEnough()
    {
        var (form, box) = CreateAutoComplete();
        box.MinimumPrefixLength = 2;

        HeadlessInput.TypeText(form, "b");
        Assert.False(box.IsDropDownOpen);

        HeadlessInput.TypeText(form, "e");
        Assert.True(box.IsDropDownOpen);
    }
}