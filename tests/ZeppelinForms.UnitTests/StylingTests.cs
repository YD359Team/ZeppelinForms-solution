using Xunit;
using ZeppelinForms.Animation;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Headless;
using ZeppelinForms.Theming;

namespace ZeppelinForms.UnitTests;

/// <summary>
/// Styles of 0.14.0: selectors, classes, pseudo-classes, the cascade and its place
/// on the ladder of value sources. Styles go into the form's collection rather than
/// the application's: App.Styles is process-wide, and a style left there would
/// match in tests of other collections running in parallel.
/// </summary>
[Collection("Platform")]
public class StylingTests
{
    private static readonly Color Red = new(255, 255, 0, 0);
    private static readonly Color Green = new(255, 0, 255, 0);
    private static readonly Color Blue = new(255, 0, 0, 255);

    private static Form CreateForm(UIElement content)
    {
        var form = new Form { Size = new Size(400, 300), Content = content };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    private static StackPanel Column(params UIElement[] children)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        foreach (UIElement child in children)
            panel.Children.Add(child);

        return panel;
    }

    private static Point Center(UIElement element)
    {
        Point at = element.GetAbsolutePosition();
        return new Point(at.X + element.ActualSize.Width / 2f, at.Y + element.ActualSize.Height / 2f);
    }

    // ===== selectors =====

    [Fact]
    public void SelectorTextIsNormalized()
    {
        Selector selector = Selector.Parse("  StackPanel.toolbar>Button.primary:hover ,Label  ");

        Assert.Equal("StackPanel.toolbar > Button.primary:hover, Label", selector.Text);
    }

    [Fact]
    public void SpecificityFollowsCss()
    {
        static Specificity Of(string text)
        {
            Selector selector = Selector.Parse(text);
            return selector.Alternatives[0].Specificity;
        }

        Assert.Equal(new Specificity(0, 0, 1), Of("Button"));
        Assert.Equal(new Specificity(0, 1, 1), Of("Button.primary"));
        Assert.Equal(new Specificity(0, 2, 1), Of("Button.primary:hover"));
        Assert.Equal(new Specificity(1, 0, 0), Of("#save"));
        Assert.Equal(new Specificity(0, 1, 2), Of("StackPanel > Button:first-child"));
        Assert.Equal(new Specificity(0, 1, 0), Of(":only-child"));
        Assert.Equal(new Specificity(0, 1, 1), Of("Button:not(.flat)"));
        Assert.Equal(new Specificity(0, 0, 0), Of("*"));

        Assert.True(Of("#save") > Of("Button.primary.large:hover"));
    }

    [Fact]
    public void SyntaxErrorsPointAtTheMistake()
    {
        var error = Assert.Throws<SelectorSyntaxException>(() => Selector.Parse("Button > "));
        Assert.Equal(9, error.Position);

        error = Assert.Throws<SelectorSyntaxException>(() => Selector.Parse("Button..x"));
        Assert.Equal(7, error.Position);

        error = Assert.Throws<SelectorSyntaxException>(() => Selector.Parse(":nth-child(2x+1)"));
        Assert.Equal(11, error.Position);

        Assert.False(Selector.TryParse("Button ,", out _, out _));
    }

    [Fact]
    public void NthChildFormulas()
    {
        var children = Enumerable.Range(0, 7).Select(_ => new Label()).ToArray();
        CreateForm(Column(children));

        int[] Matching(string selector)
        {
            Selector parsed = Selector.Parse(selector);

            return [.. children.Select((c, i) => (c, i)).Where(p => parsed.Matches(p.c)).Select(p => p.i + 1)];
        }

        Assert.Equal(new[] { 1, 3, 5, 7 }, Matching("Label:odd"));
        Assert.Equal(new[] { 2, 4, 6 }, Matching("Label:even"));
        Assert.Equal(new[] { 3, 6 }, Matching("Label:nth-child(3n)"));
        Assert.Equal(new[] { 1, 2, 3 }, Matching("Label:nth-child(-n+3)"));
        Assert.Equal(new[] { 6, 7 }, Matching("Label:nth-last-child(-n+2)"));
        Assert.Equal(new[] { 1 }, Matching("Label:first-child"));
        Assert.Equal(new[] { 7 }, Matching("Label:last-child"));
        Assert.Empty(Matching("Label:only-child"));
    }

    [Fact]
    public void TypeSelectorMatchesDerivedTypes()
    {
        var primary = new PrimaryButton { Text = "OK" };
        CreateForm(Column(primary));

        Assert.True(Selector.Parse("Button").Matches(primary));
        Assert.True(Selector.Parse("ButtonBase").Matches(primary));
        Assert.False(Selector.Parse("Label").Matches(primary));
    }

