using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Styling;

/// <summary>
/// A state of an element a style selector can ask about: <c>:hover</c>,
/// <c>:checked</c>, <c>:disabled</c>. Controls raise and lower their pseudo-classes;
/// styles whose selectors name them start and stop applying.
/// </summary>
/// <remarks>
/// <para>
/// Pseudo-classes are registered by name, once per process, and compared by
/// reference. A control of the application registers its own the same way the
/// framework does: <c>public static readonly PseudoClass Busy = PseudoClass.Register("busy");</c>.
/// Registering a name twice returns the first registration.
/// </para>
/// <para>
/// Some are computed rather than stored: <c>:disabled</c> follows
/// <see cref="UIElement.IsEffectivelyEnabled"/>, <c>:focus</c> the focus, <c>:rtl</c>
/// the flow direction. Nobody raises those by hand — the element answers them from
/// its own state when a selector asks, and the framework restyles the element
/// when that state changes.
/// </para>
/// <para>
/// Structural conditions — <c>:first-child</c>, <c>:nth-child(2n+1)</c>, <c>:odd</c>
/// and the rest — are not pseudo-classes of this kind: they depend on the element's
/// place among its siblings, not on the element, and live in the selector itself.
/// </para>
/// </remarks>
public sealed class PseudoClass
{
    private static readonly Dictionary<string, PseudoClass> ByName = new(StringComparer.Ordinal);
    private static readonly List<PseudoClass> ByIndex = [];
    private static readonly System.Threading.Lock RegistrySync = new();

    /// <summary>The name without the colon: "hover", "focus-visible".</summary>
    public string Name { get; }

    /// <summary>The bit in the element's pseudo-class mask and in the style usage index.</summary>
    internal int Index { get; }

    /// <summary>For a computed pseudo-class: how the element answers it.
    /// Null — a stored one, raised and lowered by the control.</summary>
    internal Func<UIElement, bool>? Computed { get; }

    /// <summary>Whether the element answers it from its own state.</summary>
    public bool IsComputed => Computed is not null;

    private PseudoClass(string name, int index, Func<UIElement, bool>? computed)
    {
        Name = name;
        Index = index;
        Computed = computed;
    }

    /// <summary>Register a stored pseudo-class, or get the one already registered
    /// under this name.</summary>
    public static PseudoClass Register(string name) => Register(name, null);

    private static PseudoClass Register(string name, Func<UIElement, bool>? computed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.StartsWith(':'))
            name = name[1..];

        lock (RegistrySync)
        {
            if (ByName.TryGetValue(name, out PseudoClass? existing))
                return existing;

            var created = new PseudoClass(name, ByIndex.Count, computed);

            ByName.Add(name, created);
            ByIndex.Add(created);

            return created;
        }
    }

    /// <summary>The pseudo-class registered under this name; null — none is.</summary>
    public static PseudoClass? Find(string name)
    {
        if (name.StartsWith(':'))
            name = name[1..];

        lock (RegistrySync)
            return ByName.TryGetValue(name, out PseudoClass? found) ? found : null;
    }

    /// <summary>Everything registered so far, for the inspector and the style sheet
    /// diagnostics.</summary>
    public static IReadOnlyList<PseudoClass> Registered
    {
        get
        {
            lock (RegistrySync)
                return [.. ByIndex];
        }
    }

    public override string ToString() => ":" + Name;

    // ===== stored: raised by the controls =====

    /// <summary>The pointer is over the element.</summary>
    public static readonly PseudoClass Hover = Register("hover");

    /// <summary>The element is held down by the left button, a finger or a pen.</summary>
    public static readonly PseudoClass Pressed = Register("pressed");

    /// <summary>A check box, a toggle button, a radio button or a switch is on.</summary>
    public static readonly PseudoClass Checked = Register("checked");

    /// <summary>A three-state check box is in its third state.</summary>
    public static readonly PseudoClass Indeterminate = Register("indeterminate");

    /// <summary>An item is selected.</summary>
    public static readonly PseudoClass Selected = Register("selected");

    /// <summary>An expandable element shows its content: an open spoiler.</summary>
    public static readonly PseudoClass Expanded = Register("expanded");

    /// <summary>The element has its flyout open: a combo box, a date picker, a split button.</summary>
    public static readonly PseudoClass Open = Register("open");

    /// <summary>A text field that can't be edited.</summary>
    public static readonly PseudoClass ReadOnly = Register("read-only");

    /// <summary>A field whose validator rejected the content.</summary>
    public static readonly PseudoClass Invalid = Register("invalid");

    /// <summary>A field whose validator accepted the content. A field without
    /// a validator, or not checked yet, is neither valid nor invalid.</summary>
    public static readonly PseudoClass Valid = Register("valid");

    // ===== computed: answered by the element =====

    /// <summary>The element or any of its ancestors is disabled.</summary>
    public static readonly PseudoClass Disabled = Register("disabled", static e => !e.IsEffectivelyEnabled);

    /// <summary>The element and all its ancestors are enabled.</summary>
    public static readonly PseudoClass Enabled = Register("enabled", static e => e.IsEffectivelyEnabled);

    /// <summary>The element has the keyboard focus.</summary>
    public static readonly PseudoClass Focus = Register("focus", static e => e is IInputElement { IsFocused: true });

    /// <summary>The element has the focus, and the form shows it: the user works
    /// from the keyboard. See <see cref="Form.IsFocusVisible"/>.</summary>
    public static readonly PseudoClass FocusVisible = Register("focus-visible", static e => e.IsFocusVisible);

    /// <summary>The element or one of its descendants has the focus.</summary>
    public static readonly PseudoClass FocusWithin = Register("focus-within", static e => e.IsFocusWithin);

    /// <summary>The element is laid out right to left.</summary>
    public static readonly PseudoClass RightToLeft = Register("rtl", static e => e.IsRightToLeft);

    /// <summary>The element is laid out left to right.</summary>
    public static readonly PseudoClass LeftToRight = Register("ltr", static e => !e.IsRightToLeft);

    /// <summary>The element has no children: an empty panel, a wrapper without content.</summary>
    public static readonly PseudoClass Empty = Register("empty", static e => !UIElement.HasChildren(e));
}