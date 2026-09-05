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
    private static readonly Dictionary<string, Image> AssetCache = [];

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