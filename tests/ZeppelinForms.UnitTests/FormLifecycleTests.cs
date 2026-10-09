using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.UnitTests;

/// <summary>The 0.14 release fixes: the form's size, its events and virtual
/// methods, its frame, the dispatcher; the hover of a styled button, the clip of
/// a padded panel, the text line of a field.</summary>
public sealed class FormLifecycleTests
{
    private static (Form Form, HeadlessPlatform Platform, HeadlessWindow Window) Create(
        UIElement? content = null, Form? form = null)
    {
        form ??= new Form();
        form.Content = content;

        var platform = new HeadlessPlatform();
        var window = (HeadlessWindow)platform.CreateWindow(form);
        form.UpdateLayout();

        return (form, platform, window);
    }

    // ===== Size =====

    [Fact]
    public void AFormWithoutASizeGetsTheDefaultOne()
    {
        var form = new Form();

        Assert.Equal(new Size(800, 600), form.Size);
        Assert.False(form.IsSizeSet);

        (_, _, HeadlessWindow window) = Create(form: form);
        form.Show();

        Assert.True(window.IsShown);
        Assert.Equal(new Size(800, 600), form.ClientSize);
    }

    [Fact]
    public void ASetSizeReplacesTheDefault()
    {
        var form = new Form { Size = new Size(320, 200) };

        Assert.True(form.IsSizeSet);
        Assert.Equal(new Size(320, 200), form.Size);
    }

    [Fact]
    public void AFormOfTheApplicationNamesItsOwnDefault()
    {
        Assert.Equal(new Size(400, 250), new SmallForm().Size);
    }

    private sealed class SmallForm : Form
    {
        protected override Size DefaultSize => new(400, 250);
    }

    // ===== Life =====

    [Fact]
    public void LoadComesOnceBeforeTheFirstShownAndShownComesEveryTime()
    {
        (Form form, _, _) = Create();
        List<string> log = [];

        form.Load += (_, _) => log.Add("Load");
        form.Shown += (_, _) => log.Add("Shown");

        form.Show();
        form.Show();

        Assert.Equal(["Load", "Shown", "Shown"], log);
    }

    [Fact]
    public void AnOverrideHearsTheLifeWithoutSubscribing()
    {
        var form = new LoggingForm();
        Create(form: form);

        form.Show();
        form.Close();

        Assert.Equal(["Load", "Shown", "Closing Code", "Closed"], form.Log);
    }

    private sealed class LoggingForm : Form
    {
        public List<string> Log { get; } = [];

        protected override void OnLoad(EventArgs e) { Log.Add("Load"); base.OnLoad(e); }
        protected override void OnShown(EventArgs e) { Log.Add("Shown"); base.OnShown(e); }
        protected override void OnClosing(FormClosingEventArgs e) { Log.Add($"Closing {e.Reason}"); base.OnClosing(e); }
        protected override void OnClosed(EventArgs e) { Log.Add("Closed"); base.OnClosed(e); }
    }

    [Fact]
    public void ClosingCanKeepTheFormOpen()
    {
        (Form form, _, HeadlessWindow window) = Create();
        form.Show();

        bool closed = false;
        form.Closed += (_, _) => closed = true;
        form.Closing += (_, e) => e.Cancel = true;

        form.Close();

        Assert.False(window.IsClosed);
        Assert.False(closed);
    }

    [Fact]
    public void ARefusedAcceptLeavesNoResultBehind()
    {
        (Form form, _, HeadlessWindow window) = Create();
        bool refuse = true;
        List<bool> acceptedAtClosing = [];

        form.Closing += (_, e) =>
        {
            e.Cancel = refuse;
            acceptedAtClosing.Add(form.Result<string>().IsAccepted);
        };

        form.Accept("value");

        // the handler saw what the dialog was closing with, and kept it open
        Assert.Equal([true], acceptedAtClosing);
        Assert.False(window.IsClosed);
        Assert.False(form.Result<string>().IsAccepted);

        // closed later in another way, the dialog doesn't return the refused value
        refuse = false;
        form.Close();

        Assert.Equal([true, false], acceptedAtClosing);
        Assert.True(window.IsClosed);
        Assert.False(form.Result<string>().IsAccepted);
    }

    [Fact]
    public void CloseFromInsideClosingDoesNotRecurse()
    {
        (Form form, _, HeadlessWindow window) = Create();
        form.Show();

        int closings = 0;
        form.Closing += (_, _) =>
        {
            closings++;
            form.Close();
        };

        form.Close();

        Assert.Equal(1, closings);
        Assert.True(window.IsClosed);
    }

