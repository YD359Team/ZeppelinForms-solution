namespace ZeppelinForms;

/// <summary>Кто просит перерисовку. На настольных платформах это таймер
/// внутри окна, в браузере — requestAnimationFrame, на Android — Choreographer.</summary>
public interface IFrameDriver
{
    /// <summary>Идёт ли непрерывная выдача кадров.</summary>
    bool IsRunning { get; }

    /// <summary>Начать выдавать кадры с заданным интервалом.
    /// Повторный вызов при уже идущей выдаче ничего не меняет.</summary>
    void Start(int intervalMs);

    void Stop();

    /// <summary>Один кадр по требованию, без непрерывной выдачи.
    /// Пригодится там, где нужен единичный пересчёт без анимации.</summary>
    void RequestFrame();
}
