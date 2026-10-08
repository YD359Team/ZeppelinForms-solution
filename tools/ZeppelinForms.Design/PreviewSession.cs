using System.Globalization;
using System.Reflection;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Design.Protocol;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Theming;

namespace ZeppelinForms.Design;

/// <summary>
/// One preview, live: the user's view in a form without a window, laid out, drawn
/// into pixels on demand and answering the IDE's mouse and keyboard.
/// </summary>
/// <remarks>
/// <para>
/// The form lives on a <see cref="HeadlessPlatform"/>: layout, input, focus,
/// animations and styles all work as in an application, only the pixels go to the
/// IDE instead of a screen. Drawing is the host's business — it passes a
/// <see cref="FrameRenderer"/>, Skia in the real previewer, a stub in tests.
/// </para>
/// <para>
/// Frames are not drawn on a timer. The window counts invalidations, and a frame is
/// drawn only after something asked for one; while an animation runs, the session
/// ticks the form's clock the way a platform's frame timer would.
/// </para>
/// </remarks>
public sealed class PreviewSession : IDisposable
{
    /// <summary>Draw a form into pixels: device size and the scale from
    /// device-independent pixels.</summary>
    public delegate Image FrameRenderer(Form form, int pixelWidth, int pixelHeight, float scale);

    /// <summary>The size of a preview of an element that asks for none.</summary>
    public static Size DefaultElementSize { get; } = new(480, 320);

    private readonly FrameRenderer _render;
    private readonly HeadlessPlatform _platform;

    private PreviewEntry? _entry;
    private Form? _form;
    private HeadlessWindow? _window;
    private PreviewSettings _settings = new();
    private int _drawnInvalidations = -1;

    public PreviewSession(FrameRenderer render, HeadlessPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(platform);

        _render = render;
        _platform = platform;
    }

    /// <summary>The form being previewed; null before the first successful open.</summary>
    public Form? Form => _form;

    public PreviewEntry? Entry => _entry;

    /// <summary>What is in effect: the IDE's settings over the preview's own.</summary>
    public PreviewSettings Settings => _settings;

    /// <summary>Something asked for a repaint since the last frame, or there was none yet.</summary>
    public bool NeedsFrame => _window is not null && _window.InvalidateCount != _drawnInvalidations;

    /// <summary>An animation runs: the form wants frames.</summary>
    public bool IsAnimating => _window?.Frames.IsRunning == true;

    /// <summary>Build a preview and show it. The user's exception comes out as it was
    /// thrown, without the reflection wrapper; the previous preview is closed either way.</summary>
    public void Open(PreviewEntry entry, PreviewSettings requested)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(requested);

        Close();

        _entry = entry;
        _settings = Merge(entry.Info.Defaults, requested);

        // global settings first: the view may read the theme or the culture while
        // it is being built
        ApplyGlobals(_settings);

        object built;

        try
        {
            built = entry.Build();
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }

        Form form = built as Form ?? new Form { Content = (UIElement)built };

        Size size = SizeFor(_settings, form, isForm: built is Form);
        form.Size = size;
        form.FlowDirection = _settings.RightToLeft ? FlowDirection.RightToLeft : null;

        _window = (HeadlessWindow)_platform.CreateWindow(form);
        _form = form;

        _window.Resize(size.Width, size.Height);
        form.UpdateLayout();

