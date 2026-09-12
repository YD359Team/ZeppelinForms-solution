using SkiaSharp;

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
/// <remarks>
/// Экземпляр принадлежит кэшу и освобождается при вытеснении поколения.
/// Держать ссылку дольше одного кадра нельзя: блобы к тому времени
/// могут быть уже уничтожены.
/// </remarks>
internal sealed class CachedLine : IDisposable
{
    public static readonly CachedLine Empty = new()
    {
        Text = string.Empty,
        Runs = [],
        Width = 0,
        Height = 0,
        Bounds = SKRect.Empty,
    };

    /// <summary>Строка, к которой относится разбор. Это тот же экземпляр,
    /// что лежит в ключе кэша, поэтому копии не возникает.</summary>
    public required string Text { get; init; }

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

    /// <summary>Готовые блобы по одному на отрезок. Создаются лениво:
    /// изрядная часть строк только измеряется и никогда не рисуется —
    /// скрытые элементы, служебные замеры высоты строки.</summary>
    private SKTextBlob?[]? _blobs;

    /// <summary>Отрезки, для которых блоб уже пытались построить.
    /// Отдельный признак нужен потому, что null — законный результат:
    /// SKTextBlob.Create на пробельном отрезке глифов не даёт, и без
    /// этого флага такой отрезок пересобирался бы каждый кадр.</summary>
    private bool[]? _probed;

    /// <summary>
    /// Блоб отрезка — набор глифов с позициями, готовый к выводу.
    /// SKCanvas.DrawText(string, ...) строит такой блоб на каждый вызов
    /// и тут же уничтожает, то есть пересобирает раскладку глифов
    /// на каждом кадре. Здесь он строится один раз на строку.
    /// </summary>
    /// <remarks>Без блокировки: экземпляр лежит в потоковом кэше и
    /// принадлежит одному потоку целиком — вместе с SKFont, из которого
    /// строится блоб. Раньше здесь допускалась гонка с лишним блобом;
    /// теперь её просто неоткуда взять.</remarks>
    public SKTextBlob? GetBlob(int index)
    {
        if (_blobs is null)
        {
            _blobs = new SKTextBlob?[Runs.Length];
            _probed = new bool[Runs.Length];
        }

        if (_probed![index])
            return _blobs[index];

        FontRun run = Runs[index];

        SKTextBlob? blob = SKTextBlob.Create(
            Text.AsSpan(run.Start, run.Length), run.Font);

        _blobs[index] = blob;
        _probed[index] = true;

        return blob;
    }

    /// <summary>Освободить блобы. Зовётся кэшем при вытеснении:
    /// SKTextBlob — обёртка над нативным объектом, и ждать финализатора
    /// означает держать нативную память до ближайшей сборки.</summary>
    public void Dispose()
    {
        if (_blobs is null) return;

        for (int i = 0; i < _blobs.Length; i++)
        {
            _blobs[i]?.Dispose();
            _blobs[i] = null;
        }

        _probed = null;
        _blobs = null;
    }
}

/// <summary>
/// Кэш с поколениями вместо LRU: когда горячий словарь перерастает лимит,
/// он целиком становится холодным, а под новые записи заводится пустой.
/// Попадание в холодный словарь переносит запись в горячий, поэтому
/// то, чем пользуются, переживает смену поколения, а разовые строки —
/// нет. Списка использования нет, значит чтение не перестраивает
/// структуру и не требует ничего, кроме одной блокировки.
/// </summary>
/// <remarks>Если значения реализуют IDisposable, уходящее поколение
/// освобождается целиком. Отсюда требование: одно значение не должно
/// лежать в двух поколениях одновременно.</remarks>
internal sealed class GenerationalCache<TKey, TValue>(int limit)
    where TKey : notnull
    where TValue : class
{
    private static readonly bool ValuesAreDisposable =
        typeof(IDisposable).IsAssignableFrom(typeof(TValue));

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

            // и убираем из холодных: один объект в двух поколениях
            // означает, что уничтожение уходящего поколения освободит
            // значение, которым ещё пользуется горячее
            _cold.Remove(key);

            return true;
        }
    }

    public void Add(TKey key, TValue value)
    {
        lock (_sync)
        {
            if (_hot.Count >= Limit)
            {
                Release(_cold);

                _cold = _hot;
                _hot = new Dictionary<TKey, TValue>(Limit);
            }

            // замена по тому же ключу: прежнее значение больше ниоткуда
            // не достать, освобождаем сразу
            if (_hot.TryGetValue(key, out TValue? replaced) && !ReferenceEquals(replaced, value))
                Dispose(replaced);

            if (_cold.Remove(key, out TValue? stale) && !ReferenceEquals(stale, value))
                Dispose(stale);

            _hot[key] = value;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            Release(_hot);
            Release(_cold);

            _hot.Clear();
            _cold.Clear();
        }
    }

    private static void Release(Dictionary<TKey, TValue> generation)
    {
        if (!ValuesAreDisposable) return;

        foreach (TValue value in generation.Values)
            ((IDisposable)value).Dispose();
    }

    private static void Dispose(TValue value)
    {
        if (ValuesAreDisposable)
            ((IDisposable)value).Dispose();
    }
}