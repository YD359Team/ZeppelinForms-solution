using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>An image gallery: a list whose items the gallery draws, so each is
/// a peer of its own — named by its title and described by its description.</summary>
public class ImageGalleryPeer : UIElementPeer
{
    private readonly ImageGallery _gallery;
    private readonly Dictionary<GalleryItem, GalleryItemPeer> _peers = new(ReferenceEqualityComparer.Instance);

    public ImageGalleryPeer(ImageGallery owner) : base(owner)
    {
        _gallery = owner;

        owner.Items.CollectionChanged += (_, _) => RaiseStructureChanged();

        owner.SelectionChanged += (_, _) =>
        {
            if (FocusedDescendant is { } current)
                AccessibilityEvents.RaiseFocusChanged(current);
        };
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.List;

    protected override AccessibilityStates ControlStates =>
        _gallery.SelectionMode == SelectionMode.Single ? AccessibilityStates.None : AccessibilityStates.MultiSelectable;

    public override IReadOnlyList<AccessibilityPeer> Children
    {
        get
        {
            foreach (GalleryItem gone in _peers.Keys.Where(item => !_gallery.Items.Contains(item)).ToList())
                _peers.Remove(gone);

            return [.. _gallery.Items.Select(PeerFor)];
        }
    }

    /// <summary>The lead item, while the gallery has the focus.</summary>
    public override AccessibilityPeer? FocusedDescendant =>
        _gallery is { IsFocused: true, SelectedItem: { } item } ? PeerFor(item) : null;

    private GalleryItemPeer PeerFor(GalleryItem item) =>
        _peers.TryGetValue(item, out GalleryItemPeer? peer) ? peer : _peers[item] = new GalleryItemPeer(item, _gallery, this);
}

/// <summary>One picture of a gallery.</summary>
public class GalleryItemPeer : AccessibilityPeer
{
    private readonly GalleryItem _item;
    private readonly ImageGallery _gallery;
    private readonly ImageGalleryPeer _parent;

    internal GalleryItemPeer(GalleryItem item, ImageGallery gallery, ImageGalleryPeer parent)
    {
        _item = item;
        _gallery = gallery;
        _parent = parent;
    }

    private int Index => _gallery.Items.IndexOf(_item);

    public override AccessibilityRole Role => AccessibilityRole.ListItem;

    /// <summary>The title; the description for a picture without one; the number
    /// for a picture with neither — never nothing.</summary>
    public override string Name =>
        _item.Title is { Length: > 0 } title ? title
        : _item.Description is { Length: > 0 } description ? description
        : Localization.Get(ZfText.ImageNumber, Index + 1);

    public override string? Description =>
        _item.Title is { Length: > 0 } && _item.Description is { Length: > 0 } description ? description : null;

    public override AccessibilityStates States
    {
        get
        {
            AccessibilityStates states = AccessibilityStates.Selectable;
            int index = Index;

            if (_gallery.IsSelected(index)) states |= AccessibilityStates.Selected;
            if (_gallery.IsFocused && _gallery.SelectedIndex == index) states |= AccessibilityStates.Focused;
            if (!_gallery.IsEffectivelyEnabled) states |= AccessibilityStates.Disabled;
            if (_item.ThumbnailState == GalleryImageState.Loading) states |= AccessibilityStates.Busy;

            return states;
        }
    }

    public override AccessibilityActions Actions =>
        _gallery.IsEffectivelyEnabled
            ? AccessibilityActions.Select | AccessibilityActions.Invoke | AccessibilityActions.ScrollIntoView
            : AccessibilityActions.ScrollIntoView;

    public override SetPosition? Position => new(Index + 1, _gallery.Items.Count);

    public override AccessibilityPeer? Parent => _parent;

    public override IReadOnlyList<AccessibilityPeer> Children => [];

    public override Rectangle Bounds
    {
        get
        {
            int index = Index;

            if (index < 0) return default;

            Rectangle local = _gallery.ThumbnailBounds(index);
            Point origin = _gallery.GetAbsolutePosition();

            return new Rectangle(new Point(origin.X + local.X, origin.Y + local.Y), local.Size);
        }
    }

    public override Form? Form => _gallery.FindOwner();

    public override bool Select()
    {
        int index = Index;

        if (index < 0 || !_gallery.IsEffectivelyEnabled) return false;

        _gallery.SelectedIndex = index;
        _gallery.ScrollIntoView(index);
        return true;
    }

    /// <summary>Open the picture, as a double click does.</summary>
    public override bool Invoke()
    {
        int index = Index;

        if (index < 0 || !_gallery.IsEffectivelyEnabled) return false;

        _gallery.Activate(index);
        return true;
    }

    public override bool ScrollIntoView()
    {
        int index = Index;

        if (index < 0) return false;

        _gallery.ScrollIntoView(index);
        return true;
    }
}

/// <summary>An image viewer: a picture named by its title, its place in the list
/// as the value. A dialog when it has a close button: it shows over the window.</summary>
public class ImageViewerPeer : UIElementPeer
{
    private readonly ImageViewer _viewer;

    public ImageViewerPeer(ImageViewer owner) : base(owner)
    {
        _viewer = owner;

        owner.IndexChanged += (_, _) =>
        {
            RaisePropertyChanged(AccessibilityProperty.Name);
            RaisePropertyChanged(AccessibilityProperty.Value);
        };
    }

    protected override AccessibilityRole DefaultRole =>
        _viewer.ShowCloseButton ? AccessibilityRole.Dialog : AccessibilityRole.Image;

    protected override bool NameFromContent => true;

    protected override string? ContentName =>
        _viewer.CurrentItem is { } item
            ? item.Title is { Length: > 0 } title ? title : item.Description
            : null;

    public override string? Description =>
        _viewer.CurrentItem is { Title.Length: > 0, Description: { Length: > 0 } description }
            ? description
            : base.Description;

    public override string? Value =>
        _viewer.Items is { Count: > 0 } items && _viewer.Index >= 0
            ? Localization.Get(ZfText.ImageCounter, _viewer.Index + 1, items.Count)
            : null;
}