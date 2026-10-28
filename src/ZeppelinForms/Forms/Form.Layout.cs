using ZeppelinForms.Diagnostics;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.DragDrop;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms;

/// <summary>The tree of the form: attaching and detaching a subtree, the layout passes and
/// what is to be redrawn.</summary>
public partial class Form
{
    /// <summary>Reset the measure cache for the whole tree. Needed where sizes
    /// depend not on the elements' properties but on something shared:
    /// the base font, the scale, the text measurer.</summary>
    internal void InvalidateMeasureTree()
    {
        if (Content is not null)
            Walk(Content, static element => element.InvalidateMeasure());

        foreach (UIElement overlay in _overlays.ToArray())
            Walk(overlay, static element => element.InvalidateMeasure());
    }

    /// <summary>Show an overlay: attach the subtree (theme, font, names,
    /// OnAttached) and put it on top of the content. The only way into
    /// the overlay list — flyouts, toasts, tooltips, menus and the inspector
    /// all go through it.</summary>
    /// <remarks>Attaching happens before measuring: the theme sets fonts
    /// and padding, and the overlay's position is computed from DesiredSize.</remarks>
    private void AttachOverlay(UIElement content)
    {
        if (_overlays.Contains(content)) return;

        ZfContract.Require(
            content.Owner is null,
            "The overlay already belongs to a form. One element cannot be " +
            "an overlay of two forms at once: Owner would be overwritten, " +
            "and the first form would lose its link to it.");

        content.Owner = this;
        AttachTree(content);

        _overlays.Add(content);
        RaiseOverlaysChanged();

        // an overlay has no parent whose arrange would bring it in: one that asks to
        // appear with a transition — a tooltip fades in — starts it here
        content.PlayEnterTransition();
    }

    /// <summary>Remove an overlay and detach the subtree. Without detaching,
    /// a closed overlay stays in the NameScope, its animations keep spinning,
    /// and the input and focus dispatchers keep references to it.</summary>
    private bool DetachOverlay(UIElement content)
    {
        if (!_overlays.Remove(content)) return false;

        DetachTree(content);
        content.Owner = null;

        RaiseOverlaysChanged();
        return true;
    }

    internal void AttachTree(UIElement root)
    {
        Walk(root, element =>
        {
            NameScope.Register(element);

            // the theme is applied before the first layout, so that sizes
            // are computed with the right fonts and padding from the start
            App.Theme.Apply(element);

            element.RaiseAttached();
        });
    }

    internal void DetachTree(UIElement root)
    {
        Walk(root, element =>
        {
            NameScope.Unregister(element);
            element.RaiseDetached();
        });

        // otherwise the discarded tree stays alive through the dispatcher's references
        if (_hoveredElement is not null && IsInTree(root, _hoveredElement))
            _hoveredElement = null;

        // a contact whose target left the tree must learn about the cancellation.
        // Previously the fields here were simply nulled, and an element that was
        // later returned to the tree stayed pressed
        foreach (PointerContact contact in _contacts.Values.ToArray())
        {
            bool affected =
                (contact.Pressed is not null && IsInTree(root, contact.Pressed)) ||
                (contact.Capture is not null && IsInTree(root, contact.Capture));

            if (affected)
                CancelContact(contact, PointerCancelReason.Detached);
        }

        // the remembered press of the right and the middle button is a reference
        // into the tree too, and a click must not come to an element that left it
        if (_rightPressed is not null && IsInTree(root, _rightPressed))
            _rightPressed = null;

        if (_middlePressed is not null && IsInTree(root, _middlePressed))
            _middlePressed = null;

        if (_dropTarget is not null && IsInTree(root, _dropTarget))
        {
            _dropTarget = null;
            _dropEffect = DragDropEffect.None;
        }

        if (_focusDispatcher.FocusedElement is { } focused && IsInTree(root, focused))
            _focusDispatcher.ClearFocus();

        // like the other references above — only if the element being reset is
        // actually in the subtree being detached. An unconditional reset became
        // noticeable once hiding the tooltip started going through DetachTree:
        // that happens on every mouse move and would wipe out the inspector's
        // selection along with it
        if (InspectedElement is not null && IsInTree(root, InspectedElement))
            InspectedElement = null;

        // the element whose tooltip is waited for or shown left: the tooltip goes too,
        // or it would hang over the place the element was until the pointer moved
        if (_toolTipOwner is not null && IsInTree(root, _toolTipOwner))
        {
            HideToolTip();
            _toolTipOwner = null;
        }

        CancelAnimationsIn(root);
    }

    private static bool IsInTree(UIElement root, UIElement candidate)
    {
        for (UIElement? current = candidate; current is not null; current = current.Parent)
            if (ReferenceEquals(current, root))
                return true;

        return false;
    }

