using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>Таблица данных с виртуализацией строк.</summary>
/// <remarks>
/// Ячейки рисуются, а не собираются из контролов: двадцать столбцов
/// на тридцать видимых строк дали бы шестьсот элементов, проходящих
/// измерение и раскладку каждый кадр. Контрол материализуется только
/// для редактируемой ячейки.
/// </remarks>
public partial class DataGridView : DecoratedControl, ITouchScrollTarget
{
    private const float ScrollBarThickness = 10f;

    private readonly List<float> _widths = [];

    private float _scrollX;
    private float _scrollY;

    /// <summary>Перелёт за край при прокрутке пальцем. В _scrollX и _scrollY
    /// его нет — они всегда в пределах, — а строки и шапка рисуются
    /// со смещением на него.</summary>
    private Point _overscroll;

    private readonly TouchScroller _touch;

    /// <summary>Порядок показа: строка на экране → индекс в Items.
    /// null — как в источнике. Сама коллекция не трогается: сортировка
    /// таблицы — способ смотреть на данные, а не менять их.</summary>
    private List<int>? _order;

    private int _resizingColumn = -1;
    private float _resizeStartX;
    private float _resizeStartWidth;

    /// <summary>Нажатие пришлось на границу столбцов: это была тяга ширины,
    /// и превращать её в клик по заголовку не надо.</summary>
    private bool _suppressHeaderClick;

    private int _hoveredRow = -1;

    public List<DataGridViewColumn> Columns { get; init; } = [];

    /// <summary>Строки данных. Тип элементов произвольный — столбцы знают,
    /// что из него доставать.</summary>
    public ObservableCollection<object> Items { get; } = [];

    /// <summary>Прокручивать ли таблицу пальцем и пером. Мышью — никогда:
    /// на десктопе протаскивание мышью выделяет, а прокрутка у колеса.</summary>
    public bool PanToScroll { get; set; } = true;

    public DataGridView()
    {
        Items.CollectionChanged += OnItemsChanged;

        // грид прокручивается сам, минуя PanelControl, поэтому жест
        // прокрутки ему нужен свой — но физика та же, общая
        _touch = TouchScroller.Attach(this);

        // таблица занимает отведённое место целиком: центрование,
        // унаследованное от UnitControl, оставляло бы её узкой полосой
        // посреди страницы
        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // выделенная строка могла исчезнуть вместе с данными
        if (SelectedIndex >= Items.Count) SelectedIndex = -1;

        // порядок показа построен по прежнему составу: в нём остались
        // индексы, которых больше нет
        if (SortColumnIndex >= 0) ApplySort();
        else _order = null;

        // смещение зажмёт EnsureLayout, здесь достаточно позвать раскладку
        Invalidate();
    }

    public float WheelStep { get; set; } = 48f;

    [Styled(Category = "DataGrid", AffectsLayout = true)]
    public partial float RowHeight { get; set; }
    private static float RowHeightDefault => 26f;

    [Styled(Category = "DataGrid", AffectsLayout = true)]
    public partial float HeaderHeight { get; set; }
    private static float HeaderHeightDefault => 30f;

    [Styled(Category = "DataGrid", AffectsLayout = true)]
    public partial Thickness CellPadding { get; set; }
    private static Thickness CellPaddingDefault => new(8, 4);

    [Styled(Category = "DataGrid")]
    public partial Color HeaderColor { get; set; }
    private static Color HeaderColorDefault => new(255, 244, 244, 244);

    [Styled(Category = "DataGrid")]
    public partial Color HeaderTextColor { get; set; }
    private static Color HeaderTextColorDefault => Colors.Black;

    [Styled(Category = "DataGrid")]
    public partial Color GridLineColor { get; set; }
    private static Color GridLineColorDefault => new(40, 0, 0, 0);

    [Styled(Category = "DataGrid")]
    public partial Color SelectionColor { get; set; }
    private static Color SelectionColorDefault => new(255, 205, 226, 252);

    [Styled(Category = "DataGrid")]
    public partial Color RowHoverColor { get; set; }
    private static Color RowHoverColorDefault => new(20, 0, 0, 0);

