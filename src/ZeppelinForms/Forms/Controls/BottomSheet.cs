using System.ComponentModel;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Animation;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// A sheet that slides up from the bottom of the form over its content: options for the
/// thing tapped, a short form, details. It is dragged down to close, closes on a tap of
/// the dimmed content around it and on the system's back, and on a wide window it stands
/// in the middle, no wider than <see cref="MaxSheetWidth"/>.
/// </summary>
/// <example>
/// <code>
/// var sheet = new BottomSheet { Child = optionsPanel };
/// await sheet.ShowAsync(form);   // returns when it is closed
/// </code>
/// </example>
public partial class BottomSheet : DecoratedWrapControl, IBackHandler
{
    private BottomSheetLayer? _layer;
    private TaskCompletionSource? _closed;

    /// <summary>0 — hidden below the edge, 1 — all the way up.</summary>
    private float _progress;

    /// <summary>How far the finger has pulled the sheet down, in pixels.</summary>
    private float _drag;

    public BottomSheet()
    {
        AccessibleRole = AccessibilityRole.Dialog;

        SetControlDefault(PaddingProperty, new Thickness(16f, 0f, 16f, 16f));
        SetControlDefault(CornerRadiusProperty, new CornerRadius(16f, 16f, 0f, 0f));

        var pan = this.AddGesture(new PanGestureRecognizer { Direction = PanDirection.Vertical, CanBegin = _ => IsDismissible });
        pan.Updated += (_, e) =>
        {
            _drag = Math.Max(0f, e.TotalOffset.Y);
            _layer?.Invalidate();
        };
        pan.Completed += (_, e) => OnDragEnded(e.Velocity.Y);
        pan.Cancelled += (_, _) => OnDragEnded(0f);
    }

    public BottomSheet(UIElement content) : this() => Child = content;

    public bool IsOpen => _layer is not null;

    /// <summary>Whether the content around the sheet is dimmed and out of reach while it is
    /// open. True by default; a sheet beside which the content stays usable — a player's
    /// controls — is not modal.</summary>
    public bool IsModal { get; set; } = true;

    /// <summary>Whether a drag down, a tap outside, Escape and the system's back close the
    /// sheet. False — only <see cref="Close"/> does.</summary>
    public bool IsDismissible { get; set; } = true;

    /// <summary>Whether the short bar at the top shows that the sheet can be dragged.</summary>
    public bool ShowsHandle { get; set; } = true;

    /// <summary>The share of the form's height the sheet may take at most; its content
    /// scrolls when it needs more.</summary>
    public float MaxHeightRatio { get; set; } = 0.9f;

    /// <summary>The widest the sheet gets: on a wide window it stands in the middle.</summary>
    [Styled(Category = "Layout")]
    public partial float MaxSheetWidth { get; set; }
    private static float MaxSheetWidthDefault => 640f;

    /// <summary>The tint over the content while the sheet is open and modal.</summary>
    [Styled(Category = "Appearance")]
    public partial Color ScrimColor { get; set; }
    private static Color ScrimColorDefault => new(82, 0, 0, 0);

    [Styled(Category = "Appearance")]
    public partial Color HandleColor { get; set; }
    private static Color HandleColorDefault => new(110, 0, 0, 0);

    public int TransitionDurationMs { get; set; } = 250;

    /// <summary>The sheet is about to close; cancel to keep it.</summary>
    public event EventHandler<CancelEventArgs>? Closing;

    public event EventHandler? Opened;

    public event EventHandler? Closed;

