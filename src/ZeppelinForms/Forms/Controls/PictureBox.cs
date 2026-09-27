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
    public ImageFlip Flip
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = ImageFlip.None;

    public ImageLayout Layout
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = ImageLayout.Stretch;

    public string? Source { get; private set; }

    private Image? _image;

    /// <summary>Parsed assets live until the process ends: a repeated
    /// LoadAsset of the same picture must not decode it again.
    /// The price is retained memory: the pixel buffer doesn't depend on
    /// the control's size, and a 2048×2048 picture takes 16 MB even when
    /// shown in a hundred-by-hundred square. An application that goes
    /// through many large assets can release them via ClearAssetCache.</summary>
    private static readonly Dictionary<string, Image> AssetCache = [];

    /// <summary>Forget the parsed assets. Pictures already being shown
    /// stay alive: the controls themselves hold them.</summary>
    public static void ClearAssetCache() => AssetCache.Clear();

    /// <summary>Show an already prepared image: a snapshot of another element,
    /// a processing result, a video frame.</summary>
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