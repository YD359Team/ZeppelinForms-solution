using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Accessibility;

/// <summary>What an element is to an assistive technology: its role, name, states,
/// value, children and the actions it offers. Platform bridges translate peers to
/// UI Automation, ARIA and the others; the controls themselves know nothing of them.</summary>
/// <remarks>
/// <para>
/// A peer is created lazily, the first time a bridge asks for it — which happens
/// only when a screen reader or another assistive technology is actually connected.
/// Until then no peer exists and nothing is paid for them: no objects, no event
/// subscriptions.
/// </para>
/// <para>
/// Most peers stand for an element (<see cref="UIElementPeer"/>). Some stand for
/// a part of one that is not an element at all — a row the list draws itself, a menu
/// item, a tree node outside the realized range. Such a peer is created and cached
/// by the peer of its host, so that it stays the same object for as long as the
/// part exists: bridges identify peers by reference.
/// </para>
/// <para>
/// Everything is read on demand, never stored: a bridge asks when it needs it, and
/// the answer is always the control's current state.
/// </para>
/// </remarks>
public abstract class AccessibilityPeer
{
    public abstract AccessibilityRole Role { get; }

    /// <summary>What the element is called: the words a screen reader says
    /// together with the role. Never null; empty — nameless.</summary>
    public abstract string Name { get; }

    /// <summary>A longer explanation, read after the name and the role.</summary>
    public virtual string? Description => null;

    public virtual AccessibilityStates States => AccessibilityStates.None;

    /// <summary>The value as text: what a text field holds, which item a combo box
    /// shows. Null — the element has no value.</summary>
    public virtual string? Value => null;

    /// <summary>The numeric value of a slider, a progress, a spin button.</summary>
    public virtual RangeInfo? Range => null;

    public virtual AccessibilityActions Actions => AccessibilityActions.None;

    /// <summary>1 to 6 for a heading, 0 otherwise.</summary>
    public virtual int HeadingLevel => 0;

    /// <summary>The depth of a tree item, one-based; 0 outside a tree.</summary>
    public virtual int Level => 0;

    /// <summary>The place among the siblings of the same kind — "3 of 10" — for
    /// items, tabs, menu items and rows.</summary>
    public virtual SetPosition? Position => null;

    public virtual AccessibilityLiveSetting LiveSetting => AccessibilityLiveSetting.Off;

    /// <summary>The access key, as a screen reader announces it: "Alt+S".
    /// Null — none.</summary>
    public virtual string? AccessKey => null;

    /// <summary>Null for the root — the peer of a form.</summary>
    public abstract AccessibilityPeer? Parent { get; }

    /// <summary>The children in reading order. Built on each call from the current
    /// state; the peers in it are the cached ones.</summary>
    public abstract IReadOnlyList<AccessibilityPeer> Children { get; }

    /// <summary>The bounds in the form's client coordinates, in device-independent
    /// pixels. A bridge converts them to screen pixels with its window.</summary>
    public abstract Rectangle Bounds { get; }

    /// <summary>The form the peer belongs to; null while it is not in one.</summary>
    public abstract Form? Form { get; }

    /// <summary>The element behind the peer; null for a part that is not an element.</summary>
    public virtual UIElement? Element => null;

    /// <summary>For a focused composite — a list, a tree, a grid — the part inside
    /// it the keyboard works on: its current item. Screen readers announce that
    /// rather than the container.</summary>
    public virtual AccessibilityPeer? FocusedDescendant => null;

    // ===== actions =====
    //
    // Each returns false where it doesn't apply, or where the control refused —
    // disabled, read-only. The supported ones are listed in Actions.

    public virtual bool Invoke() => false;

    public virtual bool Toggle() => false;

    public virtual bool Select() => false;

    public virtual bool Expand() => false;

    public virtual bool Collapse() => false;

    public virtual bool SetValue(string value) => false;

    public virtual bool SetRangeValue(double value) => false;

    public virtual bool Focus() => false;

    public virtual bool ScrollIntoView() => false;

    // ===== notifications =====

    protected void RaisePropertyChanged(AccessibilityProperty property) =>
        AccessibilityEvents.RaisePropertyChanged(this, property);

    protected void RaiseStructureChanged() => AccessibilityEvents.RaiseStructureChanged(this);

    public override string ToString() => $"{Role} \"{Name}\"";
}