using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Forms;

/// <summary>The keyboard beyond the focused element: menus, access keys,
/// Escape and Enter for the window as a whole.</summary>
/// <remarks>
/// <para>
/// A menu takes the keys before the focused element does: while it is open or the
/// bar is in menu mode the arrows belong to it, not to the text box that still holds
/// the focus underneath. Access keys come next, then the focused element, and what
/// it leaves unhandled goes to the window: Escape closes the topmost flyout or
/// cancels a dialog, Enter presses the default button.
/// </para>
/// <para>
/// Menu mode is entered with F10, a tap of Alt, or the access key of a bar item,
/// and left with Escape, another tap of Alt, a pointer press, or a chosen item —
/// as in Windows.
/// </para>
/// </remarks>
public partial class Form
{
    /// <summary>The button Enter presses when the focused element doesn't take
    /// Enter itself — the OK of a dialog.</summary>
    public ButtonBase? DefaultButton { get; set; }

    /// <summary>Whether access key underlines are shown now: while Alt is held,
    /// and while a menu is worked from the keyboard.</summary>
    internal bool ShowsAccessKeys { get; private set; }

    /// <summary>The menu bar in keyboard menu mode; null — none.</summary>
    private MenuBar? _menuMode;

    /// <summary>Alt went down and nothing followed yet: releasing it toggles menu mode.</summary>
    private bool _altAlone;

    /// <summary>The key down already ran an access key, so the character the
    /// platform sends for the same press must not run it again.</summary>
    private bool _accessKeyTaken;

    private static bool IsAltKey(Key key) => key is Key.Alt or Key.LeftAlt or Key.RightAlt;

    /// <summary>Keys taken before the focused element: menus and access keys.</summary>
    private bool HandleKeyboardBeforeFocus(Key key, KeyModifiers modifiers)
    {
        if (IsAltKey(key))
        {
            _altAlone = true;
            SetShowsAccessKeys(true);
            return false;
        }

        _altAlone = false;
        _accessKeyTaken = false;

        // an open menu first: it is on top of everything, keys included
        if (_flyouts.Count > 0 && _flyouts[^1] is MenuList menu && HandleMenuListKey(menu, key, modifiers))
            return true;

        if (_menuMode is { } bar && HandleMenuBarKey(bar, key, modifiers))
            return true;

        if (key == Key.F10 && modifiers == KeyModifiers.None && FindMenuBar() is { } menuBar)
        {
            EnterMenuMode(menuBar);
            return true;
        }

        // Alt and a letter; not with Ctrl: AltGr arrives as Ctrl+Alt and types characters
        if (modifiers.HasFlag(KeyModifiers.Alt) && !modifiers.HasFlag(KeyModifiers.Control) &&
            Mnemonic.CharOf(key) is { } letter && TryAccessKey(letter))
        {
            _accessKeyTaken = true;
            return true;
        }

        return false;
    }

    /// <summary>The character of an Alt combination, where the platform gives one:
    /// on Windows WM_SYSCHAR. It is what makes an access key in the user's own layout
    /// work — "&amp;Файл" answers Alt+Ф, which no key code names.</summary>
    internal void OnAccessKeyChar(char character)
    {
        if (_accessKeyTaken)
        {
            _accessKeyTaken = false;
            return;
        }

        TryAccessKey(char.ToUpperInvariant(character));
    }

    /// <summary>Keys the focused element left unhandled.</summary>
    private void HandleKeyboardAfterFocus(Key key, KeyModifiers modifiers)
    {
        switch (key)
        {
            case Key.Escape when _flyouts.Count > 0:
                CloseFlyout(_flyouts[^1]);
                break;

            case Key.Escape when IsDialog:
                Cancel();
                break;

            case Key.Enter when modifiers == KeyModifiers.None &&
                DefaultButton is { IsEffectivelyEnabled: true, IsEffectivelyVisible: true } button:
                button.PerformAccessibilityActivation();
                break;
        }
    }

    private void HandleKeyboardKeyUp(Key key)
    {
        if (!IsAltKey(key)) return;

        // a tap of Alt alone switches menu mode on and off
        if (_altAlone)
        {
            if (_menuMode is not null) ExitMenuMode();
            else if (FindMenuBar() is { } bar) EnterMenuMode(bar);
        }

        _altAlone = false;
        SetShowsAccessKeys(_menuMode is not null);
    }

    // ===== menus =====

    private void EnterMenuMode(MenuBar bar, int index = 0)
    {
        if (!ReferenceEquals(_menuMode, bar)) _menuMode?.EndKeyboard();

        _menuMode = bar;
        bar.BeginKeyboard(index);
        SetShowsAccessKeys(true);
    }

    private void ExitMenuMode()
    {
        if (_menuMode is null) return;

        MenuBar bar = _menuMode;
        _menuMode = null;

        if (bar.OpenIndex >= 0) CloseAllFlyouts();

        bar.EndKeyboard();
        SetShowsAccessKeys(Keyboard.IsAltDown);
    }

    /// <summary>A pointer press ends menu mode: the mouse is in charge now.</summary>
    private void ExitMenuModeOnPointer() => ExitMenuMode();

