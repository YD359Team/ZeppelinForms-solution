using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms;

/// <summary>Минимум, без которого не обойдётся ни одна платформа.</summary>
public interface IPlatformWindow
{
    /// <summary>Показать окно. В браузере и на Android это не создание окна,
    /// а подключение формы к уже существующей поверхности хоста.</summary>
    void Show();

    /// <summary>Закрыть окно. Платформа обязана после разрушения поверхности
    /// вызвать Form.OnWindowClosed: на этом держится и async-диалог,
    /// и подсчёт живых окон.</summary>
    void Close();

    void Invalidate(Rectangle? rect);

    /// <summary>Отношение физических пикселей к логическим.</summary>
    float Scale { get; }

    /// <summary>Выполнить действие в потоке UI.</summary>
    void Invoke(Action action);

    void CaptureMouse();
    void ReleaseMouseCapture();
    void SetCursor(CursorKind cursor);

    /// <summary>Зарегистрировать окно приёмником системного перетаскивания.
    /// Без этого AllowDrop у элементов не сработает.</summary>
    void SetDragDropEnabled(bool enabled);

    /// <summary>Запретить или разрешить ввод в это окно. Так делается
    /// модальность: владелец диалога глохнет, пока диалог открыт.</summary>
    void SetEnabled(bool enabled);

    /// <summary>Сделать окно активным.</summary>
    void Activate();

    IFrameDriver Frames { get; }
}

/// <summary>Оформление окна как объекта рабочего стола. В браузере
/// и на Android не реализуется — там этих понятий нет.</summary>
// TODO: SetIcon, SetResizable, SetTopMost, CenterOnScreen —
// когда появятся реализации в Win32Window и X11Window
public interface IDesktopWindow
{
    void SetTitle(string? title);
    void SetBounds(Rectangle bounds);
    void SetOpacity(float opacity);
    void SetWindowState(WindowState state);

    bool SupportsTransparency { get; }
}