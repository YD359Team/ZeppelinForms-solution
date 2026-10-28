using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.DragDrop;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Forms;

/// <summary>Drag and drop from other applications: the element under the pointer that
/// allows a drop gets the data.</summary>
public partial class Form
{
    /// <summary>Whether to accept drag-and-drop from the system into this window.
    /// Without it AllowDrop on elements won't work: the window is not registered
    /// as a drop target, and the system doesn't know about it.</summary>
    public bool AllowDrop
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            PlatformWindow?.SetDragDropEnabled(value);
        }
    }

    private UIElement? _dropTarget;

    private DragDropEffect _dropEffect;

    /// <summary>A drag entered the window. The source shows the returned
    /// effect with its cursor.</summary>
    internal DragDropEffect OnDragEnterWindow(DragDropData data, Point point, KeyModifiers modifiers) =>
        UpdateDropTarget(data, point, modifiers);

    internal DragDropEffect OnDragOverWindow(DragDropData data, Point point, KeyModifiers modifiers) =>
        UpdateDropTarget(data, point, modifiers);

    /// <summary>The drag left the window or was cancelled.</summary>
    internal void OnDragLeaveWindow()
    {
        _dropTarget?.RaiseDragLeave();
        _dropTarget = null;
    }

    internal DragDropEffect OnDropWindow(DragDropData data, Point point, KeyModifiers modifiers)
    {
        // the target is recomputed: a drop may come without a preceding over —
        // for example, if the source sent only enter and then drop right away
        DragDropEffect effect = UpdateDropTarget(data, point, modifiers);

        UIElement? target = _dropTarget;
        _dropTarget = null;

        if (target is null || effect == DragDropEffect.None)
        {
            target?.RaiseDragLeave();

            return DragDropEffect.None;
        }

        var args = new DragDropEventArgs(data, point, modifiers) { Effect = effect };

        target.RaiseDrop(args);

        // DragLeave after a drop is mandatory: the target highlighted itself
        // on enter, and it has no other place to remove the highlight
        target.RaiseDragLeave();

        return args.Effect;
    }

    /// <summary>Find the drop target under the cursor, send enter and leave when
    /// it changes, and ask for the effect.</summary>
    private DragDropEffect UpdateDropTarget(DragDropData data, Point point, KeyModifiers modifiers)
    {
        _lastPointerPosition = point;

        UIElement? target = FindDropTarget(HitTestAll(point));

        if (!ReferenceEquals(target, _dropTarget))
        {
            _dropTarget?.RaiseDragLeave();
            _dropTarget = target;

            if (target is not null)
            {
                var enterArgs = new DragDropEventArgs(data, point, modifiers);

                target.RaiseDragEnter(enterArgs);

                // the effect set on enter is the initial value for the following
                // over events: the target doesn't need to repeat it
                _dropEffect = enterArgs.Effect;
            }
        }

        if (_dropTarget is null) return DragDropEffect.None;

        var args = new DragDropEventArgs(data, point, modifiers) { Effect = _dropEffect };

        _dropTarget.RaiseDragOver(args);
        _dropEffect = args.Effect;

        return _dropEffect;
    }

    /// <summary>The nearest element with AllowDrop, starting from the hit one.
    /// That way highlighting can be enabled on a panel without marking up every row.</summary>
    private static UIElement? FindDropTarget(UIElement? hit)
    {
        for (UIElement? current = hit; current is not null; current = current.Parent)
            if (current is { AllowDrop: true, IsEnabled: true })
                return current;

        return null;
    }
}
