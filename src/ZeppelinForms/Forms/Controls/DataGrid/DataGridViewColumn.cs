using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>Столбец грида: заголовок, ширина и способ добраться до значения.</summary>
public sealed class DataGridViewColumn
{
    public string? Header { get; set; }

    /// <summary>Фикс — ширина в логических единицах, звёздочка — доля остатка,
    /// Auto — по заголовку и видимым строкам.</summary>
    /// <remarks>
    /// Auto здесь считается не так, как в Table: та меряет все строки, а грид
    /// виртуализован, и обход всего набора на каждую раскладку сделал бы
    /// виртуализацию бессмысленной. Поэтому Auto смотрит на заголовок
    /// и на строки, попавшие в видимый диапазон, — то есть ширина столбца
    /// может измениться при прокрутке. Если это мешает, задавайте фикс.
    /// </remarks>
    public GridLength Width { get; set; } = GridLength.Star();

    public HorizontalContentAlignment Align { get; set; } = HorizontalContentAlignment.Left;

    /// <summary>Как достать значение из строки данных.</summary>
    /// <remarks>
    /// Делегат, а не имя свойства: отражение на каждую ячейку каждого кадра —
    /// это те самые расходы, ради которых в 0.10.0 заводили кэши текста.
    /// А Binding означал бы подписку на каждую видимую ячейку, которую
    /// пришлось бы пересоздавать при всякой прокрутке.
    /// </remarks>
    public required Func<object, object?> Value { get; init; }

    /// <summary>Как записать значение обратно. Null — столбец только для чтения.</summary>
    public Action<object, object?>? SetValue { get; set; }

    /// <summary>Как превратить значение в текст. Null — ToString с текущей культурой.</summary>
    public Func<object?, string>? Format { get; set; }

    public bool CanSort { get; set; } = true;

    /// <summary>Сравнение для сортировки. Null — Comparer.Default по значению.</summary>
    public IComparer<object?>? Comparer { get; set; }

    internal string TextOf(object item)
    {
        object? value = Value(item);

        return Format is not null ? Format(value) : value?.ToString() ?? string.Empty;
    }
}