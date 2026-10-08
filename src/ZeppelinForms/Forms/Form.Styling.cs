using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms;

/// <summary>The form's styles, and the pseudo-classes only the form knows the
/// state of: <c>:focus</c>, <c>:focus-visible</c> and <c>:focus-within</c>.</summary>
public partial class Form
{
    private Styles? _styles;

    /// <summary>Styles for everything in this form, overlays included. They beat the
    /// application's styles of equal specificity and yield to the elements' own.</summary>
    public Styles Styles => _styles ??= new Styles(RestyleAll);

    internal Styles? StylesOrNull => _styles;

    /// <summary>Apply the theme and the styles to the whole form again: the styles
    /// that cover it changed.</summary>
    internal void RestyleAll()
    {
        if (Content is not null)
            ApplyTheme(Content);

        foreach (UIElement overlay in _overlays.ToArray())
            ApplyTheme(overlay);

        Invalidate();
    }

    /// <summary>The application's styles changed: every open form restyles.</summary>
    internal static void RestyleOpenForms()
    {
        foreach (Form form in s_openForms.ToArray())
            form.RestyleAll();
    }

    // ===== deferred restyles =====

    /// <summary>Panels and wrappers whose children changed since the last layout pass,
    /// see <see cref="UIElement.RestyleAfterChildrenChanged"/>.</summary>
    private HashSet<UIElement>? _structureRestyles;

    /// <summary>Restyle the element's children before the next layout pass.</summary>
    internal void QueueStructureRestyle(UIElement element)
    {
        (_structureRestyles ??= new HashSet<UIElement>(ReferenceEqualityComparer.Instance)).Add(element);

        // inside a pass the request is picked up by the pass loop or by the next
        // EnsureLayout; outside, the next frame lays out anyway
        if (_layoutDepth == 0) Invalidate();
    }

    private bool HasPendingRestyles => _structureRestyles is { Count: > 0 };

    /// <summary>Run the queued restyles: before measuring, since styles change sizes.</summary>
    private void FlushStructureRestyles()
    {
        // a restyle may queue more — a style that changes a child's visibility,
        // and with it the children of a nested panel. Each round takes what was
        // queued before it; a bounded number of rounds keeps a style that flips
        // its own condition from spinning the frame forever
        for (int round = 0; round < 4 && _structureRestyles is { Count: > 0 } queued; round++)
        {
            UIElement[] batch = [.. queued];
            queued.Clear();

            foreach (UIElement element in batch)
                if (ReferenceEquals(element.FindOwner(), this))
                    element.RestyleStructureNow();
        }
    }

    // ===== focus =====

    /// <summary>The element that had the focus at the last change: the dispatcher
    /// reports only the new one, and the old one has pseudo-classes to drop.</summary>
    private UIElement? _focusedForStyles;

    private void OnFocusChangedForStyles(object? sender, UIElement? focused)
    {
        UIElement? previous = _focusedForStyles;
        _focusedForStyles = focused;

        if (!StyleUsage.Any) return;

        foreach (UIElement? element in (ReadOnlySpan<UIElement?>)[previous, focused])
        {
            if (element is null) continue;

            element.OnPseudoClassChanged(PseudoClass.Focus);
            element.OnPseudoClassChanged(PseudoClass.FocusVisible);
        }

        if (StyleUsage.Of(PseudoClass.FocusWithin).Used)
            RestyleFocusWithin(previous, focused);
    }

    /// <summary>:focus-within changes on the ancestors of one element and not of the
    /// other; the common part keeps the focus inside and stays as it is.</summary>
    private static void RestyleFocusWithin(UIElement? previous, UIElement? focused)
    {
        var before = new HashSet<UIElement>(ReferenceEqualityComparer.Instance);

        for (UIElement? current = previous; current is not null; current = current.Parent)
            before.Add(current);

        var after = new HashSet<UIElement>(ReferenceEqualityComparer.Instance);

        for (UIElement? current = focused; current is not null; current = current.Parent)
            after.Add(current);

        foreach (UIElement element in before)
            if (!after.Contains(element))
                element.OnPseudoClassChanged(PseudoClass.FocusWithin);

        foreach (UIElement element in after)
            if (!before.Contains(element))
                element.OnPseudoClassChanged(PseudoClass.FocusWithin);
    }

    /// <summary>The keyboard or the pointer took over: :focus-visible of the focused
    /// element flips.</summary>
    private void OnFocusVisibleChangedForStyles() =>
        _focusDispatcher.FocusedElement?.OnPseudoClassChanged(PseudoClass.FocusVisible);

    /// <summary>The language changed, and with it maybe the direction: restyle what
    /// :rtl and :ltr select.</summary>
    private void RestyleForDirection()
    {
        if (StyleUsage.Of(PseudoClass.RightToLeft).Used || StyleUsage.Of(PseudoClass.LeftToRight).Used)
            RestyleAll();
    }
}