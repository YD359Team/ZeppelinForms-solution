using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls.Docking;

/// <summary>Where a <see cref="DockItem"/> is in its host.</summary>
public enum DockState : byte
{
    /// <summary>In the host's layout: a tab of one of its groups.</summary>
    Docked,

    /// <summary>In a window of its own, over the layout, inside the host.</summary>
    Floating,

    /// <summary>A button on an edge of the host; the panel slides out while it is used.</summary>
    AutoHidden,

    /// <summary>Not shown. <see cref="DockHost.Show"/> puts it back where it was.</summary>
    Hidden,
}

/// <summary>Where an item docks against its target.</summary>
public enum DockPosition : byte
{
    /// <summary>A tab of the target's group.</summary>
    Tabbed,

    Left,
    Top,
    Right,
    Bottom,
}

/// <summary>An edge of a <see cref="DockHost"/>.</summary>
public enum DockSide : byte
{
    Left,
    Top,
    Right,
    Bottom,
}

/// <summary>
/// A panel of a <see cref="DockHost"/>: a title for its tab and an element to show. What it
/// is — a document, a tool window — is the application's, through <see cref="Tag"/>.
/// </summary>
/// <remarks>
/// <para><see cref="Id"/> names the item in a saved layout: <see cref="DockHost.LoadLayout"/>
/// puts the item of the same id where the layout says.</para>
/// <para>The <c>Can…</c> flags are the user's: they take the buttons and the menu items
/// away, and the methods of the host still do what they are told.</para>
/// </remarks>
public sealed class DockItem
{
    public DockItem(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Id = id;
        Title = id;
    }

    public DockItem(string id, string title, UIElement? content = null)
        : this(id)
    {
        Title = title;
        Content = content;
    }

    /// <summary>The name of the item in a saved layout, unique in its host.</summary>
    public string Id { get; }

    public string Title
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Host?.OnItemChanged(this, contentChanged: false);
        }
    }

    public IconSource? Icon
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Host?.OnItemChanged(this, contentChanged: false);
        }
    }

    /// <summary>The tab's tooltip.</summary>
    public string? ToolTip { get; set; }

    /// <summary>What the item shows: it fills its panel, whatever its alignment says. Only
    /// the selected item of a group is in the tree: the others are neither measured nor
    /// drawn, and keep their state.</summary>
    public UIElement? Content
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            UIElement? old = field;
            field = value;

            // the old one leaves the panel it was shown in; one the application has put
            // somewhere of its own stays there
            if (old is not null && DockHost.IsShownByHost(old)) DockHost.Release(old);

            Host?.OnItemChanged(this, contentChanged: true);
        }
    }

    /// <summary>The tab has a close button, and the menu a Close.</summary>
    public bool CanClose
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Host?.OnItemChanged(this, contentChanged: false);
        }
    } = true;

    /// <summary>The item is dragged out into a window of its own, and the menu has a Float.</summary>
    public bool CanFloat { get; set; } = true;

    /// <summary>The item is pinned to an edge, and the menu has an Auto hide.</summary>
    public bool CanAutoHide { get; set; } = true;

    /// <summary>Closed by the user, the item is hidden rather than taken out of the host:
    /// a tool window that a menu shows again.</summary>
    public bool HideOnClose { get; set; }

    /// <summary>Whatever the application ties to the item.</summary>
    public object? Tag { get; set; }

    /// <summary>The host the item is in; null — none.</summary>
    public DockHost? Host { get; internal set; }

    public DockState State { get; internal set; } = DockState.Hidden;

    public bool IsActive => Host?.ActiveItem == this;

    // ===== where it is, and where it was =====

    /// <summary>The group the item is a tab of, docked or floating.</summary>
    internal DockGroup? Group { get; set; }

    /// <summary>The edge it is pinned to while auto-hidden; the side it was docked on
    /// otherwise — the place it goes back to when its group is gone.</summary>
    internal DockSide Side { get; set; } = DockSide.Left;

    /// <summary>The group it was a tab of before it left: it goes back there while the
    /// group is in the layout.</summary>
    internal DockGroup? LastGroup { get; set; }

    /// <summary>Where it was before it was hidden.</summary>
    internal DockState LastState { get; set; } = DockState.Docked;

    internal Rectangle? LastFloatBounds { get; set; }

    /// <summary>How far the auto-hidden panel slides out; 0 — not set yet.</summary>
    internal float AutoHideExtent { get; set; }

    public override string ToString() => Title;
}

/// <summary>An item is about to close. Set <see cref="Cancel"/> to keep it — to ask about
/// unsaved changes first, say.</summary>
public sealed class DockItemClosingEventArgs(DockItem item) : EventArgs
{
    public DockItem Item { get; } = item;

    public bool Cancel { get; set; }
}
