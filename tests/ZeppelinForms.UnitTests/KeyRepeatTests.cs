using Xunit;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>0.18.0: a held key's repeat reaches the handlers as such — the platforms
/// reported it, and <see cref="KeyEventArgs.IsRepeat"/> was always false.</summary>
[Collection("Platform")]
public class KeyRepeatTests
{
    [Fact]
    public void ARepeatReachesTheFocusedElementAsARepeat()
    {
        Keyboard.Reset();

        var box = new TextBox();
        var form = new Form { Content = box };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();
        form.FocusElement(box);

        var seen = new List<bool>();
        box.KeyDown += (_, e) => seen.Add(e.IsRepeat);

        form.OnKeyDown(Key.Right, KeyModifiers.None, isRepeat: false);
        form.OnKeyDown(Key.Right, KeyModifiers.None, isRepeat: true);

        Assert.Equal([false, true], seen);
    }
}