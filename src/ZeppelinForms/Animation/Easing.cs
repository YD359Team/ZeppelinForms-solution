using System;
using System.Collections.Generic;
using System.Text;

namespace ZeppelinForms.Animation;

public static class Easing
{
    public static float Linear(float t) => t;
    public static float EaseOut(float t) => 1f - MathF.Pow(1f - t, 3f);
    public static float EaseIn(float t) => t * t * t;
    public static float EaseInOut(float t) =>
        t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;
}
