using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>A control of the platform among the framework's own: a Win32 edit on
/// Windows, an X11 window on Linux, an element of the page in the browser. The shared
/// library knows none of them — each application gives its own through
/// <see cref="Sample"/>.</summary>
public static class NativePage
{
    /// <summary>Makes the platform's sample control; null — this platform hosts none yet.</summary>
    public static Func<NativeControlHost>? Sample { get; set; }

    internal static UIElement Create()
    {
        if (Sample is not { } make)
        {
            return Demo.Page("Native controls", "A control of the platform among the framework's own.",
                Demo.Section("Not here yet", Demo.Note(
                    "This platform hosts no native controls yet: Wayland and Android get them in 0.19. " +
                    "On Windows it is an HwndHost, on X11 an X11WindowHost, in the browser an HtmlElementHost.")));
        }

        NativeControlHost host = make();
        host.Size = new Size(float.NaN, 180);
        host.Margin = new Thickness(0, 8);

        // the host in a list that scrolls: the control is cut at the list's edges
        var list = new StackPanel
        {
            Size = new Size(float.NaN, 240),
            OverflowY = Overflow.Auto,
            Spacing = 6,
            Padding = new Thickness(10),
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
        };

        for (int i = 1; i <= 4; i++) list.Children.Add(new Label { Text = $"A row of the form's own, {i}" });
        list.Children.Add(host);
        for (int i = 5; i <= 12; i++) list.Children.Add(new Label { Text = $"A row of the form's own, {i}" });

        var shown = new CheckBox { Text = "Show the control", IsChecked = true };
        shown.CheckedChanged += (_, _) => host.IsVisible = shown.IsChecked;

        Label state = Demo.Note("The control is made when it is first shown");
        host.NativeControlCreated += (_, _) => state.Text = $"Made: {host.NativeControl}";

        return Demo.Page("Native controls", "A control of the platform among the framework's own.",
            Demo.Section("In a scrolled list", Demo.Column(list, shown, state),
                "Scroll the list: the control moves with it and is cut at its edges"),
            Demo.Section("What a native control can't do", Demo.Note(
                "It is a window of its own above the form's picture: flyouts and menus open under it, " +
                "and it ignores opacity, transforms and effects. Tab leads into it, not out of it.")));
    }
}