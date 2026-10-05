using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

/// <summary>The text color of selected rows.</summary>
/// <remarks>
/// The list draws only the selection fill; the rows draw their own text, in the
/// color they inherit. Where a theme pairs a text color with the fill — high
/// contrast always does, its selection is the user's Highlight pair — the list
/// sets that color on the selected rows, as their own value, and clears it from
/// a row when it leaves the selection: the row then takes the theme's color again.
/// </remarks>
public partial class ListBox
{
    /// <summary>The text color of selected rows. Transparent — rows keep theirs.</summary>
    [Styled(Category = "Selection")]
    public partial Color SelectedTextColor { get; set; }
    private static Color SelectedTextColorDefault => Colors.Transparent;

    /// <summary>The rows that carry the selected text color now.</summary>
    private readonly HashSet<UIElement> _recolored = [];

    private void UpdateSelectedText()
    {
        Color color = SelectedTextColor;

        foreach (UIElement row in _recolored.ToList())
        {
            int index = Children.IndexOf(row);

            if (color.A > 0 && index >= 0 && _selected.Contains(index)) continue;

            row.ClearValue(TextColorProperty);
            _recolored.Remove(row);
        }

        if (color.A == 0) return;

        foreach (int index in _selected)
        {
            if (index >= Children.Count) continue;

            UIElement row = Children[index];
            row.TextColor = color;
            _recolored.Add(row);
        }
    }

    protected override void OnStyledPropertyChanged(StyledProperty property)
    {
        base.OnStyledPropertyChanged(property);

        // a theme switch brings another pair, or none
        if (property == SelectedTextColorProperty)
            UpdateSelectedText();
    }
}