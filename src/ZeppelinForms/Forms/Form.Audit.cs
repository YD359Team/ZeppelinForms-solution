using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms;

/// <summary>The accessibility audit of the F12 inspector: the issues of the form
/// listed at the bottom and outlined in place.</summary>
/// <remarks>
/// Run when the inspector opens — the state of the form at that moment; reopen the
/// inspector to run it again. Choosing an issue shows its element in the property
/// grid, where the fix usually is: AccessibleName, a color, a size.
/// </remarks>
public partial class Form
{
    private const float AuditListHeight = 160f;

    private ListBox? _auditList;
    private AuditMarkers? _auditMarkers;
    private IReadOnlyList<AccessibilityIssue> _auditIssues = [];

    /// <summary>The issues found when the inspector was last opened.</summary>
    public IReadOnlyList<AccessibilityIssue> AccessibilityIssues => _auditIssues;

    private void ShowAudit()
    {
        _auditIssues = AccessibilityAudit.Run(this);

        // the outlines first: overlays are drawn in the order they come, and the
        // list and the grid must stay readable over them
        _auditMarkers = new AuditMarkers(_auditIssues)
        {
            Position = Point.Empty,
            Size = ClientSize,
            IsHitTestVisible = false,
            IsAccessibilityHidden = true,
        };

        AttachOverlay(_auditMarkers);

        _auditList = new ListBox
        {
            Position = new Point(0, Math.Max(0, ClientSize.Height - AuditListHeight)),
            Size = new Size(Math.Max(160, ClientSize.Width - InspectorWidth), AuditListHeight),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        if (_auditIssues.Count == 0)
            _auditList.Items.Add("No accessibility issues found");

        foreach (AccessibilityIssue issue in _auditIssues)
            _auditList.Items.Add(issue.ToString());

        _auditList.SelectionChanged += (_, _) =>
        {
            int index = _auditList.SelectedIndex;

            if (index < 0 || index >= _auditIssues.Count) return;

            UIElement element = _auditIssues[index].Element;

            if (_inspectorGrid is not null) _inspectorGrid.SelectedObject = element;

            InspectedElement = element;
            InvalidateVisual();
        };

        AttachOverlay(_auditList);
    }

    private void HideAudit()
    {
        if (_auditList is not null) DetachOverlay(_auditList);
        if (_auditMarkers is not null) DetachOverlay(_auditMarkers);

        _auditList = null;
        _auditMarkers = null;
    }

    private bool IsInsideAudit(Point point) =>
        _auditList is not null && Controls.Tools.HitTester.HitTest(_auditList, point) is not null;

    /// <summary>The outlines of the elements with issues, over the whole form.</summary>
    private sealed class AuditMarkers(IReadOnlyList<AccessibilityIssue> issues) : DecoratedControl
    {
        private static readonly Color Marker = new(255, 0xE8, 0x11, 0x23);

        protected override void DrawContent(Graphics g)
        {
            Point origin = GetAbsolutePosition();

            foreach (AccessibilityIssue issue in issues)
            {
                UIElement element = issue.Element;

                if (!element.IsEffectivelyVisible || element.FindOwner() is null) continue;

                Point position = element.GetAbsolutePosition();

                g.DrawRectangle(
                    new Rectangle(
                        new Point(position.X - origin.X - 1f, position.Y - origin.Y - 1f),
                        new Size(element.ActualSize.Width + 2f, element.ActualSize.Height + 2f)),
                    Marker,
                    2f);
            }
        }

        protected override Size MeasureOverride(Size availableSize) => ResolveSize(Size, availableSize);
    }
}