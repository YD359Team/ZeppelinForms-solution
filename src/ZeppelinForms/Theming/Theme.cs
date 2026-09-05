using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

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
        // от базового типа к производному: специализация дополняет общее
        // оформление, а не подменяет его целиком. Раньше здесь стоял return
        // на первом совпадении, и .For<TextBox> отменял .For<InteractiveControl>
        var pending = new Stack<Action<UIElement>>();

        for (Type? type = element.GetType(); type is not null; type = type.BaseType)
        {
            if (_appliers.TryGetValue(type, out Action<UIElement>? apply))
                pending.Push(apply);
        }

        while (pending.Count > 0)
            pending.Pop()(element);
    }

    internal static void Apply(UIElement element, ControlStyle style)
    {
        if (style.Background is Color bg) element.Background = bg;
        if (style.CornerRadius is CornerRadius radius) element.CornerRadius = radius;

        if (element is DecoratedControl decorated)
        {
            if (style.Border is Color border) decorated.BorderColor = border;
        }

        if (element is InteractiveControl interactive)
        {
            if (style.BorderFocus is Color focus) interactive.FocusBorderColor = focus;
        }

        if (element is ITextElement text)
        {
            if (style.Text is Color color) text.TextColor = color;
        }
    }
}
