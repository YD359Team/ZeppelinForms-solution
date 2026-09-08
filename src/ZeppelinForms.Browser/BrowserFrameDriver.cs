using System;
using System.Collections.Generic;
using System.Text;

namespace ZeppelinForms.Browser;

/// <summary>
/// Кадры даёт requestAnimationFrame. Интервал соблюдается пропуском кадров,
/// а не таймером: чаще частоты экрана браузер всё равно не разбудит,
/// а собственный setInterval рядом с rAF дал бы рваную анимацию.
/// </summary>
internal sealed class BrowserFrameDriver(Action scheduleFrame, Action repaint) : IFrameDriver
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

    /// <summary>Единичная перерисовка без анимации. Как и в X11, это именно
    /// перерисовка, а не тик: пересчитывать анимации здесь нечего.</summary>
    public void RequestFrame() => repaint();

    /// <summary>Пора ли отдавать тик. Вызывается из обработчика rAF и сам
    /// заказывает следующий кадр, пока идёт непрерывная выдача.</summary>
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