    [Fact]
    public void ActivationIsReportedOncePerChange()
    {
        (Form form, _, _) = Create();
        List<string> log = [];

        form.Activated += (_, _) => log.Add("Activated");
        form.Deactivated += (_, _) => log.Add("Deactivated");

        form.OnWindowActivated(true);
        form.OnWindowActivated(true);
        Assert.True(form.IsActive);

        form.OnWindowActivated(false);
        Assert.False(form.IsActive);

        Assert.Equal(["Activated", "Deactivated"], log);
    }

    [Fact]
    public void ThePlatformsStateChangeRaisesWindowStateChanged()
    {
        (Form form, _, _) = Create();
        int changes = 0;
        form.WindowStateChanged += (_, _) => changes++;

        form.SetWindowStateFromPlatform(WindowState.Maximized);
        form.WindowState = WindowState.Normal;

        Assert.Equal(2, changes);
    }

    // ===== Keyboard =====

    [Fact]
    public void PreviewKeyDownTakesTheKeyBeforeTheFocusedElement()
    {
        var box = new TextBox { Text = "abc" };
        (Form form, _, _) = Create(box);
        form.FocusElement(box);

        form.PreviewKeyDown += (_, e) => e.Handled = e.Key == Key.Backspace;

        form.OnKeyDown(Key.Backspace, KeyModifiers.None, isRepeat: false);

        Assert.Equal("abc", box.Text);
    }

    [Fact]
    public void KeyDownHearsWhatNoElementTook()
    {
        (Form form, _, _) = Create(new Label { Text = "nothing focusable" });

        List<Key> keys = [];
        form.KeyDown += (_, e) => keys.Add(e.Key);
        form.KeyUp += (_, e) => keys.Add(e.Key);

        form.OnKeyDown(Key.F5, KeyModifiers.None, isRepeat: false);
        form.OnKeyUp(Key.F5, KeyModifiers.None);

        Assert.Equal([Key.F5, Key.F5], keys);
    }

    [Fact]
    public void TextInputCanKeepACharacterFromTheField()
    {
        var box = new TextBox();
        (Form form, _, _) = Create(box);
        form.FocusElement(box);

        form.TextInput += (_, e) => e.Handled = !char.IsDigit(e.Character);

        foreach (char c in "a1b2")
            form.OnTextInput(c);

        Assert.Equal("12", box.Text);
    }

    // ===== Pointer =====

