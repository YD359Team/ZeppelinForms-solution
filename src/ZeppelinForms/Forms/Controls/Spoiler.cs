using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Control can collapse\expand child content
/// </summary>
public class Spoiler : WrapControl, IBorderedElement
{
    public bool IsCollapsed
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnCollapsedStateChanged(value);
            Invalidate();
        }
    }

    // IBorderedElement
    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 0f;

    public override void Draw(Graphics g)
    {
        if (this.BorderWidth > 0)
            g.DrawRectangle(this.LocalBounds, this.BorderColor, this.BorderWidth);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Свёрнут — ребёнка не меряем вообще, место под него не резервируется
        // (тот же принцип, что и IsVisible=false в StackPanel).
        if (IsCollapsed)
            return ResolveSize(Size.Empty, availableSize);

        return base.MeasureOverride(availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (IsCollapsed)
            return finalSize; // ребёнка не трогаем — он не был измерен на этом проходе

        return base.ArrangeOverride(finalSize);
    }

    protected virtual void OnCollapsedStateChanged(bool isCollapsed)
    {
        // хук для наследников — например, чтобы анимировать раскрытие
    }
}