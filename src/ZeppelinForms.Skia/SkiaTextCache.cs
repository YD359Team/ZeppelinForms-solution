using SkiaSharp;

namespace ZeppelinForms.Skia;

/// <summary>
/// Отрезок строки, целиком покрытый одним шрифтом. Хранится индексами,
/// а не подстрокой: разбор не должен порождать копии — измеряют и рисуют
/// такой отрезок через ReadOnlySpan, копия там не нужна.
/// </summary>
internal readonly struct FontRun(int start, int length, SKFont font, float width)
{
    public int Start { get; } = start;
    public int Length { get; } = length;
    public SKFont Font { get; } = font;

    /// <summary>Ширина продвижения отрезка. Считается один раз при разборе,
    /// чтобы отрисовка не меряла то же самое повторно.</summary>
    public float Width { get; } = width;
}

/// <summary>Разбор строки на отрезки и её габариты при заданном шрифте.</summary>
internal sealed class CachedLine
{
    public static readonly CachedLine Empty = new()
    {
        Runs = [],
        Width = 0,
        Height = 0,
        Bounds = SKRect.Empty,
    };

    public required FontRun[] Runs { get; init; }

    /// <summary>Сумма ширин продвижения всех отрезков.</summary>
    public required float Width { get; init; }

    /// <summary>Максимальная высота чернил среди отрезков. Считается ровно
    /// так же, как считалась до появления кэша: на этой величине стоят
    /// все эталонные снимки, менять её нельзя.</summary>
    public required float Height { get; init; }

    /// <summary>Границы чернил всей строки по основному шрифту. Нужны
    /// SkiaGraphics для вертикального выравнивания по базовой линии.</summary>
    public required SKRect Bounds { get; init; }
}

/// <summary>
/// Кэш с поколениями вместо LRU: когда горячий словарь перерастает лимит,
/// он целиком становится холодным, а под новые записи заводится пустой.
/// Попадание в холодный словарь переносит запись в горячий, поэтому
/// то, чем пользуются, переживает смену поколения, а разовые строки —
/// нет. Списка использования нет, значит чтение не перестраивает
/// структуру и не требует ничего, кроме одной блокировки.
/// </summary>
internal sealed class GenerationalCache<TKey, TValue>(int limit)
    where TKey : notnull
    where TValue : class
{
    private readonly System.Threading.Lock _sync = new();

    private Dictionary<TKey, TValue> _hot = new(limit);
    private Dictionary<TKey, TValue> _cold = new(limit);

    /// <summary>Предельный размер одного поколения. Всего в памяти
    /// может находиться до двух поколений.</summary>
    public int Limit { get; } = limit;

    public bool TryGet(TKey key, out TValue? value)
    {
        lock (_sync)
        {
            if (_hot.TryGetValue(key, out value))
                return true;

            if (!_cold.TryGetValue(key, out value))
                return false;

            // запись пережила смену поколения — возвращаем её в горячие,
            // иначе она выпала бы при следующей же ротации
            _hot[key] = value;
            return true;
        }
    }

    public void Add(TKey key, TValue value)
    {
        lock (_sync)
        {
            if (_hot.Count >= Limit)
            {
                _cold = _hot;
                _hot = new Dictionary<TKey, TValue>(Limit);
            }

            _hot[key] = value;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _hot.Clear();
            _cold.Clear();
        }
    }
}