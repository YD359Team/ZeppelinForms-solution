using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>
/// The application-wide events a form follows — the theme, the text scale, the language
/// (Form.AppEvents.cs, 0.20.0): each subscribed once while the form follows them, none
/// after its window closed or it was disposed, and a change made in between caught up
/// with when the form is shown again.
/// </summary>
[Collection("Platform")]
public class FormAppEventsTests
{
    /// <summary>How many handlers of the form are on each of the three static events.</summary>
    private static (int Theme, int TextScale, int Language) Subscriptions(Form form) =>
        (Count(typeof(App), nameof(App.ThemeChanged), form),
         Count(typeof(App), nameof(App.TextScaleChanged), form),
         Count(typeof(Localization), nameof(Localization.Changed), form));

    /// <summary>The handlers of a field-like static event whose target is the form.</summary>
    private static int Count(Type owner, string eventName, Form form)
    {
        FieldInfo field = owner.GetField(eventName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{owner.Name}.{eventName} has no backing field.");

        return field.GetValue(null) is Delegate handlers
            ? handlers.GetInvocationList().Count(handler => ReferenceEquals(handler.Target, form))
            : 0;
    }

    private static Form NewForm() =>
        new() { Size = new Size(200, 100), Content = new Label { Text = "x" } };

    [Fact]
    public void FormFollowsEachEventOnceFromConstructorThroughShowing()
    {
        Form form = NewForm();

        Assert.Equal((1, 1, 1), Subscriptions(form));

        var platform = new HeadlessPlatform();
        platform.CreateWindow(form);
        form.Show();

        // the window renews the subscriptions: the constructor's must not be doubled
        Assert.Equal((1, 1, 1), Subscriptions(form));

        form.Close();
        Assert.Equal((0, 0, 0), Subscriptions(form));

        // shown again: once each, as before the close
        platform.CreateWindow(form);
        Assert.Equal((1, 1, 1), Subscriptions(form));

        form.Close();
        form.Dispose();
    }

    [Fact]
    public void DisposedFormIsUnsubscribed()
    {
        Form form = NewForm();

        form.Dispose();

        Assert.Equal((0, 0, 0), Subscriptions(form));

        // a second Dispose is a no-op and must not throw
        form.Dispose();
    }

    [Fact]
    public void DisposedFormThatWasNeverShownIsCollected()
    {
        WeakReference form = CreateAndDispose();

        for (int i = 0; i < 3 && form.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(form.IsAlive, "a disposed form must not be held by the application's static events");
    }

    /// <summary>In its own method, so that no local of the test keeps the form
    /// alive: a Debug build extends locals to the end of their method.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndDispose()
    {
        Form form = NewForm();

        form.Dispose();

        return new WeakReference(form);
    }

    [Fact]
    public void FormShownAgainTakesLanguageChangedWhileClosed()
    {
        CultureInfo before = Localization.Culture;

        try
        {
            Localization.Culture = new CultureInfo("en-US");

            Form form = NewForm().LocalizeTitle(ZfText.Cancel);
            var platform = new HeadlessPlatform();

            platform.CreateWindow(form);
            form.Show();
            form.Close();

            // the form no longer listens to the language while closed
            Localization.Culture = new CultureInfo("ru-RU");
            Assert.Equal("Cancel", form.Title);

            // shown again: it must catch up with the language switched in the meantime
            platform.CreateWindow(form);

            Assert.Equal("Отмена", form.Title);

            form.Close();
            form.Dispose();
        }
        finally
        {
            Localization.Culture = before;
        }
    }
}
