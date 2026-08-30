using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.UnitTests;

public class ControlTests
{
    [Fact]
    public void GridLengthTest()
    {
        Grid grid = new()
        {
            Columns = "150,0.2*,0.75*"
        };
        Assert.True(!grid.ColumnDefinitions[0].IsStar);
        Assert.True(grid.ColumnDefinitions[1].IsStar);
        Assert.True(grid.ColumnDefinitions[2].IsStar);
    }

    [Fact]
    public void ListBoxItemsTest()
    {
        ListBox listBox = new();
        listBox.Items.Add(1);
        listBox.Items.Add(25);
        listBox.Items.Add(-4);

        Assert.True(listBox.Items.Count == 3);
    }

    [Fact]
    public void ComboBoxItemsTest()
    {
        ComboBox comboBox = new();
        comboBox.Items.Add(3);
        comboBox.Items.Add(-66);
        comboBox.Items.Add(1234);
        comboBox.Items.Add(555);

        Assert.True(comboBox.Items.Count == 4);
    }

    [Fact]
    public void TextBoxRemoveEmojiTest()
    {
        var doc = new TextDocument { Text = "привет 👍🏽" };
        doc.SetCaret(doc.Text.Length);

        doc.Backspace();

        Assert.Equal("привет ", doc.Text);
    }

    [Fact]
    public void TextBoxUndoTest()
    {
        var doc = new TextDocument();

        foreach (char c in "привет")
            doc.Insert(c.ToString());

        doc.Undo();

        Assert.Equal(string.Empty, doc.Text);
    }

    [Fact]
    public void TextBoxBackspaceEmojiTest()
    {
        var doc = new TextDocument { Text = "тест 👍🏽" };
        doc.SetCaret(doc.Text.Length);

        doc.Backspace();

        Assert.Equal("тест ", doc.Text);
    }

    [Fact]
    public void TextBoxMoveVerticalTest()
    {
        var doc = new TextDocument { IsMultiline = true, Text = "длинная строка\nкор\nещё одна длинная" };
        doc.SetCaret(12);

        doc.MoveVertical(1, false);
        doc.MoveVertical(1, false);

        var (line, column) = doc.ToPosition(doc.CaretIndex);
        Assert.Equal(2, line);
        Assert.Equal(12, column);
    }
}
