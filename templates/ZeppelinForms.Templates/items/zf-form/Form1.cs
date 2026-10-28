using ZeppelinForms.Forms;
using ZeppelinForms.Input.Mouse;

namespace MyApp;

/// <summary>What Form1 does. Its controls are made and laid out in Form1.design.cs.</summary>
/// <remarks>The constructor is generated: it calls InitializeComponent. A constructor of
/// the form's own — one that takes what the form shows — calls InitializeComponent() first.</remarks>
public partial class Form1 : Form
{
    private void OnOk(object? sender, MouseClickEventArgs e) => Close();
}
