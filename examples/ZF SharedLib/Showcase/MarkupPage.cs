using ZeppelinForms.Forms.Controls.Base;

namespace ZF_SharedLib.Showcase;

/// <summary>Markup: a part of the interface written in .zfml rather than in code.</summary>
internal static class MarkupPage
{
    public static UIElement Create() =>
        Demo.Page("Markup", "An interface can be written in .zfml markup: the generator turns it into the C# of its class at build time, with fields for named elements and checks of every type, property and value.",
            Demo.Section("A card in markup", new SignUpCard(),
                "SignUpCard.zfml lays it out, SignUpCard.cs handles the buttons; the greeting is bound to the name field with {Bind NameBox.Text}"));
}
