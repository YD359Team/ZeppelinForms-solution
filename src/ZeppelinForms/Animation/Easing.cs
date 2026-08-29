using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Animation;

public static class Easing
{
    public static float Linear(float t) => t;
    public static float EaseOut(float t) => 1f - MathF.Pow(1f - t, 3f);
    public static float EaseIn(float t) => t * t * t;
    public static float EaseInOut(float t) =>
        t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;
}

public interface IAnimation
{
    object Target { get; }
    string Key { get; }

    /// <summary>Продвинуть на прошедшее время. false — анимация закончилась.</summary>
    bool Advance(TimeSpan elapsed);
}

public sealed class Animation<T> : IAnimation
{
    private readonly Func<T, T, float, T> _interpolate;
    private readonly Action<T> _apply;
    private readonly T _from;
    private readonly T _to;
    private readonly TimeSpan _duration;
    private readonly Func<float, float> _easing;
    private readonly Action? _completed;

    private TimeSpan _elapsed;

    public object Target { get; }

    /// <summary>Что именно анимируется. Новая анимация с тем же ключом
    /// вытесняет предыдущую — иначе два наведения подряд подерутся за цвет.</summary>
    public string Key { get; }

    public Animation(
        object target, string key,
        T from, T to, TimeSpan duration,
        Func<T, T, float, T> interpolate,
        Action<T> apply,
        Func<float, float>? easing = null,
        Action? completed = null)
    {
        Target = target;
        Key = key;
        _from = from;
        _to = to;
        _duration = duration;
        _interpolate = interpolate;
        _apply = apply;
        _easing = easing ?? Easing.EaseOut;
        _completed = completed;
    }

    public bool Advance(TimeSpan elapsed)
    {
        _elapsed += elapsed;

        float t = _duration <= TimeSpan.Zero
            ? 1f
            : Math.Clamp((float)(_elapsed / _duration), 0f, 1f);

        _apply(_interpolate(_from, _to, _easing(t)));

        if (t < 1f) return true;

        _completed?.Invoke();
        return false;
    }
}

public static class Interpolators
{
    public static float Float(float a, float b, float t) => a + (b - a) * t;

    public static Color Color(Color a, Color b, float t) => new(
        (byte)Float(a.A, b.A, t),
        (byte)Float(a.R, b.R, t),
        (byte)Float(a.G, b.G, t),
        (byte)Float(a.B, b.B, t));

    public static Point Point(Point a, Point b, float t) =>
        new(Float(a.X, b.X, t), Float(a.Y, b.Y, t));

    public static Size Size(Size a, Size b, float t) =>
        new(Float(a.Width, b.Width, t), Float(a.Height, b.Height, t));
}
