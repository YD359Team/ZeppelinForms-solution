using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Dispatchers;

public class FocusDispatcher
{
    public UIElement? FocusedElement => _focused;

    private UIElement? _focused;

    public bool FocusElement(UIElement element)
    {
        if (element is not IInputElement input || !input.TabStop)
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
        return true;
    }

    /// <summary>Снять фокус без уведомлений: элемент уже вне дерева,
    /// звать на нём RaiseLostFocus поздно и опасно.</summary>
    public void ClearFocus()
    {
        if (_focused is IInputElement input)
            input.IsFocused = false;

        _focused = null;
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
            : (current + (forward ? 1 : -1) + stops.Count) % stops.Count;   // по кругу

        return FocusElement(stops[next]);
    }

    private static List<UIElement> CollectTabStops(UIElement root)
    {
        List<UIElement> stops = [];
        Walk(root, stops);

        // TabIndex задаёт приоритет, порядок в дереве — тай-брейк.
        // OrderBy стабилен, поэтому равные TabIndex сохранят порядок обхода.
        return [.. stops.OrderBy(e => ((IInputElement)e).TabIndex)];
    }

    private static void Walk(UIElement element, List<UIElement> stops)
    {
        if (!element.IsVisible || !element.IsEnabled)
            return;

        if (element is IInputElement { TabStop: true })
            stops.Add(element);

        switch (element)
        {
            case WrapControl wrap when wrap.Child is not null:
                Walk(wrap.Child, stops);
                break;

            case PanelControl panel:
                foreach (var child in panel.Children)
                    Walk(child, stops);
                break;
        }
    }
}