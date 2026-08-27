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
            TextColor, font, HorizontalAlign.Center, VerticalAlign.Center);

        g.DrawText($"{_displayMonth:MMMM yyyy}",
            new Rectangle(new Point(content.X + 28, content.Y), new Size(content.Width - 56, HeaderHeight)),
            TextColor, font, HorizontalAlign.Center, VerticalAlign.Center);

        g.DrawText("›", new Rectangle(new Point(content.X + content.Width - 28, content.Y), new Size(28, HeaderHeight)),
            TextColor, font, HorizontalAlign.Center, VerticalAlign.Center);

        string[] dayNames = ["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс"];

        for (int i = 0; i < Columns; i++)
        {
            g.DrawText(dayNames[i],
                new Rectangle(
                    new Point(content.X + i * cell.Width, content.Y + HeaderHeight),
                    new Size(cell.Width, DayOfWeekHeight)),
                MutedColor, font, HorizontalAlign.Center, VerticalAlign.Center);
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

                g.DrawText(date.Day.ToString(), rect, color, font,
                    HorizontalAlign.Center, VerticalAlign.Center);

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

    public void SetSelectedDate(DateTime date)
    {
        SelectedDate = date;
        _displayMonth = date;
        Invalidate();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(new Size(252, 220), availableSize);
}