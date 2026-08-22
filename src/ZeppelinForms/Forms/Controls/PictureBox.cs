using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Control for showing Image
/// </summary>
public class PictureBox : UnitControl
{
    private Image? _image;

    public string? Source { get; private set; }

    public PictureBox() => Size = new Size(100, 100);

    public void Load(string path)
    {
        _image = Image.LoadFromFile(path);
        Source = path;
        Invalidate();
    }

    public override void Draw(Graphics g)
    {
        if (_image is not null)
            g.DrawImage(this.LocalBounds, _image);
    }
}