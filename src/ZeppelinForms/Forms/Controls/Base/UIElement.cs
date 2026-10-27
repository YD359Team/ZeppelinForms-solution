using System.ComponentModel;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Effects;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Base element of any UI tree node
/// </summary>
public abstract partial class UIElement : IGridPlaceable, IBorderedElement, INotifyPropertyChanged
{
    /// <summary>The element is attached to a form. Needed by those who cannot
    /// work without one: animations, subscriptions to the window's life cycle.</summary>
    public event EventHandler? Attached;
    public event EventHandler? Detached;
    /// <summary>A styled property changed its value. Makes any element usable
    /// as a binding source: <c>label.Bind(Label.TextProperty, textBox, nameof(TextBox.Text))</c>.</summary>
    /// <remarks>
    /// The arguments are created only when someone is subscribed: the
    /// null-conditional call does not evaluate them otherwise, so elements
    /// nobody listens to pay nothing.
    /// </remarks>
    public event PropertyChangedEventHandler? PropertyChanged;

    // ===
    [Styled(Category = "Appearance")]
    public partial Color Background { get; set; }
    private static Color BackgroundDefault => Colors.Transparent;

    /// <summary>Gradient background fill. Fewer than two stops — the plain
    /// <see cref="Background"/> is drawn.
    /// The list is treated as immutable: to change the gradient, assign
    /// a new one rather than editing the existing one — otherwise there will be no redraw.</summary>
    [Styled(Category = "Appearance")]
    public partial IReadOnlyList<GradientStop>? BackgroundGradient { get; set; }

    /// <summary>Gradient direction in degrees: 0 — left to right.</summary>
    [Styled(Category = "Appearance")]
    public partial float BackgroundGradientAngle { get; set; }

    /// <summary>Whether there is anything to fill with a gradient.</summary>
    protected bool HasBackgroundGradient => BackgroundGradient is { Count: >= 2 };

    // the stops of BackgroundGradient as the array the graphics take, not copied every frame
    private GradientStopCache _backgroundStops;

    /// <summary>Fill the background: with the gradient if one is set, otherwise
    /// with a solid color. Shared code for all three Decorated* bases.</summary>
    protected void FillBackground(Graphics g, Rectangle bounds)
    {
        if (HasBackgroundGradient)
        {
            g.FillGradient(bounds, CornerRadius, _backgroundStops.For(BackgroundGradient!), BackgroundGradientAngle);
            return;
        }

        if (CurrentBackground.A > 0)
            g.FillRoundRectangle(bounds, CornerRadius, CurrentBackground);
    }

    /// <summary>Set a gradient from individual stops.</summary>
    public void SetBackgroundGradient(params GradientStop[] stops) => BackgroundGradient = stops;

    /// <summary>An even transition between two colors.</summary>
    public void SetBackgroundGradient(Color from, Color to) =>
        BackgroundGradient = [new GradientStop(from, 0f), new GradientStop(to, 1f)];

    /// <summary>Text color. Inherited downward: set it on a panel,
    /// and all the labels, buttons and fields inside pick it up.</summary>
    [Styled(Category = "Text", Inherits = true)]
    public partial Color TextColor { get; set; }
    private static Color TextColorDefault => Colors.Black;

    [Styled(Category = "Appearance")]
    public partial Color BorderColor { get; set; }
    private static Color BorderColorDefault => Colors.Transparent;

    [Styled(Category = "Appearance")]
    public partial float BorderWidth { get; set; }
    // ===

    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial float FlexGrow { get; set; }

    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial Dock Docking { get; set; }

    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial HorizontalAlignment HorizontalAlignment { get; set; }

    private static HorizontalAlignment HorizontalAlignmentDefault => HorizontalAlignment.Stretch;

    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial VerticalAlignment VerticalAlignment { get; set; }

    private static VerticalAlignment VerticalAlignmentDefault => VerticalAlignment.Stretch;

    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial FlowDirection? FlowDirection { get; set; }

    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial Thickness Margin { get; set; }

    private static Thickness MarginDefault => Thickness.Zero;

    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial Thickness Padding { get; set; }
    private static Thickness PaddingDefault => Thickness.Zero;

