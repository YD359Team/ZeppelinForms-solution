using ZeppelinForms.Forms;

namespace ZeppelinForms;

public interface IPlatform
{
    IPlatformWindow CreateWindow(Form form);

    /// <summary>Запустить приложение. На настольных платформах не возвращает
    /// управление до выхода; там, где циклом владеет хост — браузер, Android —
    /// возвращает сразу, а кадры приходят через IFrameDriver.</summary>
    void Start();

    void Exit();
}