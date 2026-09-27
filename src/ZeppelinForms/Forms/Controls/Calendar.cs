using System.Globalization;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public partial class Calendar : DecoratedControl
{
    /// <summary>The culture for the month name, the day captions and the first
    /// day of the week. Null — the thread's current culture.</summary>
    public CultureInfo? Culture
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            field = value;
            _dayNamesCulture = null;
            Invalidate();
        }
    }

    private CultureInfo EffectiveCulture => Culture ?? CultureInfo.CurrentCulture;

    // the captions are computed every frame, while a culture change is a rare event
    private CultureInfo? _dayNamesCulture;
    private string[]? _abbreviatedDayNames;
    private string[]? _shortestDayNames;

    // the reference grid: the sizes at which the calendar reads without compression
    private const float ReferenceHeaderHeight = 28f;
    private const float ReferenceDayOfWeekHeight = 20f;
    private const float ReferenceArrowWidth = 28f;
    private const float ReferenceCellWidth = 36f;
    private const float ReferenceCellHeight = 24f;

    private const int Rows = 6;
    private const int Columns = 7;

    /// <summary>We don't go below this compression: a smaller grid stops being
    /// readable at all, and it is more honest to cut it off at the edge
    /// than to draw something indistinguishable.</summary>
    private const float MinimumScale = 0.6f;

    /// <summary>The geometry of one frame.</summary>
    /// <remarks>
    /// One source for drawing and for hit testing. Before this, DrawContent,
    /// OnClick and HitFromPoint kept their own copies of the same numbers,
    /// and any size change had to be made in three places in sync —
    /// and with scaling they would have drifted apart silently.
    /// </remarks>
    private readonly record struct CalendarLayout(
        float Scale,
        float HeaderHeight,
        float DayOfWeekHeight,
        float ArrowWidth,
        Size Cell,
        Font Font)
    {
        public float GridTop => HeaderHeight + DayOfWeekHeight;
    }

    private CalendarLayout GetLayout()
    {
        Rectangle content = this.ContentBounds;

        float width = Math.Max(0, content.Width);
        float height = Math.Max(0, content.Height);

        float scale = width <= 0 || height <= 0
            ? 1f
            : Math.Clamp(
                Math.Min(
                    width / (Columns * ReferenceCellWidth),
                    height / (ReferenceHeaderHeight + ReferenceDayOfWeekHeight + Rows * ReferenceCellHeight)),
                MinimumScale,
                1f);

        float header = ReferenceHeaderHeight * scale;
        float dayOfWeek = ReferenceDayOfWeekHeight * scale;

        return new CalendarLayout(
            scale,
            header,
            dayOfWeek,
            ReferenceArrowWidth * scale,
            // Max: with a height smaller than the header the division would give
            // a negative cell, and the rows would drift upward
            new Size(width / Columns, Math.Max(0, (height - header - dayOfWeek) / Rows)),
            this.EffectiveFont.WithSize(this.EffectiveFont.Size * scale));
    }

    private DateTime _displayMonth = DateTime.Today;

    public DateTime? SelectedDate { get; private set; }
    public event EventHandler<DateTime>? DateSelected;

    private int _hoveredCell = -1;
    private int _hoveredHeaderButton;   // -1 — back, 1 — forward, 0 — none

    [Styled(Category = "Calendar")]
    public partial Color MutedColor { get; set; }
    private static Color MutedColorDefault => new(255, 160, 160, 160);

    [Styled(Category = "Calendar")]
    public partial Color SelectionColor { get; set; }
    private static Color SelectionColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    [Styled(Category = "Calendar")]
    public partial Color TodayColor { get; set; }
    private static Color TodayColorDefault => new(255, 220, 235, 255);

    [Styled(Category = "Calendar")]
    public partial Color HoverColor { get; set; }
    private static Color HoverColorDefault => new(255, 235, 242, 255);

    [Styled(Category = "Calendar")]
    public partial Color HeaderHoverColor { get; set; }
    private static Color HeaderHoverColorDefault => new(255, 228, 228, 228);

    public Calendar()
    {
        SetControlDefault(BackgroundProperty, Colors.White);
        Size = new Size(252, 220);
    }

    private DateTime FirstCellDate
    {
        get
        {
            var first = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);

            // the first day of the week is set by the culture: Monday in Russia,
            // Sunday in the US, Saturday in some Arab countries
            int shift = (((int)first.DayOfWeek - (int)EffectiveCulture.DateTimeFormat.FirstDayOfWeek) + 7) % 7;

            return first.AddDays(-shift);
        }
    }

    /// <summary>Day captions, starting from the culture's first day of the week.</summary>
    /// <remarks>
    /// In .NET the day arrays always start with Sunday regardless of the culture,
    /// so they must be rotated to its first day — otherwise the captions
    /// would not match the columns.
    /// </remarks>
    private string[] GetDayNames(bool shortest)
    {
        CultureInfo culture = EffectiveCulture;

        if (!ReferenceEquals(_dayNamesCulture, culture))
        {
            DateTimeFormatInfo format = culture.DateTimeFormat;

            _abbreviatedDayNames = Rotate(format.AbbreviatedDayNames, format.FirstDayOfWeek);
            _shortestDayNames = Rotate(format.ShortestDayNames, format.FirstDayOfWeek);
            _dayNamesCulture = culture;
        }

        return (shortest ? _shortestDayNames : _abbreviatedDayNames)!;

        static string[] Rotate(string[] source, DayOfWeek firstDay)
        {
            var names = new string[Columns];
            int start = (int)firstDay;

            for (int i = 0; i < Columns; i++)
                names[i] = source[(start + i) % 7];

            return names;
        }
    }

    // the background, border and corner radius are drawn by the base — only the date grid here
    protected override void DrawContent(Graphics g)
    {
        var content = this.ContentBounds;
        CalendarLayout layout = GetLayout();
        Font font = layout.Font;

        // the arrow highlight goes before the text, otherwise the fill covers the glyph
        if (_hoveredHeaderButton == -1)
        {
            g.FillRectangle(
                new Rectangle(new Point(content.X, content.Y), new Size(layout.ArrowWidth, layout.HeaderHeight)),
                HeaderHoverColor);
        }
        else if (_hoveredHeaderButton == 1)
        {
            g.FillRectangle(
                new Rectangle(
                    new Point(content.X + content.Width - layout.ArrowWidth, content.Y),
                    new Size(layout.ArrowWidth, layout.HeaderHeight)),
                HeaderHoverColor);
        }

        // header: ‹ Month Year ›
        g.DrawText("‹",
            new Rectangle(new Point(content.X, content.Y), new Size(layout.ArrowWidth, layout.HeaderHeight)),
            TextColor, font, HorizontalContentAlignment.Center, VerticalContentAlignment.Center);

        g.DrawText("›",
            new Rectangle(
                new Point(content.X + content.Width - layout.ArrowWidth, content.Y),
                new Size(layout.ArrowWidth, layout.HeaderHeight)),
            TextColor, font, HorizontalContentAlignment.Center, VerticalContentAlignment.Center);

        // the line height is taken from a reference pair rather than the text itself:
        // otherwise the center wanders because of ascenders and descenders
        float lineHeight = TextMeasurer.Current.MeasureText("Wg", font).Height;

        var monthRect = new Rectangle(
            new Point(content.X + layout.ArrowWidth, content.Y + ((layout.HeaderHeight - lineHeight) / 2f)),
            // Max: on a narrow calendar the arrows eat the whole width,
            // and the rectangle would go negative
            new Size(Math.Max(0, content.Width - (2 * layout.ArrowWidth)), lineHeight));

        g.DrawText(_displayMonth.ToString("MMMM yyyy", EffectiveCulture), monthRect, TextColor, font,
            HorizontalContentAlignment.Center, VerticalContentAlignment.Center);

        string[] abbreviated = GetDayNames(shortest: false);

        // the abbreviations either all fit or all change: a mix of "Mon" and "M"
        // in one row looks like a bug rather than economy. A "Mon" cut to "Mo"
        // by DrawText reads worse than an honest single letter
        bool fits = true;

        foreach (string name in abbreviated)
        {
            if (TextMeasurer.Current.MeasureText(name, font).Width <= layout.Cell.Width) continue;

            fits = false;
            break;
        }

        string[] dayNames = fits ? abbreviated : GetDayNames(shortest: true);

        for (int i = 0; i < Columns; i++)
        {
            g.DrawText(dayNames[i],
                new Rectangle(
                    new Point(content.X + (i * layout.Cell.Width), content.Y + layout.HeaderHeight),
                    new Size(layout.Cell.Width, layout.DayOfWeekHeight)),
                MutedColor, font, HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
        }

        DateTime date = FirstCellDate;
        float gridTop = content.Y + layout.GridTop;

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                var rect = new Rectangle(
                    new Point(content.X + (col * layout.Cell.Width), gridTop + (row * layout.Cell.Height)),
                    layout.Cell);

                int cellIndex = (row * Columns) + col;
                bool selected = SelectedDate?.Date == date.Date;

                // exactly one fill per cell: previously selected ones were filled
                // twice, and a translucent SelectionColor came out denser
                if (selected)
                    g.FillRectangle(rect, SelectionColor);
                else if (cellIndex == _hoveredCell)
                    g.FillRectangle(rect, HoverColor);
                else if (date.Date == DateTime.Today)
                    g.FillRectangle(rect, TodayColor);

                Color color = date.Month == _displayMonth.Month
                    ? selected ? Colors.White : TextColor
                    : MutedColor;

                g.DrawText(date.Day.ToString(), rect, color, font,
                    HorizontalContentAlignment.Center, VerticalContentAlignment.Center);

                date = date.AddDays(1);
            }
        }
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        var (cell, header) = HitFromPoint(e.Location);

        e.Handled = true;

        if (header != 0)
        {
            _displayMonth = _displayMonth.AddMonths(header);
            Invalidate();
            return;
        }

        if (cell < 0) return;

        DateTime picked = FirstCellDate.AddDays(cell);

        SelectedDate = picked;
        _displayMonth = picked;
        Invalidate();

        DateSelected?.Invoke(this, picked);
    }

    private (int Cell, int HeaderButton) HitFromPoint(Point location)
    {
        Point abs = GetAbsolutePosition();
        float localX = location.X - abs.X - Padding.Left;
        float localY = location.Y - abs.Y - Padding.Top;

        CalendarLayout layout = GetLayout();

        if (localY < layout.HeaderHeight)
        {
            if (localX < layout.ArrowWidth) return (-1, -1);
            if (localX > this.ContentBounds.Width - layout.ArrowWidth) return (-1, 1);

            return (-1, 0);
        }

        if (localY < layout.GridTop) return (-1, 0);

        // a grid compressed to zero: nothing to divide by, and nothing to hit
        if (layout.Cell.Width <= 0 || layout.Cell.Height <= 0) return (-1, 0);

        int col = (int)(localX / layout.Cell.Width);
        int row = (int)((localY - layout.GridTop) / layout.Cell.Height);

        if (col < 0 || col >= Columns || row < 0 || row >= Rows) return (-1, 0);

        return ((row * Columns) + col, 0);
    }

    protected override void OnMouseMove(MouseMoveEventArgs args)
    {
        var (cell, header) = HitFromPoint(args.Location);

        if (cell == _hoveredCell && header == _hoveredHeaderButton) return;

        _hoveredCell = cell;
        _hoveredHeaderButton = header;
        InvalidateVisual();
    }

    protected override void OnMouseExit(MouseMoveEventArgs args)
    {
        if (_hoveredCell < 0 && _hoveredHeaderButton == 0) return;

        _hoveredCell = -1;
        _hoveredHeaderButton = 0;

        // without a redraw the cell or arrow stayed highlighted after the mouse left
        InvalidateVisual();
    }

    public void SetSelectedDate(DateTime date)
    {
        SelectedDate = date;
        _displayMonth = date;
        Invalidate();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(
            new Size(
                Columns * ReferenceCellWidth,
                ReferenceHeaderHeight + ReferenceDayOfWeekHeight + (Rows * ReferenceCellHeight)),
            availableSize);
}