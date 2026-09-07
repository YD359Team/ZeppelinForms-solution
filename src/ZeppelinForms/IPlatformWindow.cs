using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms;

public interface IPlatformWindow
{
    void Show();
    void Close();
    void SetTitle(string? title);
    void SetBounds(Rectangle bounds);
    void Invalidate(Rectangle? bounds = null);
    void Invoke(Action action);
    void SetOpacity(float opacity);
    void SetWindowState(WindowState state);
    void StartTicking(int intervalMs);
    void StopTicking();
    void SetCursor(CursorKind cursor);
    /// <summary>Забрать сообщения мыши себе, даже когда курсор ушёл за окно.
    /// Без этого отпускание кнопки снаружи до окна не доходит.</summary>
    void CaptureMouse();
    void ReleaseMouseCapture();
    /// <summary>Разрешить приём перетаскивания из системы. Регистрация
    /// платформенного приёмника стоит недёшево, поэтому включается по запросу,
    /// а не всегда.</summary>
    void SetDragDropEnabled(bool enabled);
    /// <summary>Поддерживает ли окружение прозрачность окна. На Windows
    /// всегда true, на Linux зависит от наличия композитора.</summary>
    bool SupportsTransparency { get; }
}