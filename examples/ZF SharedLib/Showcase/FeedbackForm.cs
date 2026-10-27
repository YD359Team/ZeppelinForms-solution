using ZeppelinForms.Animation;
using ZeppelinForms.Input.Mouse;

namespace ZF_SharedLib.Showcase;

/// <summary>What was written in <see cref="FeedbackForm"/>.</summary>
public sealed record Feedback(string Rating, string Comment, bool MayContact);

/// <summary>The behaviour of the form FeedbackForm.zfml lays out. The markup declares the
/// class, its fields and a constructor that builds it: this part only answers the
/// buttons.</summary>
public partial class FeedbackForm
{
    private void OnSend(object? sender, MouseClickEventArgs e) =>
        Accept(new Feedback(Rating.SelectedItem as string ?? "Not rated", Comment.Text ?? string.Empty, MayContact.IsChecked));

    private void OnCancel(object? sender, MouseClickEventArgs e) => Cancel();
}