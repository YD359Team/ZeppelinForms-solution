using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Содержимое, зависящее от класса размера. Пересобирается только при
/// смене класса — то есть на повороте экрана или на настоящем переходе
/// через границу, а не при каждом движении рамки окна.
/// </summary>
public sealed class AdaptiveLayout : LayoutBuilder
{
    private Func<SizeClass, UIElement>? _content;

    public AdaptiveLayout() => RebuildKey = static size => Breakpoints.Classify(size);

    public Func<SizeClass, UIElement>? Content
    {
        get => _content;
        set
        {
            _content = value;

            Builder = value is null
                ? null
                : size => value(Breakpoints.Classify(size));

            Rebuild();
        }
    }
}