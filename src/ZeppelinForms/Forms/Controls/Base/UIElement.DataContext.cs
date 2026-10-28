namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>The data context: the object the element's bindings start from — a view model —
/// set on the element or inherited from its ancestors and the form.</summary>
public abstract partial class UIElement
{
    private object? _dataContext;

    /// <summary>The effective data context the element last saw: kept by the elements that
    /// follow it — bindings to it, a handler of <see cref="DataContextChanged"/> — to tell
    /// a change from a move that left it the same.</summary>
    private object? _seenDataContext;

    /// <summary>The element follows its data context: it has bindings to it or a handler.</summary>
    private bool _followsDataContext;

    /// <summary>No element of the process has used a data context yet: the tree's changes
    /// then skip the walk that tells the elements below of a new one. An application
    /// without view models pays nothing for them.</summary>
    private static volatile bool s_dataContextInUse;

    /// <summary>
    /// The object the element's bindings to a data context start from — a view model. Not
    /// set, it is the parent's, up to the form's <see cref="Form.DataContext"/>: a form's
    /// view model reaches every element of the form, and a part of the form can be given
    /// a view model of its own.
    /// </summary>
    /// <remarks>
    /// Null is "the parent's": an element can't hide its ancestors' data context from the
    /// elements below it, only give them another one. A change — here, above, or the
    /// element moved under another — reaches the bindings at once, and raises
    /// <see cref="DataContextChanged"/> where the effective one changed.
    /// </remarks>
    public object? DataContext
    {
        get
        {
            UIElement element = this;

            while (true)
            {
                if (element._dataContext is { } own) return own;
                if (element.Parent is not { } parent) break;
                element = parent;
            }

            return element.Owner?.DataContext;
        }
        set
        {
            if (ReferenceEquals(_dataContext, value)) return;

            _dataContext = value;
            s_dataContextInUse = true;

            DataContextChangedBelow(this, isStart: true);
        }
    }

    /// <summary>The element's effective <see cref="DataContext"/> changed: set here or
    /// above, or the element moved. Raised for the elements in a form's tree as for any
    /// other.</summary>
    public event EventHandler? DataContextChanged
    {
        add
        {
            FollowDataContext();
            _dataContextChanged += value;
        }
        remove => _dataContextChanged -= value;
    }

    private EventHandler? _dataContextChanged;

    /// <summary>The element starts to follow its data context: the current one, which it
    /// remembers to tell the next change from it.</summary>
    private object? FollowDataContext()
    {
        s_dataContextInUse = true;

        object? current = DataContext;

        _followsDataContext = true;
        _seenDataContext = current;

        return current;
    }

    /// <summary>The element moved — a new parent, into or out of a form: the data context
    /// below it may be another now.</summary>
    private void OnTreeChangedForDataContext()
    {
        if (s_dataContextInUse) DataContextChangedBelow(this, isStart: true);
    }

    /// <summary>The data context of an element and those below it may have changed. Below
    /// an element with one of its own nothing did, and the walk stops there.</summary>
    internal static void DataContextChangedBelow(UIElement element, bool isStart)
    {
        if (!isStart && element._dataContext is not null) return;

        if (element._followsDataContext)
        {
            object? current = element.DataContext;

            if (!ReferenceEquals(current, element._seenDataContext))
            {
                element._seenDataContext = current;
                element.RetargetDataContextBindings(current);
                element._dataContextChanged?.Invoke(element, EventArgs.Empty);
            }
        }

        switch (element)
        {
            case WrapControl { Child: { } child }:
                DataContextChangedBelow(child, isStart: false);
                break;

            case PanelControl panel:
                foreach (UIElement child in panel.Children.ToArray())
                    DataContextChangedBelow(child, isStart: false);
                break;
        }
    }
}
