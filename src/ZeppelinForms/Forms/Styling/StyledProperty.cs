using System.Runtime.CompilerServices;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Styling;

/// <summary>
/// Describes a property that a theme or a style can control.
/// The value itself lies in an ordinary field of the control — here there are
/// only metadata and access to it without knowing the concrete type.
/// </summary>
public abstract class StyledProperty
{
    private static readonly List<StyledProperty> Registry = [];

    /// <summary>Guards the registry. Properties are registered from static field
    /// initializers, and type initializers of different types can run on different
    /// threads at the same time — parallel snapshot tests do exactly that. Without
    /// the lock two properties could get the same Index, that is, share one bit
    /// in the source masks, and the theme flag of one would silently switch
    /// the other.</summary>
    private static readonly System.Threading.Lock RegistrySync = new();

    /// <summary>The number in the shared registry. It is also the bit position
    /// in the element's source masks. The order depends on the order in which
    /// types were loaded, so it must never be persisted anywhere.</summary>
    internal int Index { get; }

    public string Name { get; }
    public Type OwnerType { get; }
    public Type ValueType { get; }

    /// <summary>The section in PropertyGrid.</summary>
    public string Category { get; }

    /// <summary>Whether the value changes layout. This decides whether
    /// a redraw is enough or sizes must be recomputed.</summary>
    public bool AffectsLayout { get; }

    /// <summary>Whether it is inherited down the tree, like the font.</summary>
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

        lock (RegistrySync)
        {
            Index = Registry.Count;
            Registry.Add(this);
        }
    }

    /// <summary>A snapshot of the registry: the live list may grow on another
    /// thread while it is being enumerated.</summary>
    public static IReadOnlyList<StyledProperty> Registered
    {
        get
        {
            lock (RegistrySync)
                return [.. Registry];
        }
    }

    /// <summary>The property with this registry number. The element's source masks
    /// keep only numbers, and whoever walks a mask needs the property back.</summary>
    internal static StyledProperty ByIndex(int index)
    {
        lock (RegistrySync)
            return Registry[index];
    }

    /// <summary>Properties declared by this type and its ancestors.</summary>
    public static IEnumerable<StyledProperty> For(Type type)
    {
        // registration happens from static fields, and those are initialized
        // on first access to the type. Without this call PropertyGrid would see
        // an empty list for a control nobody has touched yet.
        // It runs before the lock is taken: a type initializer registers its
        // properties under the same lock, and holding it here while another thread
        // is initializing that type would deadlock the two
        RuntimeHelpers.RunClassConstructor(type.TypeHandle);

        StyledProperty[] snapshot;

        lock (RegistrySync)
            snapshot = [.. Registry];

        foreach (StyledProperty property in snapshot)
            if (property.OwnerType.IsAssignableFrom(type))
                yield return property;
    }

    /// <summary>Read the value without knowing its type. Needed by PropertyGrid.</summary>
    public abstract object? GetBoxed(UIElement element);

    public abstract void SetBoxedAsUser(UIElement element, object? value);

    /// <summary>The value type. Needed by the binding: it gets the source value
    /// through reflection and must convert it to the property's type.</summary>
    public abstract Type PropertyType { get; }

    /// <summary>Write a value without knowing its type statically.
    /// The only consumer is the binding: there the value comes
    /// from PropertyInfo.GetValue, and there is nowhere to type it.</summary>
    internal abstract void WriteBoxedDirect(UIElement element, object? value);

    /// <summary>Put the value the theme no longer sets back to its default.
    /// Typed dispatch for code that knows the property only by its number.</summary>
    internal abstract void WithdrawThemeValue(UIElement element);
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

    /// <param name="set">Writes to the field directly, bypassing the source check.
    /// ClearValue and restoring the default work through it —
    /// the check would only get in their way.</param>
    public static StyledProperty<T> Register<TOwner>(
        string name,
        Func<TOwner, T> get,
        Action<TOwner, T> set,
        T defaultValue = default!,
        string category = "Other",
        bool affectsLayout = false,
        bool inherits = false)
        where TOwner : UIElement =>
        new(name, typeof(TOwner), defaultValue, category, affectsLayout, inherits,
            element => get((TOwner)element),
            (element, value) => set((TOwner)element, value));

    public T GetValue(UIElement element) => _get(element);

    internal void Write(UIElement element, T value) => _set(element, value);

    public override object? GetBoxed(UIElement element) => _get(element);

    public override void SetBoxedAsUser(UIElement element, object? value)
    {
        // from PropertyGrid the value comes from the user, so it must be marked
        // as set manually — as with a regular assignment.
        //
        // A type pattern can't be used for the check: "value is T" on null gives
        // false even when T allows null, and clearing a string or resetting
        // the font from the inspector would be impossible
        if (value is null)
        {
            if (default(T) is null) element.SetStyledValue(this, default!);
            return;
        }

        if (value is T typed) element.SetStyledValue(this, typed);
    }

    public override Type PropertyType => typeof(T);

    // the same null trap as in SetBoxedAsUser: "null is T" is false even for
    // a nullable T, so a binding that brought null into a property with
    // a non-null default used to write the default instead of null
    internal override void WriteBoxedDirect(UIElement element, object? value) =>
        Write(element, value switch
        {
            null when default(T) is null => default!,
            T typed => typed,
            _ => DefaultValue,
        });

    internal override void WithdrawThemeValue(UIElement element) =>
        element.WithdrawThemeValue(this);
}