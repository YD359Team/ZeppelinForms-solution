using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

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
        // два клика подряд без задержки — Form сам посчитает кратность
        Click(form, x, y);
        Click(form, x, y);
    }

    public static void RightClick(Form form, float x, float y) =>
        Click(form, x, y, MouseButton.Right);

    public static void PressKey(Form form, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        form.OnKeyDown(key, modifiers);
        form.OnKeyUp(key, modifiers);
    }

    public static void TypeText(Form form, string text)
    {
        foreach (char c in text)
            form.OnTextInput(c);
    }
}