    [Fact]
    public void DescendantAndChildCombinators()
    {
        var inner = new Button { Text = "x" };
        var nested = Column(inner);
        var outer = Column(nested);
        outer.Classes.Add("toolbar");

        CreateForm(outer);

        Assert.True(Selector.Parse(".toolbar Button").Matches(inner));
        Assert.False(Selector.Parse(".toolbar > Button").Matches(inner));
        Assert.True(Selector.Parse(".toolbar > StackPanel > Button").Matches(inner));
        Assert.True(Selector.Parse("Button:not(.flat, #other)").Matches(inner));
    }

    // ===== the ladder of sources =====

    [Fact]
    public void StyleBeatsTheThemeAndGivesTheValueBack()
    {
        var button = new Button { Text = "OK" };
        Form form = CreateForm(Column(button));
        Color themed = button.BackgroundColor;

        form.Styles.Add(new Style("Button.danger") { [ButtonBase.BackgroundColorProperty] = Red });
        Assert.Equal(themed, button.BackgroundColor);

        button.Classes.Add("danger");
        Assert.Equal(Red, button.BackgroundColor);
        Assert.True(button.IsSetByStyle(ButtonBase.BackgroundColorProperty));
        Assert.False(button.IsLocal(ButtonBase.BackgroundColorProperty));

        button.Classes.Remove("danger");
        Assert.Equal(themed, button.BackgroundColor);
        Assert.False(button.IsSetByStyle(ButtonBase.BackgroundColorProperty));
    }

    [Fact]
    public void CodeBeatsTheStyleUntilCleared()
    {
        var button = new Button { Text = "OK" };
        button.Classes.Add("danger");

        Form form = CreateForm(Column(button));
        form.Styles.Add(new Style(".danger") { [ButtonBase.BackgroundColorProperty] = Red });

        button.BackgroundColor = Green;
        Assert.Equal(Green, button.BackgroundColor);

        // a restyle for any reason keeps the value from code
        button.Classes.Add("other");
        Assert.Equal(Green, button.BackgroundColor);

        button.ClearValue(ButtonBase.BackgroundColorProperty);
        Assert.Equal(Red, button.BackgroundColor);
    }

    [Fact]
    public void MoreSpecificThenNearerThenLaterWins()
    {
        var button = new Button { Text = "OK" };
        button.Classes.Add("a");

        var panel = Column(button);
        Form form = CreateForm(panel);

        form.Styles.Add(new Style("Button.a") { [ButtonBase.BackgroundColorProperty] = Red });
        form.Styles.Add(new Style("Button") { [ButtonBase.BackgroundColorProperty] = Green });

        // more specific, though declared earlier
        Assert.Equal(Red, button.BackgroundColor);

        form.Styles.Add(new Style("Button.a") { [ButtonBase.BackgroundColorProperty] = Blue });

        // equal specificity: the later one
        Assert.Equal(Blue, button.BackgroundColor);

        panel.Styles.Add(new Style("Button.a") { [ButtonBase.BackgroundColorProperty] = Green });

        // equal specificity: the nearer scope, however late the form's style was added
        Assert.Equal(Green, button.BackgroundColor);
    }

    [Fact]
    public void SettersOfMissingPropertiesAreSkipped()
    {
        var button = new Button { Text = "OK" };
        var label = new Label { Text = "text" };

        Form form = CreateForm(Column(button, label));
        Color labelText = label.TextColor;

        form.Styles.Add(new Style("*")
        {
            [ButtonBase.BackgroundColorProperty] = Red,
        });

        Assert.Equal(Red, button.BackgroundColor);
        Assert.Equal(labelText, label.TextColor);
    }

    [Fact]
    public void StyledInheritedValueReachesDescendants()
    {
        var label = new Label { Text = "inside" };
        var own = new Label { Text = "own" };
        own.Classes.Add("own");

        var panel = Column(label, own);
        panel.Classes.Add("dark");

        Form form = CreateForm(panel);

        form.Styles.Add(new Style(".dark") { [UIElement.TextColorProperty] = Red });
        form.Styles.Add(new Style(".own") { [UIElement.TextColorProperty] = Blue });

        // the theme set the label's own color, but a style on the panel stands above it
        Assert.Equal(Red, label.TextColor);

        // the label's own style beats the panel's
        Assert.Equal(Blue, own.TextColor);
    }

    [Fact]
    public void ThemeValuesFollowTheTheme()
    {
        var button = new Button { Text = "OK" };
        button.Classes.Add("accent");

        Form form = CreateForm(Column(button));
        form.Styles.Add(new Style(".accent").Set(ButtonBase.BackgroundColorProperty, t => t.Colors.Error));

        Theme before = App.Theme;
        Drawing.Font fontBefore = Drawing.Font.Default;

        try
        {
            App.Theme = Themes.Light;
            Assert.Equal(Themes.Light.Colors.Error, button.BackgroundColor);

            App.Theme = Themes.Dark;
            Assert.Equal(Themes.Dark.Colors.Error, button.BackgroundColor);
        }
        finally
        {
            App.Theme = before;
            Drawing.Font.Default = fontBefore;
        }
    }

