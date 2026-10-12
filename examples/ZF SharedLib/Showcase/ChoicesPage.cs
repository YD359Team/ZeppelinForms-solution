using ZeppelinForms.Core.Collections;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZF_SharedLib.Models;

namespace ZF_SharedLib.Showcase;

/// <summary>Choosing: check boxes, radio buttons, switches, drop-down lists.</summary>
internal static class ChoicesPage
{
    public static UIElement Create()
    {
        // ===== check boxes =====

        var newsletter = new CheckBox { Text = "Subscribe to the newsletter", IsChecked = true };
        var terms = new CheckBox { Text = "I accept the terms" };
        var disabled = new CheckBox { Text = "Disabled", IsChecked = true, IsEnabled = false };

        // a third state: some of the children are checked
        var all = new CheckBox { Text = "All features", IsThreeState = true };
        var features = new[]
        {
            new CheckBox { Text = "Charts", Margin = new(24, 0, 0, 0) },
            new CheckBox { Text = "Map", Margin = new(24, 0, 0, 0), IsChecked = true },
            new CheckBox { Text = "Effects", Margin = new(24, 0, 0, 0) },
        };

        bool syncing = false;

        void FromChildren()
        {
            int on = features.Count(f => f.IsChecked);
            syncing = true;
            all.CheckedState = on == 0 ? CheckedState.Unchecked : on == features.Length ? CheckedState.Checked : CheckedState.Intermediate;
            syncing = false;
        }

        foreach (CheckBox feature in features)
            feature.CheckedChanged += (_, _) => { if (!syncing) FromChildren(); };

        all.CheckedChanged += (_, _) =>
        {
            if (syncing || all.CheckedState == CheckedState.Intermediate) return;

            syncing = true;
            foreach (CheckBox feature in features) feature.IsChecked = all.IsChecked;
            syncing = false;
        };

        FromChildren();

        var glyph = new ComboBox { Items = { "Dash", "Square", "Dot" }, SelectedIndex = 0 };
        glyph.SelectionChanged += (_, _) => all.IndeterminateGlyph = (IndeterminateGlyph)glyph.SelectedIndex;

        var boxes = Demo.Row(
            Demo.Column(newsletter, terms, disabled),
            Demo.Column([all, .. features]),
            Demo.Labeled("Indeterminate glyph", glyph));

        // ===== radio buttons =====

        Label delivery = Demo.Note("Delivery: courier");
        RadioButton Option(string text, bool isChecked = false)
        {
            var option = new RadioButton { Text = text, GroupName = "delivery" };
            if (isChecked) option.SetChecked(true);
            option.CheckedChanged += (_, _) => delivery.Text = $"Delivery: {text.ToLowerInvariant()}";
            return option;
        }

        var radios = Demo.Column(Option("Courier", isChecked: true), Option("Pick-up point"), Option("Post"), delivery);

        // ===== switches =====

        Label state = Demo.Note("Wi-Fi is on");
        var wifi = new ToggleSwitch { Text = "Wi-Fi", IsOn = true };
        wifi.Toggled += (_, _) => state.Text = wifi.IsOn ? "Wi-Fi is on" : "Wi-Fi is off";

        var switches = Demo.Column(wifi, new ToggleSwitch { Text = "Bluetooth" }, new ToggleSwitch { Text = "Airplane mode", IsEnabled = false }, state);

        // ===== drop-down lists =====

        Label chosen = Demo.Note("Nothing chosen");
        var game = new ComboBox { PlaceholderText = "Choose a game", DisplaySelector = item => ((Game)item).Title };
        game.Items.AddRange(Samples.Games);
        game.SelectionChanged += (_, _) => chosen.Text = game.SelectedItem is Game g ? $"{g.Title}, {g.Year}" : "Nothing chosen";

        Label genres = Demo.Note("Several can be checked");
        var tags = new CheckedComboBox { PlaceholderText = "Genres", Items = { "Action", "Adventure", "Puzzle", "Strategy", "RPG", "Simulation" } };
        tags.SelectionChanged += (_, _) => genres.Text = "The checked genres changed";

        var lists = Demo.Row(
            Demo.Column(Demo.Labeled("ComboBox", game), chosen),
            Demo.Column(Demo.Labeled("CheckedComboBox", tags), genres));

        return Demo.Page("Choices", "One of many, several of many, on or off.",
            Demo.Section("Check boxes", boxes, "The parent box is three-state: it shows how many of its children are checked"),
            Demo.Section("Radio buttons", radios, "One GroupName — one choice"),
            Demo.Section("Switches", switches),
            Demo.Section("Drop-down lists", lists, "DisplaySelector turns any object into its line in the list"));
    }
}
