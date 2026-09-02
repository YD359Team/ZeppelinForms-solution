using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Headless;

public static class HeadlessInput
{
    public static void MoveMouse(Form form, float x, float y) => form.OnPointerMove(new Point(x, y));
    public static void Click(Form form, float x, float y)
    {
        form.OnPointerDown(new Point(x, y));
        form.OnPointerUp(new Point(x, y));
    }
    public static void PressKey(Form form, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        form.OnKeyDown(key, modifiers);
    public static void TypeText(Form form, string text)
    {
        foreach (char c in text)
            form.OnTextInput(c);
    }
}
