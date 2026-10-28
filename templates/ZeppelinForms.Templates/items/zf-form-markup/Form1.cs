using ZeppelinForms.Forms;
using ZeppelinForms.Input.Mouse;

namespace MyApp;

/// <summary>What Form1 does. It is laid out in Form1.zfml.</summary>
/// <remarks>The constructor is generated from the markup: it calls InitializeComponent. A
/// constructor of the form's own calls InitializeComponent() first.</remarks>
public partial class Form1 : Form
{
    private void OnOk(object? sender, MouseClickEventArgs e) => Close();
}
