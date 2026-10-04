using Xunit;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>
/// The keyboard of the grid and the calendar, which took no focus before 0.13,
/// and of the calendar inside a date picker's drop-down.
/// </summary>
[Collection("Platform")]
public class ControlKeyboardTests
{
    private static Form CreateForm(UIElement content)
    {
        var form = new Form { Size = new Size(500, 400), Content = content };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    // ===== grid =====

    private static DataGridView CreateGrid(int rows)
    {
        var grid = new DataGridView
        {
            Columns = [new DataGridViewColumn { Header = "N", Value = o => o }],
        };

        for (int i = 0; i < rows; i++)
            grid.Items.Add(i);

        return grid;
    }

    [Fact]
    public void GridTakesFocusAndArrowsMoveTheRow()
    {
        DataGridView grid = CreateGrid(40);
        Form form = CreateForm(grid);

        HeadlessInput.PressKey(form, Key.Tab);
        Assert.True(grid.IsFocused);

        HeadlessInput.PressKey(form, Key.Down);
        Assert.Equal(0, grid.SelectedIndex);

        HeadlessInput.PressKey(form, Key.Down);
        Assert.Equal(1, grid.SelectedIndex);

        HeadlessInput.PressKey(form, Key.End);
        Assert.Equal(39, grid.SelectedIndex);

        HeadlessInput.PressKey(form, Key.Home);
        Assert.Equal(0, grid.SelectedIndex);

        HeadlessInput.PressKey(form, Key.PageDown);
        Assert.True(grid.SelectedIndex > 1);

        // the row the keyboard went to is in sight
        Rectangle row = grid.RowBounds(grid.SelectedIndex);
        Rectangle body = grid.VisibleBodyBounds;
        Assert.True(row.Y >= body.Y - 0.5f && row.Bottom <= body.Bottom + 0.5f);
    }

    [Fact]
    public void FocusedGridReportsItsCurrentRow()
    {
        DataGridView grid = CreateGrid(5);
        Form form = CreateForm(grid);
        grid.SelectedIndex = 3;

        var focused = new List<AccessibilityPeer>();
        Action<AccessibilityPeer> listen = focused.Add;
        AccessibilityEvents.FocusChanged += listen;

        try
        {
            HeadlessInput.PressKey(form, Key.Tab);
        }
        finally
        {
            AccessibilityEvents.FocusChanged -= listen;
        }

        Assert.Equal(AccessibilityRole.Row, focused[^1].Role);
        Assert.Equal(new SetPosition(4, 5), focused[^1].Position);
    }

    // ===== calendar =====

    [Fact]
    public void CalendarArrowsMoveCursorNotSelection()
    {
        var calendar = new Calendar();
        calendar.SetSelectedDate(new DateTime(2026, 10, 15));

        DateTime? picked = null;
        calendar.DateSelected += (_, date) => picked = date;

        Form form = CreateForm(calendar);
        HeadlessInput.PressKey(form, Key.Tab);

        HeadlessInput.PressKey(form, Key.Right);
        HeadlessInput.PressKey(form, Key.Down);

        Assert.Equal(new DateTime(2026, 10, 23), calendar.KeyboardDate);
        Assert.Equal(new DateTime(2026, 10, 15), calendar.SelectedDate);

        HeadlessInput.PressKey(form, Key.Enter);

        Assert.Equal(new DateTime(2026, 10, 23), calendar.SelectedDate);
        Assert.Equal(new DateTime(2026, 10, 23), picked);
    }

    [Fact]
    public void CalendarPagesByMonthAndYear()
    {
        var calendar = new Calendar();
        calendar.SetSelectedDate(new DateTime(2026, 10, 15));
        Form form = CreateForm(calendar);
        HeadlessInput.PressKey(form, Key.Tab);

        HeadlessInput.PressKey(form, Key.PageDown);
        Assert.Equal(new DateTime(2026, 11, 15), calendar.KeyboardDate);

        HeadlessInput.PressKey(form, Key.PageUp, KeyModifiers.Control);
        Assert.Equal(new DateTime(2025, 11, 15), calendar.KeyboardDate);

        HeadlessInput.PressKey(form, Key.End);
        Assert.Equal(new DateTime(2025, 11, 30), calendar.KeyboardDate);
    }

    [Fact]
    public void FocusedCalendarReadsTheCursorDay()
    {
        var calendar = new Calendar { Culture = System.Globalization.CultureInfo.InvariantCulture };
        calendar.SetSelectedDate(new DateTime(2026, 10, 15));
        Form form = CreateForm(calendar);
        HeadlessInput.PressKey(form, Key.Tab);

        HeadlessInput.PressKey(form, Key.Right);

        Assert.Equal("Friday, 16 October 2026", calendar.GetAccessibilityPeer()!.Value);
    }

    // ===== date picker =====

    [Fact]
    public void OpenDropDownTakesTheArrows()
    {
        var picker = new DateTimePicker();
        picker.SetValue(new DateTime(2026, 10, 15));
        Form form = CreateForm(picker);
        HeadlessInput.PressKey(form, Key.Tab);

        HeadlessInput.PressKey(form, Key.F4);
        Assert.True(picker.IsDropDownOpen);

        HeadlessInput.PressKey(form, Key.Right);
        HeadlessInput.PressKey(form, Key.Right);
        HeadlessInput.PressKey(form, Key.Enter);

        Assert.Equal(new DateTime(2026, 10, 17), picker.Value.Date);
        Assert.False(picker.IsDropDownOpen);
    }

    [Fact]
    public void ClosedPickerStillStepsByArrows()
    {
        var picker = new DateTimePicker();
        picker.SetValue(new DateTime(2026, 10, 15));
        Form form = CreateForm(picker);
        HeadlessInput.PressKey(form, Key.Tab);

        HeadlessInput.PressKey(form, Key.Up);

        Assert.Equal(new DateTime(2026, 10, 16), picker.Value.Date);
    }
}