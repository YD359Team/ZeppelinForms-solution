using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ZeppelinForms.Diagnostics;

namespace ZeppelinForms.Forms.Controls.Tree;

/// <summary>Что именно изменилось в узле.</summary>
public enum TreeChangeKind
{
    /// <summary>Набор видимых строк прежний — поменялось только содержимое.</summary>
    Content,

    /// <summary>Список видимых строк надо пересобрать: раскрытие, сворачивание,
    /// добавление или удаление потомков.</summary>
    Structure,
}

/// <summary>Узел дерева. Данные лежат в Content, контрол строится по нему.</summary>
public sealed class TreeNode
{
    public TreeNode()
    {
        Children.CollectionChanged += OnChildrenChanged;
    }

    public TreeNode(object? content) : this() => Content = content;

    public object? Content
    {
        get;
        set
        {
            if (Equals(field, value)) return;

            field = value;
            RaiseChanged(TreeChangeKind.Content);
        }
    }

    public ObservableCollection<TreeNode> Children { get; } = [];

    public TreeNode? Parent { get; private set; }

    public bool IsExpanded
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            RaiseChanged(TreeChangeKind.Structure);
        }
    }

    public bool HasChildren => Children.Count > 0;

    /// <summary>Глубина от корня. Считается обходом вверх, а не хранится:
    /// хранимое поле пришлось бы обновлять на каждом переносе поддерева,
    /// и однажды его забудут обновить.</summary>
    public int Level
    {
        get
        {
            int level = 0;

            for (TreeNode? node = Parent; node is not null; node = node.Parent)
                level++;

            return level;
        }
    }

    /// <summary>Изменение в любом узле поддерева. Событие поднимается
    /// к корню и раздаётся только там: TreeView подписан на один узел,
    /// а не на каждый, поэтому отписывать удалённые поддеревья не нужно —
    /// и нечего забыть.</summary>
    internal event Action<TreeNode, TreeChangeKind>? Changed;

    public void Expand() => IsExpanded = true;

    public void Collapse() => IsExpanded = false;

    /// <summary>Раскрыть всех предков, чтобы узел стал видимым.</summary>
    public void ExpandAncestors()
    {
        for (TreeNode? node = Parent; node is not null; node = node.Parent)
            node.IsExpanded = true;
    }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (TreeNode child in e.OldItems)
                if (ReferenceEquals(child.Parent, this))
                    child.Parent = null;

        if (e.NewItems is not null)
        {
            foreach (TreeNode child in e.NewItems)
            {
                // узел в двух деревьях сразу дал бы бесконечную проекцию:
                // обход пошёл бы по кругу через общего потомка
                ZfContract.Require(child.Parent is null || ReferenceEquals(child.Parent, this),
                    "Узел уже принадлежит другому родителю — сначала удалите его оттуда.");

                child.Parent = this;
            }
        }

        RaiseChanged(TreeChangeKind.Structure);
    }

    private void RaiseChanged(TreeChangeKind kind)
    {
        TreeNode root = this;

        while (root.Parent is not null)
            root = root.Parent;

        root.Changed?.Invoke(this, kind);
    }
}