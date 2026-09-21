using System.Runtime.CompilerServices;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Animation;

/// <summary>
/// Уходящий элемент: из панели он уже убран, но ещё дорисовывается,
/// пока не доиграет исчезание.
/// </summary>
/// <remarks>
/// Анимация висит на панели, а не на самом элементе: элемент к этому
/// моменту отвязан от формы, и часы его больше не видят. По той же
/// причине значения здесь не идут через систему свойств, а считаются
/// из прогресса прямо при отрисовке — у отвязанного элемента нет ни
/// владельца, ни кадров.
/// </remarks>
internal sealed class ExitingChild : IAnimation
{
    private readonly PanelControl _panel;
    private TimeSpan _elapsed;

    public ExitingChild(PanelControl panel, UIElement element, VisibilityTransition rule)
    {
        _panel = panel;
        Element = element;
        Rule = rule;
        Key = $"exit:{RuntimeHelpers.GetHashCode(element)}";
    }

    public UIElement Element { get; }

    public VisibilityTransition Rule { get; }

    /// <summary>Пройденная часть исчезания с учётом кривой: 0 — элемент
    /// выглядит как обычно, 1 — полностью ушёл.</summary>
    public float Progress { get; private set; }

    public object Target => _panel;

    public string Key { get; }

    public bool Advance(TimeSpan elapsed)
    {
        _elapsed += elapsed;

        float t = Rule.Duration <= TimeSpan.Zero
            ? 1f
            : Math.Clamp((float)(_elapsed / Rule.Duration), 0f, 1f);

        Progress = Rule.Easing(t);

        if (t < 1f) return true;

        _panel.RemoveExiting(this);
        return false;
    }

    public void Cancel(bool applyFinalValue) => _panel.RemoveExiting(this);
}