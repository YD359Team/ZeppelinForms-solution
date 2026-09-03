using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class ListBox : ItemsControl, IInputElement, IBorderedElement
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
            Invalidate();
        }
    }

    public object? SelectedItem =>
        _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null;

    public event EventHandler? SelectionChanged;

    public Color SelectionColor { get; set; } = new Color(255, 0x0D, 0x6E, 0xFD);

    // IBorderedElement
    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 1f;

    // IInputElement
    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    public ListBox()
    {
        Background = Colors.White;
    }

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRectangle(this.LocalBounds, Background);

        // подсветка выделенной строки рисуется ДО детей —
        // SkiaRenderer вызывает panel.Draw() перед обходом Children,
        // так что она окажется под текстом, а не поверх
        if (_selectedIndex >= 0 && _selectedIndex < Children.Count)
        {
            var container = Children[_selectedIndex];
            var highlight = new Rectangle(
                new Point(ContentBounds.X, container.Position.Y),
                new Size(ContentBounds.Width, container.ActualSize.Height));

            g.FillRectangle(highlight, SelectionColor);
        }

        if (BorderWidth > 0)
            g.DrawRectangle(this.LocalBounds, IsFocused ? new Color(255, 0x0D, 0x6E, 0xFD) : BorderColor, BorderWidth);
    }

	protected override void OnPreviewMouseDown(Point args)
	{
		float localY = args.Y - GetAbsolutePosition().Y;

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

public enum PageTransition { None, Fade, SlideLeft, SlideRight, SlideUp, SlideDown }
