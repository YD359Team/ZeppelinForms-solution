using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Effects;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Headless;

/// <summary>
/// Обход дерева элементов и вызов их отрисовки. Ничего не знает о бэкенде:
/// работает через абстрактный <see cref="Graphics"/>, поэтому годится и для
/// Skia, и для headless-заглушки, и для любого будущего рендерера.
/// </summary>
public static class ElementTreeRenderer
{
    /// <param name="clip">Грязная область в абсолютных координатах.
    /// null — рисовать всё.</param>
    public static void Draw(UIElement element, Graphics g, Rectangle? clip = null)
    {
        if (!element.IsVisible || element.Opacity <= 0f) return;
        if (!float.IsFinite(element.ActualSize.Width) || !float.IsFinite(element.ActualSize.Height)) return;

        // элемент целиком вне грязной области — пропускаем вместе с потомками
        if (clip is { } dirty && !element.DirtyBounds.IntersectsWith(dirty))
            return;

        g.Save();
        g.Translate(element.Position.X, element.Position.Y);

        // Приглушение и прозрачность — один слой на элемент.
        // SaveDisabledLayer уже умеет альфу, поэтому при выключенном
        // элементе второй слой не нужен.
        bool needsLayer = !element.IsEnabled || element.Opacity < 1f;

        if (!element.IsEnabled)
            g.SaveDisabledLayer(element.DisabledOpacity * element.Opacity, element.DisabledDesaturation);
        else if (element.Opacity < 1f)
            g.SaveLayer(element.Opacity);

        if (element.Rotation != 0f)
        {
            // поворот вокруг центра: сдвиг в центр, поворот, сдвиг обратно
            Point center = element.Center;
            g.Translate(center.X, center.Y);
            g.Rotate(element.Rotation);
            g.Translate(-center.X, -center.Y);
        }

        if (element.BoxShadow is { } shadow)
            g.DrawShadow(element.LocalBounds, shadow);

        EffectChain? effects = element.EffectsOrNull;

        if (effects is { IsEmpty: false })
            effects.Begin(g, element.LocalBounds);

        switch (element)
        {
            case UnitControl unit:
                unit.Draw(g);
                break;

            case WrapControl wrap:
                wrap.Draw(g);

                if (wrap.Child is not null)
                {
                    g.Save();
                    g.ClipRect(wrap.ContentBounds);
                    wrap.ApplyChildTransform(g);
                    Draw(wrap.Child, g, clip);
                    g.Restore();
                }

                // рамка не должна обрезаться содержимым
                wrap.DrawOverlay(g);
                break;

            case PanelControl panel:
                panel.Draw(g);
                g.Save();
                g.ClipRect(panel.ContentBounds);
                foreach (var child in panel.Children)
                    Draw(child, g, clip);
                g.Restore();

                // полоса прокрутки не должна обрезаться содержимым
                panel.DrawOverlay(g);
                break;
        }

        if (effects is { IsEmpty: false })
            effects.End(g, element.LocalBounds);

        if (needsLayer)
            g.Restore();

        g.Restore();
    }
}