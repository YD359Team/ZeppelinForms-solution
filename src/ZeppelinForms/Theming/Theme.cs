using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Theming;

public sealed class Theme
{
    private readonly Dictionary<Type, Action<UIElement>> _appliers = [];

    public required string Name { get; init; }
    public required ThemeColors Colors { get; init; }
    public Font BaseFont { get; init; } = Font.Default;
    public CornerRadius DefaultCornerRadius { get; init; } = new(4f);

    /// <summary>Как оформить контрол этого типа. Наследники подхватят
    /// оформление предка, если своего нет.</summary>
    public Theme For<T>(Action<T, ThemeColors> apply) where T : UIElement
    {
        _appliers[typeof(T)] = element => apply((T)element, Colors);
        return this;
    }

    internal void Apply(UIElement element)
    {
        // ищем оформление вверх по иерархии типов: ToggleButton
        // получит настройки Button, если своих не задано
        for (Type? type = element.GetType(); type is not null; type = type.BaseType)
        {
            if (_appliers.TryGetValue(type, out Action<UIElement>? apply))
            {
                apply(element);
                return;
            }
        }
    }
}
