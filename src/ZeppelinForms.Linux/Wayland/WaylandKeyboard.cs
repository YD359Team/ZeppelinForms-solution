using System.Runtime.InteropServices;
using System.Text;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>
/// The seat's keyboard: the compositor's keymap through xkbcommon, the focused window,
/// key repeat and the Compose sequences of dead keys.
/// </summary>
/// <remarks>
/// <para>
/// Wayland repeats no key: the compositor says how fast a held key should repeat, and the
/// client repeats it — the platform's loop asks <see cref="RepeatDueInMs"/> how long it
/// may sleep, and calls <see cref="DispatchRepeat"/>.
/// </para>
/// <para>
/// The key a form sees is the keysym of the first layout's first level — the Latin one in
/// every usual layout, so Ctrl+C keeps working under Cyrillic, as X11's XLookupKeysym(0)
/// gives it. The text is the effective layout's, through Compose.
/// </para>
/// </remarks>
internal sealed unsafe class WaylandKeyboard : IDisposable
{
    private const int PROT_READ = 0x1;
    private const int MAP_PRIVATE = 0x02;

    [DllImport("libc")] private static extern nint mmap(nint address, nuint length, int protection, int flags, int fd, long offset);
    [DllImport("libc")] private static extern int munmap(nint address, nuint length);
    [DllImport("libc")] private static extern int close(int fd);

    private readonly WaylandPlatform _platform;
    private readonly WlKeyboard _keyboard;

    private nint _context;
    private nint _keymap;
    private nint _state;
    private nint _composeTable;
    private nint _compose;

    private WaylandWindow? _focus;

    // the key held for repeat, in XKB's codes; 0 — none
    private uint _repeatKey;
    private long _repeatNextMs;
    private int _repeatRate = 25;
    private int _repeatDelayMs = 600;

    public WaylandKeyboard(WaylandPlatform platform, WlKeyboard keyboard)
    {
        _platform = platform;
        _keyboard = keyboard;

        _context = Xkb.xkb_context_new(0);

        keyboard.Keymap += OnKeymap;
        keyboard.Enter += OnEnter;
        keyboard.Leave += OnLeave;
        keyboard.Key += OnKey;
        keyboard.Modifiers += OnModifiers;
        keyboard.RepeatInfo += OnRepeatInfo;
    }

    /// <summary>Shift, Control and Alt as they are now: the pointer's events carry them too.</summary>
    public KeyModifiers Modifiers { get; private set; }

    private void OnKeymap(uint format, int fd, uint size)
    {
        try
        {
            if (format != Xkb.KeymapFormatTextV1 || _context == 0) return;

            // the keymap is text in a file the compositor shares; MAP_PRIVATE — from
            // version 7 the compositor may map it read-only
            nint text = mmap(0, size, PROT_READ, MAP_PRIVATE, fd, 0);
            if (text == -1) return;

            nint keymap;

            try
            {
                keymap = Xkb.xkb_keymap_new_from_string(_context, text, Xkb.KeymapFormatTextV1, 0);
            }
            finally
            {
                munmap(text, size);
            }

            if (keymap == 0) return;

            ReleaseKeymap();

            _keymap = keymap;
            _state = Xkb.xkb_state_new(keymap);

            CreateCompose();
        }
        finally
        {
            // the descriptor is the client's to close, whatever came of it
            close(fd);
        }
    }

    /// <summary>The Compose table of the user's locale: dead keys and the Compose key.
    /// None where the locale has no table — the keys then give their own characters.</summary>
    private void CreateCompose()
    {
        if (_composeTable != 0) return;

        string locale = FirstSet("LC_ALL", "LC_CTYPE", "LANG") ?? "C";

        _composeTable = Xkb.xkb_compose_table_new_from_locale(_context, locale, 0);

        if (_composeTable != 0)
            _compose = Xkb.xkb_compose_state_new(_composeTable, 0);

        static string? FirstSet(params string[] names)
        {
            foreach (string name in names)
                if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
                    return value;

            return null;
        }
    }

    private void OnEnter(uint serial, WlSurface? surface, byte[] keys)
    {
        // the focus coming is input too: a paste right after it is the user's
        _platform.Seat?.NoteInput(serial);
        _focus = _platform.FindWindow(surface);
        StopRepeat();
    }

    private void OnLeave(uint serial, WlSurface? surface)
    {
        StopRepeat();

        if (_compose != 0) Xkb.xkb_compose_state_reset(_compose);

        // the releases of the keys held now go elsewhere: the form forgets them, as on
        // X11's FocusOut
        _focus?.Form.OnWindowFocusLost();
        _focus = null;
    }

    private void OnModifiers(uint serial, uint depressed, uint latched, uint locked, uint group)
    {
        if (_state == 0) return;

        Xkb.xkb_state_update_mask(_state, depressed, latched, locked, 0, 0, group);

        var modifiers = KeyModifiers.None;

        if (Xkb.xkb_state_mod_name_is_active(_state, "Shift", Xkb.StateModsEffective) > 0) modifiers |= KeyModifiers.Shift;
        if (Xkb.xkb_state_mod_name_is_active(_state, "Control", Xkb.StateModsEffective) > 0) modifiers |= KeyModifiers.Control;
        if (Xkb.xkb_state_mod_name_is_active(_state, "Mod1", Xkb.StateModsEffective) > 0) modifiers |= KeyModifiers.Alt;

        Modifiers = modifiers;
    }

