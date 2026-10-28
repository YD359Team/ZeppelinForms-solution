using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.DragDrop;
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>Input: the pointer, mouse, keyboard, focus and drag-and-drop events, the
/// hooks derived controls override and the gesture recognizers.</summary>
public abstract partial class UIElement
{
    public event EventHandler<PointerEventArgs>? PointerDown;
    public event EventHandler<PointerEventArgs>? PointerMove;
    public event EventHandler<PointerEventArgs>? PointerUp;
    public event EventHandler<PointerCancelEventArgs>? PointerCanceled;
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

    internal void RaiseMouseEnter(
    Point location, UIElement? from,
    KeyModifiers modifiers = KeyModifiers.None,
    PointerButtons buttons = PointerButtons.None)
    {
        if (IsHovered) return;

        IsHovered = true;

        var args = new MouseMoveEventArgs(location)
        {
            RelatedElement = from,
            Modifiers = modifiers,
            Buttons = buttons,
        };

        OnMouseEnter(args);
        MouseEnter?.Invoke(this, args);

        InvalidateVisual();
    }

    internal void RaiseMouseExit(
        Point location, UIElement? to,
        KeyModifiers modifiers = KeyModifiers.None,
        PointerButtons buttons = PointerButtons.None)
    {
        if (!IsHovered) return;

        IsHovered = false;

        var args = new MouseMoveEventArgs(location)
        {
            RelatedElement = to,
            Modifiers = modifiers,
            Buttons = buttons,
        };

        OnMouseExit(args);
        MouseExit?.Invoke(this, args);

        InvalidateVisual();
    }

    internal void RaiseMouseMove(Point location, KeyModifiers modifiers, PointerButtons buttons)
    {
        var args = new MouseMoveEventArgs(location) { Modifiers = modifiers, Buttons = buttons };

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

    /// <summary>Capture on behalf of a recognizer. A separate entry point because
    /// CaptureMouse is protected: it can't be called from outside the element,
    /// and the recognizer lives outside.</summary>
    internal void CaptureForGesture() => FindOwner()?.CaptureMouse(this);

    protected void CaptureMouse() => FindOwner()?.CaptureMouse(this);

    protected void ReleaseMouseCapture() => FindOwner()?.ReleaseMouseCapture(this);
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
}
