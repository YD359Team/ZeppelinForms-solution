using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using ZeppelinForms.Animation;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Data;
using ZeppelinForms.Diagnostics;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Effects;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.DragDrop;
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Base element of any UI tree node
/// </summary>
public abstract partial class UIElement : IGridPlaceable, IBorderedElement, INotifyPropertyChanged
{
    // ===== events =====
    public event EventHandler<PointerEventArgs>? PointerDown;
    public event EventHandler<PointerEventArgs>? PointerMove;
    public event EventHandler<PointerEventArgs>? PointerUp;
    public event EventHandler<PointerCancelEventArgs>? PointerCanceled;
    /// <summary>The element is attached to a form. Needed by those who cannot
    /// work without one: animations, subscriptions to the window's life cycle.</summary>
    public event EventHandler? Attached;
    public event EventHandler? Detached;
    public event EventHandler<char>? TextInput;
    public event EventHandler? GotFocus;
    public event EventHandler? LostFocus;
    public event EventHandler<DragDropEventArgs>? DragEnter;
    public event EventHandler<DragDropEventArgs>? DragOver;
    public event EventHandler? DragLeave;
    public event EventHandler<DragDropEventArgs>? Drop;
    public event EventHandler<MouseClickEventArgs>? Click;
    public event EventHandler<MouseClickEventArgs>? DoubleClick;
    public event EventHandler<MouseClickEventArgs>? RightClick;
    public event EventHandler<MouseClickEventArgs>? MiddleClick;
    public event EventHandler<MouseButtonEventArgs>? MouseDown;
    public event EventHandler<MouseButtonEventArgs>? MouseUp;
    public event EventHandler<MouseMoveEventArgs>? MouseMove;
    public event EventHandler<MouseMoveEventArgs>? MouseEnter;
    public event EventHandler<MouseMoveEventArgs>? MouseExit;
    public event EventHandler<MouseWheelEventArgs>? MouseWheel;
    public event EventHandler<KeyEventArgs>? PreviewKeyDown;
    public event EventHandler<KeyEventArgs>? KeyDown;
    public event EventHandler<KeyEventArgs>? KeyUp;
    /// <summary>A styled property changed its value. Makes any element usable
    /// as a binding source: <c>label.Bind(Label.TextProperty, textBox, nameof(TextBox.Text))</c>.</summary>
    /// <remarks>
    /// The arguments are created only when someone is subscribed: the
    /// null-conditional call does not evaluate them otherwise, so elements
    /// nobody listens to pay nothing.
    /// </remarks>
    public event PropertyChangedEventHandler? PropertyChanged;

    //

    /// <summary>Bindings by property index. The dictionary is created on the first
    /// binding: most elements have no bindings at all.</summary>
    private Dictionary<int, Binding>? _bindings;

    private List<GestureRecognizer>? _gestures;

    /// <summary>This element's gesture recognizers. The list is created
    /// on first access: the vast majority of elements have no gestures,
    /// and an empty list for each of them adds up to megabytes
    /// on a tree of a thousand nodes.</summary>
    public IList<GestureRecognizer> GestureRecognizers => _gestures ??= [];

    /// <summary>The pipeline's hot path: asked on every press
    /// and must not allocate anything.</summary>
    internal IReadOnlyList<GestureRecognizer>? GestureRecognizersOrNull => _gestures;

    public T AddGesture<T>(T recognizer) where T : GestureRecognizer
    {
        recognizer.Element = this;
        GestureRecognizers.Add(recognizer);

        return recognizer;
    }

    /// <summary>A property controlled by a binding. The third value source,
    /// between the theme and explicit assignment: the theme does not override
    /// a binding, while an assignment from code overrides and breaks it.</summary>
    private ulong[]? _bound;

    public bool IsBound(StyledProperty property) => GetBit(_bound, property.Index);

    /// <summary>Bind a property to a property of the source.</summary>
    /// <remarks>The element holds the binding, the binding holds the source and looks
    /// at the element weakly — so a live model does not keep a closed window alive.</remarks>
    public Binding Bind<T>(
        StyledProperty<T> property,
        object source,
        string sourcePropertyName,
        BindingMode mode = BindingMode.OneWay)
    {
        ArgumentNullException.ThrowIfNull(source);

        PropertyInfo sourceProperty = source.GetType().GetProperty(sourcePropertyName)
            ?? throw new ArgumentException(
                $"{source.GetType().Name} has no property '{sourcePropertyName}'.",
                nameof(sourcePropertyName));

        // binding the same property again replaces the previous binding
        Unbind(property);

        var binding = new Binding(this, property, source, sourceProperty, mode);

        _bindings ??= [];
        _bindings[property.Index] = binding;

        SetBit(ref _bound, property.Index);

        binding.PushToTarget();

        return binding;
    }

    /// <summary>Break the property's binding.</summary>
    public void Unbind(StyledProperty property)
    {
        if (_bindings is null || !_bindings.Remove(property.Index, out Binding? binding))
            return;

        binding.Detach();
        ClearBit(_bound, property.Index);
    }

    /// <summary>Break all of the element's bindings.</summary>
    public void UnbindAll()
    {
        if (_bindings is null) return;

        foreach (Binding binding in _bindings.Values)
            binding.Detach();

        _bindings.Clear();
        _bound = null;
    }

    /// <summary>A write from a binding. Bypasses the explicit-assignment check,
    /// but is not itself considered explicit — otherwise the very first write
    /// from the source would close the property to all subsequent ones.</summary>
    internal void SetBoundValue(StyledProperty property, object? value)
    {
        property.WriteBoxedDirect(this, value);

        SetBit(ref _assigned, property.Index);

        // the element's own reaction goes before the invalidation,
        // see OnStyledPropertyChanged
        OnStyledPropertyChanged(property);

        if (property.AffectsLayout) Invalidate();
        else InvalidateVisual();

        RaisePropertyChanged(property);
    }

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

