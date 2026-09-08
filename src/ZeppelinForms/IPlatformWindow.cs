using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms;

/// <summary>Минимум, без которого не обойдётся ни одна платформа.</summary>
public interface IPlatformWindow
{
    void Invalidate(Rectangle? rect);
    void SetSize(Size size);
    Size ClientSize { get; }
    float Scale { get; }

    void CaptureMouse();
    void ReleaseMouseCapture();
    void SetCursor(CursorKind cursor);

    /// <summary>Запретить или разрешить ввод в это окно. Так делается
    /// модальность: владелец диалога глохнет, пока диалог открыт.</summary>
    void SetEnabled(bool enabled);

    /// <summary>Сделать окно активным.</summary>
    void Activate();

    IFrameDriver Frames { get; }
}

/// <summary>Оформление окна как объекта рабочего стола.</summary>
public interface IDesktopWindow
{
    void SetTitle(string title);
    void SetIcon(Icon icon);
    void SetOpacity(float opacity);
    void SetWindowState(WindowState state);
    void SetResizable(bool resizable);
    void SetTopMost(bool topMost);
    void CenterOnScreen();

    bool SupportsTransparency { get; }
}