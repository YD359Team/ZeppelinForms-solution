using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// A border with a header set into the top line — like GroupBox in WinForms.
/// </summary>
public partial class GroupBox : DecoratedWrapControl
{
    private const float HeaderSideGap = 8f;
    private const float HeaderTextPadding = 4f;

    public string? Header
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the header takes height and sets the minimum width
            Invalidate();
        }
    }

    [Styled(Category = "Header")]
    public partial Color HeaderColor { get; set; }
    private static Color HeaderColorDefault => Colors.Black;

    /// <summary>The header's offset from the left edge of the border.</summary>
    public float HeaderIndent
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the indent is part of the minimum width
            Invalidate();
        }
    } = 10f;

    public HorizontalContentAlignment HeaderAlign
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = HorizontalContentAlignment.Left;

    public GroupBox()
    {
        SetControlDefault(PaddingProperty, new(10, 8));
        SetControlDefault(BorderColorProperty, new Color(255, 200, 200, 200));
        SetControlDefault(BorderWidthProperty, 1f);
    }

    public GroupBox(UIElement child) : this()
    {
        Child = child;
    }

    private float HeaderHeight
    {
        get
        {
            if (string.IsNullOrEmpty(Header)) return 0;

            // height by a reference pair rather than by the text itself: otherwise
            // the border jumps when the header changes, because of ascenders and descenders
            return TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height;
        }
    }

    private float HeaderWidth => string.IsNullOrEmpty(Header)
        ? 0
        : TextMeasurer.Current.MeasureText(Header, EffectiveFont).Width;

    private Rectangle Frame
    {
        get
        {
            // the border starts at the middle of the header line — that way the text
            // is visually "set into" the line rather than hanging above it
            float top = HeaderHeight / 2f;

            return new Rectangle(
                new Point(BorderWidth / 2f, top),
                new Size(
                    Math.Max(0, ActualSize.Width - BorderWidth),
                    Math.Max(0, ActualSize.Height - top - BorderWidth / 2f)));
        }
    }

    /// <summary>The background is drawn by us, by the border's rectangle:
    /// the base would also fill the header strip above it.</summary>
    protected override Color CurrentBackground => Colors.Transparent;

    protected override void DrawContent(Graphics g)
    {
        if (Background.A > 0)
            g.FillRoundRectangle(Frame, CornerRadius, Background);
    }

    protected override void DrawDecoration(Graphics g)
    {
        Rectangle frame = Frame;

        if (BorderWidth > 0 && BorderColor.A > 0)
        {
            if (string.IsNullOrEmpty(Header))
                g.DrawRoundRectangle(frame, CornerRadius, BorderColor, BorderWidth);
            else
                DrawFrameWithGap(g, frame);
        }

        if (string.IsNullOrEmpty(Header)) return;

        g.DrawText(Header,
            new Rectangle(new Point(HeaderTextX(frame), 0), new Size(HeaderWidth, HeaderHeight)),
            HeaderColor, EffectiveFont,
            HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
    }

    // the base border doesn't fit: DrawDecoration draws it with a gap
    protected override Color CurrentBorderColor => Colors.Transparent;

    private float HeaderTextX(Rectangle frame) => HeaderAlign switch
    {
        HorizontalContentAlignment.Center => frame.X + (frame.Width - HeaderWidth) / 2f,
        HorizontalContentAlignment.Right => frame.X + frame.Width - HeaderWidth - HeaderIndent,
        _ => frame.X + HeaderIndent,
    };

    /// <summary>
    /// A border of four sides with a gap in the top line for the header:
    /// a whole rectangle would go right through the text.
    /// </summary>
    private void DrawFrameWithGap(Graphics g, Rectangle frame)
    {
        float textX = HeaderTextX(frame);

        float gapStart = textX - HeaderTextPadding;
        float gapEnd = textX + HeaderWidth + HeaderTextPadding;

        float y = frame.Y;
        float right = frame.X + frame.Width;
        float bottom = frame.Y + frame.Height;

        if (gapStart > frame.X)
            g.DrawLine(new Point(frame.X, y), new Point(gapStart, y), BorderColor, BorderWidth);

        if (gapEnd < right)
            g.DrawLine(new Point(gapEnd, y), new Point(right, y), BorderColor, BorderWidth);

        g.DrawLine(new Point(frame.X, y), new Point(frame.X, bottom), BorderColor, BorderWidth);
        g.DrawLine(new Point(right, y), new Point(right, bottom), BorderColor, BorderWidth);
        g.DrawLine(new Point(frame.X, bottom), new Point(right, bottom), BorderColor, BorderWidth);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        float headerHeight = HeaderHeight;

        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical - headerHeight));

        Size childDesired = Size.Empty;

        if (Child is not null)
        {
            Child.Measure(inner);
            childDesired = Child.DesiredSize;
        }

        // the width is no less than the header with its indents,
        // otherwise the text would stick out of the border
        float minWidth = HeaderWidth + HeaderIndent + HeaderSideGap * 2;

        return ResolveSize(
            new Size(
                Math.Max(childDesired.Width + Padding.Horizontal, minWidth),
                childDesired.Height + Padding.Vertical + headerHeight),
            availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is null) return finalSize;

        float headerHeight = HeaderHeight;

        Child.Arrange(new Rectangle(
            new Point(Padding.Left, Padding.Top + headerHeight),
            new Size(
                Math.Max(0, finalSize.Width - Padding.Horizontal),
                Math.Max(0, finalSize.Height - Padding.Vertical - headerHeight))));

        return finalSize;
    }
}