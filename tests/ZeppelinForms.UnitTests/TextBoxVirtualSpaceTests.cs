using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>The caret past the end of a line — <see cref="TextBox.AllowCaretPastLineEnd"/>.</summary>
/// <remarks>The headless measurer gives every character 0.6 of the font size: at 10 px,
/// 6 px a character and a space, and a line 12 px high.</remarks>
[Collection("Platform")]
public class TextBoxVirtualSpaceTests
{
    private const float Char = 6f;
    private const float Line = 12f;

    private static (Form Form, TextBox Box) Show(string text, int caret, bool allow = true, bool wrap = false)
    {
        Input.Keyboard.Keyboard.Reset();

        var box = new TextBox
        {
            IsMultiline = true,
            WordWrap = wrap,
            AllowCaretPastLineEnd = allow,
            Text = text,
            Font = Font.Default.WithSize(10),
            Size = new Size(300, 120),
        };

        var form = new Form { Size = new Size(400, 300), Content = box };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();
        form.FocusElement(box);

        box.Select(caret, 0);

        return (form, box);
    }

    [Fact]
    public void RightAtTheEndOfALineGoesOnIntoTheRoomPastIt()
    {
        var (form, box) = Show("ab\ncdef", caret: 2);

        HeadlessInput.PressKey(form, Key.Right);
        HeadlessInput.PressKey(form, Key.Right);

        Assert.Equal(2, box.CaretIndex);
        Assert.Equal(2, box.VirtualSpaces);
    }

    [Fact]
    public void TypingThereFillsTheGapWithSpacesAndOneUndoTakesBoth()
    {
        var (form, box) = Show("ab\ncdef", caret: 2);

        HeadlessInput.PressKey(form, Key.Right);
        HeadlessInput.PressKey(form, Key.Right);
        HeadlessInput.TypeText(form, "x");

        Assert.Equal("ab  x\ncdef", box.Text);
        Assert.Equal(5, box.CaretIndex);
        Assert.Equal(0, box.VirtualSpaces);

        box.Undo();

        Assert.Equal("ab\ncdef", box.Text);
    }

    [Fact]
    public void DownKeepsItsColumnOverAShortLine()
    {
        // the caret after "abcde" of the first line: column 5
        var (form, box) = Show("abcdef\nab\nabcdef", caret: 5);

        HeadlessInput.PressKey(form, Key.Down);

        // the end of "ab", three spaces past it
        Assert.Equal(9, box.CaretIndex);
        Assert.Equal(3, box.VirtualSpaces);

        HeadlessInput.PressKey(form, Key.Down);

        // back in the text at the same column
        Assert.Equal(15, box.CaretIndex);
        Assert.Equal(0, box.VirtualSpaces);
    }

    [Fact]
    public void LeftStepsBackOutOfTheRoomBeforeMovingInTheText()
    {
        var (form, box) = Show("ab\ncd", caret: 2);

        HeadlessInput.PressKey(form, Key.Right);
        HeadlessInput.PressKey(form, Key.Left);

        Assert.Equal(2, box.CaretIndex);
        Assert.Equal(0, box.VirtualSpaces);

        HeadlessInput.PressKey(form, Key.Left);

        Assert.Equal(1, box.CaretIndex);
    }

    [Fact]
    public void BackspaceInTheRoomOnlyStepsBack()
    {
        var (form, box) = Show("ab\ncd", caret: 2);

        HeadlessInput.PressKey(form, Key.Right);
        HeadlessInput.PressKey(form, Key.Right);
        HeadlessInput.PressKey(form, Key.Backspace);

        Assert.Equal("ab\ncd", box.Text);
        Assert.Equal(1, box.VirtualSpaces);
    }

    [Fact]
    public void DeleteInTheRoomBringsTheNextLineUpToTheCaret()
    {
        var (form, box) = Show("ab\ncd", caret: 2);

        HeadlessInput.PressKey(form, Key.Right);
        HeadlessInput.PressKey(form, Key.Right);
        HeadlessInput.PressKey(form, Key.Delete);

        Assert.Equal("ab  cd", box.Text);
    }

    [Fact]
    public void ShiftSelectsFromTheLinesEndNotFromTheRoom()
    {
        var (form, box) = Show("ab\ncd", caret: 2);

        HeadlessInput.PressKey(form, Key.Right);
        HeadlessInput.PressKey(form, Key.Right, KeyModifiers.Shift);

        Assert.Equal(0, box.VirtualSpaces);
        Assert.Equal("\n", box.SelectedText);
    }

    [Fact]
    public void AClickBeyondALinesEndPutsTheCaretWhereItWasClicked()
    {
        var (form, box) = Show("abcdef\nab", caret: 0);

        Point at = box.GetAbsolutePosition();
        Rectangle content = box.ContentBounds;

        // the second line, 5 columns in: 3 past the end of "ab"
        HeadlessInput.Click(form, at.X + content.X + 5 * Char + 1, at.Y + content.Y + Line * 1.5f);

        Assert.Equal(9, box.CaretIndex);
        Assert.Equal(3, box.VirtualSpaces);
    }

    [Fact]
    public void OffByDefaultRightGoesOnToTheNextLine()
    {
        var (form, box) = Show("ab\ncd", caret: 2, allow: false);

        HeadlessInput.PressKey(form, Key.Right);

        Assert.Equal(3, box.CaretIndex);
        Assert.Equal(0, box.VirtualSpaces);
    }

    [Fact]
    public void WordWrapHasNoRoomPastItsRows()
    {
        var (form, box) = Show("ab\ncd", caret: 2, wrap: true);

        HeadlessInput.PressKey(form, Key.Right);

        Assert.Equal(3, box.CaretIndex);
        Assert.Equal(0, box.VirtualSpaces);
    }

    [Fact]
    public void ShiftAloneLeavesTheCaretInTheRoom()
    {
        var (form, box) = Show("ab\ncd", caret: 2);

        HeadlessInput.PressKey(form, Key.Right);
        HeadlessInput.PressKey(form, Key.LeftShift);

        Assert.Equal(1, box.VirtualSpaces);
    }
}