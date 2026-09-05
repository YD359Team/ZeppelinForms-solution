using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Строит содержимое, зная выделенный размер. Позволяет менять раскладку
/// в зависимости от доступного места без подписки на изменение размера.
/// </summary>
public class LayoutBuilder : DecoratedWrapControl
{
    private Size _builtFor = Size.Empty;
    private bool _hasBuilt;

    /// <summary>Получает доступный размер, возвращает содержимое.</summary>
    public Func<Size, UIElement>? Builder { get; set; }

    /// <summary>
    /// Насколько должен измениться размер, чтобы содержимое пересобралось.
    /// Защищает от пересборки на каждый пиксель при перетаскивании рамки окна.
    /// </summary>
    public float RebuildThreshold { get; set; } = 1f;

    public event EventHandler? ContentRebuilt;

    public LayoutBuilder()
    {
        
    }

    public LayoutBuilder(UIElement child) : base(child)
    {

    }

    /// <summary>Пересобрать содержимое принудительно — например, после
    /// изменения данных, от которых зависит раскладка.</summary>
    public void Rebuild()
    {
        _hasBuilt = false;
        Invalidate();
    }

    private bool NeedsRebuild(Size available)
    {
        if (!_hasBuilt) return true;

        // бесконечность приходит от прокручиваемых панелей: строить
        // содержимое по ней бессмысленно, ждём конечного размера
        if (!float.IsFinite(available.Width) && !float.IsFinite(available.Height))
            return false;

        return Math.Abs(available.Width - _builtFor.Width) >= RebuildThreshold
            || Math.Abs(available.Height - _builtFor.Height) >= RebuildThreshold;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        if (Builder is not null && NeedsRebuild(inner))
        {
            _builtFor = inner;
            _hasBuilt = true;

            // присваивание Child само отвяжет прежнее поддерево
            // и привяжет новое через WrapControl
            Child = Builder(inner);

            ContentRebuilt?.Invoke(this, EventArgs.Empty);
        }

        return base.MeasureOverride(availableSize);
    }
}