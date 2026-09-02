using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Headless;

public sealed class HeadlessWindow : IPlatformWindow
{
    private readonly HeadlessPlatform _platform;
    private readonly Form _form;

    public bool IsShown { get; private set; }
    public bool IsClosed { get; private set; }
    public string? Title { get; private set; }
    public float Opacity { get; private set; } = 1f;
    public WindowState WindowState { get; private set; }

    public CursorKind Cursor { get; private set; } = CursorKind.Default;
    public void SetCursor(CursorKind cursor) => Cursor = cursor;

    /// <summary>Сколько раз запрашивалась перерисовка — проверяется в тестах.</summary>
    public int InvalidateCount { get; private set; }

    public Rectangle? LastInvalidatedRect { get; private set; }

    internal HeadlessWindow(HeadlessPlatform platform, Form form)
    {
        _platform = platform;
        _form = form;
    }

    public void Show()
    {
        IsShown = true;
        _form.PerformLayout();
    }

    public void Close()
    {
        if (IsClosed) return;

        IsClosed = true;
        IsShown = false;
        _platform.Remove(this);
    }

    public void SetTitle(string? title) => Title = title;

    public void SetBounds(Rectangle bounds)
    {
        _form.ClientSize = bounds.Size;
        _form.PerformLayout();
    }

    public void Invalidate(Rectangle? bounds = null)
    {
        InvalidateCount++;
        LastInvalidatedRect = bounds;
    }

    public void Invoke(Action action) => _platform.Post(action);

    public void SetOpacity(float opacity) => Opacity = opacity;

    public void SetWindowState(WindowState state) => WindowState = state;

    public void StartTicking(int intervalMs) { }

    public void StopTicking() { }

    /// <summary>Продвинуть анимации на заданное время без ожидания реального таймера.</summary>
    public void Tick() => _form.Tick();

    /// <summary>Задать размер клиентской области и пересчитать раскладку.</summary>
    public void Resize(float width, float height)
    {
        _form.ClientSize = new Size(width, height);
        _form.PerformLayout();
    }
}
