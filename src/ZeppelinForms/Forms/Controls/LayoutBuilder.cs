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
    private object? _builtKey;
    private bool _hasBuilt;

    /// <summary>Получает доступный размер, возвращает содержимое.</summary>
    public Func<Size, UIElement>? Builder { get; set; }

    /// <summary>Что считать поводом пересобрать содержимое.
    /// Null — изменение размера больше <see cref="RebuildThreshold"/>.</summary>
    /// <remarks>
    /// Порог в единицах размера годится, пока пересборка дешёвая, и плох
    /// для адаптивности: при перетаскивании рамки окна он срабатывает почти
    /// каждый кадр, а пересборка заменяет Child целиком — вместе с фокусом,
    /// позицией прокрутки и набранным текстом. Ключ позволяет пересобираться
    /// только когда меняется то, от чего раскладка действительно зависит:
    /// класс размера, ориентация, число влезающих колонок.
    /// </remarks>
    public Func<Size, object?>? RebuildKey { get; set; }

    /// <summary>
    /// Насколько должен измениться размер, чтобы содержимое пересобралось.
    /// Защищает от пересборки на каждый пиксель при перетаскивании рамки окна.
    /// Не действует, когда задан <see cref="RebuildKey"/>.
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

    private bool NeedsRebuild(Size available, object? key)
    {
        if (!_hasBuilt) return true;

        // бесконечность приходит от прокручиваемых панелей: строить
        // содержимое по ней бессмысленно, ждём конечного размера
        if (!float.IsFinite(available.Width) && !float.IsFinite(available.Height))
            return false;

        // ключ задан — размер сам по себе поводом не считается
        if (RebuildKey is not null)
            return !Equals(key, _builtKey);

        return Math.Abs(available.Width - _builtFor.Width) >= RebuildThreshold
            || Math.Abs(available.Height - _builtFor.Height) >= RebuildThreshold;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        if (Builder is not null)
        {
            object? key = RebuildKey?.Invoke(inner);

            if (NeedsRebuild(inner, key))
            {
                _builtFor = inner;
                _builtKey = key;
                _hasBuilt = true;

                // присваивание Child само отвяжет прежнее поддерево
                // и привяжет новое через WrapControl
                Child = Builder(inner);

                ContentRebuilt?.Invoke(this, EventArgs.Empty);
            }
        }

        return base.MeasureOverride(availableSize);
    }
}