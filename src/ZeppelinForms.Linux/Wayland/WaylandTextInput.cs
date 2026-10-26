using System.Text;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>
/// Input methods on Wayland through text-input-v3: ibus, fcitx and the desktop's own
/// compose their text and hand it to the focused window, which shows the composition in
/// the field — underlined at the caret — and takes the committed text as typed.
/// </summary>
/// <remarks>
/// <para>
/// The window that has the keyboard is entered; it turns the input method on while one of
/// its text fields has the focus and tells it where the caret is, so the candidate list
/// opens there. Every change goes with a commit, as the protocol has it.
/// </para>
/// <para>
/// The input method's events are double-buffered: a preedit, a committed string and a
/// deletion come, and <c>done</c> applies them together — the old composition goes, the
/// committed text goes in, the new composition is shown. A done without a preedit is an
/// empty one: the composition ended.
/// </para>
/// <para>
/// While the input method composes, the compositor keeps the keys it takes from the
/// keyboard: they don't come as key events too. The surrounding text isn't told — the
/// input methods work without it, only their deletion of it is left undone.
/// </para>
/// </remarks>
internal sealed class WaylandTextInput : IDisposable
{
    private readonly WaylandPlatform _platform;
    private readonly ZwpTextInputV3 _input;

    /// <summary>The window the text input is in: the one with the keyboard.</summary>
    private WaylandWindow? _focus;

    /// <summary>The input method is on for the focused window's text field.</summary>
    private bool _enabled;

    /// <summary>The caret last told, in the surface's coordinates.</summary>
    private Rectangle? _caret;

    // what the input method sent since the last done
    private string? _pendingPreedit;
    private int _pendingCursorBytes;
    private string? _pendingCommit;

    /// <summary>The composition the field shows now: an update that changes nothing isn't sent.</summary>
    private string _shownPreedit = string.Empty;

    public WaylandTextInput(WaylandPlatform platform, ZwpTextInputV3 input)
    {
        _platform = platform;
        _input = input;

        input.Enter += OnEnter;
        input.Leave += OnLeave;
        input.PreeditString += OnPreeditString;
        input.CommitString += OnCommitString;
        input.Done += OnDone;
    }

    private void OnEnter(WlSurface? surface)
    {
        _focus = _platform.FindWindow(surface);
        _enabled = false;
        _caret = null;

        if (_focus is not null) Update(_focus);
    }

    private void OnLeave(WlSurface? surface)
    {
        // a composition the window had ends with the focus: nothing will commit it now
        EndComposition();

        if (_enabled)
        {
            _input.Disable();
            _input.Commit();
        }

        _enabled = false;
        _caret = null;
        _focus = null;
    }

    /// <summary>The window's focused field or its caret changed: the input method is turned
    /// on or off and told where the caret is.</summary>
    public void Update(WaylandWindow window)
    {
        if (!ReferenceEquals(window, _focus)) return;

        Rectangle? caret = window.TextInputAreaOnSurface;

        if (caret is not { } area)
        {
            if (!_enabled) return;

            EndComposition();

            _input.Disable();
            _input.Commit();

            _enabled = false;
            _caret = null;

            return;
        }

        if (_enabled && _caret == area) return;

        if (!_enabled)
        {
            // enable resets the state: the content type and the caret go with it, in one commit
            _input.Enable();
            _input.SetContentType(
                (uint)ZwpTextInputV3.ContentHint.None, (uint)ZwpTextInputV3.ContentPurpose.Normal);

            _enabled = true;
        }

        _input.SetCursorRectangle(
            (int)MathF.Floor(area.X), (int)MathF.Floor(area.Y),
            Math.Max(1, (int)MathF.Ceiling(area.Width)), Math.Max(1, (int)MathF.Ceiling(area.Height)));

        _input.Commit();

        _caret = area;
    }

    private void OnPreeditString(string? text, int cursorBegin, int cursorEnd)
    {
        _pendingPreedit = text;
        _pendingCursorBytes = cursorBegin;
    }

    private void OnCommitString(string? text) => _pendingCommit = text;

    /// <summary>Apply what came, in the protocol's order: the committed text, then the new
    /// composition — or none.</summary>
    private void OnDone(uint serial)
    {
        string? commit = _pendingCommit;
        string preedit = _pendingPreedit ?? string.Empty;
        int cursorBytes = _pendingCursorBytes;

        _pendingCommit = null;
        _pendingPreedit = null;
        _pendingCursorBytes = 0;

        if (_focus is not { IsAlive: true, IsInputEnabled: true } window) return;

        if (!string.IsNullOrEmpty(commit))
        {
            // the commit ends the composition and goes in as typed characters
            window.Form.OnCompositionCommit(commit);
            _shownPreedit = string.Empty;
        }

        if (preedit == _shownPreedit) return;

        window.Form.OnCompositionUpdate(preedit, CursorIndex(preedit, cursorBytes));
        _shownPreedit = preedit;
    }

    /// <summary>The cursor in the composition: the protocol counts UTF-8 bytes, the field
    /// counts chars; -1 — hidden — puts it at the end.</summary>
    private static int CursorIndex(string text, int bytes)
    {
        if (bytes < 0) return text.Length;

        byte[] utf8 = Encoding.UTF8.GetBytes(text);

        return Encoding.UTF8.GetCharCount(utf8, 0, Math.Clamp(bytes, 0, utf8.Length));
    }

    private void EndComposition()
    {
        if (_shownPreedit.Length == 0) return;

        if (_focus is { IsAlive: true } window)
            window.Form.OnCompositionUpdate(string.Empty, 0);

        _shownPreedit = string.Empty;
    }

    /// <summary>The window closed: the text input is in it no longer.</summary>
    public void Forget(WaylandWindow window)
    {
        if (!ReferenceEquals(_focus, window)) return;

        _focus = null;
        _enabled = false;
        _caret = null;
        _shownPreedit = string.Empty;
    }

    public void Dispose()
    {
        if (_input.IsAlive) _input.Destroy();
    }
}