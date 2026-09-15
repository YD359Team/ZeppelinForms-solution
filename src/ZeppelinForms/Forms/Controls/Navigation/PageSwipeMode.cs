namespace ZeppelinForms.Forms.Controls.Navigation;

public enum PageSwipeMode
{
    None,

    /// <summary>Свайп вправо возвращает на предыдущую страницу истории.
    /// Влево не реагирует: вперёд в стеке идти некуда.</summary>
    Back,

    /// <summary>Свайп листает страницы по порядку добавления.
    /// Каждое перелистывание — обычный переход, то есть история растёт.</summary>
    Sequential,
}