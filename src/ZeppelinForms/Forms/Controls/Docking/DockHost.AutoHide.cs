using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.Docking;

/// <summary>The edges' strips of auto-hidden items, the panel that slides out of one, and
/// the menu of a tab.</summary>
public partial class DockHost
{
    private const float StripPadding = 10f;
    private const float StripGap = 4f;
    private const float StripIconSize = 14f;

    /// <summary>The auto-hidden items pinned to an edge, in the order they came.</summary>
    public IReadOnlyList<DockItem> AutoHiddenItems(DockSide side) => _autoHidden[(int)side];

    /// <summary>The item slid out of its edge; null — none.</summary>
    internal DockItem? OpenAutoHideItem => _openAutoHide;

    private void OpenAutoHide(DockItem item)
    {
        _openAutoHide = item;
        _autoPanel ??= new DockAutoHidePanel(this);
        _autoPanel.Show(item);
    }

    /// <summary>Slide the open panel back.</summary>
    private void CloseAutoHide(bool sync)
    {
        if (_openAutoHide is null) return;

        _openAutoHide = null;
        _autoPanel?.Show(null);

        if (!sync) return;

        FixActive();
        SyncViews();
    }

    private void ToggleAutoHide(DockItem item)
    {
        if (ReferenceEquals(_openAutoHide, item))
        {
            CloseAutoHide(sync: true);
            return;
        }

        OpenAutoHide(item);
        SetActive(item);
        SyncViews();
    }

    // ===== the strips =====

    private float StripButtonLength(DockItem item)
    {
        float length = StripPadding * 2 + TextMeasurer.MeasureLine(item.Title, EffectiveFont).Width;

        if (item.Icon is not null) length += StripIconSize + 6f;

        return length;
    }

    /// <summary>The buttons of every strip, in the host's coordinates.</summary>
    private IEnumerable<(DockItem Item, DockSide Side, Rectangle Rect)> StripButtons()
    {
        Size size = ActualSize;
        Rectangle inner = InnerRect(size);

        for (int s = 0; s < 4; s++)
        {
            var side = (DockSide)s;
            bool vertical = side is DockSide.Left or DockSide.Right;
            float offset = (vertical ? inner.Y : inner.X) + StripGap;

            foreach (DockItem item in _autoHidden[s])
            {
                float length = StripButtonLength(item);

                Rectangle rect = side switch
                {
                    DockSide.Left => new Rectangle(0, offset, StripThickness, length),
                    DockSide.Right => new Rectangle(size.Width - StripThickness, offset, StripThickness, length),
                    DockSide.Top => new Rectangle(offset, 0, length, StripThickness),
                    _ => new Rectangle(offset, size.Height - StripThickness, length, StripThickness),
                };

                yield return (item, side, rect);

                offset += length + StripGap;
            }
        }
    }

    /// <summary>The strip of an edge, in the host's coordinates; empty — the edge has none.</summary>
    private Rectangle StripRect(DockSide side)
    {
        if (!HasStrip(side)) return Rectangle.Empty;

        Size size = ActualSize;

        return side switch
        {
            DockSide.Left => new Rectangle(0, 0, StripThickness, size.Height),
            DockSide.Right => new Rectangle(size.Width - StripThickness, 0, StripThickness, size.Height),
            DockSide.Top => new Rectangle(0, 0, size.Width, StripThickness),
            _ => new Rectangle(0, size.Height - StripThickness, size.Width, StripThickness),
        };
    }

    private bool OnStrip(Point local)
    {
        for (int s = 0; s < 4; s++)
            if (StripRect((DockSide)s).Contains(local)) return true;

        return false;
    }

    private DockItem? StripItemAt(Point local)
    {
        foreach ((DockItem item, _, Rectangle rect) in StripButtons())
            if (rect.Contains(local)) return item;

        return null;
    }

    protected override void DrawContent(Graphics g)
    {
        for (int s = 0; s < 4; s++)
            if (StripRect((DockSide)s) is { Width: > 0 } strip)
                g.FillRectangle(strip, HeaderColor);

        Font font = EffectiveFont;

        foreach ((DockItem item, DockSide side, Rectangle rect) in StripButtons())
        {
            bool open = ReferenceEquals(item, _openAutoHide);

            if (open || ReferenceEquals(item, _hoveredStripItem))
                g.FillRectangle(rect.Inflate(-2), ButtonHoverColor);

            // the open item's mark, on the edge that faces the layout
            if (open)
            {
                Rectangle mark = side switch
                {
                    DockSide.Left => new Rectangle(rect.Right - 3, rect.Y, 3, rect.Height),
                    DockSide.Right => new Rectangle(rect.X, rect.Y, 3, rect.Height),
                    DockSide.Top => new Rectangle(rect.X, rect.Bottom - 3, rect.Width, 3),
                    _ => new Rectangle(rect.X, rect.Y, rect.Width, 3),
                };

                g.FillRectangle(mark, AccentColor);
            }

            bool vertical = side is DockSide.Left or DockSide.Right;

            g.Save();

            // along a side the text runs down the strip, read with the head turned right
            if (vertical)
            {
                g.Translate(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
                g.Rotate(90f);
            }
            else
            {
                g.Translate(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
            }

            float length = vertical ? rect.Height : rect.Width;
            float x = -length / 2f + StripPadding;

            if (item.Icon is not null)
            {
                item.Icon.Draw(g, new Rectangle(x, -StripIconSize / 2f, StripIconSize, StripIconSize), TextColor);
                x += StripIconSize + 6f;
            }

            g.DrawText(item.Title, new Rectangle(x, -StripThickness / 2f, Math.Max(0, length / 2f - StripPadding - x), StripThickness),
                TextColor, font, HorizontalContentAlignment.Left, VerticalContentAlignment.Center);

            g.Restore();
        }
    }

    // ===== the menu of a tab =====

    /// <summary>What the menu of an item's tab offers: as far as its flags allow.</summary>
    internal List<MenuItem> MenuFor(DockItem item)
    {
        var menu = new List<MenuItem>();

        void Add(TextKey text, Action act)
        {
            var entry = new MenuItem { Text = Localization.Get(text) };
            entry.Click += (_, _) => act();
            menu.Add(entry);
        }

        if (item.State == DockState.Floating) Add(ZfText.DockBack, () => Redock(item));
        else if (item.CanFloat) Add(ZfText.DockFloat, () => Float(item));

        if (item.State == DockState.Docked && item.CanAutoHide) Add(ZfText.DockAutoHide, () => AutoHide(item));

        bool othersClose = item.Group?.Items.Any(other => !ReferenceEquals(other, item) && other.CanClose) == true;

        if (item.CanClose || othersClose)
        {
            if (menu.Count > 0) menu.Add(new MenuItem { IsSeparator = true });

            if (item.CanClose) Add(ZfText.DockClose, () => Close(item));

            if (othersClose)
            {
                Add(ZfText.DockCloseOthers, () =>
                {
                    foreach (DockItem other in item.Group?.Items.ToArray() ?? [])
                        if (!ReferenceEquals(other, item) && other.CanClose) Close(other);
                });
            }
        }

        return menu;
    }
}
