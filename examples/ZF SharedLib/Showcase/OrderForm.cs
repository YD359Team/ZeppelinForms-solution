using ZeppelinForms.Input.Mouse;

namespace ZF_SharedLib.Showcase;

/// <summary>An order: what, how many, for how much.</summary>
public sealed record Order(string Product, int Quantity, decimal Total);

/// <summary>
/// A form whose design is apart from its code, as in WinForms: OrderForm.design.cs makes
/// and lays out the controls, this part only answers them. No constructor here — the
/// generator writes one that calls InitializeComponent.
/// </summary>
public partial class OrderForm : ZeppelinForms.Forms.Form
{
    private static readonly (string Name, decimal Price)[] Products =
    [
        ("Zeppelin model, 1:500", 49.90m),
        ("Mooring mast", 19.50m),
        ("Hangar, small", 120m),
    ];

    private decimal Total => Products[Math.Max(_product.SelectedIndex, 0)].Price * _quantity.Value;

    private void OnOrderChanged(object? sender, EventArgs e) => ShowTotal();

    private void ShowTotal() => _total.Text = $"Total: {Total:0.00}";

    private void OnPlace(object? sender, MouseClickEventArgs e) =>
        Accept(new Order(Products[Math.Max(_product.SelectedIndex, 0)].Name, (int)_quantity.Value, Total));

    private void OnCancel(object? sender, MouseClickEventArgs e) => Cancel();
}
