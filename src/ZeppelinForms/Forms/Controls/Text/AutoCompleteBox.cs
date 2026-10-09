using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Forms.Controls.Text;

/// <summary>How <see cref="AutoCompleteBox"/> matches its items against the text.</summary>
public enum AutoCompleteFilter : byte
{
    /// <summary>The item starts with the text.</summary>
    StartsWith,

    /// <summary>The text is anywhere in the item.</summary>
    Contains,

    /// <summary>A word of the item starts with the text: "york" finds "New York".</summary>
    WordStartsWith,
}

/// <summary>A suggestion was taken: clicked, or chosen with Enter.</summary>
public sealed class SuggestionChosenEventArgs(object item, string text) : EventArgs
{
    /// <summary>The item from <see cref="AutoCompleteBox.ItemsSource"/> or the provider.</summary>
    public object Item { get; } = item;

    /// <summary>The text it put into the field.</summary>
    public string Text { get; } = text;
}

/// <summary>
/// A text field that suggests as it is typed into: the matching items drop down
/// under it, Up and Down go through them, Enter or a click takes one.
/// </summary>
/// <remarks>
/// <para>
/// The field keeps the focus while the list is open — the list is a picture of the
/// choices, not a place to type — so typing goes on narrowing it.
/// </para>
/// <para>
/// The items come from <see cref="ItemsSource"/>, filtered by <see cref="FilterMode"/>,
/// or from <see cref="SuggestionProvider"/>, which answers for the text as a whole —
/// a search over a database, a fuzzy match.
/// </para>
/// </remarks>
public class AutoCompleteBox : TextBox
{
    private readonly FlyoutHost _flyout;
    private ListBox? _list;
    private List<object> _matches = [];

    /// <summary>The text is being written by the box itself — a suggestion taken:
    /// that must not open the list again.</summary>
    private bool _accepting;

    /// <summary>The list's highlight is being moved by the keys, not by a click.</summary>
    private bool _highlighting;

    public AutoCompleteBox()
    {
        _flyout = new FlyoutHost(this);
        _flyout.Closed += (_, _) => _list = null;
    }

    /// <summary>Only what is typed asks for suggestions: a text set from code, or by
    /// taking a suggestion, isn't a question to answer.</summary>
    protected override void OnTextEdited()
    {
        base.OnTextEdited();

        if (_accepting || !IsFocused) return;

        UpdateSuggestions();
    }

    // ===== source =====

    /// <summary>The items to suggest from.</summary>
    public IEnumerable<object>? ItemsSource { get; set; }

    /// <summary>How an item reads, in the list and in the field once taken. ToString() by default.</summary>
    public Func<object, string>? TextSelector { get; set; }

    /// <summary>Suggestions for the text, instead of filtering <see cref="ItemsSource"/>.</summary>
    public Func<string, IEnumerable<object>>? SuggestionProvider { get; set; }

    public AutoCompleteFilter FilterMode { get; set; } = AutoCompleteFilter.StartsWith;

    public bool IsCaseSensitive { get; set; }

    /// <summary>How many characters wake the suggestions: 0 — an empty field shows them
    /// all once Down is pressed.</summary>
    public int MinimumPrefixLength { get; set; } = 1;

    /// <summary>At most this many suggestions: a long list helps no one choose.</summary>
    public int MaxSuggestions { get; set; } = 50;

    public float MaxDropDownHeight { get; set; } = 200f;

    /// <summary>The item last taken; null once the text is edited away from it.</summary>
    public object? SelectedItem { get; private set; }

    public bool IsDropDownOpen => _flyout.IsOpen;

    /// <summary>The suggestions shown now.</summary>
    public IReadOnlyList<object> Suggestions => _matches;

    /// <summary>The suggestion Up and Down have reached, or −1.</summary>
    public int HighlightedIndex => _list?.SelectedIndex ?? -1;

    public event EventHandler<SuggestionChosenEventArgs>? SuggestionChosen;

    private string TextOf(object item) => TextSelector?.Invoke(item) ?? item.ToString() ?? string.Empty;

    // ===== matching =====

    private IEnumerable<object> Match(string query)
    {
        if (SuggestionProvider is { } provider)
            return provider(query);

        if (ItemsSource is null)
            return [];

        StringComparison comparison = IsCaseSensitive ? StringComparison.Ordinal : StringComparison.CurrentCultureIgnoreCase;

        return ItemsSource.Where(item => Matches(TextOf(item), query, comparison));
    }

    private bool Matches(string text, string query, StringComparison comparison)
    {
        if (query.Length == 0) return true;

        switch (FilterMode)
        {
            case AutoCompleteFilter.Contains:
                return text.Contains(query, comparison);

            case AutoCompleteFilter.WordStartsWith:
                for (int i = 0; i < text.Length; i++)
                {
                    bool wordStart = i == 0 || !char.IsLetterOrDigit(text[i - 1]);

                    if (wordStart && string.Compare(text, i, query, 0, query.Length, comparison) == 0)
                        return true;
                }

                return false;

            default:
                return text.StartsWith(query, comparison);
        }
    }

