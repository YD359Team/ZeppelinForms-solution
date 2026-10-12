using System.Reflection;
using System.Reflection.Metadata;
using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Navigation;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>What the open forms do after Hot Reload applies an edit: the form's and the
/// elements' hooks, the factories' pages made again, everything measured anew.</summary>
[Collection("Platform")]
public class HotReloadTests
{
    /// <summary>"The edit": what the code returns now.</summary>
    private static string s_caption = "before";
    private static float s_width = 50;

    private sealed class ReloadingForm : Form
    {
        public int Reloads { get; private set; }

        public ReloadingForm()
        {
            Size = new Size(400, 300);
            Content = CreateContent();
        }

        private static Label CreateContent() => new() { Text = s_caption };

        protected override void OnHotReload(HotReloadEventArgs e)
        {
            Reloads++;
            Content = CreateContent();
        }
    }

    /// <summary>A control whose code "changed": its measure reads the static.</summary>
    private sealed class Sized : UnitControl
    {
        public int Reloads { get; private set; }

        public override void Draw(Graphics g) { }

        protected override Size MeasureOverride(Size availableSize) => new(s_width, 20);

        protected override void OnHotReload(HotReloadEventArgs e) => Reloads++;
    }

    private sealed class Broken : UnitControl
    {
        public override void Draw(Graphics g) { }

        protected override void OnHotReload(HotReloadEventArgs e) =>
            throw new InvalidOperationException("half-typed code");
    }

    private static Form Show(Form form)
    {
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();
        return form;
    }

    private static Form Show(UIElement content) =>
        Show(new Form { Size = new Size(400, 300), Content = content });

    private static Label Page(string text) => new() { Text = text };

    [Fact]
    public void TheFrameworkReportsEditsToTheRuntime()
    {
        MetadataUpdateHandlerAttribute? handler = typeof(UIElement).Assembly
            .GetCustomAttributes<MetadataUpdateHandlerAttribute>()
            .SingleOrDefault();

        Assert.NotNull(handler);

        // the runtime calls them by name
        Assert.NotNull(handler!.HandlerType.GetMethod("ClearCache", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(handler.HandlerType.GetMethod("UpdateApplication", BindingFlags.Public | BindingFlags.Static));
    }

    [Fact]
    public void AnEditCountsForTheOutermostType()
    {
        // a lambda's closure is nested in the type its code was written in
        var e = new HotReloadEventArgs([typeof(ReloadingForm)]);

        Assert.True(e.AreTypesKnown);
        Assert.True(e.Changed(typeof(HotReloadTests)));
        Assert.True(e.Changed(typeof(Sized)));
        Assert.True(e.Changed(typeof(HotReloadTests).Assembly));
        Assert.False(e.Changed(typeof(Label)));
        Assert.False(e.Changed(typeof(Label).Assembly));
    }

    [Fact]
    public void AFactoryCountsForItsAssembly()
    {
        var e = new HotReloadEventArgs([typeof(HotReloadTests)]);

        Assert.True(e.Changed(() => Page("x")));

        // a factory of another assembly — a method group of the framework
        Func<string, string?> framework = Path.GetFileName;
        Assert.False(e.Changed(framework));
    }

    [Fact]
    public void UnnamedTypesMeanEverythingChanged()
    {
        var e = new HotReloadEventArgs(null);

        Assert.False(e.AreTypesKnown);
        Assert.Empty(e.UpdatedTypes);
        Assert.True(e.Changed(typeof(Label)));
        Assert.True(e.Changed(typeof(string).Assembly));
    }

    [Fact]
    public void TheFormBuildsItsContentAgain()
    {
        s_caption = "before";
        var form = (ReloadingForm)Show(new ReloadingForm());

        s_caption = "after";
        HotReload.Apply([typeof(ReloadingForm)]);

        Assert.Equal(1, form.Reloads);
        Assert.Equal("after", ((Label)form.Content!).Text);
    }

    [Fact]
    public void EveryElementHearsOfTheEditAndIsMeasuredAnew()
    {
        s_width = 50;
        var sized = new Sized();
        Form form = Show(new StackPanel { Children = { sized } });
        Assert.Equal(50, sized.DesiredSize.Width, 0.5f);

        // the measure's code "changed": nothing else invalidated the element
        s_width = 80;
        HotReload.Apply([typeof(Sized)]);
        form.UpdateLayout();

        Assert.Equal(1, sized.Reloads);
        Assert.Equal(80, sized.DesiredSize.Width, 0.5f);
    }

    [Fact]
    public void TheSelectedPageOfASplitViewIsMadeAgain()
    {
        int made = 0;
        var view = new SplitView();
        view.Items.Add(new SplitViewItem("One", content: () => Page($"one {++made}")));
        view.Items.Add(new SplitViewItem("Two", content: () => Page("two")));
        Show(view);

        Assert.Equal("one 1", ((Label)view.Content!).Text);

        HotReload.Apply([typeof(HotReloadTests)]);

        Assert.Equal("one 2", ((Label)view.Content!).Text);
    }

    [Fact]
    public void APageVisitedBeforeIsMadeAgainWhenChosen()
    {
        int made = 0;
        var view = new SplitView();
        SplitViewItem first = new("One", content: () => Page($"one {++made}"));
        view.Items.Add(first);
        view.Items.Add(new SplitViewItem("Two", content: () => Page("two")));
        Show(view);

        view.SelectedItem = view.Items[1];
        HotReload.Apply([typeof(HotReloadTests)]);
        Assert.Equal(1, made);

        view.SelectedItem = first;
        Assert.Equal("one 2", ((Label)view.Content!).Text);
    }

    [Fact]
    public void AnEditElsewhereKeepsThePages()
    {
        int made = 0;
        var view = new SplitView();
        view.Items.Add(new SplitViewItem("One", content: () => Page($"one {++made}")));
        Show(view);

        // a model in another assembly: the page has nothing to show anew
        HotReload.Apply([typeof(string)]);

        Assert.Equal(1, made);
        Assert.Equal("one 1", ((Label)view.Content!).Text);
    }

    [Fact]
    public void TheShownPageOfAPageControlIsMadeAgain()
    {
        int made = 0;
        var pages = new PageControl();
        Page shown = pages.AddPage("one", () => Page($"one {++made}"));
        pages.AddPage("two", () => Page("two"));
        Show(pages);

        Assert.Equal("one 1", ((Label)shown.Child!).Text);

        HotReload.Apply([typeof(HotReloadTests)]);

        Assert.Equal("one 2", ((Label)shown.Child!).Text);
    }

    [Fact]
    public void AFailingHookDoesNotStopTheOthers()
    {
        var sized = new Sized();
        Show(new StackPanel { Children = { new Broken(), sized } });

        HotReload.Apply([typeof(Broken)]);

        Assert.Equal(1, sized.Reloads);
    }

    [Fact]
    public void AppliedIsRaisedAfterTheForms()
    {
        var form = (ReloadingForm)Show(new ReloadingForm());
        int reloadsSeen = -1;

        void OnApplied(object? sender, HotReloadEventArgs e) => reloadsSeen = form.Reloads;

        HotReload.Applied += OnApplied;

        try
        {
            HotReload.Apply(null);
        }
        finally
        {
            HotReload.Applied -= OnApplied;
        }

        Assert.Equal(1, reloadsSeen);
    }
}
