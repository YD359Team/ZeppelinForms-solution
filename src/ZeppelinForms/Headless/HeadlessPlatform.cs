using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Tools;

namespace ZeppelinForms.Headless;

/// <summary>
/// A platform without windows: layout and input work, there is no drawing.
/// Needed for tests and for running in an environment without graphics.
/// </summary>
public sealed class HeadlessPlatform : IPlatform, INestedLoopSupport
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

        // without a system there is no system motion setting either. "Don't reduce"
        // is connected explicitly: otherwise a test run after WindowsPlatformTests
        // on a machine with animations turned off would see that machine's setting,
        // and transitions in it would become instant
        ZeppelinForms.Animation.Motion.UseSystemSettings(HeadlessMotionSettings.Instance);
    }

    /// <summary>A deterministic motion setting for a headless run.</summary>
    private sealed class HeadlessMotionSettings : ISystemMotionSettings
    {
        public static readonly HeadlessMotionSettings Instance = new();

        public bool PrefersReducedMotion => false;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }

    public IPlatformWindow CreateWindow(Form form)
    {
        var window = new HeadlessWindow(this, form);

        form.PlatformWindow = window;
        form.Platform = this;

        // there is no window, so the client area is set right away from Form.Size
        form.ClientSize = form.Size;
        form.PerformLayout();

        _windows.Add(window);
        return window;
    }

    public void RunNestedLoop(IPlatformWindow until)
    {
        var window = (HeadlessWindow)until;

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

    /// <summary>Run one deferred action. false — the queue is empty.</summary>
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

    /// <summary>Pump the queue to the end — handy in tests after Invoke.</summary>
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

    /// <summary>The platform's entry point for <see cref="App.Run"/>: runs the loop
    /// until the last window closes, like a desktop platform.</summary>
    /// <remarks>
    /// Used to throw NotImplementedException, so an application could not be
    /// started on this platform at all — although running without graphics is
    /// one of the two reasons it exists. The loop itself was already here, in Run.
    /// </remarks>
    public void Start() => Run();
}