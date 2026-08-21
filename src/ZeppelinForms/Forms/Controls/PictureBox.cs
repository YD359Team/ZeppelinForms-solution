using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Control for showing Image
/// </summary>
public class PictureBox : UnitControl
{
    public string Source { get; set; }

    private Image _image;

    public override void Draw(Graphics g)
    {
        // draw _image
    }
}
