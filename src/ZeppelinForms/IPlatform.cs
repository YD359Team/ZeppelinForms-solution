using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms;

public interface IPlatform
{
    IPlatformWindow CreateWindow(Form form);

    /// <summary>Запустить приложение. На настольных платформах не возвращает
    /// управление до выхода; там, где циклом владеет хост — браузер, Android —
    /// возвращает сразу, а кадры приходят через IFrameDriver.</summary>
    void Start();

    void Exit();

    /// <summary>Владеет ли платформа собственным циклом. false означает,
    /// что после Start приложение продолжает жить на вызовах извне.</summary>
    bool OwnsEventLoop { get; }
}
