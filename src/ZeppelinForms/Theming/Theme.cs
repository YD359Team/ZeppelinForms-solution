using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Theming;

public sealed class Theme
{
    private readonly Dictionary<Type, Action<UIElement>> _appliers = [];

    /// <summary>
    /// Готовые цепочки применителей по типу элемента, от базового
    /// к производному. Считаются один раз на тип: цепочка зависит
    /// только от иерархии и набора применителей, а раньше на каждый
    /// элемент заводился Stack и заново обходились все базовые типы
    /// со словарным поиском на каждом шаге — на форме из трёхсот
    /// меток это триста стеков и триста обходов.
    /// </summary>
    private readonly Dictionary<Type, Action<UIElement>[]> _chains = [];

    private readonly System.Threading.Lock _chainSync = new();

    public required string Name { get; init; }
    public required ThemeColors Colors { get; init; }
    public Font BaseFont { get; init; } = Font.Default;

    /// <summary>Как оформить контрол этого типа. Наследники подхватят
    /// оформление предка, если своего нет.</summary>
    public Theme For<T>(Action<T, ThemeColors> apply) where T : UIElement
    {
        _appliers[typeof(T)] = element => apply((T)element, Colors);

        // набор применителей изменился — посчитанные цепочки больше
        // не описывают тему
        lock (_chainSync)
            _chains.Clear();

        return this;
    }

    internal void Apply(UIElement element)
    {
        Action<UIElement>[] chain = GetChain(element.GetType());

        if (chain.Length == 0) return;

        // на время обхода сеттеры помечают записи как «от темы».
        // Предыдущее значение сохраняется, а не гасится в false:
        // применитель может создать или присоединить дочерний элемент,
        // и вложенный вызов иначе снял бы пометку у внешнего обхода
        bool wasApplying = UIElement.ApplyingTheme;
        UIElement.ApplyingTheme = true;

        try
        {
            // от базового типа к производному: специализация дополняет
            // общее оформление, а не подменяет его целиком
            foreach (Action<UIElement> apply in chain)
                apply(element);
        }
        finally
        {
            UIElement.ApplyingTheme = wasApplying;
        }
    }

    /// <summary>Цепочка применителей для типа, от базового к производному.</summary>
    private Action<UIElement>[] GetChain(Type type)
    {
        lock (_chainSync)
        {
            if (_chains.TryGetValue(type, out Action<UIElement>[]? cached))
                return cached;
        }

        var chain = new List<Action<UIElement>>();

        for (Type? current = type; current is not null; current = current.BaseType)
        {
            if (_appliers.TryGetValue(current, out Action<UIElement>? apply))
                chain.Add(apply);
        }

        // собирали от производного к базовому — разворачиваем,
        // порядок применения обратный
        chain.Reverse();

        Action<UIElement>[] result = chain.Count == 0 ? [] : [.. chain];

        lock (_chainSync)
            _chains[type] = result;

        return result;
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