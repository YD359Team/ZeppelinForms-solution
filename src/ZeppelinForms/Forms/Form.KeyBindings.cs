using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Input.Commands;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Forms;

/// <summary>Shortcuts: the form's key bindings and the gestures of its menu bar.</summary>
/// <remarks>
/// <para>
/// A shortcut is tried after the focused element left the key unhandled: a text
/// field keeps Ctrl+C, Ctrl+V and Ctrl+Z, and Ctrl+S, which it doesn't take, goes on
/// to the form. The key bindings come first, then the items of the menu bar.
/// </para>
/// <para>
/// A menu item's state is often set only as its menu opens, in
/// <see cref="MenuItem.Opening"/>. Before a shortcut runs an item, the form raises
/// Opening on the items above it, as opening the menu would: Ctrl+S pressed with the
/// menu closed sees the state the open menu would show, not the one from the last
/// time it was open.
/// </para>
/// </remarks>
public partial class Form
{
    /// <summary>Shortcuts that run commands — for actions with no menu item to carry
    /// the shortcut. Items of the menu bar run their own gestures and need no binding.</summary>
    public List<KeyBinding> KeyBindings { get; } = [];

    /// <summary>Run the shortcut of this key, if the form has one that can run now.</summary>
    private bool TryKeyGesture(Key key, KeyModifiers modifiers)
    {
        // a modifier alone is never a shortcut, only part of one
        if (key is Key.Shift or Key.LeftShift or Key.RightShift or
            Key.Control or Key.LeftControl or Key.RightControl or
            Key.Alt or Key.LeftAlt or Key.RightAlt)
            return false;

        // ToArray: a command may change the bindings, adding a mode's own keys
        foreach (KeyBinding binding in KeyBindings.ToArray())
        {
            if (!binding.Gesture.Matches(key, modifiers) ||
                !binding.Command.CanExecute(binding.CommandParameter))
                continue;

            binding.Command.Execute(binding.CommandParameter);
            return true;
        }

        if (FindMenuBar() is not { } bar) return false;

        foreach (MenuItem item in bar.Items.ToArray())
        {
            if (TryMenuGesture(item, key, modifiers))
                return true;
        }

        return false;
    }

    /// <summary>Run the item with this shortcut, at or under <paramref name="item"/>.</summary>
    private bool TryMenuGesture(MenuItem item, Key key, KeyModifiers modifiers)
    {
        if (item.IsSeparator || !item.IsEnabled) return false;

        if (item.EffectiveGesture is { } gesture && gesture.Matches(key, modifiers))
        {
            if (!item.IsEffectivelyEnabled) return false;

            // open menus close, as after a click on the item; other flyouts — a picker
            // the shortcut has nothing to do with — stay
            if (_menuMode is not null || _flyouts.Exists(flyout => flyout is MenuList))
                AfterMenuItemRun();

            return item.Invoke();
        }

        // the submenu's Opening only where the shortcut is: raising it for every
        // menu on every key would run the application's state updates per keystroke
        if (item.Items.Count == 0 || !HasGesture(item, key, modifiers)) return false;

        item.RaiseOpening();

        foreach (MenuItem child in item.Items.ToArray())
        {
            if (TryMenuGesture(child, key, modifiers))
                return true;
        }

        return false;
    }

    private static bool HasGesture(MenuItem item, Key key, KeyModifiers modifiers)
    {
        foreach (MenuItem child in item.Items)
        {
            if (child.EffectiveGesture is { } gesture && gesture.Matches(key, modifiers))
                return true;

            if (HasGesture(child, key, modifiers))
                return true;
        }

        return false;
    }
}