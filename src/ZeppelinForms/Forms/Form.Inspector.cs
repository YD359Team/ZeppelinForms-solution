using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Tools;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms;

/// <summary>The F12 inspector: the element under the pointer and its properties.</summary>
public partial class Form
{
    public bool IsInspectorEnabled { get; private set; }
    public UIElement? InspectedElement { get; private set; }

    private bool IsInsideInspector(Point point) =>
        (_inspectorGrid is not null && HitTester.HitTest(_inspectorGrid, point) is not null) ||
        IsInsideAudit(point);

    /// <summary>The width of the inspector's property grid at the right edge.</summary>
    private const float InspectorWidth = 340f;

    /// <summary>The room between the inspector's cards and the window's edges: they float
    /// over the form as cards, rather than being bolted to its side.</summary>
    private const float InspectorMargin = 8f;

    private PropertyGrid? _inspectorGrid;

    private void ToggleInspector()
    {
        IsInspectorEnabled = !IsInspectorEnabled;

        if (IsInspectorEnabled)
        {
            // the accessibility audit: its outlines go under the grid
            ShowAudit();

            // a card floating over the right edge, with the theme's shadow: the overlay
            // was a slab glued to the window's side, taller than it and without depth
            _inspectorGrid = new PropertyGrid
            {
                Size = new Size(InspectorWidth, Math.Max(120, ClientSize.Height - InspectorMargin * 2)),
                Position = new Point(Math.Max(0, ClientSize.Width - InspectorWidth - InspectorMargin), InspectorMargin),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                BoxShadow = Drawing.Primitives.BoxShadow.Large,
            };

            AttachOverlay(_inspectorGrid);
        }
        else if (_inspectorGrid is not null)
        {
            DetachOverlay(_inspectorGrid);
            _inspectorGrid = null;

            HideAudit();
        }

        Invalidate();
    }
}
