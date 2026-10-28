using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Headless;

public static class HeadlessInput
{
    public static void MoveMouse(Form form, float x, float y) =>
        form.OnPointerMove(new Point(x, y));

    public static void Click(Form form, float x, float y, MouseButton button = MouseButton.Left)
    {
        form.OnPointerDown(new Point(x, y), button);
        form.OnPointerUp(new Point(x, y), button);
    }

    public static void DoubleClick(Form form, float x, float y)
    {
        // two clicks in a row without a delay — Form counts the multiplicity itself
        Click(form, x, y);
        Click(form, x, y);
    }

    /// <summary>A right click, and the context menu after it, as the platforms send it:
    /// the release first, then the menu.</summary>
    public static void RightClick(Form form, float x, float y)
    {
        Click(form, x, y, MouseButton.Right);
        form.OnContextMenu(new Point(x, y));
    }

    // ===== Touches =====
    // No backend delivers multi-touch yet, and until Android the only way
    // to check the pipeline is synthetic input. The identifiers start
    // from ten, so as not to collide with Form.MousePointerId.

    public static void TouchDown(Form form, int fingerId, float x, float y, long timestamp = 0) =>
        form.OnPointerDown(MakeTouch(fingerId, x, y, timestamp));

    public static void TouchMove(Form form, int fingerId, float x, float y, long timestamp = 0) =>
        form.OnPointerMove(MakeTouch(fingerId, x, y, timestamp));

    public static void TouchUp(Form form, int fingerId, float x, float y, long timestamp = 0) =>
        form.OnPointerUp(MakeTouch(fingerId, x, y, timestamp));

    public static void TouchCancel(Form form, int fingerId) =>
        form.OnPointerCancel(10 + fingerId);

    /// <summary>A touch and a release at one point.</summary>
    public static void Tap(Form form, float x, float y, int fingerId = 0)
    {
        TouchDown(form, fingerId, x, y);
        TouchUp(form, fingerId, x, y);
    }

    /// <summary>A one-finger drag over the given number of steps.
    /// The steps must be real: the threshold for breaking into a pan is counted
    /// over the accumulated path, and a jump from start to end doesn't reproduce it.</summary>
    public static void Swipe(Form form, Point from, Point to, int steps = 8, int fingerId = 0)
    {
        TouchDown(form, fingerId, from.X, from.Y);

        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;

            TouchMove(
                form,
                fingerId,
                from.X + ((to.X - from.X) * t),
                from.Y + ((to.Y - from.Y) * t),
                i * 16);
        }

        TouchUp(form, fingerId, to.X, to.Y, steps * 16);
    }

    private static PointerEventArgs MakeTouch(int fingerId, float x, float y, long timestamp) =>
        new(10 + fingerId, PointerKind.Touch, new Point(x, y))
        {
            Timestamp = timestamp,
            IsPrimary = fingerId == 0,
        };

    /// <summary>A whole chord: the key pressed and released with the modifiers held,
    /// then the modifiers released too.</summary>
    /// <remarks>The modifiers used to stay held in <see cref="Keyboard"/> after the chord:
    /// a Ctrl+S in one test made the next test's click a Ctrl-click, and its wheel —
    /// after a Shift chord — a sideways one.</remarks>
    public static void PressKey(Form form, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        form.OnKeyDown(key, modifiers, false);
        form.OnKeyUp(key, modifiers);

        if (modifiers != KeyModifiers.None)
            Keyboard.Reset();
    }

    public static void TypeText(Form form, string text)
    {
        foreach (char c in text)
            form.OnTextInput(c);
    }

    /// <summary>An input method composing: the text so far, not committed. Empty — the
    /// composition was cancelled.</summary>
    /// <param name="form">The form.</param>
    /// <param name="text">The text being composed.</param>
    /// <param name="cursor">The input method's cursor in it; the end by default.</param>
    public static void Compose(Form form, string text, int cursor = -1)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(text);

        form.OnCompositionUpdate(text, cursor < 0 ? text.Length : cursor);
    }

    /// <summary>An input method committing its composition.</summary>
    public static void CommitComposition(Form form, string text)
    {
        ArgumentNullException.ThrowIfNull(form);

        form.OnCompositionCommit(text);
    }
}