    // ===== the list =====

    /// <summary>Match the text again and show the result, or close the list when there
    /// is nothing to show.</summary>
    public void UpdateSuggestions()
    {
        string query = Text ?? string.Empty;

        // the text no longer reads as the item taken before
        if (SelectedItem is not null && TextOf(SelectedItem) != query)
            SelectedItem = null;

        if (query.Length < MinimumPrefixLength)
        {
            CloseDropDown();
            return;
        }

        _matches = [.. Match(query).Take(Math.Max(1, MaxSuggestions))];

        // the only suggestion is what is already typed: nothing to suggest
        if (_matches.Count == 0 || (_matches.Count == 1 && TextOf(_matches[0]) == query))
        {
            CloseDropDown();
            return;
        }

        if (_list is not null)
        {
            Fill(_list);
            return;
        }

        _flyout.Open(BuildList(), FlyoutPlacement.Bottom);
    }

    private ListBox BuildList()
    {
        var list = new ListBox
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            OverflowY = Overflow.Auto,

            // a click on a suggestion leaves the focus in the field
            TabStop = false,
        };

        list.SelectionChanged += (_, _) =>
        {
            if (_highlighting || list.SelectedIndex < 0) return;

            // a click: the suggestion is taken
            Accept(list.SelectedIndex);
        };

        _list = list;
        Fill(list);

        return list;
    }

    private void Fill(ListBox list)
    {
        _highlighting = true;

        try
        {
            list.Items.Clear();

            foreach (object item in _matches)
                list.Items.Add(TextOf(item));

            list.SelectedIndex = -1;
        }
        finally
        {
            _highlighting = false;
        }

        // first learn how much the list needs, and only then limit it
        list.Size = new Size(float.NaN, float.NaN);
        list.Measure(new Size(ActualSize.Width, float.PositiveInfinity));

        list.Size = new Size(Math.Max(ActualSize.Width, list.DesiredSize.Width), Math.Min(list.DesiredSize.Height, MaxDropDownHeight));
        list.ScrollTo(0, 0);
    }

    public void CloseDropDown()
    {
        _flyout.Close();
        _list = null;
    }

    private void Highlight(int index)
    {
        if (_list is null || _matches.Count == 0) return;

        index = Math.Clamp(index, 0, _matches.Count - 1);

        _highlighting = true;

        try
        {
            _list.SelectedIndex = index;
        }
        finally
        {
            _highlighting = false;
        }

        KeepVisible(_list, index);
    }

    /// <summary>Scroll the list so that a row Up or Down reached is in sight.</summary>
    private static void KeepVisible(ListBox list, int index)
    {
        if (index >= list.Children.Count) return;

        float top = 0;

        for (int i = 0; i < index; i++)
            top += list.Children[i].ActualSize.Height;

        float bottom = top + list.Children[index].ActualSize.Height;
        float visible = list.ActualSize.Height - list.Padding.Vertical - list.BorderWidth * 2;

        if (top < list.ScrollY) list.ScrollTo(0, top);
        else if (bottom > list.ScrollY + visible) list.ScrollTo(0, bottom - visible);
    }

    /// <summary>Take a suggestion: its text goes into the field, the caret after it.</summary>
    private void Accept(int index)
    {
        if (index < 0 || index >= _matches.Count) return;

        object item = _matches[index];
        string text = TextOf(item);

        _accepting = true;

        try
        {
            Text = text;
            Select(text.Length, 0);
        }
        finally
        {
            _accepting = false;
        }

        SelectedItem = item;
        CloseDropDown();

        SuggestionChosen?.Invoke(this, new SuggestionChosenEventArgs(item, text));
    }

    // ===== keyboard and focus =====

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (IsDropDownOpen)
        {
            switch (e.Key)
            {
                case Key.Down:
                    Highlight(HighlightedIndex + 1);
                    e.Handled = true;
                    return;

                case Key.Up:
                    Highlight(HighlightedIndex <= 0 ? 0 : HighlightedIndex - 1);
                    e.Handled = true;
                    return;

                case Key.PageDown:
                    Highlight(HighlightedIndex + 8);
                    e.Handled = true;
                    return;

                case Key.PageUp:
                    Highlight(HighlightedIndex - 8);
                    e.Handled = true;
                    return;

                case Key.Enter when HighlightedIndex >= 0:
                    Accept(HighlightedIndex);
                    e.Handled = true;
                    return;

                case Key.Escape:
                    CloseDropDown();
                    e.Handled = true;
                    return;

                case Key.Tab:
                    // Tab leaves the field; what was highlighted is taken, as in a browser
                    if (HighlightedIndex >= 0) Accept(HighlightedIndex);
                    else CloseDropDown();
                    break;
            }
        }
        else if (e.Key == Key.Down && !IsMultiline)
        {
            // Down opens the suggestions for what is typed, or for an empty field
            // when they don't wait for a prefix
            UpdateSuggestions();

            if (IsDropDownOpen)
            {
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        CloseDropDown();
    }
}