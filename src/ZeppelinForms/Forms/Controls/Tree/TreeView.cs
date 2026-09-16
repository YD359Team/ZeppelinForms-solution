using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

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
public class TreeView : DecoratedPanel
{
    private readonly VirtualizingStackPanel _panel = new();
    private readonly List<object> _flat = [];
    private readonly TreeNode _root = new();

    /// <summary>Корневые узлы. Служебный корень наружу не виден: он нужен,
    /// чтобы подписка на изменения была одна, а не по узлу на каждый.</summary>
    public IList<TreeNode> Nodes => _root.Children;

    /// <summary>Отступ на уровень вложенности.</summary>
    public float Indent { get; set; } = 16f;

    public float ItemHeight
    {
        get => _panel.ItemHeight;
        set => _panel.ItemHeight = value;
    }

    /// <summary>Как получить подпись узла. По умолчанию — ToString содержимого.</summary>
    public Func<TreeNode, string>? ItemText { get; set; }

    public event EventHandler<TreeNode?>? SelectionChanged;

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
        System.Diagnostics.Debug.WriteLine($"ZF: TreeView.Rebuild, строк станет {_flat.Count}");

        TreeFlattener.Flatten(_root.Children, _flat);

        // Refresh обязателен, а не только Invalidate: UpdateRealizedRange
        // выходит рано, когда first и count не изменились, а после раскрытия
        // узла диапазон часто прежний — меняется содержимое списка целиком,
        // и без сброса строки остались бы от старой проекции
        _panel.Refresh();

        // выделенный узел мог уехать вместе со свёрнутым предком
        if (SelectedNode is not null && !_flat.Contains(SelectedNode))
            SelectedNode = null;
    }

    private UIElement CreateRow(object item) => new TreeViewItem(this, (TreeNode)item);

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