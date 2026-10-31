namespace ZeppelinForms.Forms;

/// <summary>The form follows the application's text scale: every font is scaled
/// in EffectiveFont, and the form only has to measure everything again.</summary>
/// <remarks>The subscription itself is kept with the theme's and the language's, in
/// <see cref="FollowApp"/> and <see cref="StopFollowingApp"/>.</remarks>
public partial class Form
{
    /// <summary>Text got larger or smaller: no element's properties changed, only
    /// what its fonts measure, so the measure cache is reset for the whole tree.</summary>
    private void OnTextScaleChanged(object? sender, EventArgs e)
    {
        InvalidateMeasureTree();
        Invalidate();
    }
}
