using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing;

/// <summary>
/// Снимок содержимого элемента, снятый в обход канваса. Эффект рисует его
/// сам — столько раз и так, как ему нужно.
/// </summary>
/// <remarks>Владеет нативным изображением, поэтому освобождается явно:
/// ждать финализатора здесь означало бы держать поверхность кадра.</remarks>
public abstract class LayerCapture : IDisposable
{
    /// <summary>Область, из которой снят захват, в координатах канваса.</summary>
    public abstract Rectangle Bounds { get; }

    public abstract void Dispose();
}

/// <summary>Какие каналы пропустить при отрисовке захвата.</summary>
public enum ColorChannels
{
    All,
    Red,
    Green,
    Blue,

    /// <summary>Зелёный и синий — дополнение к красному.</summary>
    Cyan,
    Magenta,
    Yellow,
}

/// <summary>Как захват смешивается с тем, что уже нарисовано.</summary>
public enum CaptureBlend
{
    Normal,

    /// <summary>Осветление: две половины разделённых каналов складываются
    /// обратно в исходный цвет там, где они совпадают.</summary>
    Screen,
}