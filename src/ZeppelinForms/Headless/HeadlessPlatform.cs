using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Headless;

/// <summary>
/// Платформа без окон: раскладка и ввод работают, отрисовки нет.
/// Нужна для тестов и для запуска в среде без графики.
/// </summary>
public sealed class HeadlessPlatform : IPlatform
{
    private readonly List<HeadlessWindow> _windows = [];
    private readonly Queue<Action> _pending = new();

    private bool _running;

    public IPlatformWindow CreateWindow(Form form)
    {
        var window = new HeadlessWindow(this, form);

        form.PlatformWindow = window;
        form.Platform = this;

        // окна нет, поэтому клиентскую область задаём сразу из Form.Size
        form.ClientSize = form.Size;
        form.PerformLayout();

        _windows.Add(window);
        return window;
    }

    public void RunModal(IPlatformWindow dialog, IPlatformWindow? owner)
    {
        var window = (HeadlessWindow)dialog;

        while (!window.IsClosed && Pump()) { }
    }

    public void Run()
    {
        _running = true;

        while (_running && _windows.Count > 0)
            if (!Pump())
                Thread.Sleep(1);
    }

    public void Exit()
    {
        _running = false;

        foreach (HeadlessWindow window in _windows.ToList())
            window.Close();
    }

    /// <summary>Выполнить одно отложенное действие. false — очередь пуста.</summary>
    public bool Pump()
    {
        Action? action;

        lock (_pending)
        {
            if (_pending.Count == 0) return false;
            action = _pending.Dequeue();
        }

        action();
        return true;
    }

    /// <summary>Прокрутить очередь до конца — удобно в тестах после Invoke.</summary>
    public void PumpAll()
    {
        while (Pump()) { }
    }

    internal void Post(Action action)
    {
        lock (_pending)
            _pending.Enqueue(action);
    }

    internal void Remove(HeadlessWindow window)
    {
        _windows.Remove(window);

        if (_windows.Count == 0)
            _running = false;
    }
}

public sealed class HeadlessWindow : IPlatformWindow
{
    private readonly HeadlessPlatform _platform;
    private readonly Form _form;

    public bool IsShown { get; private set; }
    public bool IsClosed { get; private set; }
    public string? Title { get; private set; }
    public float Opacity { get; private set; } = 1f;
    public WindowState WindowState { get; private set; }

    /// <summary>Сколько раз запрашивалась перерисовка — проверяется в тестах.</summary>
    public int InvalidateCount { get; private set; }

    public Rectangle? LastInvalidatedRect { get; private set; }

    internal HeadlessWindow(HeadlessPlatform platform, Form form)
    {
        _platform = platform;
        _form = form;
    }

    public void Show()
    {
        IsShown = true;
        _form.PerformLayout();
    }

    public void Close()
    {
        if (IsClosed) return;

        IsClosed = true;
        IsShown = false;
        _platform.Remove(this);
    }

    public void SetTitle(string? title) => Title = title;

    public void SetBounds(Rectangle bounds)
    {
        _form.ClientSize = bounds.AsSize();
        _form.PerformLayout();
    }

    public void Invalidate(Rectangle? bounds = null)
    {
        InvalidateCount++;
        LastInvalidatedRect = bounds;
    }

    public void Invoke(Action action) => _platform.Post(action);

    public void SetOpacity(float opacity) => Opacity = opacity;

    public void SetWindowState(WindowState state) => WindowState = state;

    public void StartTicking(int intervalMs) { }

    public void StopTicking() { }

    /// <summary>Продвинуть анимации на заданное время без ожидания реального таймера.</summary>
    public void Tick() => _form.Tick();

    /// <summary>Задать размер клиентской области и пересчитать раскладку.</summary>
    public void Resize(float width, float height)
    {
        _form.ClientSize = new Size(width, height);
        _form.PerformLayout();
    }
}

public static class HeadlessInput
{
    public static void MoveMouse(Form form, float x, float y) => form.OnPointerMove(new Point(x, y));
    public static void Click(Form form, float x, float y)
    {
        form.OnPointerDown(new Point(x, y));
        form.OnPointerUp(new Point(x, y));
    }
    public static void PressKey(Form form, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        form.OnKeyDown(key, modifiers);
    public static void TypeText(Form form, string text)
    {
        foreach (char c in text)
            form.OnTextInput(c);
    }
}