    [Styled(Category = "Grid", AffectsLayout = true)]
    public partial int Row { get; set; }

    [Styled(Category = "Grid", AffectsLayout = true)]
    public partial int Column { get; set; }

    [Styled(Category = "Grid", AffectsLayout = true)]
    public partial int RowSpan { get; set; }

    private static int RowSpanDefault => 1;

    [Styled(Category = "Grid", AffectsLayout = true)]
    public partial int ColumnSpan { get; set; }

    private static int ColumnSpanDefault => 1;

    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial bool IsVisible { get; set; }

    private static bool IsVisibleDefault => true;

    /// <summary>Whether the element is visible, taking ancestors into account.</summary>
    /// <remarks>
    /// Its own IsVisible is not enough: PageControl hides pages without detaching
    /// them from the tree, so an element on a hidden page keeps IsVisible true
    /// while it is not on screen.
    /// </remarks>
    public bool IsEffectivelyVisible
    {
        get
        {
            for (UIElement? node = this; node is not null; node = node.Parent)
                if (!node.IsVisible) return false;

            return true;
        }
    }

    /// <summary>Whether the element is enabled, taking ancestors into account.</summary>
    /// <remarks>
    /// IsEnabled is not inherited as a value, but a disabled container disables
    /// everything inside it. Controls that draw their own rows need this: the hit
    /// lands on a row, which is itself enabled, so the form's check for a disabled
    /// hit doesn't stop a click inside a disabled list.
    /// </remarks>
    public bool IsEffectivelyEnabled
    {
        get
        {
            for (UIElement? node = this; node is not null; node = node.Parent)
                if (!node.IsEnabled) return false;

            return true;
        }
    }

    [Styled(Category = "Text", AffectsLayout = true)]
    public partial Font? Font { get; set; }

    /// <summary>Caption casing. Inherited: set on a panel, it applies to every
    /// caption inside. The text itself is not changed — see ApplyTextTransform.</summary>
    [Styled(Category = "Text", AffectsLayout = true, Inherits = true)]
    public partial TextTransform TextTransform { get; set; }

    /// <remarks>
    /// By the rules of the interface language, Localization.Culture: in Turkish
    /// "i" becomes "İ", not "I". Not the thread's culture — the application's
    /// language is what the captions are written in.
    /// With <see cref="UseMnemonic"/> the access key marks are taken out first:
    /// what is measured and drawn is the caption as shown.
    /// </remarks>
    protected string ApplyTextTransform(string text) =>
        TransformCase(UseMnemonic ? Mnemonic.Strip(text) : text);

    /// <summary>The case transform alone, without the access key marks.</summary>
    private protected string TransformCase(string text) => TextTransform switch
    {
        Enums.TextTransform.UpperCase => text.ToUpper(Localization.Culture),
        Enums.TextTransform.LowerCase => text.ToLower(Localization.Culture),
        _ => text,
    };

    //
    /// <summary>Rotation in degrees around the element's center.</summary>
    [Styled(Category = "Appearance")]
    public partial float Rotation { get; set; }

    /// <summary>A draw-time offset that doesn't touch layout.</summary>
    /// <remarks>
    /// The element's place stays where layout computed it: neighbours don't
    /// move apart, sizes aren't recomputed, hit testing follows the picture.
    /// That is exactly why offset and scale are suitable for transitions,
    /// while Margin or Size are not: those change the layout of everything around.
    /// </remarks>
    [Styled(Category = "Appearance")]
    public partial float TranslateX { get; set; }

    [Styled(Category = "Appearance")]
    public partial float TranslateY { get; set; }

    /// <summary>Draw-time scale around the element's center.</summary>
    [Styled(Category = "Appearance")]
    public partial float ScaleX { get; set; }
    private static float ScaleXDefault => 1f;

    [Styled(Category = "Appearance")]
    public partial float ScaleY { get; set; }
    private static float ScaleYDefault => 1f;

    [Styled(Category = "Behavior")]
    public partial bool IsEnabled { get; set; }

    private static bool IsEnabledDefault => true;

    [Styled(Category = "Appearance")]
    public partial float DisabledOpacity { get; set; }

    private static float DisabledOpacityDefault => 0.5f;

    [Styled(Category = "Appearance")]
    public partial float DisabledDesaturation { get; set; }

