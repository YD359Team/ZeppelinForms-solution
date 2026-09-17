using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Control for showing Image
/// </summary>
public class PictureBox : DecoratedControl
{
    public ImageFlip Flip { get; set; } = ImageFlip.None;
    public ImageLayout Layout { get; set; } = ImageLayout.Stretch;
    public string? Source { get; private set; }

    private Image? _image;

    /// <summary>Разобранные ресурсы живут до конца процесса: повторный
    /// LoadAsset той же картинки не должен декодировать её заново.
    /// Расплата — удержанная память: буфер пикселей не зависит от размера
    /// контрола, и картинка 2048×2048 занимает 16 МБ, даже если показана
    /// в квадрате сто на сто. Приложение, которое перебирает много крупных
    /// ресурсов, может освободить их через ClearAssetCache.</summary>
    private static readonly Dictionary<string, Image> AssetCache = [];

    /// <summary>Забыть разобранные ресурсы. Уже показываемые картинки
    /// остаются живыми: их держат сами контролы.</summary>
    public static void ClearAssetCache() => AssetCache.Clear();

    /// <summary>Показать уже готовое изображение: снимок другого элемента,
    /// результат обработки, кадр из видео.</summary>
    public void SetImage(Image? image)
    {
        _image = image;
        Source = null;
        Invalidate();
    }

    public void Load(string path)
    {
        _image = Image.LoadFromFile(path);
        Source = path;
        Invalidate();
    }

    public void LoadAsset(string relativePath)
    {
        if (!AssetCache.TryGetValue(relativePath, out Image? image))
        {
            image = Image.LoadAsset(relativePath);
            AssetCache[relativePath] = image;
        }

        _image = image;
        Source = relativePath;
        Invalidate();
    }

    protected override void DrawContent(Graphics g)
    {
        if (_image is not null)
            g.DrawImage(this.ContentBounds, _image, Flip, Layout);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size content = _image is not null
            ? new Size(_image.Width + Padding.Horizontal, _image.Height + Padding.Vertical)
            : Size.Empty;

        return ResolveSize(content, availableSize);
    }
}