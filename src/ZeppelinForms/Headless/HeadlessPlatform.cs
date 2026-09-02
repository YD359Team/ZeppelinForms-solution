using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Tools;

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

    public HeadlessPlatform(bool registerServices = true)
    {
        if (!registerServices) return;

        HeadlessTextMeasurer.Register();
        HeadlessImageDecoder.Register();
        HeadlessElementRenderer.Register();
        BuiltInProperties.Register();
    }

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
