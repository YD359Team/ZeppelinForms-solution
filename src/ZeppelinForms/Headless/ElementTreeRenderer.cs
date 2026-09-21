using ZeppelinForms.Animation;
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
        // внутри этой области чтение свойств отдаёт промежуточные значения
        // идущих переходов. Снаружи — цели: раскладка, логика и биндинги
        // должны видеть то, что присвоено, а не полпути к нему
        using (UIElement.BeginPresentation())
            Draw(element, g, Point.Empty, clip, cull: true);
    }

    /// <param name="origin">Абсолютная позиция родителя: обход накапливает
    /// её при спуске вместо подъёма к корню на каждом элементе.</param>
    /// <param name="clip">Видимая область в абсолютных координатах: грязный
    /// прямоугольник, сужённый областями всех предков. null — ограничений
    /// нет, рисуем всё.</param>
    /// <param name="cull">Можно ли доверять сложению смещений. Под поворотом
    /// или своим преобразованием содержимого нельзя: там прямоугольники
    /// в абсолютных координатах больше не описывают положение на холсте,
    /// и поддерево рисуется целиком.</param>
    /// <remarks>
    /// Клип сужается при спуске, а не только берётся из грязной области.
    /// Раньше отсечение работало лишь там, где платформа давала частичную
    /// перерисовку: на Android и в браузере clip всегда null, и панель
    /// со сотней строк рисовала их все, хотя видно десять. ClipRect обрезал
    /// пиксели, но строить текстовые блобы, пути и тени всё равно
    /// приходилось на каждую.
    /// </remarks>
    private static void Draw(UIElement element, Graphics g, Point origin, Rectangle? clip, bool cull)
    {
        // прозрачность могут на мгновение увести за [0; 1] кривые перехода —
        // приводим её к допустимой здесь, а не в сеттере
        float opacity = Math.Clamp(element.Opacity, 0f, 1f);

        if (!element.IsVisible || opacity <= 0f) return;
        if (!float.IsFinite(element.ActualSize.Width) || !float.IsFinite(element.ActualSize.Height)) return;

        var placed = new Point(origin.X + element.Position.X, origin.Y + element.Position.Y);

        // элемент целиком вне видимой области — пропускаем вместе с потомками.
        // Сдвиг при отрисовке в LocalDirtyBounds уже учтён, поэтому здесь
        // берётся место из раскладки, без него
        if (cull && clip is { } visible &&
            !element.LocalDirtyBounds.Offset(placed.X, placed.Y).IntersectsWith(visible))
            return;

        // а вот детям достаётся уже сдвинутое начало координат: сдвиг —
        // тот же перенос, что и Position, и отсечению он не мешает
        var position = new Point(
            placed.X + element.TranslateX,
            placed.Y + element.TranslateY);

        g.Save();
        g.Translate(element.Position.X, element.Position.Y);
        element.ApplyTransform(g);

        // Приглушение и прозрачность — один слой на элемент.
        // SaveDisabledLayer уже умеет альфу, поэтому при выключенном
        // элементе второй слой не нужен.
        bool needsLayer = !element.IsEnabled || element.Opacity < 1f;

        if (!element.IsEnabled)
            g.SaveDisabledLayer(element.DisabledOpacity * element.Opacity, element.DisabledDesaturation);
        else if (element.Opacity < 1f)
            g.SaveLayer(element.Opacity);

        // поворот и масштаб ломают сложение смещений: под ними прямоугольник
        // в абсолютных координатах уже не описывает, где ребёнок окажется
        // на холсте. Отсечение ниже отключаем — рисуем всё поддерево.
        // Сдвиг в этот список не входит: он уже сложен с position выше
        bool cullChildren = cull && !element.HasComplexTransform;

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

                    // своё преобразование содержимого — то же, что поворот:
                    // ZoomBox масштабирует ребёнка, и его абсолютные
                    // координаты уже не складываются из смещений
                    bool cullChild = cullChildren && !wrap.TransformsChild;

                    Draw(
                        wrap.Child,
                        g,
                        position,
                        cullChild ? Narrow(clip, wrap.ContentBounds, position) : null,
                        cullChild);

                    g.Restore();
                }

                // рамка не должна обрезаться содержимым
                wrap.DrawOverlay(g);
                break;

            case PanelControl panel:
                panel.Draw(g);
                g.Save();
                g.ClipRect(panel.ClipBounds);

                Rectangle? inside = cullChildren
                    ? Narrow(clip, panel.ClipBounds, position)
                    : null;

                foreach (var child in panel.Children)
                    Draw(child, g, position, inside, cullChildren);

                // уходящие — поверх живых: их место уже заняли соседи,
                // и под соседями исчезание было бы не видно
                if (panel.Exiting is { } exiting)
                    foreach (ExitingChild ghost in exiting)
                        DrawExiting(ghost, g, position);

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

    /// <summary>Нарисовать уходящего ребёнка в виде, соответствующем
    /// пройденной части исчезания. Отсечения нет: призраков единицы,
    /// а их положение уже не описывает ни одна раскладка.</summary>
    private static void DrawExiting(ExitingChild ghost, Graphics g, Point origin)
    {
        UIElement element = ghost.Element;
        VisibilityTransition rule = ghost.Rule;
        float t = ghost.Progress;

        float opacity = 1f + (rule.Opacity - 1f) * t;
        float scale = 1f + (rule.Scale - 1f) * t;

        if (opacity <= 0f) return;

        // масштаб — вокруг центра элемента, как у ScaleX/ScaleY:
        // исчезание и появление должны быть зеркальны
        var center = new Point(
            element.Position.X + element.ActualSize.Width / 2f,
            element.Position.Y + element.ActualSize.Height / 2f);

        g.Save();

        g.Translate(center.X + rule.OffsetX * t, center.Y + rule.OffsetY * t);
        g.Scale(scale, scale);
        g.Translate(-center.X, -center.Y);

        if (opacity < 1f) g.SaveLayer(opacity);

        Draw(element, g, origin, clip: null, cull: false);

        if (opacity < 1f) g.Restore();

        g.Restore();
    }

    /// <summary>Сузить видимую область областью содержимого элемента.
    /// area задана в его собственных координатах, position — его абсолютная
    /// позиция.</summary>
    private static Rectangle? Narrow(Rectangle? clip, Rectangle area, Point position)
    {
        Rectangle absolute = area.Offset(position.X, position.Y);

        return clip is { } visible ? visible.Intersect(absolute) : absolute;
    }
}