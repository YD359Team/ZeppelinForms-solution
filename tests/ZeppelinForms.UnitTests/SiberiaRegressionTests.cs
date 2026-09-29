using System.ComponentModel;
using System.Runtime.CompilerServices;
using Xunit;
using ZeppelinForms.Animation;
using ZeppelinForms.Data;
using ZeppelinForms.Diagnostics;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Effects;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Theming;

namespace ZeppelinForms.UnitTests;

/// <summary>
/// Regression tests for the bugs fixed in 0.13.0 "Siberia". Each one is built so
/// that it fails on the code before the fix — a test that passes either way guards
/// nothing.
/// </summary>
[Collection("Platform")]
public class SiberiaRegressionTests
{
    private static Form CreateForm(UIElement content, float width = 400, float height = 300)
    {
        var form = new Form { Size = new Size(width, height), Content = content };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    // ===== hit testing under rotation =====

    [Fact]
    public void ClickLandsOnRotatedElementWhereItIsDrawn()
    {
        int clicks = 0;

        // no Margin: it is applied by panels, and the form's root content
        // doesn't get it — the button stands at (0, 0)
        var button = new Button
        {
            Size = new Size(200, 20),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Rotation = 90f,
        };

        button.Click += (_, _) => clicks++;

        Form form = CreateForm(button);

        // turned a quarter around its center (100, 10), the button stands upright:
        // x 90..110, y -90..110. At twice the angle — the old bug — it would lie
        // flat again, x 0..200, y 0..20, and this click would miss
        HeadlessInput.Click(form, 100, 80);
        Assert.Equal(1, clicks);

        // where it lay before turning, it is no longer
        HeadlessInput.Click(form, 180, 10);
        Assert.Equal(1, clicks);
    }

    // ===== the first frame of a layout transition =====

    private sealed class FixedBox : UnitControl
    {
        public override void Draw(Graphics g) { }

        protected override Size MeasureOverride(Size availableSize) => new(100, 40);
    }

    [Fact]
    public void FirstTransitionFrameRepaintsWhereTheRowWasDrawn()
    {
        var first = new FixedBox();

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ChildrenLayoutTransition = LayoutTransition.Ease(200, Easing.Linear),
        };

        panel.Children.Add(first);
        panel.Children.Add(new FixedBox());

        Form form = CreateForm(panel);

        panel.Children.Insert(0, new FixedBox());
        form.UpdateLayout();

        // the frame the transition starts in: `first` has moved to y 40..80 by layout,
        // but is drawn at its old place, y 0..40. Horizontally the boxes are centered —
        // UnitControl's default alignment — so they span x 150..250
        ElementTreeRenderer.Draw(form.Content!, new HeadlessGraphics());
        form.TakeDirtyRegion();

        form.Clock.Advance(TimeSpan.FromMilliseconds(100));

        // the first tick must repaint the old place too: the row has moved away
        // from it, and nothing else knows it was ever drawn there
        Rectangle? dirty = form.TakeDirtyRegion();

        Assert.NotNull(dirty);
        Assert.True(dirty.Value.Contains(new Point(200, 5)),
            $"the first tick must repaint where the row was drawn; dirty was {dirty}");
    }

    // ===== DataGridView selection =====

    private sealed record Person(string Name, int Age);

    private static DataGridView CreateGrid(int count)
    {
        var grid = new DataGridView
        {
            Columns =
            {
                new DataGridViewColumn { Header = "Name", Value = item => ((Person)item).Name },
            },
        };

        for (int i = 0; i < count; i++)
            grid.Items.Add(new Person($"P{i:D2}", i));

        return grid;
    }

    [Fact]
    public void RemovingFromSortedGridKeepsSelection()
    {
        DataGridView grid = CreateGrid(10);

        // descending: the last source record is on row 0
        grid.SortBy(0, descending: true);
        grid.SelectedIndex = 0;

        object selected = grid.SelectedItem!;
        Assert.Same(grid.Items[^1], selected);

        // used to throw: the selection was read through the stale sort order,
        // whose index 9 no longer existed in the nine remaining items
        grid.Items.RemoveAt(0);

        Assert.Same(selected, grid.SelectedItem);
    }

