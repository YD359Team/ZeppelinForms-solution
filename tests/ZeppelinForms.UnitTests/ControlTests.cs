using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
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
}
