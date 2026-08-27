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

        if (local.X < 0 || local.Y < 0 || local.X > root.Size.Width || local.Y > root.Size.Height)
            return null;

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
}
