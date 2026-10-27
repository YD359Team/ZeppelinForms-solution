using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;

namespace ZF_SharedLib.Showcase;

/// <summary>Markup: a part of the interface written in .zfml rather than in code.</summary>
internal static class MarkupPage
{
    public static UIElement Create(Form form) =>
        Demo.Page("Markup", "An interface can be written in .zfml markup: the generator turns it into the C# of its class at build time, with fields for named elements and checks of every type, property and value.",
            Demo.Section("A card in markup", new SignUpCard(),
                "SignUpCard.zfml lays it out, SignUpCard.cs handles the buttons; the greeting is bound to the name field with {Bind NameBox.Text}"),
            Demo.Section("A form in markup", WholeForm(form),
                "FeedbackForm.zfml is the whole window: <Form Class=\"…\"> at the root, no namespaces to declare; FeedbackForm.cs answers its two buttons"));

    /// <summary>A dialog written entirely in markup, opened and awaited as any form.</summary>
    private static UIElement WholeForm(Form form)
    {
        Label answer = Demo.Note("Nothing sent yet");

        var open = new PrimaryButton { Text = "Leave feedback" };
        open.Click += async (_, _) =>
        {
            DialogResult<Feedback> result = await new FeedbackForm().ShowDialogAsync<Feedback>(form);

            answer.Text = result is { IsAccepted: true, Value: { } feedback }
                ? $"{feedback.Rating}{(feedback.Comment.Length > 0 ? $": \"{feedback.Comment}\"" : string.Empty)}{(feedback.MayContact ? ", may be contacted" : string.Empty)}"
                : "Cancelled";
        };

        return Demo.Column(open, answer);
    }
}