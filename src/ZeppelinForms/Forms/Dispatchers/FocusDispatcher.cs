using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Dispatchers;

public class FocusDispatcher
{
    /// <summary>Focus moved. Null — there is no focus anymore.</summary>
    public event EventHandler<UIElement?>? FocusChanged;

    public UIElement? FocusedElement => _focused;

    private UIElement? _focused;

    public bool FocusElement(UIElement element)
    {
        if (element is not IInputElement input || !input.TabStop)
            return false;

        // a disabled control does not take focus, and neither does anything
        // inside a disabled container: the Tab walk already skips them, but
        // a programmatic focus went around it and let keystrokes into
        // a control that is drawn as unavailable
        if (!IsEffectivelyEnabled(element))
            return false;

        if (ReferenceEquals(_focused, element))
            return true;

        if (_focused is IInputElement prevInput)
        {
            prevInput.IsFocused = false;
            _focused.RaiseLostFocus();
            _focused.Invalidate();
        }

        input.IsFocused = true;
        element.RaiseGotFocus();
        element.Invalidate();

        _focused = element;

        FocusChanged?.Invoke(this, element);

        return true;
    }

    /// <summary>Remove focus without notifications: the element is already
    /// out of the tree, and calling RaiseLostFocus on it is too late and dangerous.</summary>
    public void ClearFocus()
    {
        if (_focused is IInputElement input)
            input.IsFocused = false;

        _focused = null;

        FocusChanged?.Invoke(this, null);
    }

    public bool MoveNext(UIElement root) => Move(root, forward: true);

    public bool MovePrevious(UIElement root) => Move(root, forward: false);

    private bool Move(UIElement root, bool forward)
    {
        List<UIElement> stops = CollectTabStops(root);
        if (stops.Count == 0) return false;

        int current = _focused is null ? -1 : stops.IndexOf(_focused);

        int next = current < 0
            ? (forward ? 0 : stops.Count - 1)
            : (current + (forward ? 1 : -1) + stops.Count) % stops.Count;   // wrapping around

        return FocusElement(stops[next]);
    }

    /// <summary>IsEnabled is not inherited as a value, but a disabled container
    /// disables everything inside it — the same rule the Tab walk follows.</summary>
    private static bool IsEffectivelyEnabled(UIElement element)
    {
        for (UIElement? node = element; node is not null; node = node.Parent)
            if (!node.IsEnabled) return false;

        return true;
    }

    private static List<UIElement> CollectTabStops(UIElement root)
    {
        List<UIElement> stops = [];
        Walk(root, stops);

        // TabIndex sets the priority, the tree order breaks ties.
        // OrderBy is stable, so equal TabIndex values keep the walk order.
        return [.. stops.OrderBy(e => ((IInputElement)e).TabIndex)];
    }

    private static void Walk(UIElement element, List<UIElement> stops)
    {
        if (!element.IsVisible || !element.IsEnabled)
            return;

        // a radio group is one stop: its checked button, or the first one
        // while none is checked — the arrows move within the group
        if (element is IInputElement { TabStop: true } &&
            element is not RadioButton { IsGroupTabStop: false })
        {
            stops.Add(element);
        }

        switch (element)
        {
            case WrapControl wrap when wrap.Child is not null:
                Walk(wrap.Child, stops);
                break;

            case PanelControl panel:
                // the reading order, not the drawing one: a split view's pane is
                // drawn last, over the content, but comes first
                foreach (var child in panel.NavigationOrder)
                    Walk(child, stops);
                break;
        }
    }

    /// <summary>Focus the first element under root, in Tab order, that accepts
    /// text input. Buttons and other stops are skipped: focusing them without
    /// the user asking puts a focus ring on a random button, and Enter would
    /// press it.</summary>
    public bool FocusFirstTextInput(UIElement root)
    {
        foreach (UIElement stop in CollectTabStops(root))
            if (stop.AcceptsTextInput && FocusElement(stop))
                return true;

        return false;
    }
}