using ZeppelinForms.Forms.Controls.Tools;

namespace ZeppelinForms.Forms.Controls;

/// <summary>What an editor of a <see cref="PropertyGrid"/> row gets: the property, the
/// objects being edited, the value it starts with, and the way to write a new one.</summary>
/// <remarks>
/// <para>
/// An edit is a run of changes ended by a commit: a number spun with the wheel, a
/// name typed letter by letter, a color dragged across the spectrum — each change is
/// written at once, so the result is seen as it is made, and the commit makes it one
/// step for undo, <see cref="PropertyGrid.PropertyValueChanged"/>.
/// </para>
/// <para>
/// An editor that changes in one go — a check box, a list — calls <see cref="Set"/>.
/// One that changes as the user works calls <see cref="Change"/> on the way and
/// <see cref="Commit"/> at the end: on Enter, on losing the focus. An edit left open
/// is committed when another property starts changing or the selection moves on.
/// </para>
/// </remarks>
public sealed class PropertyEditContext
{
    private readonly PropertyGrid _grid;

    internal PropertyEditContext(PropertyGrid grid, PropertyDescriptor[] descriptors, IReadOnlyList<object> targets)
    {
        _grid = grid;
        Descriptors = descriptors;
        Targets = targets;

        Value = descriptors[0].GetValue(targets[0]);

        for (int i = 1; i < targets.Count; i++)
        {
            if (!Equals(descriptors[i].GetValue(targets[i]), Value))
            {
                IsMixed = true;
                break;
            }
        }
    }

    /// <summary>The property, as the first object describes it.</summary>
    public PropertyDescriptor Property => Descriptors[0];

    /// <summary>The property as each object describes it, in the order of <see cref="Targets"/>:
    /// objects of different kinds read and write a property of the same name each its own way.</summary>
    internal PropertyDescriptor[] Descriptors { get; }

    /// <summary>Some object can't take a value: the row is shown, not edited.</summary>
    public bool IsReadOnly => Descriptors.Any(descriptor => descriptor.IsReadOnly);

    /// <summary>The value of the object at this place among <see cref="Targets"/>.</summary>
    internal object? GetValue(object target, int index) => Descriptors[index].GetValue(target);

    /// <summary>The objects being edited: one, or all of a multiple selection.</summary>
    public IReadOnlyList<object> Targets { get; }

    /// <summary>The value when the editor was made — the first object's, when there are several.</summary>
    public object? Value { get; }

    /// <summary>The objects don't agree on the value: the editor shows none of them
    /// rather than the first one's, which would pass for everyone's.</summary>
    public bool IsMixed { get; }

    /// <summary>Write a value to every object, as a step of the edit in progress.</summary>
    /// <returns>False — a handler of <see cref="PropertyGrid.PropertyValueChanging"/>
    /// refused it; the objects keep what they had.</returns>
    public bool Change(object? value) => _grid.ChangeValue(this, value);

    /// <summary>End the edit: one <see cref="PropertyGrid.PropertyValueChanged"/> for all of
    /// its changes, if they changed anything. Nothing happens without an edit in progress.</summary>
    public void Commit() => _grid.CommitValue(this);

    /// <summary>Write a value and end the edit — for an editor that changes in one go.</summary>
    public bool Set(object? value)
    {
        bool written = Change(value);
        Commit();
        return written;
    }
}

/// <summary>A value is about to be written to the edited objects.</summary>
public sealed class PropertyValueChangingEventArgs(
    PropertyDescriptor property, IReadOnlyList<object> targets, object? newValue) : EventArgs
{
    public PropertyDescriptor Property { get; } = property;

    public IReadOnlyList<object> Targets { get; } = targets;

    public object? NewValue { get; } = newValue;

    /// <summary>Refuse the value: nothing is written, and the editor shows what the
    /// objects still have.</summary>
    public bool Cancel { get; set; }
}

/// <summary>An edit ended with a change — one step for undo.</summary>
/// <remarks>
/// The step undoes and redoes itself, each object with its own descriptor:
/// <code>
/// grid.PropertyValueChanged += (_, e) => history.Push(undo: e.Undo, redo: e.Redo);
/// </code>
/// The grid reads the values again when the objects tell it they changed; objects
/// that don't tell need <see cref="PropertyGrid.Refresh"/> after an undo.
/// </remarks>
public sealed class PropertyValueChangedEventArgs(
    PropertyDescriptor property, IReadOnlyList<object> targets, IReadOnlyList<object?> oldValues, object? newValue) : EventArgs
{
    public PropertyDescriptor Property { get; } = property;

    public IReadOnlyList<object> Targets { get; } = targets;

    /// <summary>What each object had before the edit, in the order of <see cref="Targets"/>.</summary>
    public IReadOnlyList<object?> OldValues { get; } = oldValues;

    /// <summary>What every object has now.</summary>
    public object? NewValue { get; } = newValue;

    /// <summary>The property as each object describes it; null — <see cref="Property"/> for all.</summary>
    internal PropertyDescriptor[]? Descriptors { get; init; }

    private PropertyDescriptor DescriptorAt(int index) =>
        Descriptors is { } descriptors && index < descriptors.Length ? descriptors[index] : Property;

    /// <summary>Write back what each object had before the edit.</summary>
    public void Undo()
    {
        for (int i = 0; i < Targets.Count; i++)
            DescriptorAt(i).SetValue(Targets[i], OldValues[i]);
    }

    /// <summary>Write the edit's value again, after an <see cref="Undo"/>.</summary>
    public void Redo()
    {
        for (int i = 0; i < Targets.Count; i++)
            DescriptorAt(i).SetValue(Targets[i], NewValue);
    }
}
