using System.Runtime.InteropServices;

namespace ZeppelinForms.Linux;

/// <summary>libxkbcommon: the keymap a Wayland compositor sends, the state of the
/// modifiers and layouts over it, and the Compose sequences of dead keys.</summary>
/// <remarks>
/// On X11 the server does all of it through Xlib and the input method; a Wayland client
/// gets bare key codes and turns them into keysyms and text itself. The keysyms are X11's
/// own — the same <see cref="X11KeyMap"/> maps both.
/// </remarks>
internal static unsafe class Xkb
{
    internal const string Lib = "libxkbcommon.so.0";

    public const int KeymapFormatTextV1 = 1;

    /// <summary>xkb_state_component: the modifiers in effect — held, latched or locked.</summary>
    public const int StateModsEffective = 1 << 3;

    // xkb_compose_status
    public const int ComposeNothing = 0;
    public const int ComposeComposing = 1;
    public const int ComposeComposed = 2;
    public const int ComposeCancelled = 3;

    /// <summary>Wayland's key codes are evdev's; XKB's are 8 higher, as X11's are.</summary>
    public const uint EvdevOffset = 8;

    [DllImport(Lib)] public static extern nint xkb_context_new(int flags);
    [DllImport(Lib)] public static extern void xkb_context_unref(nint context);

    [DllImport(Lib)] public static extern nint xkb_keymap_new_from_string(nint context, nint text, int format, int flags);
    [DllImport(Lib)] public static extern void xkb_keymap_unref(nint keymap);
    [DllImport(Lib)] public static extern int xkb_keymap_key_repeats(nint keymap, uint key);

    [DllImport(Lib)]
    public static extern int xkb_keymap_key_get_syms_by_level(nint keymap, uint key, uint layout, uint level, out uint* syms);

    [DllImport(Lib)] public static extern nint xkb_state_new(nint keymap);
    [DllImport(Lib)] public static extern void xkb_state_unref(nint state);

    [DllImport(Lib)]
    public static extern int xkb_state_update_mask(
        nint state, uint depressed, uint latched, uint locked, uint depressedLayout, uint latchedLayout, uint lockedLayout);

    [DllImport(Lib)] public static extern uint xkb_state_key_get_one_sym(nint state, uint key);
    [DllImport(Lib)] public static extern int xkb_state_key_get_utf8(nint state, uint key, byte* buffer, nuint size);

    [DllImport(Lib)]
    public static extern int xkb_state_mod_name_is_active(nint state, [MarshalAs(UnmanagedType.LPStr)] string name, int type);

    [DllImport(Lib)]
    public static extern nint xkb_compose_table_new_from_locale(nint context, [MarshalAs(UnmanagedType.LPStr)] string locale, int flags);

    [DllImport(Lib)] public static extern void xkb_compose_table_unref(nint table);
    [DllImport(Lib)] public static extern nint xkb_compose_state_new(nint table, int flags);
    [DllImport(Lib)] public static extern void xkb_compose_state_unref(nint state);
    [DllImport(Lib)] public static extern int xkb_compose_state_feed(nint state, uint keysym);
    [DllImport(Lib)] public static extern void xkb_compose_state_reset(nint state);
    [DllImport(Lib)] public static extern int xkb_compose_state_get_status(nint state);
    [DllImport(Lib)] public static extern int xkb_compose_state_get_utf8(nint state, byte* buffer, nuint size);
}