using System.Collections.Concurrent;
using Xunit;
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
/// Style sheets of 0.14.0: the .zss text, its values, nesting, variables, imports,
/// diagnostics and reloading. Like StylingTests, sheets go into the form's styles.
/// </summary>
[Collection("Platform")]
public class StyleSheetTests
{
    private static Form CreateForm(params UIElement[] children)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        foreach (UIElement child in children)
            panel.Children.Add(child);

        var form = new Form { Size = new Size(400, 300), Content = panel };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    private static StyleSheet Parse(string text)
    {
        StyleSheet sheet = StyleSheet.Parse(text, "test.zss");

        Assert.Empty(sheet.Diagnostics);
        return sheet;
    }

    // ===== values =====

    [Fact]
    public void ColorsInEveryForm()
    {
        var a = new Button { Text = "a" };
        var b = new Button { Text = "b" };
        var c = new Button { Text = "c" };
        var d = new Button { Text = "d" };
        a.Classes.Add("a"); b.Classes.Add("b"); c.Classes.Add("c"); d.Classes.Add("d");

        Form form = CreateForm(a, b, c, d);
        form.Styles.Add(Parse("""
            .a { BackgroundColor: #f00; }
            .b { BackgroundColor: #00ff0080; }
            .c { BackgroundColor: rgba(0, 0, 255, 0.5); }
            .d { BackgroundColor: transparent; }
            """));

        Assert.Equal(new Color(255, 255, 0, 0), a.BackgroundColor);
        Assert.Equal(new Color(128, 0, 255, 0), b.BackgroundColor);
        Assert.Equal(new Color(128, 0, 0, 255), c.BackgroundColor);
        Assert.Equal(Colors.Transparent, d.BackgroundColor);
    }

    [Fact]
    public void StructsTakeConstructorArguments()
    {
        var button = new Button { Text = "OK" };
        Form form = CreateForm(button);

        form.Styles.Add(Parse("""
            Button {
                Padding: 14 6;
                CornerRadius: 1 2 3 4;
                FocusRingInset: 3px;
                box-shadow: 0 2 8 0 #0003;
            }
            """));

        Assert.Equal(new Thickness(14, 6), button.Padding);
        Assert.Equal(new CornerRadius(1, 2, 3, 4), button.CornerRadius);
        Assert.Equal(3f, button.FocusRingInset);
        Assert.Equal(new BoxShadow(0, 2, 8, 0, new Color(0x33, 0, 0, 0)), button.BoxShadow);
    }

    [Fact]
    public void StaticMembersEnumsAndNone()
    {
        var label = new Label { Text = "x" };
        Form form = CreateForm(label);

        form.Styles.Add(Parse("""
            Label {
                BoxShadow: medium;
                HorizontalAlignment: right;
                text-transform: upper-case;
            }
            Label.flat { BoxShadow: none; }
            """));

        Assert.Equal(BoxShadow.Medium, label.BoxShadow);
        Assert.Equal(HorizontalAlignment.Right, label.HorizontalAlignment);
        Assert.Equal(TextTransform.UpperCase, label.TextTransform);

        label.Classes.Add("flat");
        Assert.Null(label.BoxShadow);
    }

    [Fact]
    public void ThemeReferencesFollowTheTheme()
    {
        var button = new Button { Text = "OK" };
        Form form = CreateForm(button);

        form.Styles.Add(Parse("""
            Button {
                BackgroundColor: @Error;
                TextColor: alpha(@Colors.Text, 0.5);
                CornerRadius: @Metrics.ControlCornerRadius;
            }
            """));

        Theme before = App.Theme;
        Drawing.Font fontBefore = Drawing.Font.Default;

        try
        {
            foreach (Theme theme in new[] { Themes.Light, Themes.FluentDark })
            {
                App.Theme = theme;

                Assert.Equal(theme.Colors.Error, button.BackgroundColor);
                Assert.Equal(theme.Colors.Text.WithA(128), button.TextColor);
                Assert.Equal(theme.Metrics.ControlCornerRadius, button.CornerRadius);
            }
        }
        finally
        {
            App.Theme = before;
            Drawing.Font.Default = fontBefore;
        }
    }

