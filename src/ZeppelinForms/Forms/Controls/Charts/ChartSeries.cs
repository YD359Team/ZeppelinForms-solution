using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls.Charts;

public sealed class ChartSeries
{
    public string? Name { get; set; }
    public List<float> Values { get; init; } = [];
    public Color? Color { get; set; }
}
