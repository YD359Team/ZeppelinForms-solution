namespace ZeppelinForms.Forms;

/// <summary>The form follows the application's text scale: every font is scaled
/// in EffectiveFont, and the form only has to measure everything again.</summary>
public partial class Form
{
    /// <summary>The scale at the moment the subscription was dropped; null — subscribed.</summary>
    private float? _textScaleAtUnsubscribe;

    /// <summary>Text got larger or smaller: no element's properties changed, only
    /// what its fonts measure, so the measure cache is reset for the whole tree.</summary>
    private void OnTextScaleChanged(object? sender, EventArgs e)
    {
        InvalidateMeasureTree();
        Invalidate();
    }

    private void RenewTextScaleSubscription()
    {
        App.TextScaleChanged -= OnTextScaleChanged;
        App.TextScaleChanged += OnTextScaleChanged;

        if (_textScaleAtUnsubscribe is { } scale && scale != App.TextScale)
            OnTextScaleChanged(null, EventArgs.Empty);

        _textScaleAtUnsubscribe = null;
    }

    private void DropTextScaleSubscription()
    {
        App.TextScaleChanged -= OnTextScaleChanged;
        _textScaleAtUnsubscribe = App.TextScale;
    }
}