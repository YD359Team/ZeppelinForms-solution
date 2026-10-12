using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Forms.Validation;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>When a field shows its validation result: not before the user has
/// changed it and left it, or a form asked; then "praise at once, blame late".</summary>
[Collection("Platform")]
public class ValidationTimingTests
{
    private static (Form Form, TextBox Box, TextBox Other) Create(Func<string, string?> validator)
    {
        var box = new TextBox { Validator = validator };
        var other = new TextBox();

        var form = new Form
        {
            Size = new Size(400, 200),
            Content = new StackPanel { Orientation = Orientation.Vertical, Children = { box, other } },
        };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, box, other);
    }

    private static void Type(Form form, string text)
    {
        foreach (char c in text)
            form.OnTextInput(c);
    }

    private static void Erase(Form form, int count)
    {
        for (int i = 0; i < count; i++)
            form.OnKeyDown(Key.Backspace, KeyModifiers.None, isRepeat: false);
    }

    [Fact]
    public void PassingThroughAnUntouchedFieldShowsNothing()
    {
        // the example's e-mail field lit up green when the focus merely passed it
        var (form, box, other) = Create(Validators.Email());

        form.FocusElement(box);
        form.FocusElement(other);

        Assert.Equal(ValidationState.None, box.ValidationState);
        Assert.False(box.HasPseudoClass(PseudoClass.Valid));
        Assert.False(box.HasPseudoClass(PseudoClass.Invalid));
    }

    [Fact]
    public void AFieldIsJudgedWhenTheUserLeavesIt()
    {
        var (form, box, other) = Create(Validators.Email());

        form.FocusElement(box);
        Type(form, "ada@");

        // not halfway through the address
        Assert.Equal(ValidationState.None, box.ValidationState);

        form.FocusElement(other);

        Assert.Equal(ValidationState.Error, box.ValidationState);
        Assert.True(box.HasPseudoClass(PseudoClass.Invalid));
    }

    [Fact]
    public void AFixedErrorClearsWhileTyping()
    {
        var (form, box, other) = Create(Validators.Email());

        form.FocusElement(box);
        Type(form, "ada@example");
        form.FocusElement(other);
        Assert.Equal(ValidationState.Error, box.ValidationState);

        form.FocusElement(box);
        Type(form, ".com");

        // praised at once, without leaving the field
        Assert.Equal(ValidationState.Success, box.ValidationState);
        Assert.Null(box.ValidationMessage);
    }

    [Fact]
    public void ACorrectFieldBeingRetypedGoesNeutralNotRed()
    {
        var (form, box, other) = Create(Validators.Email());

        form.FocusElement(box);
        Type(form, "ada@example.com");
        form.FocusElement(other);
        Assert.Equal(ValidationState.Success, box.ValidationState);

        form.FocusElement(box);
        Erase(form, 4);

        // blamed late: neutral while typing
        Assert.Equal(ValidationState.None, box.ValidationState);

        form.FocusElement(other);

        Assert.Equal(ValidationState.Error, box.ValidationState);
    }

    [Fact]
    public void AnEmptyOptionalFieldIsNotPraised()
    {
        var (_, box, _) = Create(Validators.Email());

        Assert.True(box.Validate());

        Assert.Equal(ValidationState.None, box.ValidationState);
        Assert.False(box.HasPseudoClass(PseudoClass.Valid));
    }

    [Fact]
    public void ValidateShowsTheErrorOfAnUntouchedRequiredField()
    {
        // a form's submit button: the empty required field is blamed at once
        var (_, box, _) = Create(Validators.Required());

        Assert.False(box.Validate());

        Assert.Equal(ValidationState.Error, box.ValidationState);
    }

    [Fact]
    public void ResetValidationMakesTheFieldNewAgain()
    {
        var (form, box, other) = Create(Validators.Required());

        box.Validate();
        Assert.Equal(ValidationState.Error, box.ValidationState);

        box.ResetValidation();
        Assert.Equal(ValidationState.None, box.ValidationState);

        // and passing through it judges nothing again
        form.FocusElement(box);
        form.FocusElement(other);

        Assert.Equal(ValidationState.None, box.ValidationState);
    }
}