    private static float DisabledDesaturationDefault => 0.6f;

    [Styled(Category = "Appearance")]
    public partial BoxShadow? BoxShadow { get; set; }

    // ===== the tree: attaching, the parent and the form =====

    internal void RaiseAttached()
    {
        // the language may have changed while the element was out of every form's
        // tree: no form walked it then, so it catches up here, before anyone sees it
        if (_localizationVersion != Localization.Version)
            ApplyLocalization();

        OnAttached();
        Attached?.Invoke(this, EventArgs.Empty);
    }

    public UIElement? Parent
    {
        get;
        internal set
        {
            if (ReferenceEquals(field, value)) return;

            field = value;
            BumpOwnerGeneration();
            OnParentChangedForAccessibility(value);
        }
    }

    internal Form? Owner
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            field = value;
            BumpOwnerGeneration();
        }
    }

    // A shared generation counter: any change of Parent or Owner anywhere
    // invalidates all caches at once. Crude, but the tree structure changes
    // orders of magnitude less often than FindOwner is called, and this way
    // it is impossible to miss an invalidation — both properties are declared
    // right here.
    // Incremented atomically: snapshot tests build trees on several threads
    // in parallel, and a lost increment would leave a cache valid after a change.
    private static int _ownerGeneration;

    private static void BumpOwnerGeneration() => Interlocked.Increment(ref _ownerGeneration);

    private Form? _cachedOwner;
    private int _cachedOwnerGeneration = -1;

    public Point Position { get; set; }
    // Auto by default — size to content until Size is set explicitly
    private Size _explicitSize = Size.Auto;
    private Size _actualSize = Size.Empty;

    /// <summary>The explicitly set size. Size.Auto means "fit to content".</summary>
    public Size Size
    {
        get => _explicitSize;
        set
        {
            if (_explicitSize == value) return;

            _explicitSize = value;
            _actualSize = value;   // until the first layout, draw at the given size
            Invalidate();
        }
    }

    /// <summary>The explicit width alone; NaN — the width is left to the layout,
    /// whatever <see cref="Height"/> says.</summary>
    /// <remarks>
    /// <see cref="Size"/> sets both axes at once, so giving a text box a height meant
    /// giving it a width as well, and a box docked to the top no longer stretched
    /// across the form. The workaround — <c>new Size(float.NaN, 30)</c> — worked but
    /// nobody could guess it. Width and Height read and write the same explicit size
    /// as Size, one axis each.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">A negative or infinite width.</exception>
    public float Width
    {
        get => _explicitSize.Width;
        set => Size = new Size(CheckExplicitLength(value, nameof(Width)), _explicitSize.Height);
    }

    /// <summary>The explicit height alone; NaN — the height is left to the layout,
    /// whatever <see cref="Width"/> says.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A negative or infinite height.</exception>
    public float Height
    {
        get => _explicitSize.Height;
        set => Size = new Size(_explicitSize.Width, CheckExplicitLength(value, nameof(Height)));
    }

    /// <summary>An explicit length is a finite non-negative number, or NaN for auto:
    /// an infinite one would mean "no constraint" to the layout, and a negative one
    /// has no meaning at all.</summary>
    private static float CheckExplicitLength(float value, string name) =>
        float.IsNaN(value) || (float.IsFinite(value) && value >= 0f)
            ? value
            : throw new ArgumentOutOfRangeException(name, value, "Expected a finite non-negative length, or NaN for auto.");

    /// <summary>The actual size after layout. This is what the element is drawn with.</summary>
    public Size ActualSize => _actualSize;
    public Rectangle Rectangle => new(Position, _actualSize);
    public Rectangle LocalBounds => new(Point.Empty, SanitizedSize);
    public Rectangle ContentBounds => new(
        new Point(Padding.Left, Padding.Top),
        new Size(
            NonNegative(SanitizedSize.Width - Padding.Horizontal),
            NonNegative(SanitizedSize.Height - Padding.Vertical)));

    /// <summary>The rectangle to clip descendants by. ContentBounds by default;
    /// panels and wrappers cut at their own edge instead, and a scrolling panel at
    /// its viewport along the axis it scrolls.</summary>
    protected internal virtual Rectangle ClipBounds => ContentBounds;

    private Size SanitizedSize => new(
        float.IsFinite(_actualSize.Width) ? _actualSize.Width : 0f,
        float.IsFinite(_actualSize.Height) ? _actualSize.Height : 0f);

    private static float NonNegative(float value) =>
        float.IsFinite(value) && value > 0f ? value : 0f;

    [Styled(Category = "Appearance")]
    public partial CornerRadius CornerRadius { get; set; }
    private static CornerRadius CornerRadiusDefault => CornerRadius.Zero;

    protected internal bool HasTransform =>
        Rotation != 0f || ScaleX != 1f || ScaleY != 1f || TranslateX != 0f || TranslateY != 0f;

    /// <summary>Whether there is a transform more complex than an offset.
    /// The tree walk can add an offset to the position, so it doesn't get
    /// in the way of culling, while rotation and scale do.</summary>
    protected internal bool HasComplexTransform => Rotation != 0f || ScaleX != 1f || ScaleY != 1f;

    internal Point Center => new(ActualSize.Width / 2f, ActualSize.Height / 2f);

    /// <summary>Apply the element's transforms to the canvas. Called after
    /// translating by Position, that is, already in the element's coordinate system.</summary>
    /// <remarks>
    /// The order is the same here and in the inverse point mapping: offset,
    /// then rotation and scale around the center. Two implementations of one
    /// transform must stay side by side — if they drift apart, you get
    /// the picture in one place and the clicks in another.
    /// </remarks>
    internal void ApplyTransform(Graphics g)
    {
        if (TranslateX != 0f || TranslateY != 0f)
            g.Translate(TranslateX, TranslateY);

        if (!HasComplexTransform) return;

        Point center = Center;

        g.Translate(center.X, center.Y);

        if (Rotation != 0f) g.Rotate(Rotation);
        if (ScaleX != 1f || ScaleY != 1f) g.Scale(ScaleX, ScaleY);

        g.Translate(-center.X, -center.Y);
    }

    /// <summary>Map a point from the parent's coordinate system to our own —
    /// the inverse of ApplyTransform together with the Position offset.</summary>
    internal Point TransformPointToLocal(Point pointInParentSpace)
    {
        var local = new Point(
            pointInParentSpace.X - Position.X - TranslateX,
            pointInParentSpace.Y - Position.Y - TranslateY);

        if (!HasComplexTransform) return local;

        Point center = Center;

        if (ScaleX != 0f && ScaleY != 0f && (ScaleX != 1f || ScaleY != 1f))
            local = new Point(
                center.X + (local.X - center.X) / ScaleX,
                center.Y + (local.Y - center.Y) / ScaleY);

        if (Rotation != 0f)
            local = RotateAround(local, center, -Rotation);

        return local;
    }

    internal static Point RotateAround(Point point, Point center, float degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);

        float dx = point.X - center.X;
        float dy = point.Y - center.Y;

        return new Point(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos);
    }

    public string? ToolTip { get; set; }

    /// <summary>The tooltip at a point of the form: <see cref="ToolTip"/> by default.
    /// An element that draws parts of its own — dots, cells, chart points — answers
    /// with the part's text, and calls <see cref="RefreshToolTip"/> when the part
    /// under the pointer changes.</summary>
    protected internal virtual string? GetToolTip(Point location) => ToolTip;

    /// <summary>The part under the pointer changed: the form asks
    /// <see cref="GetToolTip"/> again. A tooltip already shown moves on to the new
    /// part at once, as system tooltips do between the items of one control.</summary>
    protected void RefreshToolTip() => FindOwner()?.RefreshToolTip(this);

    /// <summary>The element's name: unique in its form, and what <c>#name</c>
    /// in a style selector matches.</summary>
    /// <remarks>An element given no name gets one when it joins a form — "button3", as
    /// WinForms' designer names them. A name given here, by markup's <c>x:Name</c> or by
    /// code, is the element's own: a control with a caption shows it until it is given a
    /// text of its own, see <see cref="NameCaption"/>.</remarks>
    public string Name
    {
        get => _name;
        set => SetName(value, generated: false);
    }

    private string _name = string.Empty;

    /// <summary>The name was made by the form's name scope, not given.</summary>
    private bool _nameGenerated;

    /// <summary>A name the form's name scope made up for an element given none: it
    /// identifies the element, it isn't shown as a caption.</summary>
    internal void AssignGeneratedName(string name) => SetName(name, generated: true);

    private void SetName(string? value, bool generated)
    {
        value ??= string.Empty;

        string? caption = NameCaption;

        _nameGenerated = generated;

        if (!string.Equals(_name, value, StringComparison.Ordinal))
        {
            _name = value;

            if (StyleUsage.UsesIds) Restyle(subtree: false);
        }

        if (!string.Equals(caption, NameCaption, StringComparison.Ordinal)) OnNameCaptionChanged();
    }

    /// <summary>The caption a control with one shows while its own text was never set:
    /// the name given to it — markup's <c>x:Name</c>, or <see cref="Name"/> set in code —
    /// as a control the WinForms designer adds shows "label1" until it gets a text. Null
    /// for an element without a name of its own.</summary>
    /// <remarks>Only "never set" falls back: a text of null. An empty text set on purpose
    /// shows nothing, as does an element named only by its form.</remarks>
    protected internal string? NameCaption => !_nameGenerated && Name.Length > 0 ? Name : null;

    /// <summary>The name shown as a caption changed: a control showing it, its text not
    /// set, measures and draws again.</summary>
    protected virtual void OnNameCaptionChanged() { }

    /// <summary>The caption as a captioned control shows it — its text, or the name it
    /// falls back to; null for an element without one. What assistive technology reads.</summary>
    internal virtual string? ShownText => null;

    /// <summary>The backdrop color for the current state. Override it here rather
    /// than drawing the background by hand: the border and corner radius
    /// are picked up automatically.</summary>
    protected virtual Color CurrentBackground => Background;

    /// <summary>The border color for the current state.</summary>
    protected virtual Color CurrentBorderColor => BorderColor;

    public List<MenuItem>? ContextMenu { get; set; }
    /// <summary>The menu the element offers when <see cref="ContextMenu"/> is not set — a
    /// text field's Cut, Copy and Paste. Asked each time the menu opens, so its items show
    /// the element's state at that moment. Null — none; the search goes on up the tree.</summary>
    protected virtual List<MenuItem>? GetDefaultContextMenu() => null;

    internal List<MenuItem>? DefaultContextMenu() => GetDefaultContextMenu();

    // IGridPlaceable

    public Size DesiredSize { get; private set; }

    /// <summary>Whether the element accepts text input.</summary>
    /// <remarks>
    /// The flag lives here rather than in each control, because the form makes
    /// the keyboard decision based on focus. If a control requested the display
    /// itself, every future input would have to remember about mobile platforms —
    /// and one day it would forget.
    /// </remarks>
    public virtual bool AcceptsTextInput => false;

    public virtual SoftKeyboardKind SoftKeyboardKind => SoftKeyboardKind.Text;

    public bool IsHitTestVisible { get; set; } = true;
    /// <summary>Whether to accept drag-and-drop from the system. The receiver is
    /// searched from the hit element upward, so it is enough to enable it
    /// on a panel rather than on every descendant.</summary>
    public bool AllowDrop { get; set; }

    /// <summary>The cursor over the element. Default — inherited from ancestors.</summary>
    public CursorKind Cursor { get; set; } = CursorKind.Default;

    /// <summary>A cursor drawn from a picture over the element. Set, it wins over
    /// <see cref="Cursor"/> here, and descendants that set neither inherit it.</summary>
    public CursorImage? CursorImage { get; set; }

    /// <summary>The cursor over the element: the first one set on the way up, a picture
    /// or a kind; the arrow when none is.</summary>
    internal (CursorKind Kind, CursorImage? Image) EffectiveCursor
    {
        get
        {
            for (UIElement? current = this; current is not null; current = current.Parent)
            {
                if (current.CursorImage is { } image)
                    return (image.Fallback, image);

                if (current.Cursor != CursorKind.Default)
                    return (current.Cursor, null);
            }

            return (CursorKind.Arrow, null);
        }
    }

    /// <summary>The pointer is over the element: the <c>:hover</c> pseudo-class.</summary>
    protected bool IsHovered
    {
        get => GetBit(_pseudo, PseudoClass.Hover.Index);
        set => SetPseudoClass(PseudoClass.Hover, value);
    }

    /// <summary>The element is held down: the <c>:pressed</c> pseudo-class.</summary>
    protected bool IsPressed
    {
        get => GetBit(_pseudo, PseudoClass.Pressed.Index);
        set => SetPseudoClass(PseudoClass.Pressed, value);
    }

    /// <summary>The direction from the element itself, its ancestors, its form —
    /// and last, from the culture: a right-to-left language mirrors the layout
    /// of everything that set no direction of its own.</summary>
    public FlowDirection EffectiveFlowDirection =>
        GetInheritedValue(FlowDirectionProperty)
        ?? FindOwner()?.FlowDirection
        ?? Localization.LayoutDirection;

    public bool IsRightToLeft => EffectiveFlowDirection == Core.Text.FlowDirection.RightToLeft;

    public Image RenderToImage()
    {
        // layout is deferred until the frame, and a snapshot is an out-of-turn frame
        FindOwner()?.UpdateLayout();

        int width = (int)MathF.Ceiling(ActualSize.Width);
        int height = (int)MathF.Ceiling(ActualSize.Height);

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException(
                "The element has not been laid out yet (Size == 0). A snapshot can only be taken after a layout pass.");

        return ElementRenderer.Current.Render(this, width, height);
    }

    /// <summary>Opacity of the element together with its descendants: 1 — opaque,
    /// 0 — invisible and not drawn.</summary>
    /// <remarks>
    /// A styled property rather than a plain one: that way it gets transitions,
    /// enter and exit, and the theme. It doesn't affect layout — a transparent
    /// element still takes its place. Values outside [0; 1] are brought into
    /// range by the renderer itself: clamping them in the setter would break
    /// interpolation, which may overshoot for a moment.
    /// </remarks>
    [Styled(Category = "Appearance")]
    public partial float Opacity { get; set; }
    private static float OpacityDefault => 1f;

    /// <summary>How the element, with its descendants, is put over what is drawn under
    /// it: Normal — over it; Multiply, Screen, Overlay… — blended with it, as
    /// Avalonia's BitmapBlendingMode and CSS mix-blend-mode do.</summary>
    /// <remarks>
    /// Any mode but Normal draws the element into a layer of its own — a cost of its
    /// own, like an opacity below 1. What it blends with ends at the nearest ancestor
    /// with a layer: see <see cref="Drawing.BlendMode"/>. It doesn't affect layout or
    /// hit testing: a cut-out element still takes its clicks.
    /// </remarks>
    [Styled(Category = "Appearance")]
    public partial BlendMode BlendMode { get; set; }

    // === effects

    private EffectChain? _effects;

    /// <summary>Visual effects. Created on first access: most elements have
    /// no effects, and an extra object is of no use to them.</summary>
    public EffectChain Effects
    {
        get
        {
            if (_effects is not null) return _effects;

            _effects = new EffectChain();
            _effects.Changed += (_, _) => InvalidateVisual();

            return _effects;
        }
    }

    internal EffectChain? EffectsOrNull => _effects;

    /// <summary>The rectangle to redraw together with the element: the element
    /// itself plus a margin for the border and shadow that stick out of its bounds.</summary>
    public Rectangle DirtyBounds
    {
        get
        {
            // under a content transform — a ZoomBox scales and moves its child — the
            // place on screen doesn't add up from offsets: the whole box is redrawn,
            // the outermost one if they are nested. A hovered button inside a zoomed
            // box used to repaint the rectangle it would have had unzoomed
            UIElement? transformed = null;

            for (UIElement? current = Parent; current is not null; current = current.Parent)
                if (current is WrapControl { TransformsChild: true })
                    transformed = current;

            if (transformed is not null)
                return transformed.DirtyBounds;

            Point absolute = GetAbsolutePosition();

            return LocalDirtyBounds.Offset(absolute.X, absolute.Y);
        }
    }

    /// <summary>The same, but relative to its own top-left corner. The tree walk
    /// computes the absolute position itself, accumulating it on the way down:
    /// walking up to the root for every element is O(depth) per element
    /// and O(n·depth) per frame.</summary>
    internal Rectangle LocalDirtyBounds
    {
        get
        {
            var bounds = new Rectangle(Point.Empty, ActualSize);

            if (_effects is { IsEmpty: false })
            {
                Thickness bleed = _effects.TotalBleed(LocalBounds);

                bounds = new Rectangle(
                    new Point(bounds.X - bleed.Left, bounds.Y - bleed.Top),
                    new Size(
                        bounds.Width + bleed.Horizontal,
                        bounds.Height + bleed.Vertical));
            }

            if (Rotation != 0f)
            {
                // the bounding rectangle around the rotated one
                float radians = Math.Abs(Rotation) * MathF.PI / 180f;
                float cos = MathF.Abs(MathF.Cos(radians));
                float sin = MathF.Abs(MathF.Sin(radians));

                float w = ActualSize.Width * cos + ActualSize.Height * sin;
                float h = ActualSize.Width * sin + ActualSize.Height * cos;

                var center = new Point(
                    bounds.X + ActualSize.Width / 2f,
                    bounds.Y + ActualSize.Height / 2f);

                bounds = new Rectangle(
                    new Point(center.X - w / 2f, center.Y - h / 2f),
                    new Size(w, h));
            }

            if (ScaleX != 1f || ScaleY != 1f)
            {
                // scaling goes around the center, so the rectangle
                // grows on both sides of it
                Point center = Center;

                float left = center.X + (bounds.X - center.X) * ScaleX;
                float top = center.Y + (bounds.Y - center.Y) * ScaleY;

                bounds = new Rectangle(
                    new Point(left, top),
                    new Size(bounds.Width * ScaleX, bounds.Height * ScaleY));
            }

            if (TranslateX != 0f || TranslateY != 0f)
                bounds = bounds.Offset(TranslateX, TranslateY);

            if (BoxShadow is { } shadow)
            {
                float spread = shadow.Blur + shadow.Spread
                    + Math.Max(Math.Abs(shadow.OffsetX), Math.Abs(shadow.OffsetY));

                bounds = bounds.Inflate(spread);
            }

            // drawing of one's own past the bounds, such as a focus ring around an
            // indicator at the edge. Taken by its largest side and after the
            // transforms, like the shadow: a few pixels need no exact geometry
            Thickness overflow = VisualOverflow;
            float outset = Math.Max(
                Math.Max(overflow.Left, overflow.Right),
                Math.Max(overflow.Top, overflow.Bottom));

            if (outset > 0f)
                bounds = bounds.Inflate(outset);

            return bounds.Inflate(2f);   // margin for antialiasing and the border
        }
    }

    public abstract void Draw(Graphics g);

    protected virtual void OnAttached()
    {
        // called when element (first time?) added to form (and\or parent?)
    }

    protected virtual void OnSizeChanged()
    {
        // called when the arranged size actually changed; never on the first arrange
    }

    internal Form? FindOwner()
    {
        // the generation is read once and before the walk: if the tree changes
        // while we walk it, the cache is stored with the old generation
        // and gets recomputed on the next call instead of being trusted
        int generation = Volatile.Read(ref _ownerGeneration);

        if (_cachedOwnerGeneration == generation)
            return _cachedOwner;

        UIElement root = this;
        while (root.Parent is not null)
            root = root.Parent;

        _cachedOwner = root.Owner;
        _cachedOwnerGeneration = generation;

        return _cachedOwner;
    }

    protected virtual void OnDetached() { }

    internal void RaiseDetached()
    {
        OnDetached();
        Detached?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drawn after the descendants and outside their clip — scrollbars, borders on top.</summary>
    protected internal virtual void DrawOverlay(Graphics g) { }

    /// <summary>Take the hit for itself without descending to the descendants (the scrollbar area).</summary>
    protected internal virtual bool HitTestSelfFirst(Point localPoint) => false;
}

public static class UIElementExtensions
{
    /// <summary>Configure the element in place without breaking the expression.</summary>
    public static T With<T>(this T element, Action<T> configure) where T : UIElement
    {
        configure(element);
        return element;
    }

    /// <summary>Position in a Grid without breaking the expression.</summary>
    public static T At<T>(this T element, int row, int column) where T : UIElement
    {
        element.Row = row;
        element.Column = column;
        return element;
    }
}