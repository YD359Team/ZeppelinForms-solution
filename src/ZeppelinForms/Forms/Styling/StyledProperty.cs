using System.Runtime.CompilerServices;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Styling;

/// <summary>
/// Описание свойства, которым может управлять тема или стиль.
/// Само значение лежит в обычном поле контрола — здесь только метаданные
/// и доступ к нему без знания конкретного типа.
/// </summary>
public abstract class StyledProperty
{
    private static readonly List<StyledProperty> Registry = [];

    /// <summary>Номер в общем реестре. Он же — позиция бита в масках
    /// источника у элемента. Порядок зависит от того, в каком порядке
    /// загрузились типы, поэтому сохранять его куда-либо нельзя.</summary>
    internal int Index { get; }

    public string Name { get; }
    public Type OwnerType { get; }
    public Type ValueType { get; }

    /// <summary>Раздел в PropertyGrid.</summary>
    public string Category { get; }

    /// <summary>Меняет ли значение раскладку. Отсюда решается,
    /// хватит ли перерисовки или нужен пересчёт размеров.</summary>
    public bool AffectsLayout { get; }

    /// <summary>Наследуется ли вниз по дереву, как шрифт.</summary>
    public bool Inherits { get; }

    protected StyledProperty(
        string name, Type ownerType, Type valueType,
        string category, bool affectsLayout, bool inherits)
    {
        Name = name;
        OwnerType = ownerType;
        ValueType = valueType;
        Category = category;
        AffectsLayout = affectsLayout;
        Inherits = inherits;

        Index = Registry.Count;
        Registry.Add(this);
    }

    public static IReadOnlyList<StyledProperty> Registered => Registry;

    /// <summary>Свойства, объявленные этим типом и его предками.</summary>
    public static IEnumerable<StyledProperty> For(Type type)
    {
        // регистрация идёт из статических полей, а они инициализируются
        // при первом обращении к типу. Без этого вызова PropertyGrid
        // увидел бы пустой список у контрола, которого ещё не касались
        RuntimeHelpers.RunClassConstructor(type.TypeHandle);

        foreach (StyledProperty property in Registry)
            if (property.OwnerType.IsAssignableFrom(type))
                yield return property;
    }

    /// <summary>Прочитать значение, не зная его типа. Нужно PropertyGrid.</summary>
    public abstract object? GetBoxed(UIElement element);

    public abstract void SetBoxed(UIElement element, object? value);
}

public sealed class StyledProperty<T> : StyledProperty
{
    private readonly Func<UIElement, T> _get;
    private readonly Action<UIElement, T> _set;

    public T DefaultValue { get; }

    private StyledProperty(
        string name, Type ownerType, T defaultValue,
        string category, bool affectsLayout, bool inherits,
        Func<UIElement, T> get, Action<UIElement, T> set)
        : base(name, ownerType, typeof(T), category, affectsLayout, inherits)
    {
        DefaultValue = defaultValue;
        _get = get;
        _set = set;
    }

    /// <param name="set">Пишет в поле напрямую, минуя проверку источника.
    /// Через него работают ClearValue и восстановление умолчания —
    /// им проверка была бы помехой.</param>
    public static StyledProperty<T> Register<TOwner>(
        string name,
        Func<TOwner, T> get,
        Action<TOwner, T> set,
        T defaultValue = default!,
        string category = "Прочее",
        bool affectsLayout = false,
        bool inherits = false)
        where TOwner : UIElement =>
        new(name, typeof(TOwner), defaultValue, category, affectsLayout, inherits,
            element => get((TOwner)element),
            (element, value) => set((TOwner)element, value));

    public T GetValue(UIElement element) => _get(element);

    internal void Write(UIElement element, T value) => _set(element, value);

    public override object? GetBoxed(UIElement element) => _get(element);

    public override void SetBoxed(UIElement element, object? value)
    {
        // из PropertyGrid значение приходит от пользователя, значит должно
        // помечаться как заданное вручную — как при обычном присваивании
        if (value is T typed) element.SetStyledValue(this, typed);
    }
}