    [Fact]
    public void ANameMeansTheElementsOwnProperty()
    {
        var box = new CheckBox { Text = "a" };
        var radio = new RadioButton { Text = "b" };
        box.Classes.Add("accent");
        radio.Classes.Add("accent");

        Form form = CreateForm(box, radio);
        form.Styles.Add(Parse(".accent { CheckColor: #123456; }"));

        // two different properties of the same name, each element gets its own
        Assert.Equal(new Color(255, 0x12, 0x34, 0x56), box.CheckColor);
        Assert.Equal(new Color(255, 0x12, 0x34, 0x56), radio.CheckColor);
    }

    // ===== structure =====

    [Fact]
    public void NestingVariablesAndComments()
    {
        var button = new Button { Text = "OK", Size = new Size(100, 30) };
        var label = new Label { Text = "inside" };
        button.Classes.Add("primary");

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        toolbar.Classes.Add("toolbar");
        toolbar.Children.Add(label);

        Form form = CreateForm(button, toolbar);

        form.Styles.Add(Parse("""
            /* the accent of the whole sheet */
            $accent: #102030;
            $soft: alpha($accent, 0.25);

            Button.primary {
                BackgroundColor: $accent; // a line comment
                &:hover { BackgroundColor: $soft; }
            }

            .toolbar {
                Label { Opacity: 0.5; }
            }
            """));

        Assert.Equal(new Color(255, 0x10, 0x20, 0x30), button.BackgroundColor);
        Assert.Equal(0.5f, label.Opacity);

        Point at = button.GetAbsolutePosition();
        HeadlessInput.MoveMouse(form, at.X + 10, at.Y + 10);

        Assert.Equal(new Color(64, 0x10, 0x20, 0x30), button.BackgroundColor);
    }

    [Fact]
    public void TransitionsAreRead()
    {
        StyleSheet sheet = Parse("""
            Button { transition: BackgroundColor 150ms linear, Opacity 0.2s; }
            """);

        Style style = Assert.Single(sheet.Styles);

        Assert.Equal(2, style.Transitions.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(150), style.Transitions[0].Duration);
        Assert.Same(ButtonBase.BackgroundColorProperty, style.Transitions[0].Property);
        Assert.Equal(TimeSpan.FromMilliseconds(200), style.Transitions[1].Duration);
    }

    // ===== diagnostics =====

    [Fact]
    public void MistakesAreReportedAndTheRestApplies()
    {
        var button = new Button { Text = "OK" };
        Form form = CreateForm(button);

        StyleSheet sheet = StyleSheet.Parse("""
            Button {
                BackgroundColr: #f00;
                Padding: 1 2 3;
                Opacity: 0.5;
            }

            Button..broken { Opacity: 0.1; }

            Buton { Opacity: 0.2; }
            Button:hovr { Opacity: 0.3; }
            Label { Opacity: $missing; }
            """, "broken.zss");

        form.Styles.Add(sheet);

        // what could be read applies
        Assert.Equal(0.5f, button.Opacity);

        StyleDiagnostic[] errors = [.. sheet.Diagnostics.Where(d => d.Severity == StyleDiagnosticSeverity.Error)];
        StyleDiagnostic[] warnings = [.. sheet.Diagnostics.Where(d => d.Severity == StyleDiagnosticSeverity.Warning)];

        Assert.Equal(4, errors.Length);
        Assert.Equal(2, warnings.Length);

        // the unknown property, at its line and column
        Assert.Equal(2, errors[0].Line);
        Assert.Equal(5, errors[0].Column);
        Assert.Contains("BackgroundColr", errors[0].Message);

        // three values for a Thickness, which takes one, two or four
        Assert.Equal(3, errors[1].Line);
        Assert.Contains("Thickness", errors[1].Message);

        // the selector, at the second dot
        Assert.Equal(7, errors[2].Line);
        Assert.Equal(8, errors[2].Column);

        Assert.Contains("$missing", errors[3].Message);

        Assert.Contains("Buton", warnings[0].Message);
        Assert.Contains(":hovr", warnings[1].Message);

        Assert.Equal("broken.zss(2,5): error ZSS: No control has a property 'BackgroundColr'", errors[0].ToString());
    }

    // ===== files =====

