using ZeppelinForms.Input.Mouse;

namespace ZF_SharedLib.Showcase;

/// <summary>The behaviour of the card SignUpCard.zfml lays out. The constructor
/// InitializeComponent is called from is generated too: the class declares none.</summary>
public partial class SignUpCard
{
    private void OnSubmit(object? sender, MouseClickEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(EmailBox.Text))
        {
            Result.Text = "Fill in the name and the e-mail.";
            return;
        }

        Result.Text = Terms.IsChecked
            ? $"Welcome, {NameBox.Text}! A letter is on its way to {EmailBox.Text}."
            : "Accept the terms first.";
    }

    private void OnClear(object? sender, MouseClickEventArgs e)
    {
        NameBox.Text = string.Empty;
        EmailBox.Text = string.Empty;
        Terms.IsChecked = false;
        Result.Text = string.Empty;
    }
}
