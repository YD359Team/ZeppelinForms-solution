using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms;

public interface IPlatformWindow
{
    void Show();
    void Close();
    void SetTitle(string? title);
    void SetBounds(Rectangle bounds);
    void Invalidate();
    void Invoke(Action action);
    void SetOpacity(float opacity);
}
