using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Forms.Controls.Tree;

/// <summary>Дерево с виртуализацией.</summary>
/// <remarks>
/// Раскрытые узлы разворачиваются в плоский список, который и отдаётся
/// VirtualizingStackPanel. Вложенные контейнеры были бы проще, но убили бы
/// виртуализацию: видимый диапазон там вычисляется делением прокрутки
/// на высоту строки, а это требует одномерной последовательности.
///
/// Панель именно вложена, а не унаследована: у неё публичные ItemsSource
/// и ItemTemplate, а у дерева они служебные — любая запись снаружи
/// разъехалась бы с проекцией.
/// </remarks>
public class TreeView : DecoratedPanel, IInputElement
{
    private readonly VirtualizingStackPanel _panel = new();
    private readonly List<object> _flat = [];
    private readonly TreeNode _root = new();

    /// <summary>Корневые узлы. Служебный корень наружу не виден: он нужен,
    /// чтобы подписка на изменения была одна, а не по узлу на каждый.</summary>
    public IList<TreeNode> Nodes => _root.Children;

    /// <summary>Отступ на уровень вложенности.</summary>
    public float Indent
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // высота строк фиксированная, поэтому отступ — только отрисовка
            InvalidateVisual();
        }
    } = 16f;

    public float ItemHeight
    {
        get => _panel.ItemHeight;
        set => _panel.ItemHeight = value;
    }

    /// <summary>Как получить подпись узла. По умолчанию — ToString содержимого.</summary>
    public Func<TreeNode, string>? ItemText
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // подписи у уже созданных строк устарели
            InvalidateVisual();
        }
    }

    public event EventHandler<TreeNode?>? SelectionChanged;

    // дерево принимает фокус: без него нет и клавиатуры
    public bool IsFocused { get; set; }

    public bool TabStop { get; set; } = true;

    public uint TabIndex { get; set; }

    public TreeNode? SelectedNode
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            field = value;

            SelectionChanged?.Invoke(this, value);

            // перерисовка, а не пересборка: набор видимых строк тот же,
            // поменялась только заливка двух из них
            InvalidateVisual();
        }
    }

    public TreeView()
    {
        _panel.ItemsSource = _flat;
        _panel.ItemTemplate = CreateRow;

        // без этого дерево выше своей коробки обрезается, и виртуализация
        // считает диапазон по вечно нулевому ScrollY
        _panel.OverflowY = Overflow.Auto;

        Children.Add(_panel);

        _root.Changed += OnTreeChanged;

        // дерево — контейнер, а не элемент управления по месту: центровать
        // его незачем, а унаследованный от UnitControl Center делает именно это
        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);

        Rebuild();
    }

    internal string GetItemText(TreeNode node) =>
        ItemText?.Invoke(node) ?? node.Content?.ToString() ?? string.Empty;

    public void ExpandAll() => SetExpandedAll(true);

    public void CollapseAll() => SetExpandedAll(false);

    private void SetExpandedAll(bool expanded)
    {
        Stack<TreeNode> pending = new();

        foreach (TreeNode node in _root.Children)
            pending.Push(node);

        while (pending.Count > 0)
        {
            TreeNode node = pending.Pop();

            if (node.HasChildren)
                node.IsExpanded = expanded;

            foreach (TreeNode child in node.Children)
                pending.Push(child);
        }
    }

    private void OnTreeChanged(TreeNode node, TreeChangeKind kind)
    {
        if (kind == TreeChangeKind.Content)
        {
            InvalidateVisual();
            return;
        }

        Rebuild();
    }

    private void Rebuild()
    {
        TreeFlattener.Flatten(_root.Children, _flat);

        // Refresh обязателен, а не только Invalidate: UpdateRealizedRange
        // выходит рано, когда first и count не изменились, а после раскрытия
        // узла диапазон часто прежний — меняется содержимое списка целиком,
        // и без сброса строки остались бы от старой проекции.
        // Сброс не пересоздаёт строки: панель сопоставляет контейнеры
        // по узлу, поэтому уже видимые строки остаются теми же объектами
        // и только сдвигаются, а создаются лишь строки новых потомков
        _panel.Refresh();

        // выделенный узел мог уехать вместе со свёрнутым предком
        if (SelectedNode is not null && !_flat.Contains(SelectedNode))
            SelectedNode = null;
    }

    private UIElement CreateRow(object item) => new TreeViewItem(this, (TreeNode)item);

    // ===== клавиатура =====

    /// <summary>Место узла в развёрнутом списке или −1, если он под
    /// свёрнутым предком и сейчас не показан.</summary>
    private int RowOf(TreeNode? node) => node is null ? -1 : _flat.IndexOf(node);

    /// <summary>Сколько строк помещается в окне — на это двигают
    /// PageUp и PageDown.</summary>
    private int PageRows =>
        ItemHeight <= 0 ? 1 : Math.Max(1, (int)(_panel.ActualSize.Height / ItemHeight) - 1);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_flat.Count == 0) return;

        TreeNode? current = SelectedNode;
        int row = RowOf(current);

        switch (e.Key)
        {
            case Key.Down:
                SelectRow(row + 1);
                break;

            case Key.Up:
                // фокус без выделения — первая стрелка выбирает край,
                // а не прыгает в никуда
                SelectRow(row < 0 ? _flat.Count - 1 : row - 1);
                break;

            case Key.Home:
                SelectRow(0);
                break;

            case Key.End:
                SelectRow(_flat.Count - 1);
                break;

            case Key.PageDown:
                SelectRow(row < 0 ? 0 : row + PageRows);
                break;

            case Key.PageUp:
                SelectRow(row < 0 ? 0 : row - PageRows);
                break;

            case Key.Right:
                // закрытый узел раскрывается, раскрытый пускает внутрь
                if (current is null) SelectRow(0);
                else if (current.HasChildren && !current.IsExpanded) current.Expand();
                else if (current.HasChildren) SelectRow(row + 1);
                else return;

                break;

            case Key.Left:
                // раскрытый сворачивается, свёрнутый отдаёт фокус родителю
                if (current is null) SelectRow(0);
                else if (current.IsExpanded && current.HasChildren) current.Collapse();
                else if (current.Parent is { } parent && RowOf(parent) >= 0) SelectNode(parent);
                else return;

                break;

            case Key.Enter:
            case Key.Space:
                if (current is not { HasChildren: true }) return;

                current.IsExpanded = !current.IsExpanded;
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    private void SelectRow(int row)
    {
        if (_flat.Count == 0) return;

        SelectNode((TreeNode)_flat[Math.Clamp(row, 0, _flat.Count - 1)]);
    }

    private void SelectNode(TreeNode node)
    {
        SelectedNode = node;

        ScrollIntoView(node);
    }

    /// <summary>Подтянуть узел в видимую часть. Узел под свёрнутым предком
    /// не показывается вовсе — его и подтягивать некуда.</summary>
    public void ScrollIntoView(TreeNode node)
    {
        int row = RowOf(node);
        if (row < 0 || ItemHeight <= 0) return;

        float top = row * ItemHeight;
        float bottom = top + ItemHeight;
        float viewport = _panel.ActualSize.Height;

        if (top < _panel.ScrollY)
            _panel.ScrollTo(_panel.ScrollX, top);
        else if (bottom > _panel.ScrollY + viewport)
            _panel.ScrollTo(_panel.ScrollX, bottom - viewport);
    }

    protected override Size MeasureContentOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        _panel.Measure(inner);

        var content = new Size(
            _panel.DesiredSize.Width + Padding.Horizontal,
            _panel.DesiredSize.Height + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }

    protected override void ArrangeContentOverride(Size finalSize)
    {
        _panel.Arrange(new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, finalSize.Width - Padding.Horizontal),
                Math.Max(0, finalSize.Height - Padding.Vertical))));
    }
}