namespace ZeppelinForms.Forms.Controls.Tree;

/// <summary>Разворачивает раскрытые узлы в плоский список — в том порядке,
/// в котором они видны на экране.</summary>
/// <remarks>
/// Плоский список нужен виртуализации: VirtualizingStackPanel вычисляет
/// видимый диапазон делением прокрутки на высоту строки, и для этого
/// строки обязаны быть одномерной последовательностью. Вложенные
/// контейнеры виртуализацию убивают.
/// </remarks>
internal static class TreeFlattener
{
    /// <summary>Обход итеративный, а не рекурсивный: глубина дерева задаётся
    /// данными, а не нами, и цепочка из десятков тысяч узлов переполнила бы
    /// стек прямо в раскладке.</summary>
    public static void Flatten(IReadOnlyList<TreeNode> roots, List<object> output)
    {
        output.Clear();

        if (roots.Count == 0) return;

        Stack<TreeNode> pending = new();

        for (int i = roots.Count - 1; i >= 0; i--)
            pending.Push(roots[i]);

        while (pending.Count > 0)
        {
            TreeNode node = pending.Pop();

            output.Add(node);

            if (!node.IsExpanded) continue;

            // в обратном порядке: стек вернёт их прямым
            for (int i = node.Children.Count - 1; i >= 0; i--)
                pending.Push(node.Children[i]);
        }
    }
}