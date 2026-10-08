using System.Collections.Concurrent;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Theming;

namespace ZeppelinForms.Forms.Styling;

/// <summary>
/// A declaration of a style sheet: a property named rather than referenced.
/// </summary>
/// <remarks>
/// <para>
/// A name doesn't always mean one property. <c>.accent { CheckColor: … }</c> may meet
/// a check box and a radio button, and each declares a CheckColor of its own. So the
/// declaration keeps every property of that name its selector can meet, and picks the
/// one of the element's type when it is applied — once per type, then from a cache.
/// </para>
/// <para>
/// The value is compiled per value type in advance, and errors are reported with the
/// sheet's line when it is loaded, not when the style first meets an element.
/// <see cref="Setter.Property"/> is the first of the candidates.
/// </para>
/// </remarks>
internal sealed class SheetSetter : Setter
{
    private readonly StyledProperty[] _candidates;
    private readonly IReadOnlyDictionary<Type, ValueExpr> _compiled;
    private readonly ConcurrentDictionary<Type, Setter?> _byElementType = new();

    public SheetSetter(StyledProperty[] candidates, string text, IReadOnlyDictionary<Type, ValueExpr> compiled)
        : base(candidates[0])
    {
        _candidates = candidates;
        _compiled = compiled;
        Text = text;
    }

    /// <summary>The value as written in the sheet.</summary>
    public string Text { get; }

    public override object? Value => Text;

    public override bool IsFromTheme => _compiled.Values.Any(v => v.UsesTheme);

    public override object? Resolve(Theme theme) =>
        _compiled.TryGetValue(Property.ValueType, out ValueExpr? expr) ? expr.Evaluate(theme) : null;

    internal override void Apply(UIElement element, Theme theme) =>
        _byElementType.GetOrAdd(element.GetType(), Build)?.Apply(element, theme);

    private Setter? Build(Type elementType)
    {
        StyledProperty? property = _candidates.FirstOrDefault(p => p.OwnerType.IsAssignableFrom(elementType));

        if (property is null || !_compiled.TryGetValue(property.ValueType, out ValueExpr? expr))
            return null;

        return expr.UsesTheme
            ? property.CreateSetter(theme => expr.Evaluate(theme))
            : property.CreateSetter(expr.Evaluate(null));
    }
}