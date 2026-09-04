using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing.Effects;

/// <summary>
/// Преобразование того, как элемент попадает на экран. Эффекты
/// применяются цепочкой и не знают друг о друге.
/// </summary>
public abstract class VisualEffect
{
    /// <summary>Насколько эффект расширяет область отрисовки за границы
    /// элемента: тень и размытие выходят наружу и должны попасть в dirty-регион.</summary>
    public virtual float BleedRadius => 0f;

    /// <summary>Нужен ли отдельный слой. Эффекты, читающие уже
    /// нарисованное, обязаны его требовать.</summary>
    public virtual bool RequiresLayer => false;

    /// <summary>Подготовить канвас перед отрисовкой элемента.</summary>
    public abstract void Begin(Graphics g, Rectangle bounds);

    /// <summary>Завершить: закрыть слой, дорисовать поверх.</summary>
    public virtual void End(Graphics g, Rectangle bounds) { }
}
