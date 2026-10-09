using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Text;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>A Markdown viewer: a group whose children are the document's own
/// structure — headings with their levels, paragraphs, lists, tables, links — so
/// that a screen reader can go by headings or by links, as on a web page.</summary>
public class MarkdownViewerPeer : UIElementPeer
{
    private readonly MarkdownViewer _viewer;
    private readonly Dictionary<MarkdownNode, MarkdownNodePeer> _peers = new(ReferenceEqualityComparer.Instance);
    private MarkdownLayout? _builtFor;

    public MarkdownViewerPeer(MarkdownViewer owner) : base(owner)
    {
        _viewer = owner;

        owner.LayoutChanged += (_, _) => RaiseStructureChanged();

        owner.FocusedLinkChanged += (_, _) =>
        {
            if (FocusedDescendant is { } link)
                AccessibilityEvents.RaiseFocusChanged(link);
        };
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.Group;

    public override IReadOnlyList<AccessibilityPeer> Children
    {
        get
        {
            MarkdownLayout? layout = _viewer.Layout;

            if (layout is null) return [];

            // a new layout is a new tree: the old peers describe nodes that are gone
            if (!ReferenceEquals(layout, _builtFor))
            {
                _peers.Clear();
                _builtFor = layout;
            }

            return [.. layout.Root.Children.Select(node => PeerFor(node, this))];
        }
    }

    /// <summary>The link the keyboard is on.</summary>
    public override AccessibilityPeer? FocusedDescendant
    {
        get
        {
            int link = _viewer.FocusedLink;

            if (link < 0 || _viewer.Layout is not { } layout) return null;

            return Find(layout.Root, node => node.Link == link) is { } found ? PeerFor(found, ParentOf(layout.Root, found)) : null;
        }
    }

    internal MarkdownNodePeer PeerFor(MarkdownNode node, AccessibilityPeer parent) =>
        _peers.TryGetValue(node, out MarkdownNodePeer? peer) ? peer : _peers[node] = new MarkdownNodePeer(node, _viewer, this, parent);

    private static MarkdownNode? Find(MarkdownNode root, Func<MarkdownNode, bool> match)
    {
        foreach (MarkdownNode child in root.Children)
        {
            if (match(child)) return child;
            if (Find(child, match) is { } deeper) return deeper;
        }

        return null;
    }

    /// <summary>The peer of the node's parent: the viewer for a top-level node.</summary>
    private AccessibilityPeer ParentOf(MarkdownNode root, MarkdownNode target)
    {
        AccessibilityPeer Walk(MarkdownNode node, AccessibilityPeer peer)
        {
            foreach (MarkdownNode child in node.Children)
            {
                if (ReferenceEquals(child, target)) return peer;

                if (Find(child, n => ReferenceEquals(n, target)) is not null)
                    return Walk(child, PeerFor(child, peer));
            }

            return peer;
        }

        return Walk(root, this);
    }
}

/// <summary>A node of a Markdown document: a heading, a paragraph, a list item,
/// a cell, a link.</summary>
public class MarkdownNodePeer : AccessibilityPeer
{
    private readonly MarkdownNode _node;
    private readonly MarkdownViewer _viewer;
    private readonly MarkdownViewerPeer _root;
    private readonly AccessibilityPeer _parent;

    internal MarkdownNodePeer(MarkdownNode node, MarkdownViewer viewer, MarkdownViewerPeer root, AccessibilityPeer parent)
    {
        _node = node;
        _viewer = viewer;
        _root = root;
        _parent = parent;
    }

    public override AccessibilityRole Role => _node.Role;

    public override string Name => _node.Name;

    public override int HeadingLevel => _node.Role == AccessibilityRole.Heading ? _node.Level : 0;

    public override string? Value =>
        _node.Link >= 0 && _viewer.Layout is { } layout && _node.Link < layout.Links.Count
            ? layout.Links[_node.Link].Url
            : null;

    public override AccessibilityStates States
    {
        get
        {
            AccessibilityStates states = AccessibilityStates.None;

            if (_node.IsChecked == true) states |= AccessibilityStates.Checked;

            if (_node.Link >= 0)
            {
                states |= AccessibilityStates.Focusable;
                if (_viewer.FocusedLink == _node.Link) states |= AccessibilityStates.Focused;
            }

            return states;
        }
    }

    public override AccessibilityActions Actions =>
        _node.Link >= 0
            ? AccessibilityActions.Invoke | AccessibilityActions.ScrollIntoView
            : AccessibilityActions.ScrollIntoView;

    public override AccessibilityPeer? Parent => _parent;

    public override IReadOnlyList<AccessibilityPeer> Children =>
        [.. _node.Children.Select(child => _root.PeerFor(child, this))];

    public override Rectangle Bounds
    {
        get
        {
            Rectangle content = _node.Bounds;

            // a link inside a paragraph is where its first piece is
            if (_node.Link >= 0 && _viewer.Layout is { } layout && _node.Link < layout.Links.Count
                && layout.Links[_node.Link].Rects is [var first, ..])
                content = first;

            Rectangle local = _viewer.FromContent(content);
            Point origin = _viewer.GetAbsolutePosition();

            return new Rectangle(new Point(origin.X + local.X, origin.Y + local.Y), local.Size);
        }
    }

    public override Form? Form => _viewer.FindOwner();

    public override bool Invoke()
    {
        if (_node.Link < 0 || !_viewer.IsEffectivelyEnabled) return false;

        _viewer.Activate(_node.Link);
        return true;
    }

    public override bool ScrollIntoView()
    {
        Rectangle content = _node.Bounds;

        _viewer.ScrollTo(_viewer.ScrollX, content.Y);
        return true;
    }
}