using ZeppelinForms.Input.Commands;
using ZeppelinForms.Input.Mouse;

namespace ZF_SharedLib.Showcase;

/// <summary>The behaviour of the card SignUpCard.zfml lays out.</summary>
public partial class SignUpCard
{
    /// <summary>The markup's Command="Clear": made before InitializeComponent, which
    /// hands it to the button.</summary>
    public Command Clear { get; }

    public SignUpCard()
    {
        Clear = new Command(ClearFields, () =>
            !string.IsNullOrEmpty(NameBox.Text) || !string.IsNullOrEmpty(EmailBox.Text) || Terms.IsChecked);

        InitializeComponent();

        // the button asks again whenever what CanExecute reads changes
        NameBox.TextChanged += (_, _) => Clear.NotifyCanExecuteChanged();
        EmailBox.TextChanged += (_, _) => Clear.NotifyCanExecuteChanged();
        Terms.CheckedChanged += (_, _) => Clear.NotifyCanExecuteChanged();
    }

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

    private void ClearFields()
    {
        NameBox.Text = string.Empty;
        EmailBox.Text = string.Empty;
        Terms.IsChecked = false;
        Result.Text = string.Empty;
    }
}