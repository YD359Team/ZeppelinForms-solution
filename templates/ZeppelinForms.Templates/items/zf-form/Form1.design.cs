using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace MyApp;

// The design of Form1: its controls, how they look and where they go. What they do is in
// Form1.cs. Hot Reload runs InitializeComponent again after an edit, so the controls are
// made here — not where the fields are declared — and their events are wired here too.
partial class Form1
{
    private Label _greeting;
    private PrimaryButton _ok;

    private void InitializeComponent()
    {
        Title = "Form1";
        Size = new Size(480, 320);

        _greeting = new Label { Text = "Hello from Form1", TextStyle = TextStyle.Subtitle };

        _ok = new PrimaryButton { Text = "OK" };
        _ok.Click += OnOk;

        Content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 12,
            Padding = new Thickness(16),
            Children = { _greeting, _ok },
        };
    }
}