    /// <summary>Fill the background: with the gradient if one is set, otherwise
    /// with a solid color. Shared code for all three Decorated* bases.</summary>
    protected void FillBackground(Graphics g, Rectangle bounds)
    {
        if (HasBackgroundGradient)
        {
            g.FillGradient(bounds, CornerRadius, [.. BackgroundGradient!], BackgroundGradientAngle);
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

    [Styled(Category = "Text", AffectsLayout = true)]
    public partial Font? Font { get; set; }
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

    // ===== hooks for derived classes =====
    /// <summary>A drag entered the element's bounds.
    /// This is where highlighting is usually turned on and Effect is set.</summary>
    protected virtual void OnDragEnter(DragDropEventArgs e) { }

    /// <summary>The cursor moves inside the element. Called often,
    /// so heavy work does not belong here.</summary>
    protected virtual void OnDragOver(DragDropEventArgs e) { }

    /// <summary>The drag left or was cancelled. Always called if there was
    /// a DragEnter — including when the drop didn't happen.</summary>
    protected virtual void OnDragLeave() { }
    protected virtual void OnDrop(DragDropEventArgs e) { }
    protected virtual void OnMouseEnter(MouseMoveEventArgs e) { }
    protected virtual void OnMouseExit(MouseMoveEventArgs e) { }
    protected virtual void OnMouseMove(MouseMoveEventArgs e) { }
    protected virtual void OnMouseDown(MouseButtonEventArgs e) { }
    protected virtual void OnMouseUp(MouseButtonEventArgs e) { }
    protected virtual void OnClick(MouseClickEventArgs e) { }
    protected virtual void OnDoubleClick(MouseClickEventArgs e) { }
    protected virtual void OnRightClick(MouseClickEventArgs e) { }
    protected virtual void OnMiddleClick(MouseClickEventArgs e) { }
    protected virtual void OnMouseWheel(MouseWheelEventArgs e) { }
    /// <summary>A press before it reaches the hit element.
    /// Called from the root down to it, so an ancestor can step in earlier.</summary>
    protected virtual void OnPreviewMouseDown(MouseButtonEventArgs e) { }
    protected virtual void OnPreviewKeyDown(KeyEventArgs e) { }
    /// <summary>A release before it reaches the pressed element.
    /// Needed by ancestors that tracked the press through the preview.</summary>
    protected virtual void OnPreviewMouseUp(MouseButtonEventArgs e) { }
    /// <summary>A contact touched the element. Goes from the root to the target —
    /// before the compatibility mouse events are raised. Gesture recognizers
    /// hook in here: an ancestor needs to see the contact before a descendant,
    /// otherwise arbitration is impossible.</summary>
    protected virtual void OnPointerDown(PointerEventArgs e) { }

    /// <summary>The contact moved. Same order — from the root to the target.</summary>
    protected virtual void OnPointerMove(PointerEventArgs e) { }

    /// <summary>The contact was released.</summary>
    protected virtual void OnPointerUp(PointerEventArgs e) { }

    /// <summary>The interaction was cut off: there will be no completion, and
    /// everything started on the press must be rolled back, not committed.</summary>
    protected virtual void OnPointerCanceled(PointerCancelEventArgs e) { }
    protected virtual void OnKeyUp(KeyEventArgs e) { }
    protected virtual void OnTextInput(char c) { }
    /// <summary>Mouse movement before it reaches the hit element.
    /// Called from the root down to it — an ancestor can follow a drag
    /// over its descendants without taking over either the hit or the capture.</summary>
    protected virtual void OnPreviewMouseMove(MouseMoveEventArgs e) { }

    // ===== raising events =====
    internal void RaiseAttached()
    {
        OnAttached();
        Attached?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseDragEnter(DragDropEventArgs e)
    {
        OnDragEnter(e);
        DragEnter?.Invoke(this, e);
    }

    internal void RaiseDragOver(DragDropEventArgs e)
    {
        OnDragOver(e);
        DragOver?.Invoke(this, e);
    }

    internal void RaiseDragLeave()
    {
        OnDragLeave();
        DragLeave?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseDrop(DragDropEventArgs e)
    {
        OnDrop(e);
        Drop?.Invoke(this, e);
    }

    internal void RaisePreviewMouseMove(MouseMoveEventArgs e) => OnPreviewMouseMove(e);

    internal void RaisePreviewMouseUp(MouseButtonEventArgs e) => OnPreviewMouseUp(e);

    internal void RaiseTextInput(char c)
    {
        OnTextInput(c);
        TextInput?.Invoke(this, c);
    }

    internal void RaiseGotFocus()
    {
        OnGotFocus();
        GotFocus?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseLostFocus()
    {
        OnLostFocus();
        LostFocus?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseMouseEnter(Point location, UIElement? from)
    {
        if (IsHovered) return;

        IsHovered = true;

        var args = new MouseMoveEventArgs(location) { RelatedElement = from };

        OnMouseEnter(args);
        MouseEnter?.Invoke(this, args);

        InvalidateVisual();
    }

    internal void RaiseMouseExit(Point location, UIElement? to)
    {
        if (!IsHovered) return;

        IsHovered = false;

        var args = new MouseMoveEventArgs(location) { RelatedElement = to };

        OnMouseExit(args);
        MouseExit?.Invoke(this, args);

        InvalidateVisual();
    }

    internal void RaiseMouseMove(Point location)
    {
        var args = new MouseMoveEventArgs(location);

        OnMouseMove(args);
        MouseMove?.Invoke(this, args);
    }

    internal void RaiseMouseDown(MouseButtonEventArgs e)
    {
        // only the left button counts as pressed: right and middle don't "hold" a control
        if (e.Button == MouseButton.Left)
            IsPressed = true;

        OnMouseDown(e);
        MouseDown?.Invoke(this, e);

        InvalidateVisual();
    }

    internal void RaiseMouseUp(MouseButtonEventArgs e)
    {
        // no IsPressed check: the release is delivered only to the one that was
        // pressed and to the one that captured the mouse — routing in Form already
        // guarantees that. And the capturing one might never have been pressed:
        // DragList takes capture from OnPreviewMouseDown when the hit went
        // to a row, and previously simply never learned about the release
        if (e.Button == MouseButton.Left)
            IsPressed = false;

        OnMouseUp(e);
        MouseUp?.Invoke(this, e);
    }

    internal void RaiseClick(MouseClickEventArgs args)
    {
        switch (args.Button)
        {
            case MouseButton.Right:
                OnRightClick(args);
                RightClick?.Invoke(this, args);
                return;

            case MouseButton.Middle:
                OnMiddleClick(args);
                MiddleClick?.Invoke(this, args);
                return;
        }

        // a double click comes second: first a regular Click with Count=1,
        // then another one with Count=2 — system controls behave the same way
        if (args.Count >= 2)
        {
            OnDoubleClick(args);
            DoubleClick?.Invoke(this, args);

            if (args.Handled) return;
        }

        OnClick(args);
        Click?.Invoke(this, args);
    }

    internal void RaiseKeyDown(KeyEventArgs e)
    {
        OnKeyDown(e);
        KeyDown?.Invoke(this, e);
    }

    internal void RaiseKeyUp(KeyEventArgs e)
    {
        OnKeyUp(e);
        KeyUp?.Invoke(this, e);
    }

    internal void RaiseMouseWheel(MouseWheelEventArgs e)
    {
        OnMouseWheel(e);
        MouseWheel?.Invoke(this, e);
    }

    internal void RaisePointerDown(PointerEventArgs e)
    {
        OnPointerDown(e);
        PointerDown?.Invoke(this, e);
    }

    internal void RaisePointerMove(PointerEventArgs e)
    {
        OnPointerMove(e);
        PointerMove?.Invoke(this, e);
    }

    internal void RaisePointerUp(PointerEventArgs e)
    {
        OnPointerUp(e);
        PointerUp?.Invoke(this, e);
    }

    internal void RaisePointerCanceled(PointerCancelEventArgs e)
    {
        // the state is reset here rather than left to the handler: MouseUp
        // will never come after a cancel, and an element that forgot to reset
        // IsPressed itself would stay pressed for the rest of its life
        bool wasPressed = IsPressed;
        IsPressed = false;

        OnPointerCanceled(e);
        PointerCanceled?.Invoke(this, e);

        if (wasPressed) InvalidateVisual();
    }

    // ===================

    public UIElement? Parent
    {
        get;
        internal set
        {
            if (ReferenceEquals(field, value)) return;

            field = value;
            BumpOwnerGeneration();
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

    // ===== value source of styled properties =====

    // Values live in ordinary fields; only the source is here: two bits
    // per property — "was ever set" and "was set from user code".
    // The arrays are created on the first write: most elements have
    // at best one property out of fifty set explicitly
    private ulong[]? _assigned;
    private ulong[]? _local;

    /// <summary>The theme is being applied: setters mark writes as "from the theme",
    /// so that the theme doesn't overwrite values the user set explicitly.</summary>
    /// <remarks>ThreadStatic, for the same reason as the brush pool in SkiaGraphics:
    /// xunit snapshot tests run in parallel, and a shared flag would make setters
    /// on one thread treat values as theme values while the theme is being
    /// applied on another.</remarks>
    [ThreadStatic]
    internal static bool ApplyingTheme;

    private static bool GetBit(ulong[]? bits, int index) =>
        bits is not null && (index >> 6) < bits.Length && (bits[index >> 6] & (1UL << index)) != 0;

    private static void SetBit(ref ulong[]? bits, int index)
    {
        int word = index >> 6;

        if (bits is null) bits = new ulong[word + 1];
        else if (word >= bits.Length) Array.Resize(ref bits, word + 1);

        bits[word] |= 1UL << index;
    }

    private static void ClearBit(ulong[]? bits, int index)
    {
        if (bits is null || (index >> 6) >= bits.Length) return;

        bits[index >> 6] &= ~(1UL << index);
    }

    /// <summary>The value has been set: it is neither the default nor inherited.</summary>
    public bool HasValue(StyledProperty property) => GetBit(_assigned, property.Index);

    /// <summary>The value was set from code — the theme won't touch it anymore.</summary>
    public bool IsLocal(StyledProperty property) => GetBit(_local, property.Index);

    /// <summary>Write the control's own default. The theme overrides such a value,
    /// and user code all the more so. For constructors only: a regular assignment
    /// from them would mark the property as set manually and close it
    /// to the theme forever.</summary>
    protected void SetControlDefault<T>(StyledProperty<T> property, T value)
    {
        property.Write(this, value);

        SetBit(ref _assigned, property.Index);
        ClearBit(_local, property.Index);
    }

    /// <summary>A bridge for property editors: write the value the same way
    /// a regular assignment would.</summary>
    internal bool SetStyledValue<T>(StyledProperty<T> property, T value) =>
        SetValue(property, value);

    /// <summary>Write a value taking its source into account.
    /// false — the write was rejected: the theme is writing, and the property
    /// was set manually or is controlled by a binding.</summary>
    protected bool SetValue<T>(StyledProperty<T> property, ref T storage, T value)
    {
        // The ladder of sources, top to bottom: explicit assignment, binding,
        // theme, the control's default. The theme overrides neither the first nor the second.
        if (ApplyingTheme && (IsLocal(property) || IsBound(property))) return false;

        bool assigned = HasValue(property);

        if (assigned && EqualityComparer<T>.Default.Equals(storage, value))
        {
            // same value, but the source may have changed: the user assigned
            // exactly what the theme had already put there — and now it is theirs
            if (!ApplyingTheme) SetBit(ref _local, property.Index);

            return false;
        }

        // an explicit assignment from code is the top of the ladder. In OneWay
        // it breaks the binding: otherwise the next change of the source would
        // silently overwrite what the user wrote, and they wouldn't understand why.
        // In TwoWay the value goes to the source, and the binding stays.
        if (!ApplyingTheme && IsBound(property))
            PushOrBreakBinding(property, value);

        // the transition starts from the previous visible value — before the write
        BeginTransition(property, storage);
        storage = value;

        SetBit(ref _assigned, property.Index);

        if (ApplyingTheme) ClearBit(_local, property.Index);
        else SetBit(ref _local, property.Index);

        // the element's own reaction goes before the invalidation,
        // see OnStyledPropertyChanged
        OnStyledPropertyChanged(property);

        if (property.AffectsLayout) Invalidate();
        else InvalidateVisual();

        RaisePropertyChanged(property);
        return true;
    }

    /// <summary>For properties without a field of their own: the value lives
    /// in another object, and here we only route it. The write goes through
    /// the property's delegate, so no ref storage is needed.</summary>
    protected bool SetValue<T>(StyledProperty<T> property, T value)
    {
        if (ApplyingTheme && (IsLocal(property) || IsBound(property))) return false;

        bool assigned = HasValue(property);

        if (assigned && EqualityComparer<T>.Default.Equals(property.GetValue(this), value))
        {
            if (!ApplyingTheme) SetBit(ref _local, property.Index);
            return false;
        }

        if (!ApplyingTheme && IsBound(property))
            PushOrBreakBinding(property, value);

        BeginTransition(property, property.GetValue(this));
        property.Write(this, value);

        SetBit(ref _assigned, property.Index);

        if (ApplyingTheme) ClearBit(_local, property.Index);
        else SetBit(ref _local, property.Index);

        OnStyledPropertyChanged(property);

        if (property.AffectsLayout) Invalidate();
        else InvalidateVisual();

        RaisePropertyChanged(property);
        return true;
    }

    /// <summary>An explicit assignment to a bound property: TwoWay hands the value
    /// to the source, OneWay is broken.</summary>
    private void PushOrBreakBinding(StyledProperty property, object? value)
    {
        if (_bindings is null || !_bindings.TryGetValue(property.Index, out Binding? binding))
            return;

        if (binding.Mode == BindingMode.TwoWay)
            binding.PushToSource(value);
        else
            Unbind(property);
    }

    /// <summary>The value of a styled property changed. A hook for reactions that
    /// the write itself doesn't express: stop an animation on hide, reconcile
    /// dependent values, recompute a cache.</summary>
    /// <remarks>
    /// Called before the element requests layout or a redraw. A platform may
    /// paint right inside Invalidate — X11 does — and a reaction that runs after
    /// it leaves that frame drawn from stale state: Label measured its new text
    /// with the lines split from the old one and lagged one keystroke behind
    /// a bound TextBox.
    /// </remarks>
    protected virtual void OnStyledPropertyChanged(StyledProperty property) { }

    /// <summary>Tell outside subscribers that a styled property changed. Goes last,
    /// after the element's own reaction and the invalidation: by the time
    /// a subscriber reads the value, the element is already consistent.</summary>
    private void RaisePropertyChanged(StyledProperty property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property.Name));

    /// <summary>Forget the manually set value and hand control back to the theme.</summary>
    public void ClearValue<T>(StyledProperty<T> property)
    {
        // a binding is a value source too, and clearing removes it along with
        // the rest: otherwise the source would keep writing into a property
        // that is considered cleared
        Unbind(property);

        ClearBit(_local, property.Index);
        ClearBit(_assigned, property.Index);

        property.Write(this, property.DefaultValue);

        // the write above bypassed SetValue, so the element's reaction is called
        // by hand: without it the OnStyledPropertyChanged reactions (Label's line
        // cache, Loader's animation) would keep the state from before the clear.
        // It goes before the theme: if the theme sets a value of its own, that
        // write brings its own notification on top of a consistent element
        OnStyledPropertyChanged(property);

        // the theme may have a value of its own — ask it again
        App.Theme.Apply(this);

        if (property.AffectsLayout) Invalidate();
        else InvalidateVisual();

        RaisePropertyChanged(property);
    }

    /// <summary>The value taking inheritance into account.
    /// A value set manually — on the element itself or on any ancestor — beats
    /// what the theme gave this element. Otherwise panel.TextColor would never
    /// reach the nested labels: the theme sets their color on each of them individually.</summary>
    /// <remarks>
    /// A binding counts the same as a manual value: on the ladder of sources it
    /// stands above the theme. Previously only explicit assignment was checked,
    /// so an element's own bound value lost to an ancestor's explicit one,
    /// and a value bound on an ancestor did not reach its descendants at all.
    /// </remarks>
    public T GetInheritedValue<T>(StyledProperty<T> property)
    {
        for (UIElement? current = this; current is not null; current = current.Parent)
            if (current.IsLocal(property) || current.IsBound(property))
                return property.GetValue(current);

        if (HasValue(property))
            return property.GetValue(this);

        return property.DefaultValue;
    }

    /// <summary>Tell the binding that the property's value changed other than
    /// through assignment. Needed by controls that edit their state directly:
    /// typing in a TextBox changes the document, not the property,
    /// and the ladder of sources doesn't see such a change.</summary>
    /// <remarks>
    /// Outside subscribers are notified here as well: a binding that uses this
    /// element as its source learns about typing only from this call.
    /// </remarks>
    protected void NotifyBoundValueChanged(StyledProperty property, object? value)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property.Name));

        if (_bindings is null ||
            !_bindings.TryGetValue(property.Index, out Binding? binding) ||
            binding.Mode != BindingMode.TwoWay)
            return;

        binding.PushToSource(value);
    }

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

    /// <summary>The actual size after layout. This is what the element is drawn with.</summary>
    public Size ActualSize => _actualSize;
    public Rectangle Rectangle => new(Position, _actualSize);
    public Rectangle LocalBounds => new(Point.Empty, SanitizedSize);
    public Rectangle ContentBounds => new(
        new Point(Padding.Left, Padding.Top),
        new Size(
            NonNegative(SanitizedSize.Width - Padding.Horizontal),
            NonNegative(SanitizedSize.Height - Padding.Vertical)));
    /// <summary>The rectangle to clip descendants by. Usually equals
    /// ContentBounds; panels narrow it by the space for the scrollbars.</summary>
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
    public string Name { get; set; } = string.Empty;

    /// <summary>The backdrop color for the current state. Override it here rather
    /// than drawing the background by hand: the border and corner radius
    /// are picked up automatically.</summary>
    protected virtual Color CurrentBackground => Background;

    /// <summary>The border color for the current state.</summary>
    protected virtual Color CurrentBorderColor => BorderColor;

    public List<MenuItem>? ContextMenu { get; set; }
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

    internal CursorKind EffectiveCursor
    {
        get
        {
            for (UIElement? current = this; current is not null; current = current.Parent)
                if (current.Cursor != CursorKind.Default)
                    return current.Cursor;

            return CursorKind.Arrow;
        }
    }

    /// <summary>Own font, or if not set — the nearest one set on an ancestor,
    /// then the form's font, otherwise Font.Default.</summary>
    public Font EffectiveFont =>
        GetInheritedValue(FontProperty) ?? FindOwner()?.Font ?? Font.Default;

    protected bool IsHovered { get; set; }
    protected bool IsPressed { get; set; }

    /// <summary>Own direction, or if not set — inherited from ancestors,
    /// then from the form.</summary>
    public FlowDirection EffectiveFlowDirection =>
        GetInheritedValue(FlowDirectionProperty)
        ?? FindOwner()?.FlowDirection
        ?? Core.Text.FlowDirection.LeftToRight;

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

            return bounds.Inflate(2f);   // margin for antialiasing and the border
        }
    }

    public abstract void Draw(Graphics g);

    // ===== Transitions =====

    private List<Transition>? _transitionRules;
    private List<IPropertyTransition>? _running;

    [ThreadStatic] private static int s_presentationDepth;

    /// <summary>The global switch for transitions: for the system "reduce motion"
    /// setting and for tests that animation gets in the way of.</summary>
    public static bool TransitionsEnabled { get; set; } = true;

    /// <summary>Whether transitions run right now: they weren't turned off in code,
    /// and the system doesn't ask to reduce motion.</summary>
    internal static bool TransitionsActive => TransitionsEnabled && !Motion.IsReduced;

    /// <summary>This element's transition rules: which property gets to a new
    /// value and over what time.</summary>
    /// <remarks>
    /// The assignment stays instant. The property reads as the target — that is
    /// how code, bindings and PropertyGrid see it. The intermediate value
    /// is returned only inside drawing, through Presented.
    /// </remarks>
    public IList<Transition> Transitions => _transitionRules ??= [];

    /// <summary>Drawing is in progress: reading properties returns the intermediate
    /// transition values instead of the targets.</summary>
    internal static bool IsPresenting => s_presentationDepth > 0;

    /// <summary>Open a presentation scope. The tree walk holds it for the whole
    /// walk — nesting is needed in case an element snapshot is taken in the middle
    /// of someone else's drawing.</summary>
    internal static PresentationScope BeginPresentation()
    {
        s_presentationDepth++;

        return default;
    }

    internal readonly struct PresentationScope : IDisposable
    {
        public void Dispose() => s_presentationDepth--;
    }

    /// <summary>The value that should go into drawing: the intermediate one while
    /// a transition runs, and the target in all other cases. Called
    /// from generated getters.</summary>
    protected T Presented<T>(StyledProperty<T> property, T target)
    {
        if (!IsPresenting || _running is null) return target;

        foreach (IPropertyTransition running in _running)
            if (ReferenceEquals(running.Property, property))
                return ((PropertyTransition<T>)running).Current;

        return target;
    }

    /// <summary>Start a transition to a new value. Called before the write:
    /// it must start from what is visible now.</summary>
    private void BeginTransition<T>(StyledProperty<T> property, T from)
    {
        if (!TransitionsActive) return;
        if (_transitionRules is null || _transitionRules.Count == 0) return;

        // an element that hasn't been shown yet doesn't transition, it appears:
        // otherwise every form would open with all theme values sliding
        // from the defaults to the real ones
        if (!_hasBeenArranged) return;

        if (FindOwner() is not { } owner) return;

        Transition? rule = null;

        foreach (Transition candidate in _transitionRules)
            if (ReferenceEquals(candidate.Property, property))
            {
                rule = candidate;
                break;
            }

        if (rule is null) return;

        if (property.AffectsLayout)
        {
            // only drawing sees the intermediate value, while sizes are computed
            // from the target — a transition of such a property would look like
            // the picture and the layout falling out of sync
            ZfContract.Fail(
                $"A transition on {property.Name} is impossible: the property affects " +
                "layout. Animate what is drawn — color, " +
                "opacity, rotation.");

            return;
        }

        if (Interpolator.Find<T>() is not { } interpolate)
        {
            ZfContract.Fail(
                $"A transition on {property.Name} is impossible: there is no interpolator " +
                $"for {typeof(T).Name}. Register one with Interpolator.Register.");

            return;
        }

        // without a form there is nobody to drive the transition — the frame clock lives on it
        if (FindOwner() is null) return;

        // an interrupted transition continues from where it was
        // rather than jumping back to the old start
        StartTransition(property, PresentedOrTarget(property, from), rule);
    }

    /// <summary>The value visible right now: the intermediate one if a transition
    /// is already running, and the given one otherwise.</summary>
    private T PresentedOrTarget<T>(StyledProperty<T> property, T target)
    {
        if (_running is null) return target;

        foreach (IPropertyTransition running in _running)
            if (ReferenceEquals(running.Property, property))
                return ((PropertyTransition<T>)running).Current;

        return target;
    }

    /// <summary>Start a property transition from the given value to its current
    /// target. The target is taken from the property itself and read every frame.</summary>
    internal void StartTransition<T>(StyledProperty<T> property, T from, Transition rule)
    {
        if (FindOwner() is not { } owner) return;

        if (Interpolator.Find<T>() is not { } interpolate)
        {
            ZfContract.Fail(
                $"A transition on {property.Name} is impossible: there is no interpolator " +
                $"for {typeof(T).Name}. Register one with Interpolator.Register.");

            return;
        }

        var transition = new PropertyTransition<T>(this, property, from, rule, interpolate);

        _running ??= [];
        _running.Add(transition);

        // the previous transition of the same property is displaced by key, in the clock
        owner.AddAnimation(transition);
    }

    internal void RemoveTransition(IPropertyTransition transition)
    {
        if (_running is null) return;

        _running.Remove(transition);

        if (_running.Count == 0) _running = null;

        InvalidateVisual();
    }

    /// <summary>A transition frame: redraw the element without touching layout.</summary>
    internal void InvalidateTransitionVisual() => InvalidateVisual();

    private Point _arrangedPosition;
    private bool _skipLayoutTransition;
    private bool _skipEnterTransition;
    private bool _skipExitTransition;

    /// <summary>How the element appears on an already shown screen.
    /// null — instantly, as before.</summary>
    public VisibilityTransition? EnterTransition { get; set; }

    /// <summary>How the element disappears when it is removed from a panel.
    /// null — instantly, as before.</summary>
    public VisibilityTransition? ExitTransition { get; set; }

    internal VisibilityTransition? EffectiveEnterTransition =>
        EnterTransition ?? (Parent as PanelControl)?.ChildrenEnterTransition;

    /// <summary>The exit rule relative to a specific panel: asked at the moment
    /// of removal, when Parent may already be reset.</summary>
    internal VisibilityTransition? ExitTransitionIn(PanelControl panel) =>
        ExitTransition ?? panel.ChildrenExitTransition;

    /// <summary>Don't treat the next insertion and removal as appearing
    /// and disappearing. Needed by those who create and discard containers
    /// while scrolling: a row that rolled in from beyond the edge didn't
    /// appear — it just became visible.</summary>
    internal void SkipNextVisibilityTransitions()
    {
        _skipEnterTransition = true;
        _skipExitTransition = true;
    }

    /// <summary>Take the skip-exit mark: it is one-shot.</summary>
    internal bool ConsumeSkipExit()
    {
        bool skip = _skipExitTransition;
        _skipExitTransition = false;

        return skip;
    }

    /// <summary>Whether the element qualifies as exiting: only what was
    /// already on screen can disappear.</summary>
    internal bool CanAnimateExit => TransitionsActive && _hasBeenArranged && IsVisible;

    private void StartEnterTransition()
    {
        if (!TransitionsActive) return;
        if (EffectiveEnterTransition is not { } rule) return;
        if (!IsEffectivelyVisible) return;

        // from the invisible look to what lies in the properties: the properties
        // themselves are not touched, the model sees the final values from the first frame
        if (rule.Opacity != 1f)
            StartTransition(OpacityProperty, Opacity * rule.Opacity, rule.ForOpacity);

        if (rule.Scale != 1f)
        {
            StartTransition(ScaleXProperty, ScaleX * rule.Scale, rule.ForScaleX);
            StartTransition(ScaleYProperty, ScaleY * rule.Scale, rule.ForScaleY);
        }

        if (rule.OffsetX != 0f)
            StartTransition(TranslateXProperty, TranslateX + rule.OffsetX, rule.ForTranslateX);

        if (rule.OffsetY != 0f)
            StartTransition(TranslateYProperty, TranslateY + rule.OffsetY, rule.ForTranslateY);
    }

    /// <summary>Don't treat the next move as a move. Needed by those who
    /// reuse containers: a row rebound to another list item didn't move —
    /// it became a different row, and there is no point driving it
    /// across half the screen.</summary>
    internal void SkipNextLayoutTransition() => _skipLayoutTransition = true;

    /// <summary>How the element travels when layout puts it in a new place.
    /// null — instantly, as before.</summary>
    /// <remarks>
    /// The rule is taken from the element itself, or if it has none — from the
    /// parent panel: one line on a list turns on smooth rearrangement of all its
    /// rows, and the controls inside need to know nothing about it.
    /// </remarks>
    public LayoutTransition? LayoutTransition { get; set; }

    internal LayoutTransition? EffectiveLayoutTransition =>
        LayoutTransition ?? (Parent as PanelControl)?.ChildrenLayoutTransition;

    /// <summary>A move by the FLIP rule: the element already stands in its new
    /// place, but is drawn from the old one and travels to a zero offset.</summary>
    private void StartLayoutTransition(Point previous, Point placed)
    {
        if (!TransitionsActive) return;
        if (EffectiveLayoutTransition is not { } rule) return;

        // an invisible subtree doesn't travel: a page rearranged off screen
        // must open with everything already in place
        if (!IsEffectivelyVisible) return;

        float dx = previous.X - placed.X;
        float dy = previous.Y - placed.Y;

        // the duration may depend on how far to travel: a move by one row
        // and a move across the whole screen taking the same time
        // look wrong in different ways
        if (dx != 0f)
            StartTransition(
                TranslateXProperty,
                PresentedOrTarget(TranslateXProperty, TranslateX) + dx,
                rule.For(TranslateXProperty, dx));

        if (dy != 0f)
            StartTransition(
                TranslateYProperty,
                PresentedOrTarget(TranslateYProperty, TranslateY) + dy,
                rule.For(TranslateYProperty, dy));
    }

    // ===== Measure/Arrange =====

    private Size _measuredAgainst;
    private bool _measureValid;

    /// <summary>The measure cache. Turned off when you suspect an Invalidate
    /// is missing somewhere: comparing the behavior with and without
    /// the cache is the fastest way to confirm it.</summary>
    public static bool MeasureCacheEnabled { get; set; } = true;

    /// <summary>Check the cache instead of trusting it: the measurement is
    /// recomputed and compared with the stored one, and a mismatch is reported
    /// through ZfContract. A mismatch means the element's state changed without
    /// Invalidate. A debugging mode — twice as many measurements as without
    /// a cache at all.</summary>
    public static bool VerifyMeasureCache { get; set; }

    /// <remarks>
    /// Measuring again with the same constraint returns the same result —
    /// if the element's state hasn't changed. Whether it changed is told
    /// by Invalidate: it marks the element and all ancestors whose size
    /// is computed from it. Without this cache a full pass measured every
    /// element on every frame, although only a handful change per frame.
    ///
    /// The price is a requirement on controls: a property that affects size
    /// must call Invalidate. For [Styled] with AffectsLayout the property system
    /// does it itself, for ordinary properties — the property's author.
    /// </remarks>
    public void Measure(Size availableSize)
    {
        bool reusable = MeasureCacheEnabled
            && _measureValid
            && SameConstraint(_measuredAgainst, availableSize);

        if (reusable && !VerifyMeasureCache) return;

        Size measured = MeasureOverride(availableSize);

        if (reusable && measured != DesiredSize)
        {
            ZfContract.Fail(
                $"{GetType().Name} \"{Name}\": measuring gave {measured}, " +
                $"but the cache holds {DesiredSize}. So the element's state " +
                "changed without Invalidate — a property that affects size " +
                "must call it.");
        }

        DesiredSize = measured;
        _measuredAgainst = availableSize;
        _measureValid = true;
    }

    /// <summary>Mark the measurement stale on the element and its ancestors:
    /// their size is computed from ours.</summary>
    /// <remarks>
    /// The walk up stops at the first element that is already marked: if an
    /// element is invalid, so are all its ancestors — they were marked together with it.
    /// </remarks>
    internal void InvalidateMeasure()
    {
        for (UIElement? current = this; current is not null; current = current.Parent)
        {
            if (!current._measureValid) break;

            current._measureValid = false;
        }
    }

    /// <summary>Infinities compare as equal, and so do NaNs:
    /// "no constraint" and "auto axis" must match themselves,
    /// otherwise the cache would never hit.</summary>
    private static bool SameConstraint(Size a, Size b) =>
        (a.Width == b.Width || (float.IsNaN(a.Width) && float.IsNaN(b.Width)))
        && (a.Height == b.Height || (float.IsNaN(a.Height) && float.IsNaN(b.Height)));

    private bool _hasBeenArranged;

    /// <summary>The size of the previous arrange. OnSizeChanged compares with it rather
    /// than with ActualSize: the Size setter writes ActualSize ahead of layout,
    /// and a comparison with that would miss a real change.</summary>
    private Size _arrangedSize;

    public void Arrange(Rectangle finalRect)
    {
        // Dock.Fill is an explicit demand to take everything, it overrides alignment
        bool fill = Docking == Dock.Fill;

        bool stretchH = fill || HorizontalAlignment == HorizontalAlignment.Stretch;
        bool stretchV = fill || VerticalAlignment == VerticalAlignment.Stretch;

        float width = stretchH ? finalRect.Width : Math.Min(DesiredSize.Width, finalRect.Width);
        float height = stretchV ? finalRect.Height : Math.Min(DesiredSize.Height, finalRect.Height);

        float x = stretchH ? finalRect.X : HorizontalAlignment switch
        {
            HorizontalAlignment.Right => finalRect.X + finalRect.Width - width,
            HorizontalAlignment.Center => finalRect.X + (finalRect.Width - width) / 2f,
            _ => finalRect.X,
        };

        float y = stretchV ? finalRect.Y : VerticalAlignment switch
        {
            VerticalAlignment.Bottom => finalRect.Y + finalRect.Height - height,
            VerticalAlignment.Center => finalRect.Y + (finalRect.Height - height) / 2f,
            _ => finalRect.Y,
        };

        var placed = new Point(x, y);

        // the first arrange inside an already shown parent is an appearance:
        // the element was added to a screen that has already been seen.
        // A first arrange together with the parent is just the form opening,
        // there is no point animating it
        bool appearing = !_hasBeenArranged && Parent is { _hasBeenArranged: true };

        // compare with the place from the previous layout, not with Position:
        // a scrolling panel adjusts its children's Position after Arrange,
        // and a move by the scroll amount must not be animated
        Point previous = _arrangedPosition;
        bool moved = _hasBeenArranged && (previous.X != placed.X || previous.Y != placed.Y);

        Position = placed;
        _arrangedPosition = placed;

        // the layout result goes into ActualSize; Size stays what the user set,
        // otherwise auto-sizing would work only once
        _actualSize = ArrangeOverride(new Size(width, height));

        if (moved && !_skipLayoutTransition) StartLayoutTransition(previous, placed);

        _skipLayoutTransition = false;

        if (appearing && !_skipEnterTransition) StartEnterTransition();

        _skipEnterTransition = false;

        // the first pass only records the size: derived classes that react
        // to a change must not fire on the transition out of "not laid out".
        // Later passes call it only on an actual change: Arrange runs on every
        // layout pass, and most of them leave the size as it was
        if (!_hasBeenArranged)
            _hasBeenArranged = true;
        else if (_actualSize != _arrangedSize)
            OnSizeChanged();

        _arrangedSize = _actualSize;
    }

    public void Arrange(Point point, Size size) => Arrange(new Rectangle(point, size));

    public Point GetAbsolutePosition()
    {
        float x = 0, y = 0;

        for (UIElement? current = this; current is not null; current = current.Parent)
        {
            x += current.Position.X;
            y += current.Position.Y;
        }

        return new Point(x, y);
    }

    // Default for leaf controls that didn't override MeasureOverride:
    // if Size is set explicitly, use it, otherwise (Auto) assume "I want 0".
    protected virtual Size MeasureOverride(Size availableSize) =>
        ResolveSize(Size.Empty, availableSize);

    // Default — simply fill everything the parent gave ("stretch").
    protected virtual Size ArrangeOverride(Size finalSize) => finalSize;

    // Shared helper: an explicitly set Size axis beats contentSize,
    // an auto axis (NaN) takes the size computed from content, and neither
    // can exceed what the parent actually allotted.
    protected Size ResolveSize(Size contentSize, Size availableSize)
    {
        float w = _explicitSize.IsWidthAuto ? contentSize.Width : _explicitSize.Width;
        float h = _explicitSize.IsHeightAuto ? contentSize.Height : _explicitSize.Height;

        w = Math.Min(w, availableSize.Width);
        h = Math.Min(h, availableSize.Height);

        // infinity must not reach the sizes: it means "no constraint",
        // not "the element is infinite"
        return new Size(
            float.IsFinite(w) ? w : 0f,
            float.IsFinite(h) ? h : 0f);
    }

    // ===== mouse/focus events (unchanged) =====

    /// <summary>Redraw only this element, without recomputing layout.</summary>
    protected internal void InvalidateVisual()
    {
        if (!float.IsFinite(ActualSize.Width) || !float.IsFinite(ActualSize.Height))
            return;

        FindOwner()?.InvalidateRect(DirtyBounds);
    }

    // protected internal — available both to derived classes (as before) and to
    // code inside the assembly such as FocusDispatcher, which needs to request
    // a redraw without being a UIElement subclass.
    /// <summary>The geometry changed — a full recompute and redraw are needed.</summary>
    protected internal void Invalidate()
    {
        // first our own measure cache and the ancestors' caches, then the request
        // to the form: without this the form would recompute the layout with stale sizes
        InvalidateMeasure();

        FindOwner()?.Invalidate();
    }

    /// <summary>Capture on behalf of a recognizer. A separate entry point because
    /// CaptureMouse is protected: it can't be called from outside the element,
    /// and the recognizer lives outside.</summary>
    internal void CaptureForGesture() => FindOwner()?.CaptureMouse(this);

    protected void CaptureMouse() => FindOwner()?.CaptureMouse(this);

    protected void ReleaseMouseCapture() => FindOwner()?.ReleaseMouseCapture(this);

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
    protected virtual void OnGotFocus() { }
    protected virtual void OnLostFocus() { }

    internal void RaisePreviewMouseDown(MouseButtonEventArgs e) => OnPreviewMouseDown(e);

    internal void RaisePreviewKeyDown(KeyEventArgs e)
    {
        OnPreviewKeyDown(e);
        PreviewKeyDown?.Invoke(this, e);
    }

    /// <summary>Whether the control treats Space/Enter as a click (buttons, checkboxes).</summary>
    protected virtual bool IsKeyActivatable => false;

    protected virtual void OnKeyDown(KeyEventArgs e)
    {
        if (!IsKeyActivatable) return;

        if (e.Key is Key.Space or Key.Enter)
        {
            // a click "from its own center" — the coordinate is needed by those
            // who read it (ListBox, for example, determines the row from it)
            Point absolute = GetAbsolutePosition();
            var center = new Point(
                absolute.X + ActualSize.Width / 2f,
                absolute.Y + ActualSize.Height / 2f);

            RaiseClick(new MouseClickEventArgs(MouseButton.Left, MouseButtonState.Up, center, 1));
            e.Handled = true;
        }
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

public static class UIElementEx
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