    private bool HandleMenuBarKey(MenuBar bar, Key key, KeyModifiers modifiers)
    {
        switch (key)
        {
            case Key.Left:
                bar.MoveHighlight(-1);
                HighlightTopMenu();
                return true;

            case Key.Right:
                bar.MoveHighlight(1);
                HighlightTopMenu();
                return true;

            case Key.Down or Key.Enter or Key.Space:
                if (bar.OpenHighlighted()) HighlightTopMenu();
                return true;

            case Key.Escape:
                ExitMenuMode();
                return true;
        }

        if (!modifiers.HasFlag(KeyModifiers.Control) && Mnemonic.CharOf(key) is { } letter &&
            bar.IndexOfAccessKey(letter) is >= 0 and var index)
        {
            bar.BeginKeyboard(index);
            if (bar.OpenHighlighted()) HighlightTopMenu();
            return true;
        }

        // anything else — Tab, a shortcut — leaves the menu and goes on as usual
        ExitMenuMode();
        return false;
    }

    private bool HandleMenuListKey(MenuList menu, Key key, KeyModifiers modifiers)
    {
        switch (key)
        {
            case Key.Down:
                menu.MoveHighlight(1);
                return true;

            case Key.Up:
                menu.MoveHighlight(-1);
                return true;

            case Key.Home:
                menu.HighlightFirst();
                return true;

            case Key.End:
                menu.HighlightLast();
                return true;

            case Key.Enter or Key.Space:
                if (menu.InvokeHighlighted()) AfterMenuItemRun();
                return true;

            case Key.Escape:
                // back to the bar, its item still highlighted
                CloseFlyout(menu);
                return true;

            case Key.Left or Key.Right when _menuMode is { } bar:
                bar.MoveHighlight(key == Key.Right ? 1 : -1);
                HighlightTopMenu();
                return true;

            // Tab doesn't leave a menu, as in every system menu
            case Key.Tab:
                return true;
        }

        if (!modifiers.HasFlag(KeyModifiers.Control) && Mnemonic.CharOf(key) is { } letter &&
            menu.InvokeAccessKey(letter))
        {
            AfterMenuItemRun();
            return true;
        }

        return false;
    }

    /// <summary>A run item closes the menus: menu mode is over too.</summary>
    private void AfterMenuItemRun()
    {
        CloseAllFlyouts();
        ExitMenuMode();
    }

    /// <summary>A submenu opened from the keyboard starts with its first item lit.</summary>
    private void HighlightTopMenu()
    {
        if (_flyouts.Count > 0 && _flyouts[^1] is MenuList menu)
            menu.HighlightFirst();
    }

    private MenuBar? FindMenuBar() => Content is null ? null : Find<MenuBar>(Content);

    // ===== access keys =====

    /// <summary>The menu bar first — Alt+F is the File menu in every application —
    /// then the elements of the window.</summary>
    private bool TryAccessKey(char key)
    {
        if (FindMenuBar() is { } bar && bar.IndexOfAccessKey(key) is >= 0 and var index)
        {
            EnterMenuMode(bar, index);
            if (bar.OpenHighlighted()) HighlightTopMenu();
            return true;
        }

        if (Content is null) return false;

        List<UIElement> matches = [];
        CollectAccessKeys(Content, key, matches);

        if (matches.Count == 0) return false;

        // the same key on several elements cycles the focus through them without
        // running any — the user can't mean all of them at once
        if (matches.Count > 1)
        {
            int current = matches.FindIndex(element =>
                ReferenceEquals(element, _focusDispatcher.FocusedElement));

            FocusTarget(matches[(current + 1) % matches.Count]);
            return true;
        }

        return RunAccessKey(matches[0]);
    }

    /// <summary>What an access key does depends on what it marks: a label moves the
    /// focus to its target, a group to its first control, a button is pressed, a
    /// check box toggled, a field just focused.</summary>
    private bool RunAccessKey(UIElement element)
    {
        switch (element)
        {
            case Label { Target: { } target }:
                return FocusTarget(target);

            case GroupBox group:
                return _focusDispatcher.MoveNext(group);

            case ButtonBase or CheckBox or RadioButton or ToggleSwitch:
                FocusTarget(element);
                return element.PerformAccessibilityActivation();

            case IInputElement:
                return FocusTarget(element);

            default:
                return false;
        }
    }

    private bool FocusTarget(UIElement element) =>
        element is IInputElement && _focusDispatcher.FocusElement(element);

    private static void CollectAccessKeys(UIElement element, char key, List<UIElement> matches)
    {
        if (!element.IsVisible || !element.IsEnabled) return;

        if (element.AccessKey == key)
            matches.Add(element);

        switch (element)
        {
            case WrapControl { Child: { } child }:
                CollectAccessKeys(child, key, matches);
                break;

            case PanelControl panel:
                foreach (UIElement child in panel.Children)
                    CollectAccessKeys(child, key, matches);
                break;
        }
    }

    private static T? Find<T>(UIElement element) where T : UIElement
    {
        if (!element.IsVisible) return null;

        if (element is T found) return found;

        switch (element)
        {
            case WrapControl { Child: { } child }:
                return Find<T>(child);

            case PanelControl panel:
                foreach (UIElement child in panel.Children)
                {
                    if (Find<T>(child) is { } inChild)
                        return inChild;
                }

                break;
        }

        return null;
    }

    private void SetShowsAccessKeys(bool shows)
    {
        if (ShowsAccessKeys == shows) return;

        ShowsAccessKeys = shows;
        InvalidateVisual();
    }
}