    [Styled(Category = "Scrolling")]
    public partial Color ScrollTrackColor { get; set; }
    private static Color ScrollTrackColorDefault => new(40, 0, 0, 0);

    [Styled(Category = "Scrolling")]
    public partial Color ScrollThumbColor { get; set; }
    private static Color ScrollThumbColorDefault => new(120, 0, 0, 0);

    public int SelectedIndex
    {
        get;
        set
        {
            int clamped = value < 0 || value >= Items.Count ? -1 : value;

            if (field == clamped) return;

            field = clamped;

            SelectionChanged?.Invoke(this, SelectedItem);
            InvalidateVisual();
        }
    } = -1;

    public object? SelectedItem => SelectedIndex >= 0 ? RowItem(SelectedIndex) : null;

    /// <summary>Данные строки по её месту на экране. При сортировке это
    /// не одно и то же, что Items[row].</summary>
    public object RowItem(int row) => Items[_order is null ? row : _order[row]];

    // ===== сортировка =====

    /// <summary>По какому столбцу отсортировано. −1 — порядок источника.</summary>
    public int SortColumnIndex { get; private set; } = -1;

    public bool SortDescending { get; private set; }

    /// <summary>Разрешить сортировку щелчком по заголовку. Сам столбец
    /// может отказаться через CanSort.</summary>
    public bool CanSortByHeaderClick { get; set; } = true;

    public event EventHandler? SortChanged;

