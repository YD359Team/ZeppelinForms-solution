using System;
using System.Collections.Generic;
using System.Text;
using System.Collections.Specialized;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls.Charts;

public sealed class ChartSeries
{
    public ChartSeries()
    {
        _values.CollectionChanged += OnValuesChanged;
    }

    /// <summary>The series' name: the legend's and the readout's.</summary>
    public string? Name
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Changed?.Invoke();
        }
    }

    /// <summary>The values, by category. A value added or changed after the chart is
    /// shown shows at once: since 0.20 a <see cref="ZfCollection{T}"/>, it was a list.</summary>
    public ZfCollection<float> Values
    {
        get => _values;
        init => ZfCollectionOwner.Replace(ref _values, value, OnValuesChanged);
    }

    private ZfCollection<float> _values = [];

    public Color? Color
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Changed?.Invoke();
        }
    }

    /// <summary>The series changed: its values, name or color. The charts that show it
    /// listen, see ChartBase.WatchSeries.</summary>
    internal event Action? Changed;

    private void OnValuesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Changed?.Invoke();
}
