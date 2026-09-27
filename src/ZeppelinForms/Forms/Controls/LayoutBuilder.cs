using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Builds content knowing the allotted size. Allows changing the layout
/// depending on the available space without subscribing to size changes.
/// </summary>
public class LayoutBuilder : DecoratedWrapControl
{
    private Size _builtFor = Size.Empty;
    private object? _builtKey;
    private bool _hasBuilt;

    /// <summary>Receives the available size, returns the content.</summary>
    public Func<Size, UIElement>? Builder { get; set; }

    /// <summary>What counts as a reason to rebuild the content.
    /// Null — a size change larger than <see cref="RebuildThreshold"/>.</summary>
    /// <remarks>
    /// A threshold in size units is fine while rebuilding is cheap, and bad
    /// for adaptivity: when the window frame is dragged it fires almost every
    /// frame, and a rebuild replaces Child entirely — together with focus,
    /// scroll position and typed text. A key allows rebuilding only when what
    /// the layout actually depends on changes: the size class, the orientation,
    /// the number of columns that fit.
    /// </remarks>
    public Func<Size, object?>? RebuildKey { get; set; }

    /// <summary>
    /// How much the size must change for the content to be rebuilt.
    /// Protects against rebuilding on every pixel while the window frame is dragged.
    /// Has no effect when <see cref="RebuildKey"/> is set.
    /// </summary>
    public float RebuildThreshold { get; set; } = 1f;

    public event EventHandler? ContentRebuilt;

    public LayoutBuilder()
    {

    }

    public LayoutBuilder(UIElement child) : base(child)
    {

    }

    /// <summary>Rebuild the content forcibly — for example, after a change
    /// of the data the layout depends on.</summary>
    public void Rebuild()
    {
        _hasBuilt = false;
        Invalidate();
    }

    private bool NeedsRebuild(Size available, object? key)
    {
        if (!_hasBuilt) return true;

        // infinity comes from scrollable panels: building content
        // for it is meaningless, wait for a finite size
        if (!float.IsFinite(available.Width) && !float.IsFinite(available.Height))
            return false;

        // a key is set — the size by itself doesn't count as a reason
        if (RebuildKey is not null)
            return !Equals(key, _builtKey);

        return Math.Abs(available.Width - _builtFor.Width) >= RebuildThreshold
            || Math.Abs(available.Height - _builtFor.Height) >= RebuildThreshold;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        if (Builder is not null)
        {
            object? key = RebuildKey?.Invoke(inner);

            if (NeedsRebuild(inner, key))
            {
                _builtFor = inner;
                _builtKey = key;
                _hasBuilt = true;

                // assigning Child detaches the previous subtree
                // and attaches the new one through WrapControl by itself
                Child = Builder(inner);

                ContentRebuilt?.Invoke(this, EventArgs.Empty);
            }
        }

        return base.MeasureOverride(availableSize);
    }
}