        _drawnInvalidations = -1;
    }

    /// <summary>The IDE changed a setting: resized the window, switched the theme.</summary>
    public void Apply(PreviewSettings requested)
    {
        ArgumentNullException.ThrowIfNull(requested);

        if (_entry is null || _form is null || _window is null) return;

        PreviewSettings next = Merge(_entry.Info.Defaults, requested);

        ApplyGlobals(next);

        _form.FlowDirection = next.RightToLeft ? FlowDirection.RightToLeft : null;

        Size size = SizeFor(next, _form, _entry.Info.IsForm);

        if (size != _form.ClientSize)
        {
            _form.Size = size;
            _window.Resize(size.Width, size.Height);
        }

        _settings = next;
        _drawnInvalidations = -1;
    }

    /// <summary>One frame of the form's animations, as a platform's frame timer gives it.</summary>
    public void Tick() => _window?.Tick();

    /// <summary>Draw the preview. Null — nothing is open.</summary>
    public FrameMessage? Render()
    {
        if (_form is null || _window is null) return null;

        _form.UpdateLayout();

        float scale = _settings.Scale > 0 ? _settings.Scale : 1f;
        int width = Math.Max(1, (int)MathF.Ceiling(_form.ClientSize.Width * scale));
        int height = Math.Max(1, (int)MathF.Ceiling(_form.ClientSize.Height * scale));

        Image image = _render(_form, width, height, scale);

        // after drawing: drawing itself may invalidate — a transition's frame — and
        // that request is for the next frame, not for this one
        _drawnInvalidations = _window.InvalidateCount;

        return new FrameMessage(image.Width, image.Height, scale, image.Pixels);
    }

    /// <summary>Count the pending repaint as done without drawing: drawing failed,
    /// and only a new change of the view is worth another attempt.</summary>
    public void SkipFrame()
    {
        if (_window is not null) _drawnInvalidations = _window.InvalidateCount;
    }

    // ===== input =====

    public void Pointer(PointerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_form is null) return;

        var point = new Point(message.X, message.Y);
        KeyModifiers modifiers = ToModifiers(message.Modifiers);
        MouseButton button = message.Button switch
        {
            DesignerMouseButton.Right => MouseButton.Right,
            DesignerMouseButton.Middle => MouseButton.Middle,
            _ => MouseButton.Left,
        };

        switch (message.Action)
        {
            case PointerAction.Move: _form.OnPointerMove(point, modifiers); break;
            case PointerAction.Down: _form.OnPointerDown(point, button, modifiers); break;
            case PointerAction.Up: _form.OnPointerUp(point, button, modifiers); break;
            case PointerAction.Wheel: _form.OnMouseWheel(point, message.WheelDelta); break;
            case PointerAction.Leave: _form.OnPointerLeaveWindow(); break;
        }
    }

    /// <summary>A key by its virtual-key code or its name in <see cref="Key"/>. False —
    /// neither is known, and the key is dropped.</summary>
    public bool Key(KeyMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_form is null) return false;

        Key key;

        if (message.Code != 0 && Enum.IsDefined(typeof(Key), message.Code))
            key = (Key)message.Code;
        else if (!Enum.TryParse(message.Key, ignoreCase: true, out key) || !Enum.IsDefined(key))
            return false;

        KeyModifiers modifiers = ToModifiers(message.Modifiers);

        if (message.IsDown) _form.OnKeyDown(key, modifiers, message.IsRepeat);
        else _form.OnKeyUp(key, modifiers);

        return true;
    }

    public void Text(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (_form is null) return;

        foreach (char c in text)
            _form.OnTextInput(c);
    }

    // ===== lifetime =====

    public void Close()
    {
        if (_form is null) return;

        Form form = _form;

        _form = null;
        _window = null;
        _entry = null;

        form.Close();
        form.Dispose();
    }

    public void Dispose() => Close();

    // ===== settings =====

    /// <summary>The IDE's choice where it made one, the preview's own otherwise.</summary>
    internal static PreviewSettings Merge(PreviewSettings own, PreviewSettings requested) => new()
    {
        Width = requested.Width > 0 ? requested.Width : own.Width,
        Height = requested.Height > 0 ? requested.Height : own.Height,
        Scale = requested.Scale > 0 ? requested.Scale : 1f,
        Theme = requested.Theme ?? own.Theme,
        Culture = requested.Culture ?? own.Culture,
        RightToLeft = requested.RightToLeft || own.RightToLeft,
        TextScale = requested.TextScale > 0 ? requested.TextScale : own.TextScale,
    };

    private static Size SizeFor(PreviewSettings settings, Form form, bool isForm)
    {
        // a form brings its own size; an element without one gets the default
        Size fallback = isForm && form.Size.Width > 0 && form.Size.Height > 0
            ? form.Size
            : DefaultElementSize;

        return new Size(
            settings.Width > 0 ? settings.Width : fallback.Width,
            settings.Height > 0 ? settings.Height : fallback.Height);
    }

    private static void ApplyGlobals(PreviewSettings settings)
    {
        if (settings.Theme is { } name)
            App.Theme = FindTheme(name)
                ?? throw new ArgumentException($"There is no theme '{name}'. Built-in: {string.Join(", ", ThemeNames)}.");

        if (settings.Culture is { } culture)
            Localization.Culture = CultureInfo.GetCultureInfo(culture);

        App.OverrideTextScale(settings.TextScale > 0 ? settings.TextScale : null);
    }

    private static IEnumerable<string> ThemeNames =>
        typeof(Themes).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(Theme))
            .Select(p => p.Name);

    /// <summary>A built-in theme by the name of its property or by its own name.</summary>
    internal static Theme? FindTheme(string name)
    {
        foreach (PropertyInfo property in typeof(Themes).GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (property.PropertyType != typeof(Theme)) continue;

            var theme = (Theme)property.GetValue(null)!;

            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase))
                return theme;
        }

        return null;
    }

    private static KeyModifiers ToModifiers(DesignerModifiers modifiers)
    {
        KeyModifiers result = KeyModifiers.None;

        if ((modifiers & DesignerModifiers.Shift) != 0) result |= KeyModifiers.Shift;
        if ((modifiers & DesignerModifiers.Control) != 0) result |= KeyModifiers.Control;
        if ((modifiers & DesignerModifiers.Alt) != 0) result |= KeyModifiers.Alt;

        return result;
    }
}