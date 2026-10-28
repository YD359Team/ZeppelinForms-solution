using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

// The design of OrderForm: its controls, how they look and where they go — what WinForms'
// designer keeps in Form1.Designer.cs, written by hand. The behaviour is in OrderForm.cs.
//
// The generator gives the class a constructor that calls InitializeComponent, and runs it
// again on Hot Reload: an edit here shows in the open form. So the controls are made here,
// not where the fields are declared, and their events are wired here too.
partial class OrderForm
{
    private ComboBox _product;
    private NumericUpDown _quantity;
    private Label _total;
    private Button _cancel;
    private PrimaryButton _place;

    private void InitializeComponent()
    {
        Title = "New order";
        Size = new Size(380, 320);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _product = new ComboBox { Items = [.. Products.Select(product => (object)product.Name)], SelectedIndex = 0 };
        _product.SelectionChanged += OnOrderChanged;

        _quantity = new NumericUpDown { Minimum = 1, Maximum = 99, Value = 1 };
        _quantity.ValueChanged += OnOrderChanged;

        _total = new Label { TextStyle = TextStyle.Subtitle };

        _cancel = new Button { Text = "Cancel" };
        _cancel.Click += OnCancel;

        _place = new PrimaryButton { Text = "Place the order" };
        _place.Click += OnPlace;

        Content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            Padding = new Thickness(16),
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
            Children =
            {
                new Label { Text = "Product" },
                _product,
                new Label { Text = "Quantity" },
                _quantity,
                _total,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    MainAxisAlignment = MainAxisAlignment.End,
                    Children = { _cancel, _place },
                },
            },
        };

        ShowTotal();
    }
}