    private static void Walk(UIElement root, Action<UIElement> action)
    {
        action(root);

        switch (root)
        {
            case WrapControl wrap when wrap.Child is not null:
                Walk(wrap.Child, action);
                break;

            case PanelControl panel:
                foreach (var child in panel.Children)
                    Walk(child, action);
                break;
        }
    }

    private int _layoutDepth;

    /// <summary>The layout is stale and waits for the next frame.</summary>
    private bool _layoutDirty;

    /// <summary>How many passes are allowed per call.</summary>
    /// <remarks>
    /// Two are enough for the regular case: the first creates the containers,
    /// the second computes the final size from them. The margin up to four is
    /// for nested virtualizing panels, a tree inside a scrollable panel.
    /// </remarks>
    private const int MaxLayoutPasses = 4;

    internal void PerformLayout()
    {
        // Invalidate during layout is legitimate: a virtualizing panel creates
        // containers right in measuring, because until then it is unknown how
        // many rows fit, and creating a container changes Children. Re-entering
        // the pass is not allowed, and the request is already recorded
        // in _layoutDirty — the pass loop below will fulfil it
        if (_layoutDepth > 0) return;

        _layoutDepth++;

        try
        {
            for (int pass = 0; ; pass++)
            {
                // styles that depend on places among siblings, deferred from
                // changes of children: before measuring, they change sizes
                FlushStructureRestyles();

                _layoutDirty = false;

                LayoutPass();

                if (!_layoutDirty) break;

                if (pass + 1 >= MaxLayoutPasses)
                {
                    // the layout doesn't converge: someone asks for a new pass
                    // every time. Silently spinning on that would hang the frame
                    ZfContract.Fail(
                        $"Layout did not converge in {MaxLayoutPasses} passes: " +
                        "something keeps calling Invalidate from MeasureOverride " +
                        "or ArrangeOverride. Change the geometry there directly " +
                        "instead of requesting a new pass.");

                    break;
                }
            }
        }
        finally
        {
            _layoutDepth--;
            _layoutDirty = false;
        }
    }

    /// <summary>Perform the deferred layout if it is needed. Called before
    /// drawing and before hit testing — that is, everywhere the geometry
    /// must be fresh.</summary>
    internal void EnsureLayout()
    {
        if (!_layoutDirty && !HasPendingRestyles) return;

        PerformLayout();
    }

    /// <summary>Complete the deferred layout immediately. Needed where code reads
    /// the geometry right after a change: a position, a size, the set of
    /// containers of a virtualizing panel.</summary>
    public void UpdateLayout() => EnsureLayout();

    private void LayoutPass()
    {
        if (Content is not null)
        {
            Content.Measure(ClientSize);
            Content.Arrange(new Rectangle(Point.Empty, ClientSize));
        }

        foreach (var overlay in _overlays)
        {
            overlay.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));
            overlay.Arrange(new Rectangle(overlay.Position, overlay.DesiredSize));
        }
    }

    private Rectangle? _dirtyRegion;

    internal Rectangle? TakeDirtyRegion()
    {
        Rectangle? region = _dirtyRegion;
        _dirtyRegion = null;
        return region;
    }

    internal void InvalidateRect(Rectangle bounds)
    {
        _dirtyRegion = _dirtyRegion is { } existing ? existing.Union(bounds) : bounds;
        PlatformWindow?.Invalidate(bounds);
    }

    /// <summary>Redraw the whole client area without a layout pass.</summary>
    internal void InvalidateVisual()
    {
        _dirtyRegion = new Rectangle(Point.Empty, ClientSize);
        PlatformWindow?.Invalidate(null);
    }

    /// <summary>Mark the layout stale and ask for a frame.</summary>
    /// <remarks>
    /// This used to run a full Measure/Arrange pass over the whole tree right here.
    /// Then setting five properties meant five layouts, and scrolling with a finger
    /// meant a layout per move event, and those arrive more often than frames.
    /// Now there is one pass, and it runs before the frame: before drawing and
    /// before hit testing the geometry will be fresh anyway, and nobody sees
    /// the intermediate states. Code that needs the geometry right away uses UpdateLayout.
    /// </remarks>
    internal void Invalidate()
    {
        _layoutDirty = true;

        // an element's visibility is a layout property, so its change always
        // passes through here. The clock reconsiders whether frames are needed:
        // an animation on a page shown again must come alive, and one on a hidden
        // page must stop waking the window
        ReviewFrames();

        // a full redraw: accumulate the whole client area
        _dirtyRegion = new Rectangle(Point.Empty, ClientSize);
        PlatformWindow?.Invalidate(null);
    }
}
