namespace ZeppelinForms.Drawing.Primitives;

public sealed record DisplayInfo
{
    /// <summary>Полная область экрана в физических пикселях.</summary>
    public required Rectangle Bounds { get; init; }

    /// <summary>Область без панели задач и системных панелей.</summary>
    public required Rectangle WorkingArea { get; init; }

    public required float Scale { get; init; }

    /// <summary>Настоящая плотность пикселей, точек на дюйм.</summary>
    /// <remarks>
    /// Отдельно от Scale и обязательно к заполнению. Scale — это решение
    /// о размере интерфейса: X11Dpi округляет его до четверти и зажимает
    /// в [1, 4], Android считает от базы 160, а не 96. Восстановить
    /// плотность из такого числа нельзя, а порогам жестов нужна именно
    /// она: порог срыва в pan, заданный в пикселях, настроится под мышь
    /// и окажется неработающим на телефоне.
    /// </remarks>
    public required float Dpi { get; init; }

    public required bool IsPrimary { get; init; }

    public string? Name { get; init; }

    /// <summary>Логический размер рабочей области — в этих единицах живут контролы.</summary>
    public Size LogicalWorkingSize =>
        new(WorkingArea.Width / Scale, WorkingArea.Height / Scale);

    /// <summary>Сколько логических единиц в миллиметре. Через это
    /// переводятся все пороги, заданные в физических единицах.</summary>
    public float LogicalUnitsPerMillimeter => Dpi / 25.4f / Scale;

    /// <summary>Миллиметры в логические единицы этого экрана.</summary>
    public float MillimetersToLogical(float millimeters) =>
        millimeters * LogicalUnitsPerMillimeter;
}