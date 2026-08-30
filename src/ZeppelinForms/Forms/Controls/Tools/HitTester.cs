using System;
using System.Collections.Generic;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls.Tools;

internal static class HitTester
{
    public static UIElement? HitTest(UIElement root, Point pointInParentSpace)
    {
        if (!root.IsVisible || !root.IsHitTestVisible)
            return null;

        var local = new Point(
            pointInParentSpace.X - root.Position.X,
            pointInParentSpace.Y - root.Position.Y);

        // рендер поворачивает холст, значит курсор надо повернуть в обратную
        // сторону — иначе клики уедут тем сильнее, чем больше угол
        if (root.Rotation != 0f)
            local = RotateAround(local, root.Center, -root.Rotation);

        if (local.X < 0 || local.Y < 0 || local.X > root.ActualSize.Width || local.Y > root.ActualSize.Height)
            return null;

        if (root.HitTestSelfFirst(local))
            return root;
        switch (root)
        {
            case WrapControl single when single.Child is not null:
                return HitTest(single.Child, single.TransformPointToChild(local)) ?? root;

            case PanelControl panel:
                // с конца — последний добавленный рисуется поверх остальных
                for (int i = panel.Children.Count - 1; i >= 0; i--)
                {
                    var hit = HitTest(panel.Children[i], local);
                    if (hit is not null)
                        return hit;
                }
                return root;

            default:
                return root;
        }
    }

    private static Point RotateAround(Point point, Point center, float degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);

        float dx = point.X - center.X;
        float dy = point.Y - center.Y;

        return new Point(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos);
    }
}