    [Fact]
    public void ImportsAreReadInPlaceAndCyclesReported()
    {
        string folder = Directory.CreateTempSubdirectory("zss").FullName;

        try
        {
            File.WriteAllText(Path.Combine(folder, "colors.zss"), "$accent: #00ff00;\n@import \"main.zss\";");
            File.WriteAllText(Path.Combine(folder, "main.zss"), "@import \"colors.zss\";\nButton { BackgroundColor: $accent; }");

            StyleSheet sheet = StyleSheet.Load(Path.Combine(folder, "main.zss"));

            Assert.Equal(2, sheet.Files.Count);
            Assert.Single(sheet.Styles);
            Assert.Contains(sheet.Diagnostics, d => d.Message.Contains("imports itself", StringComparison.Ordinal));

            var button = new Button { Text = "OK" };
            Form form = CreateForm(button);
            form.Styles.Add(sheet);

            Assert.Equal(new Color(255, 0, 255, 0), button.BackgroundColor);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AReloadReplacesTheSheetInPlace()
    {
        string folder = Directory.CreateTempSubdirectory("zss").FullName;
        string path = Path.Combine(folder, "app.zss");

        try
        {
            File.WriteAllText(path, "Button { Opacity: 0.5; }");

            var button = new Button { Text = "OK" };
            Form form = CreateForm(button);

            form.Styles.Add(new Style("Button") { [UIElement.OpacityProperty] = 0.9f });
            StyleSheetLink link = form.Styles.Load(path);
            form.Styles.Add(new Style("Button") { [UIElement.OpacityProperty] = 0.7f });

            // the last of three equal styles wins
            Assert.Equal(0.7f, button.Opacity);

            File.WriteAllText(path, "Button { Opacity: 0.4; } Button.x { Opacity: 0.3; }");
            link.Reload();

            // the sheet kept its place, now with two rules: still between the two
            Assert.Equal(4, form.Styles.Count);
            Assert.Same(link.Sheet.Styles[0], form.Styles[1]);
            Assert.Equal(0.7f, button.Opacity);

            button.Classes.Add("x");
            Assert.Equal(0.3f, button.Opacity);

            link.Dispose();

            Assert.Equal(2, form.Styles.Count);
            Assert.Equal(0.7f, button.Opacity);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AWatchedSheetFollowsSaves()
    {
        string folder = Directory.CreateTempSubdirectory("zss").FullName;
        string path = Path.Combine(folder, "live.zss");

        SynchronizationContext? before = SynchronizationContext.Current;
        var ui = new QueueContext();

        try
        {
            File.WriteAllText(path, "Button { Opacity: 0.5; }");

            var button = new Button { Text = "OK" };
            Form form = CreateForm(button);

            SynchronizationContext.SetSynchronizationContext(ui);

            StyleSheetLink link = form.Styles.Load(path, watch: true);
            Assert.True(link.IsWatching);
            Assert.Equal(0.5f, button.Opacity);

            bool reloaded = false;
            link.Reloaded += (_, _) => reloaded = true;

            File.WriteAllText(path, "Button { Opacity: 0.25; }");

            // the reload comes back through the UI context: run it here, as the UI
            // thread would, until it arrives or the time is up
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);

            while (!reloaded && DateTime.UtcNow < deadline)
            {
                ui.RunPending();
                Thread.Sleep(20);
            }

            Assert.True(reloaded);
            Assert.Equal(0.25f, button.Opacity);

            link.Dispose();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(before);
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A UI thread in miniature: posts wait in a queue until the test runs them.</summary>
    private sealed class QueueContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        public void RunPending()
        {
            while (_queue.TryDequeue(out var item))
                item.Callback(item.State);
        }
    }

    [Fact]
    public void TheExampleSheetIsClean()
    {
        string path = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(ThisFile())!, "..", "..", "examples", "ZF SharedLib", "Assets", "styles.zss"));

        StyleSheet sheet = StyleSheet.Load(path);

        Assert.Empty(sheet.Diagnostics);
        Assert.NotEmpty(sheet.Styles);
    }

    private static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    // ===== the inspector =====

    [Fact]
    public void TheValueSourceIsReported()
    {
        var button = new Button { Text = "OK" };
        button.Classes.Add("styled");

        Form form = CreateForm(button);
        form.Styles.Add(Parse(".styled { Opacity: 0.5; }"));

        Assert.Equal(ValueSource.Style, button.GetValueSource(UIElement.OpacityProperty));
        Assert.Equal(ValueSource.Theme, button.GetValueSource(ButtonBase.BackgroundColorProperty));
        Assert.Equal(ValueSource.ControlDefault, button.GetValueSource(UIElement.PaddingProperty));

        button.Opacity = 1f;
        Assert.Equal(ValueSource.Local, button.GetValueSource(UIElement.OpacityProperty));

        Style matched = Assert.Single(button.GetMatchedStyles());
        Assert.Equal("test.zss:1", matched.Source);
    }
}