    /// <summary>Отсортировать по столбцу. Без явного направления повторный
    /// вызов по тому же столбцу переворачивает порядок — так ведёт себя
    /// щелчок по заголовку.</summary>
    public void SortBy(int columnIndex, bool? descending = null)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count)
        {
            ClearSort();
            return;
        }

        SortDescending = descending ?? (SortColumnIndex == columnIndex && !SortDescending);
        SortColumnIndex = columnIndex;

        ApplySort();

        SortChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Вернуться к порядку источника.</summary>
    public void ClearSort()
    {
        if (SortColumnIndex < 0) return;

        SortColumnIndex = -1;
        SortDescending = false;

        ApplySort();

        SortChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplySort()
    {
        // выделение держится за строку данных, а не за её место на экране:
        // после сортировки выделенной должна остаться та же запись
        object? selected = SelectedItem;

        if (SortColumnIndex < 0 || SortColumnIndex >= Columns.Count)
        {
            _order = null;
        }
        else
        {
            DataGridViewColumn column = Columns[SortColumnIndex];
            IComparer<object?> comparer = column.Comparer ?? Comparer<object?>.Default;

            var order = new List<int>(Items.Count);

            for (int i = 0; i < Items.Count; i++) order.Add(i);

            order.Sort((left, right) =>
            {
                int result = comparer.Compare(column.Value(Items[left]), column.Value(Items[right]));

                // List.Sort неустойчива: равные значения без этого меняются
                // местами от вызова к вызову, и строки прыгают на глазах
                if (result == 0) return left.CompareTo(right);

                return SortDescending ? -result : result;
            });

            _order = order;
        }

        RestoreSelection(selected);

        InvalidateVisual();
    }

    private void RestoreSelection(object? selected)
    {
        if (selected is null) return;

        for (int row = 0; row < Items.Count; row++)
        {
            if (!ReferenceEquals(RowItem(row), selected)) continue;

            SelectedIndex = row;
            return;
        }

        SelectedIndex = -1;
    }

    public event EventHandler<object?>? SelectionChanged;

    // ===== геометрия =====

    private bool _verticalBar;
    private bool _horizontalBar;
    private int _visibleFirst;
    private int _visibleLast = -1;

    /// <summary>Область строк: содержимое за вычетом шапки и полос.</summary>
    private Rectangle BodyBounds
    {
        get
        {
            Rectangle content = this.ContentBounds;

            return new Rectangle(
                new Point(content.X, content.Y + HeaderHeight),
                new Size(
                    Math.Max(0, content.Width - (_verticalBar ? ScrollBarThickness : 0)),
                    Math.Max(0, content.Height - HeaderHeight - (_horizontalBar ? ScrollBarThickness : 0))));
        }
    }

    private float TotalWidth
    {
        get
        {
            float total = 0;

            foreach (float width in _widths) total += width;

            return total;
        }
    }

    private float TotalHeight => Items.Count * RowHeight;

    private float MaxScrollX => Math.Max(0, TotalWidth - BodyBounds.Width);

    private float MaxScrollY => Math.Max(0, TotalHeight - BodyBounds.Height);

    /// <summary>Пересчитать видимый диапазон, полосы и ширины столбцов.</summary>
    /// <remarks>
    /// Зовётся отовсюду, где нужна геометрия, а не только из рисования:
    /// иначе попадание и ScrollTo работали бы по данным прошлого кадра,
    /// а до первого кадра — по пустым.
    ///
    /// Три величины зависят друг от друга по кругу: видимый диапазон нужен
    /// Auto-ширинам, ширины — решению о горизонтальной полосе, полоса —
    /// высоте тела, а высота тела — видимому диапазону. Круг разрывается
    /// пессимистичной оценкой диапазона: считаем так, будто горизонтальная
    /// полоса есть всегда. Цена ошибки — одна лишняя строка в расчёте
    /// Auto-ширины, и та в запас.
    /// </remarks>
    private void EnsureLayout()
    {
        Rectangle content = this.ContentBounds;

        float bodyHeightGuess = Math.Max(0, content.Height - HeaderHeight - ScrollBarThickness);

        _visibleFirst = RowHeight <= 0 ? 0 : Math.Max(0, (int)(_scrollY / RowHeight));
        _visibleLast = RowHeight <= 0
            ? -1
            : Math.Min(Items.Count - 1, (int)((_scrollY + bodyHeightGuess) / RowHeight));

        // два прохода: полосы отнимают место друг у друга, и одного не хватает
        _verticalBar = TotalHeight > content.Height - HeaderHeight;

        float available = content.Width - (_verticalBar ? ScrollBarThickness : 0);

        ResolveWidths(available, _visibleFirst, _visibleLast);

        _horizontalBar = TotalWidth > available;

        if (_horizontalBar && !_verticalBar)
        {
            _verticalBar = TotalHeight > content.Height - HeaderHeight - ScrollBarThickness;

            if (_verticalBar)
                ResolveWidths(content.Width - ScrollBarThickness, _visibleFirst, _visibleLast);
        }

        // содержимое могло убавиться — смещение обязано остаться в пределах
        _scrollX = Math.Clamp(_scrollX, 0, MaxScrollX);
        _scrollY = Math.Clamp(_scrollY, 0, MaxScrollY);
    }

    /// <summary>Распределяет ширины: сначала фиксированные и Auto,
    /// остаток делится между звёздочками.</summary>
    private void ResolveWidths(float available, int firstRow, int lastRow)
    {
        _widths.Clear();

        float taken = 0;
        float starWeight = 0;

        foreach (DataGridViewColumn column in Columns)
        {
            if (column.Width.IsStar)
            {
                starWeight += column.Width.Value;
                _widths.Add(0);
                continue;
            }

            float width = column.Width.IsAuto
                ? MeasureAuto(column, firstRow, lastRow)
                : column.Width.Value;

            _widths.Add(width);
            taken += width;
        }

        if (starWeight <= 0) return;

        float rest = Math.Max(0, available - taken);

        for (int i = 0; i < Columns.Count; i++)
        {
            if (!Columns[i].Width.IsStar) continue;

            _widths[i] = rest * (Columns[i].Width.Value / starWeight);
        }
    }

    /// <summary>Ширина по заголовку и видимым строкам — не по всем.</summary>
    private float MeasureAuto(DataGridViewColumn column, int firstRow, int lastRow)
    {
        Font font = this.EffectiveFont;

        float width = string.IsNullOrEmpty(column.Header)
            ? 0
            : TextMeasurer.Current.MeasureText(column.Header, font).Width;

        for (int i = firstRow; i <= lastRow && i < Items.Count; i++)
        {
            float cell = TextMeasurer.Current.MeasureText(column.TextOf(RowItem(i)), font).Width;

            if (cell > width) width = cell;
        }

        return width + CellPadding.Horizontal;
    }

    // ===== прокрутка =====

    public void ScrollTo(float x, float y)
    {
        // явная прокрутка из кода или колесом главнее инерции броска
        _touch.Stop();

        EnsureLayout();

        float clampedX = Math.Clamp(x, 0, MaxScrollX);
        float clampedY = Math.Clamp(y, 0, MaxScrollY);

        if (clampedX == _scrollX && clampedY == _scrollY) return;

        _scrollX = clampedX;
        _scrollY = clampedY;

        InvalidateVisual();
    }

    /// <summary>Подтянуть строку в видимую часть.</summary>
    public void ScrollIntoView(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= Items.Count) return;

        float top = rowIndex * RowHeight;
        float bottom = top + RowHeight;

        if (top < _scrollY) ScrollTo(_scrollX, top);
        else if (bottom > _scrollY + BodyBounds.Height) ScrollTo(_scrollX, bottom - BodyBounds.Height);
    }

    UIElement ITouchScrollTarget.Element => this;

    bool ITouchScrollTarget.CanPanHorizontally
    {
        get
        {
            EnsureLayout();

            return PanToScroll && MaxScrollX > 0;
        }
    }

    bool ITouchScrollTarget.CanPanVertically
    {
        get
        {
            EnsureLayout();

            return PanToScroll && MaxScrollY > 0;
        }
    }

    Size ITouchScrollTarget.PanViewport => BodyBounds.Size;

    Point ITouchScrollTarget.PanScroll => new(_scrollX, _scrollY);

    Point ITouchScrollTarget.PanMaxScroll => new(MaxScrollX, MaxScrollY);

    void ITouchScrollTarget.ApplyPanScroll(Point scroll, Point overscroll)
    {
        _scrollX = scroll.X;
        _scrollY = scroll.Y;
        _overscroll = overscroll;

        // видимый диапазон строк считается по _scrollY, поэтому пересчёт
        // геометрии обязателен — иначе при броске снизу окажутся пустые полосы
        EnsureLayout();

        InvalidateVisual();
    }

    protected override void OnPointerDown(PointerEventArgs e)
    {
        if (e.Kind != PointerKind.Mouse) _touch.StopFling();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        float before = _scrollY;

        ScrollTo(_scrollX, _scrollY - (e.Delta / 120f * WheelStep));

        // событие помечаем обработанным только если действительно сдвинулись:
        // иначе грид, прокрученный до упора, съедал бы колесо у родителя
        if (_scrollY != before) e.Handled = true;
    }

    // ===== ввод =====

    private int RowAt(Point location)
    {
        EnsureLayout();

        Point abs = GetAbsolutePosition();
        float localY = location.Y - abs.Y - Padding.Top;

        Rectangle body = BodyBounds;
        float bodyTop = HeaderHeight;

        if (localY < bodyTop || localY > bodyTop + body.Height) return -1;

        int index = (int)((localY - bodyTop + _scrollY + _overscroll.Y) / RowHeight);

        return index >= 0 && index < Items.Count ? index : -1;
    }

    /// <summary>Можно ли тянуть границы столбцов мышью.</summary>
    public bool CanResizeColumns { get; set; } = true;

    /// <summary>Уже столько столбец не сузить: иначе его легко потерять
    /// совсем, а вернуть мышью будет нечем.</summary>
    public float MinColumnWidth { get; set; } = 32f;

    /// <summary>Насколько близко к границе надо подвести курсор, чтобы
    /// схватить её. Ровно по линии не попадёт никто.</summary>
    private const float ResizeGrip = 4f;

    /// <summary>Точка в собственных координатах контрола, без отступов.</summary>
    private Point ToLocal(Point location)
    {
        Point abs = GetAbsolutePosition();

        return new Point(location.X - abs.X - Padding.Left, location.Y - abs.Y - Padding.Top);
    }

    private bool IsOverHeader(Point local) => local.Y >= 0 && local.Y < HeaderHeight;

    /// <summary>Столбец, чью правую границу держит курсор, или −1.</summary>
    private int ColumnEdgeAt(Point local)
    {
        if (!CanResizeColumns || !IsOverHeader(local)) return -1;

        EnsureLayout();

        // шапка ездит вместе с телом по горизонтали, поэтому границы
        // считаем от того же смещения, с которым она рисуется
        float x = -_scrollX - _overscroll.X;

        for (int col = 0; col < _widths.Count; col++)
        {
            x += _widths[col];

            if (MathF.Abs(local.X - x) <= ResizeGrip) return col;
        }

        return -1;
    }

    private int ColumnAt(Point local)
    {
        EnsureLayout();

        float x = -_scrollX - _overscroll.X;

        for (int col = 0; col < _widths.Count; col++)
        {
            if (local.X >= x && local.X < x + _widths[col]) return col;

            x += _widths[col];
        }

        return -1;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        _suppressHeaderClick = false;

        int edge = ColumnEdgeAt(ToLocal(e.Location));
        if (edge < 0) return;

        _resizingColumn = edge;
        _resizeStartX = e.Location.X;
        _resizeStartWidth = _widths[edge];
        _suppressHeaderClick = true;

        // без захвата тяга оборвётся, как только курсор уйдёт за окно
        CaptureMouse();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_resizingColumn < 0) return;

        _resizingColumn = -1;
        ReleaseMouseCapture();
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        Point local = ToLocal(e.Location);

        if (IsOverHeader(local))
        {
            // щелчок, которым тянули границу, сортировкой не считается
            if (_suppressHeaderClick) return;

            if (!CanSortByHeaderClick) return;

            int column = ColumnAt(local);

            if (column >= 0 && Columns[column].CanSort)
            {
                SortBy(column);
                e.Handled = true;
            }

            return;
        }

        int row = RowAt(e.Location);
        if (row < 0) return;

        SelectedIndex = row;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (_resizingColumn >= 0)
        {
            // ширина задаётся фиксированной: тянуть звезду или Auto
            // бессмысленно — следующий же пересчёт вернул бы прежнее
            Columns[_resizingColumn].Width = GridLength.Fixed(
                Math.Max(MinColumnWidth, _resizeStartWidth + (e.Location.X - _resizeStartX)));

            Invalidate();
            return;
        }

        Point local = ToLocal(e.Location);

        Cursor = ColumnEdgeAt(local) >= 0 ? CursorKind.SizeWestEast : CursorKind.Default;

        int row = RowAt(e.Location);

        if (row == _hoveredRow) return;

        _hoveredRow = row;
        InvalidateVisual();
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        if (_hoveredRow < 0) return;

        _hoveredRow = -1;
        InvalidateVisual();
    }

    // ===== рисование =====

    protected override void DrawContent(Graphics g)
    {
        EnsureLayout();

        Rectangle content = this.ContentBounds;
        Rectangle body = BodyBounds;
        Font font = this.EffectiveFont;

        DrawRows(g, body, _visibleFirst, _visibleLast, font);
        DrawHeader(g, content, font);
        DrawScrollBars(g, content);
    }

    private void DrawRows(Graphics g, Rectangle body, int first, int last, Font font)
    {
        g.Save();
        g.ClipRect(body);

        for (int row = first; row <= last; row++)
        {
            float top = body.Y + (row * RowHeight) - _scrollY - _overscroll.Y;
            var rowRect = new Rectangle(new Point(body.X, top), new Size(TotalWidth, RowHeight));

            if (row == SelectedIndex) g.FillRectangle(rowRect, SelectionColor);
            else if (row == _hoveredRow) g.FillRectangle(rowRect, RowHoverColor);

            float x = body.X - _scrollX - _overscroll.X;

            for (int col = 0; col < Columns.Count; col++)
            {
                var cell = new Rectangle(
                    new Point(x + CellPadding.Left, top + CellPadding.Top),
                    new Size(
                        Math.Max(0, _widths[col] - CellPadding.Horizontal),
                        Math.Max(0, RowHeight - CellPadding.Vertical)));

                g.DrawText(Columns[col].TextOf(RowItem(row)), cell, TextColor, font,
                    Columns[col].Align, VerticalContentAlignment.Center);

                x += _widths[col];
            }

            g.DrawLine(
                new Point(body.X, top + RowHeight),
                new Point(body.X + body.Width, top + RowHeight),
                GridLineColor, 1f);
        }

        g.Restore();
    }

    private void DrawHeader(Graphics g, Rectangle content, Font font)
    {
        var headerRect = new Rectangle(
            new Point(content.X, content.Y), new Size(content.Width, HeaderHeight));

        g.FillRectangle(headerRect, HeaderColor);

        // шапка ездит по горизонтали вместе с телом, но не по вертикали —
        // ради этого грид и держит смещения сам, а не наследует PanelControl
        g.Save();
        g.ClipRect(headerRect);

        float x = content.X - _scrollX - _overscroll.X;

        for (int col = 0; col < Columns.Count; col++)
        {
            var cell = new Rectangle(
                new Point(x + CellPadding.Left, content.Y),
                new Size(Math.Max(0, _widths[col] - CellPadding.Horizontal), HeaderHeight));

            if (col == SortColumnIndex) DrawSortMarker(g, cell);

            g.DrawText(Columns[col].Header ?? string.Empty, cell, HeaderTextColor, font,
                Columns[col].Align, VerticalContentAlignment.Center);

            x += _widths[col];
        }

        g.Restore();

        g.DrawLine(
            new Point(content.X, content.Y + HeaderHeight),
            new Point(content.X + content.Width, content.Y + HeaderHeight),
            GridLineColor, 1f);
    }

    /// <summary>Треугольник направления сортировки у правого края ячейки
    /// заголовка. Место под него не резервируется: столбцов обычно немного,
    /// а отнимать ширину у всех ради одного — хуже, чем изредка наложить
    /// значок на длинный заголовок.</summary>
    private void DrawSortMarker(Graphics g, Rectangle cell)
    {
        const float half = 4f;

        float centerX = cell.X + cell.Width - half;
        float centerY = cell.Y + cell.Height / 2f;
        float direction = SortDescending ? 1f : -1f;

        Span<Point> triangle =
        [
            new Point(centerX - half, centerY - half / 2f * direction),
            new Point(centerX + half, centerY - half / 2f * direction),
            new Point(centerX, centerY + half * direction),
        ];

        g.FillPolygon(triangle, HeaderTextColor);
    }

    private void DrawScrollBars(Graphics g, Rectangle content)
    {
        if (_verticalBar)
        {
            Rectangle body = BodyBounds;

            float length = Math.Max(20f, body.Height * (body.Height / TotalHeight));
            float position = MaxScrollY <= 0 ? 0 : (body.Height - length) * (_scrollY / MaxScrollY);

            var track = new Rectangle(
                new Point(content.X + content.Width - ScrollBarThickness, body.Y),
                new Size(ScrollBarThickness, body.Height));

            g.FillRoundRectangle(track, new CornerRadius(ScrollBarThickness / 2f), ScrollTrackColor);
            g.FillRoundRectangle(
                new Rectangle(
                    new Point(track.X + 2, track.Y + position),
                    new Size(ScrollBarThickness - 4, length)),
                new CornerRadius((ScrollBarThickness - 4) / 2f), ScrollThumbColor);
        }

        if (!_horizontalBar) return;

        Rectangle bodyH = BodyBounds;

        float lengthH = Math.Max(20f, bodyH.Width * (bodyH.Width / TotalWidth));
        float positionH = MaxScrollX <= 0 ? 0 : (bodyH.Width - lengthH) * (_scrollX / MaxScrollX);

        var trackH = new Rectangle(
            new Point(content.X, content.Y + content.Height - ScrollBarThickness),
            new Size(bodyH.Width, ScrollBarThickness));

        g.FillRoundRectangle(trackH, new CornerRadius(ScrollBarThickness / 2f), ScrollTrackColor);
        g.FillRoundRectangle(
            new Rectangle(
                new Point(trackH.X + positionH, trackH.Y + 2),
                new Size(lengthH, ScrollBarThickness - 4)),
            new CornerRadius((ScrollBarThickness - 4) / 2f), ScrollThumbColor);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(
            new Size(
                Columns.Count * 100f,
                HeaderHeight + (Math.Min(Items.Count, 10) * RowHeight)),
            availableSize);
}