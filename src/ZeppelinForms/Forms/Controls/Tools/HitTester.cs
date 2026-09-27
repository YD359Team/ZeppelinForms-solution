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

        // the renderer shifts, rotates and scales the canvas, so the point must be
        // taken through the same transform in reverse. The transform itself lives
        // in UIElement — in one place with the forward one, so that the picture
        // and the clicks don't drift apart.
        //
        // The rotation is part of it. The point used to be rotated here once more
        // on top of that, and on a rotated element clicks landed where it would
        // have been at twice the angle
        Point local = root.TransformPointToLocal(pointInParentSpace);

        if (local.X < 0 || local.Y < 0 || local.X > root.ActualSize.Width || local.Y > root.ActualSize.Height)
            return null;

        if (root.HitTestSelfFirst(local))
            return root;
        switch (root)
        {
            case WrapControl single when single.Child is not null:
                return HitTest(single.Child, single.TransformPointToChild(local)) ?? root;

            case PanelControl panel:
                // from the end — the last added one is drawn on top of the others
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