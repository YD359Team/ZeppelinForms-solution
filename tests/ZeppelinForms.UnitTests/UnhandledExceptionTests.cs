using Xunit;
using ZeppelinForms.Animation;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>App.UnhandledException is static: every test unsubscribes in finally,
/// and the class runs in the Platform collection, one test at a time.</summary>
[Collection("Platform")]
public class UnhandledExceptionTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    private static (Form Form, Button Button) CreateForm()
    {
        var platform = new HeadlessPlatform();

        var button = new Button
        {
            Text = "кнопка",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        panel.Children.Add(button);

        var form = new Form { Size = new Size(400, 300), Content = panel };
        platform.CreateWindow(form);
        form.UpdateLayout();

        return (form, button);
    }

    [Fact]
    public void HandledFailureRemovesOnlyTheFailedAnimation()
    {
        var (form, button) = CreateForm();
        List<Exception> offered = [];

        EventHandler<DispatcherUnhandledExceptionEventArgs> handler = (_, args) =>
        {
            offered.Add(args.Exception);
            args.Handled = true;
        };

        App.UnhandledException += handler;

        try
        {
            int healthy = 0;
            int broken = 0;

            button.AnimateFrames("healthy", _ => { healthy++; return true; });
            button.AnimateFrames("broken", _ => { broken++; throw new InvalidOperationException("кадр"); });

            form.Clock.Advance(Frame);
            form.Clock.Advance(Frame);

            // the neighbour runs on, the failed one is gone after its one failure
            Assert.Equal(2, healthy);
            Assert.Equal(1, broken);
            Assert.IsType<InvalidOperationException>(Assert.Single(offered));
        }
        finally
        {
            App.UnhandledException -= handler;
        }
    }

    [Fact]
    public void WithoutHandlerTheFailureEscapesAsBefore()
    {
        var (form, button) = CreateForm();

        button.AnimateFrames("broken", _ => throw new InvalidOperationException("кадр"));

        Assert.Throws<InvalidOperationException>(() => form.Clock.Advance(Frame));
    }

    [Fact]
    public void DeclinedFailureIsOfferedOnceAndRethrown()
    {
        int calls = 0;

        EventHandler<DispatcherUnhandledExceptionEventArgs> handler = (_, _) => calls++;

        App.UnhandledException += handler;

        try
        {
            // a queued action inside a window message: the inner guard offers it,
            // the handler declines, and the outer guard must not ask again
            Assert.Throws<InvalidOperationException>(() =>
                App.RunGuarded(() =>
                    App.RunGuarded(() => throw new InvalidOperationException("шаг"))));

            Assert.Equal(1, calls);
        }
        finally
        {
            App.UnhandledException -= handler;
        }
    }

    [Fact]
    public void HandledStepThatAnswersTheSystemGivesTheFallback()
    {
        EventHandler<DispatcherUnhandledExceptionEventArgs> handler = (_, args) => args.Handled = true;

        App.UnhandledException += handler;

        try
        {
            // an Android callback that answers "the event was mine": handled, it
            // still says so, rather than letting the system act on it a second time
            bool answer = App.RunGuarded<bool>(
                () => throw new InvalidOperationException("касание"), whenHandled: true);

            Assert.True(answer);
        }
        finally
        {
            App.UnhandledException -= handler;
        }
    }
}