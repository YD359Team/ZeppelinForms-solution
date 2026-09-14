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
        // два клика подряд без задержки — Form сам посчитает кратность
        Click(form, x, y);
        Click(form, x, y);
    }

    public static void RightClick(Form form, float x, float y) =>
        Click(form, x, y, MouseButton.Right);

    // ===== Касания =====
    // Пока ни один бэкенд мультитач не отдаёт, и до Android единственный
    // способ проверить конвейер — синтетика. Идентификаторы начинаются
    // с десяти, чтобы не пересечься с Form.MousePointerId.

    public static void TouchDown(Form form, int fingerId, float x, float y, long timestamp = 0) =>
        form.OnPointerDown(MakeTouch(fingerId, x, y, timestamp));

    public static void TouchMove(Form form, int fingerId, float x, float y, long timestamp = 0) =>
        form.OnPointerMove(MakeTouch(fingerId, x, y, timestamp));

    public static void TouchUp(Form form, int fingerId, float x, float y, long timestamp = 0) =>
        form.OnPointerUp(MakeTouch(fingerId, x, y, timestamp));

    public static void TouchCancel(Form form, int fingerId) =>
        form.OnPointerCancel(10 + fingerId);

    /// <summary>Касание и отпускание в одной точке.</summary>
    public static void Tap(Form form, float x, float y, int fingerId = 0)
    {
        TouchDown(form, fingerId, x, y);
        TouchUp(form, fingerId, x, y);
    }

    /// <summary>Проведение одним пальцем за указанное число шагов.
    /// Шаги нужны настоящие: порог срыва в pan считается по накопленному
    /// пути, и прыжок из начала в конец его не воспроизводит.</summary>
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

    public static void PressKey(Form form, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        form.OnKeyDown(key, modifiers, false);
        form.OnKeyUp(key, modifiers);
    }

    public static void TypeText(Form form, string text)
    {
        foreach (char c in text)
            form.OnTextInput(c);
    }
}