    [Fact]
    public void IndexerChecksTheValueType()
    {
        var style = new Style("Button");

        Assert.Throws<ArgumentException>(() => style[ButtonBase.BackgroundColorProperty] = 2f);
        Assert.Throws<ArgumentException>(() => style[ButtonBase.BackgroundColorProperty] = Style.FromTheme(t => t.Metrics));

        // a number of another numeric type is converted
        style[ButtonBase.FocusRingInsetProperty] = 3;
        Assert.Equal(3f, style[ButtonBase.FocusRingInsetProperty]);
    }

    // ===== staging =====

    [Fact]
    public void RestyleWritesAPropertyOnceAndOnlyWhenItChanges()
    {
        var button = new Button { Text = "OK" };
        button.Classes.Add("danger");

        Form form = CreateForm(Column(button));
        form.Styles.Add(new Style(".danger") { [ButtonBase.BackgroundColorProperty] = Red });
        form.Styles.Add(new Style(".big") { [UIElement.OpacityProperty] = 0.9f });

        var changes = new List<string>();
        button.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        // the theme rule writes Surface, the style Red: without staging the button
        // would flip to Surface and back on every restyle
        button.Classes.Add("big");

        Assert.DoesNotContain(nameof(ButtonBase.BackgroundColor), changes);
        Assert.Single(changes, nameof(UIElement.Opacity));
        Assert.Equal(Red, button.BackgroundColor);
    }

    // ===== pseudo-classes =====

    [Fact]
    public void HoverAppliesAndLeaves()
    {
        var button = new Button { Text = "OK", Size = new Size(100, 30) };
        var other = new Button { Text = "Other", Size = new Size(100, 30) };

        Form form = CreateForm(Column(button, other));
        Color themed = button.BackgroundColor;

        form.Styles.Add(new Style("Button:hover") { [ButtonBase.BackgroundColorProperty] = Red });

        Point center = Center(button);
        HeadlessInput.MoveMouse(form, center.X, center.Y);

        Assert.True(button.HasPseudoClass(PseudoClass.Hover));
        Assert.Equal(Red, button.BackgroundColor);
        Assert.Equal(themed, other.BackgroundColor);

        Point away = Center(other);
        HeadlessInput.MoveMouse(form, away.X, away.Y);

        Assert.False(button.HasPseudoClass(PseudoClass.Hover));
        Assert.Equal(themed, button.BackgroundColor);
        Assert.Equal(Red, other.BackgroundColor);
    }

    [Fact]
    public void CheckedFollowsTheCheckBox()
    {
        var box = new CheckBox { Text = "Remember me" };
        Form form = CreateForm(Column(box));

        form.Styles.Add(new Style("CheckBox:checked") { [UIElement.OpacityProperty] = 0.5f });
        Assert.Equal(1f, box.Opacity);

        box.IsChecked = true;
        Assert.True(box.HasPseudoClass(PseudoClass.Checked));
        Assert.Equal(0.5f, box.Opacity);

        box.IsThreeState = true;
        box.CheckedState = CheckedState.Intermediate;
        Assert.True(box.HasPseudoClass(PseudoClass.Indeterminate));
        Assert.False(box.HasPseudoClass(PseudoClass.Checked));
        Assert.Equal(1f, box.Opacity);
    }

    [Fact]
    public void DisabledIsEffective()
    {
        var button = new Button { Text = "OK" };
        var panel = Column(button);

        Form form = CreateForm(panel);
        form.Styles.Add(new Style("Button:disabled") { [UIElement.OpacityProperty] = 0.3f });

        panel.IsEnabled = false;

        Assert.True(button.HasPseudoClass(PseudoClass.Disabled));
        Assert.Equal(0.3f, button.Opacity);

        panel.IsEnabled = true;
        Assert.Equal(1f, button.Opacity);
    }

    [Fact]
    public void FocusWithinFollowsTheFocus()
    {
        var box = new TextBox { Size = new Size(200, 30) };
        var group = Column(box);
        group.Classes.Add("group");

        var button = new Button { Text = "OK" };

        Form form = CreateForm(Column(group, button));
        form.Styles.Add(new Style(".group:focus-within") { [UIElement.OpacityProperty] = 0.8f });
        form.Styles.Add(new Style("TextBox:focus") { [UIElement.OpacityProperty] = 0.7f });

        Point inBox = Center(box);
        HeadlessInput.Click(form, inBox.X, inBox.Y);

        Assert.Equal(0.8f, group.Opacity);
        Assert.Equal(0.7f, box.Opacity);

        Point onButton = Center(button);
        HeadlessInput.Click(form, onButton.X, onButton.Y);

        Assert.Equal(1f, group.Opacity);
        Assert.Equal(1f, box.Opacity);
    }

