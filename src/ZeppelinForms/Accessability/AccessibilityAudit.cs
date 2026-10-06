using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Accessibility;

/// <summary>What the audit found wrong with an element.</summary>
public enum AccessibilityIssueKind
{
    /// <summary>A control a screen reader can't name: a button with an icon only,
    /// a field without a label.</summary>
    MissingName,

    /// <summary>A picture without a name: if it carries meaning, it needs one;
    /// if it is decoration, IsAccessibilityHidden.</summary>
    UnnamedImage,

    /// <summary>Text below the WCAG contrast minimum against what it is drawn on.</summary>
    LowTextContrast,

    /// <summary>A control smaller than 24×24 — hard to hit with a finger or
    /// an unsteady hand (WCAG 2.2, 2.5.8).</summary>
    SmallTarget,
}

/// <summary>One finding of the audit.</summary>
public sealed record AccessibilityIssue(UIElement Element, AccessibilityIssueKind Kind, string Message)
{
    public override string ToString() => $"{Kind}: {Message}";
}

/// <summary>Checks a form for the accessibility problems a machine can find: names,
/// contrast, target sizes. Run by the F12 inspector; usable from tests as well,
/// so that a form can be kept free of them.</summary>
/// <remarks>
/// It finds what is certainly wrong, not everything that is: whether a name says
/// what the control does, or whether the reading order makes sense, only a person
/// can tell. Disabled elements are not checked for contrast — WCAG exempts them,
/// and their dimmed look is meant.
/// </remarks>
public static class AccessibilityAudit
{
    /// <summary>The smallest target WCAG 2.2 accepts, in device-independent pixels.</summary>
    public const float MinimumTargetSize = 24f;

    /// <summary>Roles a screen reader can't do without a name for.</summary>
    private static readonly HashSet<AccessibilityRole> NamedRoles =
    [
        AccessibilityRole.Button, AccessibilityRole.ToggleButton, AccessibilityRole.SplitButton,
        AccessibilityRole.CheckBox, AccessibilityRole.RadioButton, AccessibilityRole.Switch,
        AccessibilityRole.TextBox, AccessibilityRole.SpinButton, AccessibilityRole.Slider,
        AccessibilityRole.ComboBox, AccessibilityRole.Link,
    ];

    public static IReadOnlyList<AccessibilityIssue> Run(Form form) =>
        form.Content is { } content ? Run(content) : [];

    public static IReadOnlyList<AccessibilityIssue> Run(UIElement root)
    {
        var issues = new List<AccessibilityIssue>();
        Visit(root, issues);

        return issues;
    }

    private static void Visit(UIElement element, List<AccessibilityIssue> issues)
    {
        if (!element.IsVisible || element.IsAccessibilityHidden) return;

        Check(element, issues);

        foreach (UIElement child in UIElementPeer.ChildElements(element))
            Visit(child, issues);
    }

    private static void Check(UIElement element, List<AccessibilityIssue> issues)
    {
        AccessibilityPeer? peer = element.GetAccessibilityPeer();

        if (peer is not null && NamedRoles.Contains(peer.Role) && peer.Name.Length == 0)
        {
            issues.Add(new(element, AccessibilityIssueKind.MissingName,
                $"{Describe(element)} has no accessible name: give it AccessibleName, a Label with Target, or a ToolTip"));
        }

        if (peer is { Role: AccessibilityRole.Image, Name.Length: 0 })
        {
            issues.Add(new(element, AccessibilityIssueKind.UnnamedImage,
                $"{Describe(element)} has no name: name it, or set IsAccessibilityHidden if it is decoration"));
        }

        if (element.IsEffectivelyEnabled && AccessibilityText.Own(element) is { Length: > 0 })
            CheckContrast(element, issues);

        bool interactive = element is IInputElement { TabStop: true } ||
            (peer?.Actions & (AccessibilityActions.Invoke | AccessibilityActions.Toggle)) != 0;

        if (interactive && element.IsEffectivelyEnabled &&
            (element.ActualSize.Width < MinimumTargetSize || element.ActualSize.Height < MinimumTargetSize))
        {
            issues.Add(new(element, AccessibilityIssueKind.SmallTarget,
                $"{Describe(element)} is {element.ActualSize.Width:0}×{element.ActualSize.Height:0}, " +
                $"less than {MinimumTargetSize:0}×{MinimumTargetSize:0}"));
        }
    }

    /// <summary>WCAG 2: 4.5:1 for text, 3:1 for large text — 24 px, or 18.66 px
    /// semibold and heavier.</summary>
    private static void CheckContrast(UIElement element, List<AccessibilityIssue> issues)
    {
        Color background = BackgroundBehind(element);
        Color text = Blend(element.TextColor, background);

        Drawing.Font font = element.EffectiveFont;
        bool large = font.Size >= 24f || (font.Size >= 18.66f && font.Weight >= Drawing.FontWeight.SemiBold);
        float minimum = large ? 3f : 4.5f;

        float ratio = text.ContrastRatio(background);

        if (ratio < minimum)
        {
            issues.Add(new(element, AccessibilityIssueKind.LowTextContrast,
                $"{Describe(element)}: text contrast {ratio:0.0}:1, less than {minimum:0.#}:1"));
        }
    }

    /// <summary>The color the element's text lies on: its own background and its
    /// ancestors', translucent ones blended over what is under them, down to the
    /// first opaque one — or the page of the theme, if there is none.</summary>
    private static Color BackgroundBehind(UIElement element)
    {
        var layers = new List<Color>();

        for (UIElement? current = element; current is not null; current = current.Parent)
        {
            Color fill = current is ButtonBase { BackgroundColor.A: > 0 } button
                ? button.BackgroundColor
                : current.Background;

            if (fill.A == 0) continue;

            layers.Add(fill);

            if (fill.A == 255) break;
        }

        Color result = layers.Count > 0 && layers[^1].A == 255
            ? layers[^1]
            : App.Theme.Colors.Background;

        for (int i = layers.Count - 1; i >= 0; i--)
        {
            if (layers[i].A < 255)
                result = Blend(layers[i], result);
        }

        return result;
    }

    /// <summary>A translucent color over an opaque one.</summary>
    private static Color Blend(Color top, Color under) =>
        top.A == 255 ? top : Color.Lerp(under, new Color(top.R, top.G, top.B), top.A / 255f);

    private static string Describe(UIElement element)
    {
        string type = element.GetType().Name;
        string? text = AccessibilityText.Own(element);

        return string.IsNullOrEmpty(text) ? type : $"{type} \"{text}\"";
    }
}