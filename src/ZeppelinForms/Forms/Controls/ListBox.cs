using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class ListBox : ItemsControl, IInputElement
{
    private int _selectedIndex = -1;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int clamped = value < 0 || value >= Items.Count ? -1 : value;
            if (_selectedIndex == clamped) return;

            _selectedIndex = clamped;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    public object? SelectedItem =>
        _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null;

    public event EventHandler? SelectionChanged;

    public Color SelectionColor { get; set; } = new Color(255, 0x0D, 0x6E, 0xFD);

    public Color FocusBorderColor { get; set; } = new Color(255, 0x0D, 0x6E, 0xFD);

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    public ListBox()
    {
        SetControlDefault(BackgroundProperty, Colors.White);
    }

    protected override void DrawContent(Graphics g)
    {
        // подсветка выделенной строки рисуется до потомков:
        // SkiaRenderer вызывает Draw, затем обходит Children
        if (_selectedIndex >= 0 && _selectedIndex < Children.Count)
        {
            UIElement container = Children[_selectedIndex];

            g.FillRectangle(
                new Rectangle(
                    new Point(ContentBounds.X, container.Position.Y),
                    new Size(ContentBounds.Width, container.ActualSize.Height)),
                SelectionColor);
        }
    }

    /// <summary>В фокусе рамку подсвечиваем — база нарисует её сама.</summary>
    protected override Color CurrentBorderColor =>
        IsFocused && FocusBorderColor.A > 0 ? FocusBorderColor : BorderColor;

    /// <summary>Выбор строки по нажатию, а не по клику: содержимое строки
    /// может погасить клик, а выделение всё равно должно смениться.</summary>
    protected override void OnPreviewMouseDown(Point location)
    {
        float localY = location.Y - GetAbsolutePosition().Y;

        for (int i = 0; i < Children.Count; i++)
        {
            UIElement child = Children[i];

            if (localY >= child.Position.Y && localY < child.Position.Y + child.ActualSize.Height)
            {
                SelectedIndex = i;
                return;
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                SelectedIndex = Math.Max(0, _selectedIndex - 1);
                e.Handled = true;
                break;

            case Key.Down:
                SelectedIndex = Math.Min(Items.Count - 1, _selectedIndex + 1);
                e.Handled = true;
                break;

            case Key.Home:
                SelectedIndex = 0;
                e.Handled = true;
                break;

            case Key.End:
                SelectedIndex = Items.Count - 1;
                e.Handled = true;
                break;
        }
    }
}