    [Fact]
    public void PositionsFollowInsertions()
    {
        var first = new Label { Text = "1" };
        var second = new Label { Text = "2" };
        var panel = Column(first, second);

        Form form = CreateForm(panel);
        form.Styles.Add(new Style("StackPanel > :first-child") { [UIElement.OpacityProperty] = 0.5f });

        Assert.Equal(0.5f, first.Opacity);
        Assert.Equal(1f, second.Opacity);

        var newFirst = new Label { Text = "0" };
        panel.Children.Insert(0, newFirst);

        // the siblings are restyled with the layout pass, not on the spot
        form.UpdateLayout();

        Assert.Equal(0.5f, newFirst.Opacity);
        Assert.Equal(1f, first.Opacity);
    }

    [Fact]
    public void FillingAListRestylesTheSiblingsOnce()
    {
        var panel = Column();
        Form form = CreateForm(panel);

        form.Styles.Add(new Style("Label:odd") { [UIElement.OpacityProperty] = 0.5f });

        var first = new Label { Text = "first" };
        panel.Children.Add(first);

        int restyles = 0;
        first.PropertyChanged += (_, _) => restyles++;

        for (int i = 0; i < 200; i++)
            panel.Children.Add(new Label { Text = i.ToString(System.Globalization.CultureInfo.InvariantCulture) });

        form.UpdateLayout();

        // the first row stays odd: two hundred insertions after it changed nothing for it
        Assert.Equal(0.5f, first.Opacity);
        Assert.Equal(0, restyles);

        Assert.Equal(1f, ((Label)panel.Children[1]).Opacity);
        Assert.Equal(0.5f, ((Label)panel.Children[2]).Opacity);

        // an insertion at the top shifts every row by one
        panel.Children.Insert(0, new Label { Text = "top" });
        form.UpdateLayout();

        Assert.Equal(1f, first.Opacity);
        Assert.Equal(0.5f, ((Label)panel.Children[2]).Opacity);
    }

    [Fact]
    public void CustomPseudoClassOfAControl()
    {
        var busy = new BusyLabel { Text = "loading" };
        Form form = CreateForm(Column(busy));

        form.Styles.Add(new Style("BusyLabel:busy") { [UIElement.OpacityProperty] = 0.4f });

        busy.IsBusy = true;
        Assert.Equal(0.4f, busy.Opacity);

        busy.IsBusy = false;
        Assert.Equal(1f, busy.Opacity);

        Assert.Throws<InvalidOperationException>(() => busy.Force(PseudoClass.Disabled));
    }

    private sealed class BusyLabel : Label
    {
        public static readonly PseudoClass Busy = PseudoClass.Register("busy");

        public bool IsBusy
        {
            get => HasPseudoClass(Busy);
            set => SetPseudoClass(Busy, value);
        }

        public void Force(PseudoClass pseudoClass) => SetPseudoClass(pseudoClass, true);
    }

    // ===== transitions =====

    [Fact]
    public void StyleTransitionsAnimateBothWays()
    {
        var button = new Button { Text = "OK", Size = new Size(100, 30) };
        var other = new Button { Text = "Other", Size = new Size(100, 30) };

        Form form = CreateForm(Column(button, other));
        Color themed = button.BackgroundColor;

        form.Styles.Add(new Style("Button")
        {
            Transitions = { Transition.Ease(ButtonBase.BackgroundColorProperty, 200, Easing.Linear) },
        });

        form.Styles.Add(new Style("Button:hover") { [ButtonBase.BackgroundColorProperty] = Red });

        Point center = Center(button);
        HeadlessInput.MoveMouse(form, center.X, center.Y);

        form.Clock.Advance(TimeSpan.FromMilliseconds(100));

        Color halfway;
        using (UIElement.BeginPresentation())
            halfway = button.BackgroundColor;

        // the target is read at once, the picture is on its way
        Assert.Equal(Red, button.BackgroundColor);
        Assert.NotEqual(Red, halfway);
        Assert.NotEqual(themed, halfway);

        form.Clock.Advance(TimeSpan.FromMilliseconds(300));

        Point away = Center(other);
        HeadlessInput.MoveMouse(form, away.X, away.Y);
        form.Clock.Advance(TimeSpan.FromMilliseconds(100));

        using (UIElement.BeginPresentation())
            halfway = button.BackgroundColor;

        // the way back is animated by the base style's rule
        Assert.Equal(themed, button.BackgroundColor);
        Assert.NotEqual(themed, halfway);
    }
}