    [Fact]
    public void SelectionFollowsRecordWhenRowIsInsertedAbove()
    {
        DataGridView grid = CreateGrid(10);

        grid.SelectedIndex = 5;
        object selected = grid.SelectedItem!;

        grid.Items.Insert(0, new Person("New", -1));

        Assert.Same(selected, grid.SelectedItem);
        Assert.Equal(6, grid.SelectedIndex);
    }

    [Fact]
    public void RemovedSelectedRecordClearsSelection()
    {
        DataGridView grid = CreateGrid(10);

        grid.SelectedIndex = 5;
        grid.Items.Remove(grid.SelectedItem!);

        Assert.Equal(-1, grid.SelectedIndex);
    }

    // ===== closed forms and the theme =====

    [Fact]
    public void ClosedFormIsCollected()
    {
        WeakReference form = ShowAndClose();

        for (int i = 0; i < 3 && form.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        // App.ThemeChanged is static, and a closed form used to stay subscribed:
        // every MessageBox ever shown stayed in memory with its whole tree
        Assert.False(form.IsAlive, "a closed form must not be held by App.ThemeChanged");
    }

    /// <summary>In its own method, so that no local of the test keeps the form
    /// alive: a Debug build extends locals to the end of their method.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ShowAndClose()
    {
        var form = new Form { Size = new Size(200, 100), Content = new Label { Text = "x" } };

        new HeadlessPlatform().CreateWindow(form);
        form.Show();
        form.Close();

        return new WeakReference(form);
    }

    [Fact]
    public void FormShownAgainTakesThemeChangedWhileClosed()
    {
        Theme themeBefore = App.Theme;
        Font fontBefore = Font.Default;

        try
        {
            App.Theme = Themes.Light;

            var button = new Button { Text = "x" };
            var form = new Form { Size = new Size(200, 100), Content = button };
            var platform = new HeadlessPlatform();

            platform.CreateWindow(form);
            form.Show();
            form.Close();

            // the form no longer listens to the theme while closed
            App.Theme = Themes.Dark;

            // shown again: it must catch up with the theme switched in the meantime
            platform.CreateWindow(form);

            Assert.Equal(Themes.Dark.Colors.Surface, button.BackgroundColor);
        }
        finally
        {
            // the theme setter also replaces Font.Default, and the snapshot tests
            // in this collection depend on the font they set up
            App.Theme = themeBefore;
            Font.Default = fontBefore;
        }
    }

    // ===== TextBox =====

    private sealed class NumberModel : INotifyPropertyChanged
    {
        private int _value;

        public int Value
        {
            get => _value;
            set
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class TestClipboard(string? text) : IClipboard
    {
        public string? Text { get; private set; } = text;

        public string? GetText() => Text;

        public void SetText(string text) => Text = text;
    }

    /// <summary>A form whose only field has the focus — Form.Show gives it.</summary>
    private static Form ShowWithField(UIElement content)
    {
        Form form = CreateForm(content);
        form.Show();

        return form;
    }

    [Fact]
    public void TypingPartialNumberIntoTwoWayBindingDoesNotThrow()
    {
        ContractViolationBehavior before = ZfContract.Behavior;
        ZfContract.Behavior = ContractViolationBehavior.Throw;

        try
        {
            var model = new NumberModel { Value = 3 };
            var box = new TextBox();

            box.Bind(TextBox.TextProperty, model, nameof(NumberModel.Value), BindingMode.TwoWay);

            Form form = ShowWithField(box);

            // "" and "-" are not numbers — ordinary intermediate states of typing.
            // In a Debug build the binding used to report them as contract
            // violations, and ZfContract threw right on the keystroke
            box.Text = string.Empty;
            HeadlessInput.TypeText(form, "-");
            HeadlessInput.TypeText(form, "5");

            Assert.Equal(-5, model.Value);
        }
        finally
        {
            ZfContract.Behavior = before;
        }
    }

    [Fact]
    public void PasteTakesFirstLineInSingleLineField()
    {
        IClipboard before = Clipboard.Current;
        Clipboard.Current = new TestClipboard("first\r\nsecond");

        try
        {
            var box = new TextBox();
            Form form = ShowWithField(box);

            // the body of the Ctrl+V branch used to be lost: pasting did nothing
            HeadlessInput.PressKey(form, Key.V, KeyModifiers.Control);

            Assert.Equal("first", box.Text);
        }
        finally
        {
            Clipboard.Current = before;
        }
    }

    [Fact]
    public void PasteIsCutToMaxLength()
    {
        IClipboard before = Clipboard.Current;
        Clipboard.Current = new TestClipboard("abcdefgh");

        try
        {
            var box = new TextBox { MaxLength = 5 };
            Form form = ShowWithField(box);

            // an insertion that didn't fit used to be dropped entirely
            HeadlessInput.PressKey(form, Key.V, KeyModifiers.Control);

            Assert.Equal("abcde", box.Text);
        }
        finally
        {
            Clipboard.Current = before;
        }
    }

    [Fact]
    public void CtrlShiftZRedoes()
    {
        var box = new TextBox();
        Form form = ShowWithField(box);

        HeadlessInput.TypeText(form, "ab");

        HeadlessInput.PressKey(form, Key.Z, KeyModifiers.Control);
        Assert.Equal(string.Empty, box.Text);

        // used to undo a second time, like plain Ctrl+Z
        HeadlessInput.PressKey(form, Key.Z, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.Equal("ab", box.Text);
    }

    [Fact]
    public void OwnTextTransformCasesTypedText()
    {
        var box = new TextBox { TextTransform = TextTransform.UpperCase };
        Form form = ShowWithField(box);

        HeadlessInput.TypeText(form, "ab");

        Assert.Equal("AB", box.Text);
    }

    [Fact]
    public void InheritedTextTransformLeavesTypedTextAlone()
    {
        var box = new TextBox();
        var panel = new StackPanel { TextTransform = TextTransform.UpperCase };

        panel.Children.Add(box);

        Form form = ShowWithField(panel);

        // a panel with upper-case captions must not rewrite what is typed into its fields
        HeadlessInput.TypeText(form, "ab");

        Assert.Equal("ab", box.Text);
    }

    [Fact]
    public void ShownFormFocusesItsFirstTextField()
    {
        var button = new Button { Text = "OK" };
        var box = new TextBox();
        var panel = new StackPanel();

        panel.Children.Add(button);
        panel.Children.Add(box);

        ShowWithField(panel);

        // the first text field, not the first tab stop: a focus ring on a random
        // button, or Enter pressing it, is not what a form should open with
        Assert.True(box.IsFocused);
        Assert.False(button.IsFocused);
    }

    // ===== gestures =====

    [Fact]
    public void PanThatNeverStartedIsNotCancelled()
    {
        var surface = new Panel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        // refuses every contact before the threshold, as a scrolling panel with
        // nothing to scroll does
        var pan = surface.AddGesture(new PanGestureRecognizer { CanBegin = _ => false });

        int started = 0;
        int cancelled = 0;

        pan.Started += (_, _) => started++;
        pan.Cancelled += (_, _) => cancelled++;

        Form form = CreateForm(surface);

        HeadlessInput.TouchDown(form, 0, 100, 100);
        HeadlessInput.TouchUp(form, 0, 100, 100);

        // a subscriber used to get the cancel of a gesture it never saw start;
        // TouchScroller restarted its bounce on exactly that
        Assert.Equal(0, started);
        Assert.Equal(0, cancelled);
    }

    // ===== Glitch =====

    [Fact]
    public void StoppedGlitchDoesNotComeBackOnReattach()
    {
        var label = new Label { Text = "x" };
        var host = new StackPanel();

        host.Children.Add(label);

        Form form = CreateForm(host);

        label.Glitch();
        label.StopGlitch();

        // a tick with nothing to animate stops the frames
        form.Tick();

        host.Children.Remove(label);
        host.Children.Add(label);

        // the Attached handler used to outlive StopGlitch and restart the animation
        // of an effect no longer in the chain: frames never stopped again
        Assert.Null(label.Effects.Get<GlitchEffect>());
        Assert.False(form.PlatformWindow!.Frames.IsRunning);
    }

    [Fact]
    public void RepeatedGlitchDoesNotStack()
    {
        var label = new Label { Text = "x" };
        CreateForm(label);

        label.Glitch();
        label.Glitch();

        Assert.Single(label.Effects.Effects, effect => effect is GlitchEffect);

        label.StopGlitch();

        Assert.Null(label.Effects.Get<GlitchEffect>());
    }
}