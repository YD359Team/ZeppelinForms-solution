using ZF_SharedLib.Models;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;

namespace ZF_SharedLib.Showcase;

/// <summary>Markup: a part of the interface written in .zfml rather than in code — and a
/// design written in code, but apart from the behaviour.</summary>
internal static class MarkupPage
{
    public static UIElement Create(Form form) =>
        Demo.Page("Markup", "An interface can be written in .zfml markup: the generator turns it into the C# of its class at build time, with fields for named elements and checks of every type, property and value.",
            Demo.Section("A card in markup", new SignUpCard(),
                "SignUpCard.zfml lays it out, SignUpCard.cs handles the buttons; the greeting is bound to the name field with {Bind NameBox.Text}"),
            Demo.Section("A form in markup", WholeForm(form),
                "FeedbackForm.zfml is the whole window: <Form Class=\"…\"> at the root, no namespaces to declare; FeedbackForm.cs answers its two buttons"),
            Demo.Section("A view model", new ProfileCard { DataContext = new ProfileViewModel() },
                "ProfileCard.zfml binds to ProfileViewModel, its DataContext: x:DataType says what it is, {Bind Name, Mode=TwoWay} and {Bind Address.City} are checked and compiled at build time, Command=\"{Bind Save}\" follows the command's CanExecute"),
            Demo.Section("Design apart from code", DesignedForm(form),
                "OrderForm.design.cs makes and lays out the controls in InitializeComponent, OrderForm.cs only answers them — the WinForms split, by hand; the generator adds the constructor and Hot Reload"));

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

    /// <summary>A dialog whose design is a .design.cs: code too, but apart from its behaviour.</summary>
    private static UIElement DesignedForm(Form form)
    {
        Label answer = Demo.Note("No order yet");

        var open = new PrimaryButton { Text = "New order" };
        open.Click += async (_, _) =>
        {
            DialogResult<Order> result = await new OrderForm().ShowDialogAsync<Order>(form);

            answer.Text = result is { IsAccepted: true, Value: { } order }
                ? $"{order.Quantity} × {order.Product}, {order.Total:0.00}"
                : "Cancelled";
        };

        return Demo.Column(open, answer);
    }
}