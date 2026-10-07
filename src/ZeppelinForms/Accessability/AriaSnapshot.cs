using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Accessibility;

/// <summary>The accessibility tree of the windows on a canvas, as ARIA: the roles,
/// names and aria-* attributes of the DOM the browser mirrors it into.</summary>
/// <remarks>
/// <para>
/// Platform-neutral on purpose: the browser bridge only sends this JSON across and
/// applies it to the DOM, and the mapping — the part with the decisions in it —
/// lives where it is tested with the rest of the core.
/// </para>
/// <para>
/// The shape: { label, focus, windows: [ { id, label, dialog, hidden, x, y, w, h,
/// children } ] }, and each node { id, role, label, text, attrs, x, y, w, h,
/// children }, its position relative to its parent node — the DOM places each node
/// absolutely inside the one above it.
/// </para>
/// </remarks>
internal static class AriaSnapshot
{
    /// <summary>A window on the canvas: its form, its place, whether it is a modal
    /// dialog over the windows below.</summary>
    public readonly record struct Layer(Form Form, Point Origin, bool IsDialog);

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        // the text goes into the DOM through the API, not into markup:
        // escaping for HTML would only bloat the message
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <param name="idOf">The stable DOM id of a peer.</param>
    /// <param name="peers">Filled with every peer written, by its id — the bridge
    /// finds the peer of a DOM node by it when a screen reader acts on the node.</param>
    public static string Build(
        IReadOnlyList<Layer> layers,
        Func<AccessibilityPeer, string> idOf,
        Dictionary<string, AccessibilityPeer> peers)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, WriterOptions))
        {
            json.WriteStartObject();

            Layer? root = layers.Count > 0 ? layers[0] : null;
            Layer? top = layers.Count > 0 ? layers[^1] : null;

            json.WriteString("label", root?.Form.Title ?? string.Empty);

            // the keyboard's place: the focused element of the top window,
            // or the current item inside it
            if (top?.Form.FocusedElementForAccessibility?.GetAccessibilityPeer() is { } focused)
                json.WriteString("focus", idOf(focused.FocusedDescendant ?? focused));
            else
                json.WriteNull("focus");

            json.WriteStartArray("windows");

            for (int i = 0; i < layers.Count; i++)
            {
                Layer layer = layers[i];
                AccessibilityPeer windowPeer = layer.Form.GetAccessibilityPeer();

                json.WriteStartObject();
                json.WriteString("id", idOf(windowPeer));
                json.WriteString("label", windowPeer.Name);
                json.WriteBoolean("dialog", layer.IsDialog);

                // a modal dialog leaves the windows under it out of reach: they are
                // hidden from screen readers as they are from the keyboard
                json.WriteBoolean("hidden", layers.Skip(i + 1).Any(above => above.IsDialog));

                WriteBounds(json, new Rectangle(layer.Origin, layer.Form.ClientSize), default);

                json.WriteStartArray("children");

                foreach (AccessibilityPeer child in windowPeer.Children)
                    WriteNode(json, child, Point.Empty, idOf, peers);

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteNode(
        Utf8JsonWriter json, AccessibilityPeer peer, Point parentOrigin,
        Func<AccessibilityPeer, string> idOf, Dictionary<string, AccessibilityPeer> peers)
    {
        string id = idOf(peer);
        peers[id] = peer;

        var attrs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        (string? role, string? label, string? text) = Describe(peer, attrs);

        json.WriteStartObject();
        json.WriteString("id", id);

        if (role is not null) json.WriteString("role", role);
        if (label is not null) json.WriteString("label", label);
        if (text is not null) json.WriteString("text", text);

        json.WriteStartObject("attrs");
        foreach ((string name, string value) in attrs)
            json.WriteString(name, value);
        json.WriteEndObject();

        Rectangle bounds = peer.Bounds;
        WriteBounds(json, bounds, parentOrigin);

        json.WriteStartArray("children");

        foreach (AccessibilityPeer child in peer.Children)
            WriteNode(json, child, bounds.Position, idOf, peers);

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteBounds(Utf8JsonWriter json, Rectangle bounds, Point parentOrigin)
    {
        json.WriteNumber("x", Math.Round(bounds.X - parentOrigin.X, 1));
        json.WriteNumber("y", Math.Round(bounds.Y - parentOrigin.Y, 1));
        json.WriteNumber("w", Math.Round(Math.Max(0, bounds.Width), 1));
        json.WriteNumber("h", Math.Round(Math.Max(0, bounds.Height), 1));
    }

    /// <summary>The ARIA role of a peer, its accessible name and its text content,
    /// and the aria-* attributes of its states.</summary>
    /// <remarks>
    /// The name goes into aria-label for every role but static text, which is read
    /// by its content. A field and a combo box carry their value as content too:
    /// that is where ARIA has screen readers look for it.
    /// </remarks>
    internal static (string? Role, string? Label, string? Text) Describe(
        AccessibilityPeer peer, IDictionary<string, string> attrs)
    {
        AccessibilityStates states = peer.States;
        string name = peer.Name;
        string? label = name.Length > 0 ? name : null;
        string? text = null;

        string? role = peer.Role switch
        {
            AccessibilityRole.None => null,
            AccessibilityRole.Window => "group",
            AccessibilityRole.Dialog => "dialog",
            AccessibilityRole.Group => "group",
            AccessibilityRole.Text => null,
            AccessibilityRole.Heading => "heading",
            AccessibilityRole.Link => "link",
            AccessibilityRole.Image => "img",
            AccessibilityRole.Button or AccessibilityRole.ToggleButton or AccessibilityRole.SplitButton => "button",
            AccessibilityRole.CheckBox => "checkbox",
            AccessibilityRole.RadioButton => "radio",
            AccessibilityRole.Switch => "switch",
            AccessibilityRole.TextBox => "textbox",
            AccessibilityRole.SpinButton => "spinbutton",
            AccessibilityRole.Slider => "slider",
            AccessibilityRole.ProgressBar => "progressbar",
            AccessibilityRole.ScrollBar => "scrollbar",
            AccessibilityRole.ComboBox => "combobox",

            // a list of choices is a listbox, its rows options; a list of things
            // is a list of list items
            AccessibilityRole.List => peer.Element is ListBox ? "listbox" : "list",
            AccessibilityRole.ListItem => states.HasFlag(AccessibilityStates.Selectable) ? "option" : "listitem",

            AccessibilityRole.Tree => "tree",
            AccessibilityRole.TreeItem => "treeitem",
            AccessibilityRole.Table => "grid",
            AccessibilityRole.Row => "row",
            AccessibilityRole.Cell => "gridcell",
            AccessibilityRole.ColumnHeader => "columnheader",
            AccessibilityRole.TabList => "tablist",
            AccessibilityRole.Tab => "tab",
            AccessibilityRole.TabPanel => "tabpanel",
            AccessibilityRole.MenuBar => "menubar",
            AccessibilityRole.Menu => "menu",
            AccessibilityRole.MenuItem => "menuitem",
            AccessibilityRole.Separator => "separator",
            AccessibilityRole.Calendar => "group",
            AccessibilityRole.ToolTip => "tooltip",
            _ => null,
        };

        switch (peer.Role)
        {
            // static text is read by what it says
            case AccessibilityRole.Text:
                text = name;
                label = null;
                break;

            case AccessibilityRole.Heading:
                attrs["aria-level"] = Number(Math.Clamp(peer.HeadingLevel, 1, 6));
                text = name;
                label = null;
                break;

            // the value is the content: what was typed, what was chosen
            case AccessibilityRole.TextBox or AccessibilityRole.ComboBox:
                text = peer.Value;
                break;

            case AccessibilityRole.ToggleButton:
                attrs["aria-pressed"] = Bool(states.HasFlag(AccessibilityStates.Checked));
                break;

            case AccessibilityRole.SplitButton:
                attrs["aria-haspopup"] = "menu";
                break;

            case AccessibilityRole.CheckBox or AccessibilityRole.RadioButton or AccessibilityRole.Switch:
                attrs["aria-checked"] = states.HasFlag(AccessibilityStates.Mixed) ? "mixed"
                    : Bool(states.HasFlag(AccessibilityStates.Checked));
                break;

            case AccessibilityRole.Calendar:
                attrs["aria-roledescription"] = "calendar";
                text = peer.Value;
                break;

            // the sort order, as the peer gives it: "ascending" or "descending"
            case AccessibilityRole.ColumnHeader when peer.Value is { } sort:
                attrs["aria-sort"] = sort;
                break;

            case AccessibilityRole.MenuItem
                when (states & (AccessibilityStates.Expanded | AccessibilityStates.Collapsed)) != 0:
                attrs["aria-haspopup"] = "menu";
                break;

            case AccessibilityRole.Image when label is null:
                // a nameless picture is decoration: nothing to read
                role = "presentation";
                break;
        }

        if (peer.Range is { } range && !states.HasFlag(AccessibilityStates.Busy))
        {
            attrs["aria-valuemin"] = Number(range.Minimum);
            attrs["aria-valuemax"] = Number(range.Maximum);
            attrs["aria-valuenow"] = Number(range.Value);

            if (peer.Value is { } valueText)
                attrs["aria-valuetext"] = valueText;
        }

        if (states.HasFlag(AccessibilityStates.Busy)) attrs["aria-busy"] = "true";
        if (states.HasFlag(AccessibilityStates.Disabled)) attrs["aria-disabled"] = "true";
        if (states.HasFlag(AccessibilityStates.ReadOnly)) attrs["aria-readonly"] = "true";
        if (states.HasFlag(AccessibilityStates.Invalid)) attrs["aria-invalid"] = "true";
        if (states.HasFlag(AccessibilityStates.Multiline)) attrs["aria-multiline"] = "true";
        if (states.HasFlag(AccessibilityStates.MultiSelectable)) attrs["aria-multiselectable"] = "true";
        if (states.HasFlag(AccessibilityStates.Modal)) attrs["aria-modal"] = "true";

        if (states.HasFlag(AccessibilityStates.Expanded)) attrs["aria-expanded"] = "true";
        else if (states.HasFlag(AccessibilityStates.Collapsed)) attrs["aria-expanded"] = "false";

        // selection where the role has it; a radio button says it by aria-checked
        if (role is "option" or "tab" or "treeitem" or "row" or "gridcell")
            attrs["aria-selected"] = Bool(states.HasFlag(AccessibilityStates.Selected));

        if (peer.Position is { } position)
        {
            attrs["aria-posinset"] = Number(position.Index);
            attrs["aria-setsize"] = Number(position.Count);
        }

        if (peer.Level > 0) attrs["aria-level"] = Number(peer.Level);

        if (peer.Description is { Length: > 0 } description) attrs["aria-description"] = description;

        // "Alt+S" is already the syntax of aria-keyshortcuts
        if (peer.AccessKey is { } key) attrs["aria-keyshortcuts"] = key;

        if (peer.LiveSetting != AccessibilityLiveSetting.Off)
            attrs["aria-live"] = peer.LiveSetting == AccessibilityLiveSetting.Assertive ? "assertive" : "polite";

        return (role, label, text);
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Number(double value) => value.ToString("G", CultureInfo.InvariantCulture);
}