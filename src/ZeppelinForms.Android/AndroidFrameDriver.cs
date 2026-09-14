namespace ZeppelinForms.Android;

/// <summary>
/// Кадры даёт Choreographer. Интервал соблюдается пропуском кадров,
/// а не таймером: чаще частоты экрана система всё равно не разбудит,
/// а собственный Handler рядом с Choreographer дал бы рваную анимацию.
/// Устройство то же, что у BrowserFrameDriver — обе платформы циклом
/// не владеют, и расходиться им незачем.
/// </summary>
internal sealed class AndroidFrameDriver(Action scheduleFrame, Action repaint) : IFrameDriver
{
    private int _intervalMs;
    private double _lastFrameMs;

    public bool IsRunning { get; private set; }

    public void Start(int intervalMs)
    {
        if (IsRunning) return;

        _intervalMs = intervalMs;

        // 0 означает «тика ещё не было»: первый кадр после Start отдаём
        // сразу, иначе анимация начиналась бы с задержки в один интервал
        _lastFrameMs = 0;
        IsRunning = true;

        scheduleFrame();
    }

    public void Stop() => IsRunning = false;

    public void RequestFrame() => repaint();

    internal bool ShouldTick(double timestampMs)
    {
        if (!IsRunning) return false;

        scheduleFrame();

        if (_lastFrameMs != 0 && timestampMs - _lastFrameMs < _intervalMs)
            return false;

        _lastFrameMs = timestampMs;
        return true;
    }
}