namespace ZeppelinForms;

/// <summary>Кто просит перерисовку. На настольных платформах это таймер
/// внутри окна, в браузере — requestAnimationFrame, на Android — Choreographer.</summary>
public interface IFrameDriver
{
    void RequestFrame();
    void StartContinuous(int intervalMs);
    void StopContinuous();
}