    [Fact]
    public void ThePointerEventsObserveWithoutTakingTheClick()
    {
        var button = new Button { Text = "OK", Size = new Size(100, 30) };
        (Form form, _, _) = Create(button);

        int clicks = 0;
        button.Click += (_, _) => clicks++;

        List<string> log = [];
        form.PointerPressed += (_, e) => { log.Add("Pressed"); e.Handled = true; };
        form.PointerMoved += (_, _) => log.Add("Moved");
        form.PointerReleased += (_, e) => { log.Add("Released"); e.Handled = true; };

        Point origin = button.GetAbsolutePosition();
        var center = new Point(origin.X + 10, origin.Y + 10);
        form.OnPointerMove(center);
        form.OnPointerDown(center);
        form.OnPointerUp(center);

        Assert.Equal(["Moved", "Pressed", "Released"], log);
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void PreviewMouseWheelKeepsTheWheelFromTheElements()
    {
        var list = new StackPanel { OverflowY = Overflow.Auto, Size = new Size(200, 100) };

        for (int i = 0; i < 30; i++)
            list.Children.Add(new Label { Text = $"Row {i}" });

        (Form form, _, _) = Create(list);

        int zooms = 0;
        form.PreviewMouseWheel += (_, e) =>
        {
            zooms++;
            e.Handled = true;
        };

        form.OnMouseWheel(new Point(20, 20), delta: -120);

        Assert.Equal(1, zooms);
        Assert.Equal(0f, list.ScrollY);
    }

    // ===== Frame =====

    [Fact]
    public void TheFrameFollowsThePropertiesWhileTheWindowIsOpen()
    {
        (Form form, _, HeadlessWindow window) = Create();
        int before = window.ChromeUpdateCount;

        form.FormBorderStyle = FormBorderStyle.None;
        form.ControlBox = false;
        form.ShowInTaskbar = false;
        form.CanMinimize = false;
        form.CanMaximize = false;
        form.CanResize = false;

        // the same value again is not a change
        form.ShowInTaskbar = false;

        Assert.Equal(before + 6, window.ChromeUpdateCount);
    }

    [Fact]
    public void OnlyASizingFrameCanBeResized()
    {
        var form = new Form();
        Assert.True(form.IsResizable);

        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        Assert.False(form.IsResizable);

        form.FormBorderStyle = FormBorderStyle.SizableToolWindow;
        Assert.True(form.IsResizable);

        form.CanResize = false;
        Assert.False(form.IsResizable);
    }

    [Fact]
    public void TheButtonsGoWithTheControlBoxAndAreNotOnAToolWindow()
    {
        var form = new Form();
        Assert.True(form.HasMinimizeBox);
        Assert.True(form.HasMaximizeBox);

        form.ControlBox = false;
        Assert.False(form.HasMinimizeBox);
        Assert.False(form.HasMaximizeBox);

        form.ControlBox = true;
        form.FormBorderStyle = FormBorderStyle.FixedToolWindow;
        Assert.False(form.HasMinimizeBox);
        Assert.False(form.HasMaximizeBox);
    }

    // ===== Dispatcher =====

    [Fact]
    public void InvokeOnTheUiThreadRunsAtOnce()
    {
        (Form form, _, _) = Create();
        bool ran = false;

        form.Invoke(() => ran = true);

        Assert.True(ran);
        Assert.Equal(42, form.Invoke(() => 42));
        Assert.True(form.Dispatcher.CheckAccess());
    }

    [Fact]
    public void BeginInvokeWaitsForTheQueueEvenOnTheUiThread()
    {
        (Form form, HeadlessPlatform platform, _) = Create();
        bool ran = false;

        form.BeginInvoke(() => ran = true);
        Assert.False(ran);

        platform.PumpAll();
        Assert.True(ran);
    }

    [Fact]
    public async Task InvokeFromAnotherThreadRunsOnTheUiThreadAndWaits()
    {
        (Form form, HeadlessPlatform platform, _) = Create();
        int uiThread = Environment.CurrentManagedThreadId;

        Task<(bool Access, int Thread)> worker = Task.Factory.StartNew(
            () =>
            {
                bool access = form.Dispatcher.CheckAccess();
                int thread = form.Invoke(() => Environment.CurrentManagedThreadId);
                return (access, thread);
            },
            TestContext.Current.CancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        // the headless loop is the test's own: it is pumped until the worker gets its answer
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);

        while (!worker.IsCompleted && DateTime.UtcNow < deadline)
        {
            platform.PumpAll();
            Thread.Sleep(1);
        }

        (bool access, int thread) = await worker;

        Assert.False(access);
        Assert.Equal(uiThread, thread);
    }

    [Fact]
    public async Task AnExceptionOfInvokeReachesTheCallingThread()
    {
        (Form form, HeadlessPlatform platform, _) = Create();

        Task<Exception?> worker = Task.Factory.StartNew(
            () =>
            {
                try
                {
                    form.Invoke(() => throw new FormatException("on the UI thread"));
                    return null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            },
            TestContext.Current.CancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        DateTime deadline = DateTime.UtcNow.AddSeconds(5);

        while (!worker.IsCompleted && DateTime.UtcNow < deadline)
        {
            platform.PumpAll();
            Thread.Sleep(1);
        }

        Assert.IsType<FormatException>(await worker);
    }

    [Fact]
    public async Task InvokeAsyncCompletesWhenTheWorkRan()
    {
        (Form form, HeadlessPlatform platform, _) = Create();

        Task<int> task = form.Dispatcher.InvokeAsync(() => 7);
        Assert.False(task.IsCompleted);

        platform.PumpAll();

        Assert.Equal(7, await task);
    }

    [Fact]
    public async Task InvokeAsyncWaitsForTheWholeAsyncWork()
    {
        (Form form, HeadlessPlatform platform, _) = Create();
        var gate = new TaskCompletionSource();

        Task<int> withResult = form.Dispatcher.InvokeAsync(async () =>
        {
            await gate.Task;
            return 5;
        });

        Task withoutResult = form.Dispatcher.InvokeAsync(async () => await gate.Task);

        platform.PumpAll();

        // the work has started and stopped at its await: not done yet
        Assert.False(withResult.IsCompleted);
        Assert.False(withoutResult.IsCompleted);

        gate.SetResult();

        Assert.Equal(5, await withResult);
        await withoutResult;
    }

    [Fact]
    public void AThreadWhoseWindowsClosedKeepsItsOwnLoop()
    {
        // this thread opens a window and closes it: it has no windows left
        (Form first, _, _) = Create();
        first.Show();
        first.Close();

        // meanwhile another thread opens one and keeps it — as the Windows
        // platform test does, with its window and its message loop
        using var opened = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        var other = new Thread(() =>
        {
            Create();
            opened.Set();
            release.Wait();
        })
        {
            IsBackground = true,
        };

        other.Start();
        opened.Wait(TestContext.Current.CancellationToken);

        try
        {
            // a new window here: its work runs in this thread's loop, not in the
            // other thread's, where nobody would ever run it
            (Form form, HeadlessPlatform platform, _) = Create();

            Task<int> task = form.Dispatcher.InvokeAsync(() => 7);
            platform.PumpAll();

            Assert.True(task.IsCompletedSuccessfully);
            Assert.True(form.Dispatcher.CheckAccess());
        }
        finally
        {
            release.Set();
            other.Join();
        }
    }

    // ===== Button: hover over a styled background =====

    private sealed class ProbeButton : Button
    {
        // not Background: UIElement has one of its own
        public Color StateBackground => CurrentBackground;

        public void Hover(bool value) => IsHovered = value;
    }

    [Fact]
    public void AStyleSheetBackgroundKeepsItsOwnHover()
    {
        var button = new ProbeButton { Text = "OK" };
        button.Classes.Add("accent");

        (Form form, _, _) = Create(button);
        form.Styles.Add(StyleSheet.Parse(".accent { BackgroundColor: #0078D4; TextColor: #FFFFFF; }", "test.zss"));

        Color background = button.Background;
        button.Hover(true);

        // the theme's grey hover under white text was unreadable
        Assert.Equal(new Color(255, 0x00, 0x78, 0xD4), background);
        Assert.Equal(background, button.Background);
    }

    [Fact]
    public void AStyleSheetHoverStillWins()
    {
        var button = new ProbeButton { Text = "OK" };
        button.Classes.Add("accent");

        (Form form, _, _) = Create(button);
        form.Styles.Add(StyleSheet.Parse("""
            .accent { BackgroundColor: #0078D4; TextColor: #FFFFFF; }
            .accent:hover { BackgroundColor: #005A9E; }
            """, "test.zss"));

        button.Hover(true);

        Assert.Equal(new Color(255, 0x00, 0x5A, 0x9E), button.Background);
    }

    [Fact]
    public void ABackgroundFromCodeLeansTowardTheTextOnHover()
    {
        var button = new ProbeButton
        {
            Text = "OK",
            BackgroundColor = new Color(255, 0, 0, 0),
            TextColor = new Color(255, 255, 255, 255),
        };

        Create(button);
        button.Hover(true);

        Color hovered = button.Background;

        // lighter than the black background, and nowhere near the theme's grey
        Assert.True(hovered.R > 0 && hovered.R < 64);
    }

    [Fact]
    public void TheThemeHoverStaysOnAnUnstyledButton()
    {
        var button = new ProbeButton { Text = "OK" };
        Create(button);

        button.Hover(true);

        Assert.Equal(button.HoverBackgroundColor, button.Background);
    }

    // ===== Clip of a padded panel =====

    private sealed class ProbePanel : StackPanel
    {
        public Rectangle Clip => ClipBounds;

        public Rectangle View => Viewport;
    }

    [Fact]
    public void APanelThatDoesNotScrollClipsAtItsOwnEdge()
    {
        var panel = new ProbePanel { Padding = new Thickness(8), Size = new Size(200, 100) };
        panel.Children.Add(new CheckBox { Text = "Last" });

        Create(panel);

        Assert.Equal(panel.LocalBounds, panel.Clip);
    }

    [Fact]
    public void AScrollingPanelStillClipsAtTheViewportAlongItsAxis()
    {
        var panel = new ProbePanel
        {
            Padding = new Thickness(8),
            Size = new Size(200, 100),
            OverflowY = Overflow.Auto,
        };

        for (int i = 0; i < 20; i++)
            panel.Children.Add(new Label { Text = $"Row {i}" });

        Create(panel);

        Rectangle clip = panel.Clip;

        Assert.Equal(panel.LocalBounds.X, clip.X);
        Assert.Equal(panel.LocalBounds.Width, clip.Width);
        Assert.Equal(panel.View.Y, clip.Y);
        Assert.Equal(panel.View.Height, clip.Height);
    }

    // ===== A field draws its text as a line =====

    private sealed class LineRecorder : HeadlessGraphics
    {
        public List<string> Lines { get; } = [];
        public List<string> Texts { get; } = [];

        public override void DrawTextLine(
            string text, Rectangle rect, Color color, Font font,
            HorizontalContentAlignment hAlign = HorizontalContentAlignment.Left,
            VerticalContentAlignment vAlign = VerticalContentAlignment.Center) =>
            Lines.Add(text);

        public override void DrawText(
            string text, Rectangle rect, Color color, Font font,
            HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
            VerticalContentAlignment vAlign = VerticalContentAlignment.Center) =>
            Texts.Add(text);
    }

    [Fact]
    public void ATextBoxDrawsItsTextByTheFontsBaselineNotByItsInk()
    {
        var box = new TextBox { Text = "ay", Size = new Size(200, 30) };
        Create(box);

        var g = new LineRecorder();
        box.Draw(g);

        // a line placed by its ink jumped as letters with descenders were typed
        Assert.Contains("ay", g.Lines);
        Assert.DoesNotContain("ay", g.Texts);
    }
}