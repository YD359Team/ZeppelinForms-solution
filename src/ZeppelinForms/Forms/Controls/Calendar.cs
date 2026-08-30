using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class Calendar : UnitControl
{
    private const float HeaderHeight = 28f;
    private const float DayOfWeekHeight = 20f;
    private const int Rows = 6;
    private const int Columns = 7;

    private DateTime _displayMonth = DateTime.Today;

    public DateTime? SelectedDate { get; private set; }
    public event EventHandler<DateTime>? DateSelected;

    public Color TextColor { get; set; } = Colors.Black;
    public Color MutedColor { get; set; } = new Color(255, 160, 160, 160);
    public Color SelectionColor { get; set; } = LightThemeColors.ButtonFill;
    public Color TodayColor { get; set; } = new Color(255, 220, 235, 255);

    private int _hoveredCell = -1;
    private int _hoveredHeaderButton;   // -1 — назад, 1 — вперёд, 0 — нет

    public Color HoverColor { get; set; } = new Color(255, 235, 242, 255);
    public Color HeaderHoverColor { get; set; } = new Color(255, 228, 228, 228);

    public Calendar()
    {
        Background = Colors.White;
        Size = new Size(252, 220);
    }

    private DateTime FirstCellDate
    {
        get
        {
            var first = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
            int shift = ((int)first.DayOfWeek + 6) % 7;   // неделя с понедельника
            return first.AddDays(-shift);
        }
    }

    private Size CellSize => new(
        ContentBounds.Width / Columns,
        (ContentBounds.Height - HeaderHeight - DayOfWeekHeight) / Rows);

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRectangle(this.LocalBounds, Background);

        var content = this.ContentBounds;
        var cell = CellSize;
        Font font = this.EffectiveFont;

        // заголовок: ‹ Месяц Год ›
        g.DrawText("‹", new Rectangle(new Point(content.X, content.Y), new Size(28, HeaderHeight)),
            TextColor, font, HorizontalContentAlignment.Center, VerticalContentAlignment.Center);

        g.DrawText($"{_displayMonth:MMMM yyyy}",
            new Rectangle(new Point(content.X + 28, content.Y), new Size(content.Width - 56, HeaderHeight)),
            TextColor, font, HorizontalContentAlignment.Center, VerticalContentAlignment.Center);

        g.DrawText("›", new Rectangle(new Point(content.X + content.Width - 28, content.Y), new Size(28, HeaderHeight)),
            TextColor, font, HorizontalContentAlignment.Center, VerticalContentAlignment.Center);

        string[] dayNames = ["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс"];

        for (int i = 0; i < Columns; i++)
        {
            if (_hoveredHeaderButton == -1)
                g.FillRectangle(new Rectangle(new Point(content.X, content.Y), new Size(28, HeaderHeight)), HeaderHoverColor);
            else if (_hoveredHeaderButton == 1)
                g.FillRectangle(new Rectangle(new Point(content.X + content.Width - 28, content.Y), new Size(28, HeaderHeight)), HeaderHoverColor);
            g.DrawText(dayNames[i],
                new Rectangle(
                    new Point(content.X + i * cell.Width, content.Y + HeaderHeight),
                    new Size(cell.Width, DayOfWeekHeight)),
                MutedColor, font, HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
        }

        DateTime date = FirstCellDate;
        float gridTop = content.Y + HeaderHeight + DayOfWeekHeight;

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                var rect = new Rectangle(
                    new Point(content.X + col * cell.Width, gridTop + row * cell.Height),
                    cell);

                if (SelectedDate?.Date == date.Date)
                    g.FillRectangle(rect, SelectionColor);
                else if (date.Date == DateTime.Today)
                    g.FillRectangle(rect, TodayColor);

                Color color = date.Month == _displayMonth.Month
                    ? (SelectedDate?.Date == date.Date ? Colors.White : TextColor)
                    : MutedColor;

                int cellIndex = row * Columns + col;

                if (SelectedDate?.Date == date.Date)
                    g.FillRectangle(rect, SelectionColor);
                else if (cellIndex == _hoveredCell)
                    g.FillRectangle(rect, HoverColor);
                else if (date.Date == DateTime.Today)
                    g.FillRectangle(rect, TodayColor);

                g.DrawText(date.Day.ToString(), rect, color, font,
                    HorizontalContentAlignment.Center, VerticalContentAlignment.Center);

                date = date.AddDays(1);
            }
        }
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        Point abs = GetAbsolutePosition();
        float localX = e.Location.X - abs.X - Padding.Left;
        float localY = e.Location.Y - abs.Y - Padding.Top;

        e.Handled = true;

        if (localY < HeaderHeight)
        {
            if (localX < 28) _displayMonth = _displayMonth.AddMonths(-1);
            else if (localX > ContentBounds.Width - 28) _displayMonth = _displayMonth.AddMonths(1);

            Invalidate();
            return;
        }

        float gridTop = HeaderHeight + DayOfWeekHeight;
        if (localY < gridTop) return;

        var cell = CellSize;
        int col = (int)(localX / cell.Width);
        int row = (int)((localY - gridTop) / cell.Height);

        if (col < 0 || col >= Columns || row < 0 || row >= Rows) return;

        DateTime picked = FirstCellDate.AddDays(row * Columns + col);

        SelectedDate = picked;
        _displayMonth = picked;
        Invalidate();

        DateSelected?.Invoke(this, picked);
    }

    private(int Cell, int HeaderButton) HitFromPoint(Point location)
    {
        Point abs = GetAbsolutePosition();
        float localX = location.X - abs.X - Padding.Left;
        float localY = location.Y - abs.Y - Padding.Top;

        if (localY < HeaderHeight)
        {
            if (localX < 28) return (-1, -1);
            if (localX > ContentBounds.Width - 28) return (-1, 1);
            return (-1, 0);
        }

        float gridTop = HeaderHeight + DayOfWeekHeight;
        if (localY < gridTop) return (-1, 0);

        var cell = CellSize;
        int col = (int)(localX / cell.Width);
        int row = (int)((localY - gridTop) / cell.Height);

        if (col < 0 || col >= Columns || row < 0 || row >= Rows)
            return (-1, 0);

        return (row * Columns + col, 0);
    }

    protected override void OnMouseMove(Point location)
    {
        var (cell, header) = HitFromPoint(location);

        if (cell == _hoveredCell && header == _hoveredHeaderButton) return;

        _hoveredCell = cell;
        _hoveredHeaderButton = header;
        InvalidateVisual();
    }

    protected override void OnMouseLeave()
    {
        _hoveredCell = -1;
        _hoveredHeaderButton = 0;
    }

    public void SetSelectedDate(DateTime date)
    {
        SelectedDate = date;
        _displayMonth = date;
        Invalidate();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(new Size(252, 220), availableSize);
}