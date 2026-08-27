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
        Grid grid = new Grid
        {
            Columns = "150,0.2*,0.75*"
        };
        Assert.True(!grid.ColumnDefinitions[0].IsStar);
        Assert.True(grid.ColumnDefinitions[1].IsStar);
        Assert.True(grid.ColumnDefinitions[2].IsStar);
    }
}