    private void OnRepeatInfo(int rate, int delay)
    {
        // a rate of 0 — the compositor wants no repeat
        _repeatRate = Math.Max(0, rate);
        _repeatDelayMs = Math.Max(0, delay);

        if (_repeatRate == 0) StopRepeat();
    }

    private void OnKey(uint serial, uint time, uint evdevKey, uint state)
    {
        // Ctrl+C takes the clipboard with this very serial
        _platform.Seat?.NoteInput(serial);

        if (_state == 0 || _focus is not { IsAlive: true } window) return;

        uint key = evdevKey + Xkb.EvdevOffset;

        if (state == (uint)WlKeyboard.KeyState.Pressed)
        {
            // modality by dropping input, as on X11: the owner of an open dialog is deaf
            if (!window.IsInputEnabled) return;

            Press(window, key, isRepeat: false);

            if (_repeatRate > 0 && Xkb.xkb_keymap_key_repeats(_keymap, key) != 0)
            {
                _repeatKey = key;
                _repeatNextMs = Environment.TickCount64 + _repeatDelayMs;
            }
        }
        else
        {
            if (key == _repeatKey) StopRepeat();

            window.Form.OnKeyUp(X11KeyMap.ToKey(ShortcutKeysym(key)), Modifiers);
        }
    }

    private void Press(WaylandWindow window, uint key, bool isRepeat)
    {
        window.Form.OnKeyDown(X11KeyMap.ToKey(ShortcutKeysym(key)), Modifiers, isRepeat);

        // printable characters with a separate call, as on X11; surrogate pairs go char
        // by char, and the text controls assemble them themselves
        foreach (char c in Text(key))
            if (!char.IsControl(c))
                window.Form.OnTextInput(c);
    }

    /// <summary>The keysym of the first layout's first level: what shortcuts are matched by.</summary>
    private uint ShortcutKeysym(uint key)
    {
        if (Xkb.xkb_keymap_key_get_syms_by_level(_keymap, key, 0, 0, out uint* syms) > 0)
            return syms[0];

        return Xkb.xkb_state_key_get_one_sym(_state, key);
    }

    /// <summary>The text a key gives in the effective layout, through Compose: nothing
    /// while a sequence is being typed, the composed character at its end.</summary>
    private string Text(uint key)
    {
        byte* buffer = stackalloc byte[64];

        if (_compose != 0)
        {
            uint keysym = Xkb.xkb_state_key_get_one_sym(_state, key);
            Xkb.xkb_compose_state_feed(_compose, keysym);

            switch (Xkb.xkb_compose_state_get_status(_compose))
            {
                case Xkb.ComposeComposing:
                    return string.Empty;

                case Xkb.ComposeComposed:
                    {
                        int length = Xkb.xkb_compose_state_get_utf8(_compose, buffer, 64);
                        Xkb.xkb_compose_state_reset(_compose);

                        return length > 0 ? Encoding.UTF8.GetString(buffer, Math.Min(length, 63)) : string.Empty;
                    }

                case Xkb.ComposeCancelled:
                    Xkb.xkb_compose_state_reset(_compose);
                    return string.Empty;
            }
        }

        int count = Xkb.xkb_state_key_get_utf8(_state, key, buffer, 64);

        return count > 0 ? Encoding.UTF8.GetString(buffer, Math.Min(count, 63)) : string.Empty;
    }

    /// <summary>How long the loop may sleep before the next repeat; null — no key repeats.</summary>
    public int? RepeatDueInMs =>
        _repeatKey == 0 ? null : (int)Math.Max(0, _repeatNextMs - Environment.TickCount64);

    /// <summary>Repeat the held key if its time came: one repeat per turn of the loop,
    /// so a loop held up by a long frame doesn't make up for it with a burst.</summary>
    public void DispatchRepeat()
    {
        if (_repeatKey == 0 || _repeatRate == 0) return;

        long now = Environment.TickCount64;
        if (now < _repeatNextMs) return;

        if (_focus is not { IsAlive: true, IsInputEnabled: true } window)
        {
            StopRepeat();
            return;
        }

        _repeatNextMs = now + Math.Max(1, 1000 / _repeatRate);

        Press(window, _repeatKey, isRepeat: true);
    }

    private void StopRepeat() => _repeatKey = 0;

    /// <summary>The window closed: a keyboard focused on it holds it no longer.</summary>
    public void Forget(WaylandWindow window)
    {
        if (!ReferenceEquals(_focus, window)) return;

        _focus = null;
        StopRepeat();
    }

    private void ReleaseKeymap()
    {
        if (_state != 0) Xkb.xkb_state_unref(_state);
        if (_keymap != 0) Xkb.xkb_keymap_unref(_keymap);

        _state = 0;
        _keymap = 0;
    }

    public void Dispose()
    {
        if (_keyboard.IsAlive)
        {
            // wl_keyboard.release is version 3; an older one is only forgotten
            if (_keyboard.Version >= 3) _keyboard.Release();
            else _keyboard.Abandon();
        }

        ReleaseKeymap();

        if (_compose != 0) Xkb.xkb_compose_state_unref(_compose);
        if (_composeTable != 0) Xkb.xkb_compose_table_unref(_composeTable);
        if (_context != 0) Xkb.xkb_context_unref(_context);

        _compose = 0;
        _composeTable = 0;
        _context = 0;
    }
}