    /// <summary>Open the sheet over <paramref name="form"/>.</summary>
    public void Show(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (IsOpen) return;

        // a dialog to a screen reader: named, if the application gave it no name
        if (string.IsNullOrEmpty(AccessibleName)) AccessibleName = Localization.Get(ZfText.Sheet);

        _layer = new BottomSheetLayer(this, form);
        _closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _drag = 0f;

        form.AddOverlay(_layer);
        form.AddBackHandler(this);

        // the first field of the content, as a dialog does — not on a phone, where that
        // would open the keyboard over the sheet the moment it rises
        if (form.PlatformWindow is not ISoftKeyboard) form.FocusFirstStop(this);

        Animate(1f, () => Opened?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Open the sheet and wait until it is closed.</summary>
    public Task ShowAsync(Form form)
    {
        Show(form);
        return _closed?.Task ?? Task.CompletedTask;
    }

    /// <summary>Close the sheet. False — a handler of <see cref="Closing"/> kept it.</summary>
    public bool Close()
    {
        if (_layer is not { } layer) return false;

        var args = new CancelEventArgs();
        Closing?.Invoke(this, args);

        if (args.Cancel)
        {
            Snap();
            return false;
        }

        layer.IsClosing = true;
        layer.Form.RemoveBackHandler(this);

        Animate(0f, () =>
        {
            layer.Form.RemoveOverlay(layer);
            layer.Detach();

            _layer = null;
            _drag = 0f;

            Closed?.Invoke(this, EventArgs.Empty);

            TaskCompletionSource? closed = _closed;
            _closed = null;
            closed?.TrySetResult();
        });

        return true;
    }

    bool IBackHandler.ClosesOnEscape => true;

    bool IBackHandler.TryHandleBack(BackSource source)
    {
        if (_layer is not { IsClosing: false } || !IsDismissible) return false;

        Close();
        return true;
    }

    // ===== motion =====

    internal float Progress => _progress;

    internal float Drag => _drag;

    private void Animate(float to, Action done)
    {
        if (_layer is not { } layer) return;

        bool canAnimate = TransitionDurationMs > 0 && layer.Form.PlatformWindow is not null && !Motion.IsReduced;

        if (!canAnimate)
        {
            _progress = to;
            layer.Invalidate();
            done();
            return;
        }

        float from = _progress;

        layer.Animate("sheet", from, to, TimeSpan.FromMilliseconds(TransitionDurationMs), Interpolators.Float,
            value =>
            {
                _progress = value;
                layer.Invalidate();
            },
            Easing.EaseOut,
            completed: done);
    }

    private void OnDragEnded(float velocity)
    {
        if (_layer is null) return;

        float height = Math.Max(1f, ActualSize.Height);

        // pulled down far enough or flung: closed; otherwise it springs back
        if (IsDismissible && (_drag > height * 0.35f || velocity > 900f))
        {
            // the drag becomes the closing's start: the sheet goes on from where it was let go
            _progress = Math.Clamp(1f - _drag / height, 0f, 1f);
            _drag = 0f;

            if (!Close()) Snap();
            return;
        }

        Snap();
    }

    /// <summary>Back up, from wherever a drag left the sheet.</summary>
    private void Snap()
    {
        if (_layer is not { } layer || _drag <= 0f) return;

        float start = _drag;

        layer.Animate("sheet-snap", start, 0f, TimeSpan.FromMilliseconds(Math.Max(1, TransitionDurationMs * 0.6)), Interpolators.Float,
            value =>
            {
                _drag = value;
                layer.Invalidate();
            },
            Easing.EaseOut);
    }

    // ===== look =====

    private const float HandleZone = 20f;

    /// <summary>The padding, and below it the gesture bar of a form that draws under it:
    /// the sheet's background runs down to the edge, its content stays above.</summary>
    private Thickness ContentPadding
    {
        get
        {
            Thickness padding = Padding;
            float safe = _layer?.Form.SafeAreaInsets.Bottom ?? 0f;

            return padding with { Bottom = padding.Bottom + safe };
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        float top = ShowsHandle ? HandleZone : 12f;
        Thickness padding = ContentPadding;

        var content = new Size(
            Math.Max(0, availableSize.Width - padding.Horizontal),
            Math.Max(0, availableSize.Height - padding.Vertical - top));

        Size child = Size.Empty;

        if (Child is not null)
        {
            Child.Measure(content);
            child = Child.DesiredSize;
        }

        return ResolveSize(new Size(child.Width + padding.Horizontal, child.Height + padding.Vertical + top), availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        float top = ShowsHandle ? HandleZone : 12f;
        Thickness padding = ContentPadding;

        Child?.Arrange(new Rectangle(
            new Point(padding.Left, padding.Top + top),
            new Size(
                Math.Max(0, finalSize.Width - padding.Horizontal),
                Math.Max(0, finalSize.Height - padding.Vertical - top))));

        return finalSize;
    }

    protected override void DrawContent(Graphics g)
    {
        if (!ShowsHandle) return;

        const float width = 32f;
        const float height = 4f;

        var handle = new Rectangle(new Point((ActualSize.Width - width) / 2f, 8f), new Size(width, height));
        g.FillRoundRectangle(handle, new CornerRadius(height / 2f), HandleColor);
    }
}

/// <summary>What a <see cref="BottomSheet"/> lies in while open: an overlay over the whole
/// form — the scrim, and the sheet at its bottom — or, for a sheet that isn't modal, over
/// the sheet's own rectangle only, so the rest of the form keeps its input.</summary>
internal sealed class BottomSheetLayer : DecoratedPanel
{
    private readonly BottomSheet _sheet;

    public BottomSheetLayer(BottomSheet sheet, Form form)
    {
        _sheet = sheet;
        Form = form;

        if (sheet.IsModal) Children.Add(new BottomSheetScrim(sheet, this));

        Children.Add(sheet);

        form.ClientSizeChanged += OnFormResized;
    }

    public Form Form { get; }

    public bool IsClosing { get; set; }

    public void Detach()
    {
        Form.ClientSizeChanged -= OnFormResized;
        Children.Clear();
    }

    private void OnFormResized(object? sender, EventArgs e) => Invalidate();

    /// <summary>Where the sheet stands, in the form's coordinates, by its progress and drag.</summary>
    private Rectangle SheetRect(Size client)
    {
        float width = Math.Min(client.Width, _sheet.MaxSheetWidth);
        float maxHeight = client.Height * Math.Clamp(_sheet.MaxHeightRatio, 0.1f, 1f);

        _sheet.Measure(new Size(width, maxHeight));

        float height = Math.Min(_sheet.DesiredSize.Height, maxHeight);
        float shown = height * _sheet.Progress - _sheet.Drag;

        return new Rectangle(
            new Point((client.Width - width) / 2f, client.Height - Math.Max(0f, shown)),
            new Size(width, height));
    }

    protected override Size MeasureContentOverride(Size availableSize)
    {
        Size client = Form.ClientSize;
        Rectangle sheet = SheetRect(client);

        // overlays are placed at their Position: a modal layer covers the form, the other
        // only the sheet
        Position = _sheet.IsModal ? Point.Empty : sheet.Position;

        return _sheet.IsModal ? client : sheet.Size;
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        Rectangle sheet = SheetRect(Form.ClientSize);

        foreach (UIElement child in Children)
        {
            if (ReferenceEquals(child, _sheet)) continue;

            child.Arrange(new Rectangle(Point.Empty, contentSize));
        }

        // the sheet's own padding keeps its content above the gesture bar
        Point at = _sheet.IsModal ? sheet.Position : Point.Empty;
        _sheet.Arrange(new Rectangle(at, sheet.Size));
    }
}

/// <summary>The tint over the form under a modal <see cref="BottomSheet"/>; a press on it
/// closes the sheet.</summary>
internal sealed class BottomSheetScrim : UnitControl
{
    private readonly BottomSheet _sheet;
    private readonly BottomSheetLayer _layer;

    public BottomSheetScrim(BottomSheet sheet, BottomSheetLayer layer)
    {
        _sheet = sheet;
        _layer = layer;

        // decoration: the reader has the sheet
        IsAccessibilityHidden = true;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(float.IsFinite(availableSize.Width) ? availableSize.Width : 0f,
            float.IsFinite(availableSize.Height) ? availableSize.Height : 0f);

    public override void Draw(Graphics g)
    {
        Color scrim = _sheet.ScrimColor;
        byte alpha = (byte)Math.Round(scrim.A * Math.Clamp(_sheet.Progress, 0f, 1f));

        if (alpha > 0) g.FillRectangle(LocalBounds, new Color(alpha, scrim.R, scrim.G, scrim.B));
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        // a press beside the sheet: the content under it was meant, and is out of reach
        if (_sheet.IsDismissible && !_layer.IsClosing) _sheet.Close();

        e.Handled = true;
    }
}
