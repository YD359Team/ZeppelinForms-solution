using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Validation;
using ZF_SharedLib.Models;

namespace ZF_SharedLib.Showcase;

/// <summary>Typing: text fields and their kinds, masks, numbers, suggestions,
/// validation and binding.</summary>
internal static class TextInputPage
{
    public static UIElement Create()
    {
        // ===== fields =====

        var plain = new TextBox { Watermark = "Type here…", Size = new(260, float.NaN) };

        var search = new TextBox { Watermark = "Search", LeadingIcon = Icons.Search, TrailingIcon = Icons.Close, Size = new(260, float.NaN) };
        search.TrailingIconClick += (_, _) => search.Text = string.Empty;

        var password = new TextBox { Watermark = "Password", PasswordChar = '•', Size = new(260, float.NaN) };
        var readOnly = new TextBox { Text = "Read only: select and copy", IsReadOnly = true, Size = new(260, float.NaN) };

        var fields = Demo.Row(
            Demo.Labeled("Plain", plain),
            Demo.Labeled("With icons — the cross clears", search),
            Demo.Labeled("Password", password),
            Demo.Labeled("Read only", readOnly));

        // ===== multi-line =====

        var notes = new TextBox
        {
            IsMultiline = true,
            WordWrap = true,
            Size = new(520, 140),
            Text = "A multi-line field wraps its lines at the edge when WordWrap is on. Up and Down move along the rows on screen.\n\nTabs\tand spaces show with ShowWhitespace.",
        };

        var wrap = new ToggleSwitch { Text = "Word wrap", IsOn = true };
        wrap.Toggled += (_, _) => notes.WordWrap = wrap.IsOn;

        var whitespace = new ToggleSwitch { Text = "Show whitespace" };
        whitespace.Toggled += (_, _) => notes.ShowWhitespace = whitespace.IsOn;

        var multiline = Demo.Column(notes, Demo.Row(wrap, whitespace));

        // ===== masks and numbers =====

        var phone = new MaskedTextBox { Mask = MaskDefinition.RussianPhone, Size = new(200, float.NaN) };
        var date = new MaskedTextBox { Mask = MaskDefinition.Date, Size = new(140, float.NaN) };
        var amount = new NumericUpDown { Minimum = 0, Maximum = 1000, Step = 0.5m, DecimalPlaces = 2, Value = 9.99m };
        var quantity = new NumericUpDown { Minimum = 1, Maximum = 99, Value = 1 };

        var masks = Demo.Row(
            Demo.Labeled("Phone", phone),
            Demo.Labeled("Date", date),
            Demo.Labeled("Price", amount),
            Demo.Labeled("Quantity", quantity));

        // ===== suggestions =====

        Label picked = Demo.Note("Type a country: the list narrows as you type");
        var country = new AutoCompleteBox
        {
            Watermark = "Country",
            ItemsSource = Samples.Countries,
            FilterMode = AutoCompleteFilter.Contains,
            Size = new(260, float.NaN),
        };
        country.SuggestionChosen += (_, e) => picked.Text = $"Chosen: {e.Text}";

        // ===== validation =====

        var email = new TextBox { Watermark = "E-mail (optional)", Validator = Validators.Email(), Size = new(260, float.NaN) };
        var name = new TextBox { Watermark = "Name (required)", Validator = Validators.Required(), Size = new(260, float.NaN) };
        var code = new TextBox { Watermark = "Code: 4 to 6 digits", Validator = Validators.Combine(Validators.Digits(), Validators.Length(4, 6)), Size = new(260, float.NaN) };

        Label result = Demo.Note("A field is judged when you leave it, or when the form is sent");

        var submit = new PrimaryButton { Text = "Send" };
        submit.Click += (_, _) =>
        {
            // Validate on every field: one that nobody touched is judged too
            bool ok = email.Validate() & name.Validate() & code.Validate();
            result.Text = ok ? "Sent" : "Some fields are wrong: hover them to read why";
        };

        var reset = new Button { Text = "Reset" };
        reset.Click += (_, _) =>
        {
            foreach (TextBox field in new[] { email, name, code })
            {
                field.Text = string.Empty;
                field.ResetValidation();
            }

            result.Text = "A field is judged when you leave it, or when the form is sent";
        };

        var validation = Demo.Column(email, name, code, Demo.Row(submit, reset), result);

        // ===== binding =====

        var source = new TextBox { Watermark = "Type, and the label follows", Size = new(260, float.NaN) };
        var mirror = new Label { Text = "…" };
        mirror.Bind(Label.TextProperty, source, nameof(TextBox.Text));

        var binding = Demo.Column(source, Demo.Row(new Label { Text = "Bound label:" }, mirror));

        return Demo.Page("Text input", "Fields for text, numbers and masked values, with suggestions, validation and binding.",
            Demo.Section("Fields", fields),
            Demo.Section("Multi-line", multiline),
            Demo.Section("Masks and numbers", masks, "A mask takes only what fits it; NumericUpDown steps with the arrows, the wheel and the keys"),
            Demo.Section("Suggestions", Demo.Column(country, picked)),
            Demo.Section("Validation", validation, "Green only for a field with text that passed; red on leaving a wrong one; an error clears as you fix it"),
            Demo.Section("Binding", binding, "label.Bind(Label.TextProperty, textBox, nameof(TextBox.Text))"));
    }
}
