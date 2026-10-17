using System.Globalization;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using Calendar = ZeppelinForms.Forms.Controls.Calendar;

namespace ZF_SharedLib.Showcase;

/// <summary>Picking a date, a range of dates, a time and a color.</summary>
internal static class PickersPage
{
    public static UIElement Create()
    {
        Label day = Demo.Note("Click a day");
        var calendar = new Calendar();
        calendar.DateSelected += (_, date) => day.Text = date.ToString("D", CultureInfo.CurrentCulture);

        Label stay = Demo.Note("Click the first day, then the last");
        var range = new Calendar { SelectionMode = CalendarSelectionMode.Range };
        range.RangeSelected += (_, _) =>
        {
            DateTime from = range.RangeStart!.Value, to = range.RangeEnd!.Value;
            stay.Text = $"{from:d} – {to:d}, {(to - from).Days + 1} day(s)";
        };

        Label when = Demo.Note("Pick a date and a time");
        var date = new DateTimePicker();
        var time = new TimePicker { MinuteStep = 15 };

        void ShowWhen() => when.Text = $"{date.Value:d} at {time.Value:t}";
        date.ValueChanged += (_, _) => ShowWhen();
        time.ValueChanged += (_, _) => ShowWhen();

        var pickers = Demo.Column(
            Demo.Row(Demo.Labeled("DateTimePicker", date), Demo.Labeled("TimePicker, 15-minute steps", time)),
            when);

        var swatch = new Label { Text = "The chosen color", Padding = new(16, 10), CornerRadius = new(6) };
        var color = new ColorPicker { AllowAlpha = true, Value = new(255, 0x0D, 0x6E, 0xFD) };

        void ShowColor()
        {
            swatch.Background = color.Value;
            swatch.TextColor = color.Value.R * 0.299 + color.Value.G * 0.587 + color.Value.B * 0.114 > 150 || color.Value.A < 100
                ? ZeppelinForms.Drawing.Primitives.Colors.Black
                : ZeppelinForms.Drawing.Primitives.Colors.White;
        }

        color.ValueChanged += (_, _) => ShowColor();
        ShowColor();

        return Demo.Page("Date and color", "Calendars, date and time pickers, and a color picker.",
            Demo.Section("Calendar", Demo.Row(Demo.Column(calendar, day), Demo.Column(range, stay)),
                "SelectionMode = Range: two clicks pick the ends and the days between are marked; Esc drops a range half picked"),
            Demo.Section("Date and time", pickers, "Their formats follow the culture: switch it in Settings"),
            Demo.Section("Color", Demo.Row(color, swatch), "AllowAlpha adds the alpha slider and #RRGGBBAA"));
    }
}