using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Navigation;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

/// <summary>When an <see cref="AppBar"/> shows its back button.</summary>
public enum BackButtonMode : byte
{
    /// <summary>While its <see cref="AppBar.Host"/> has a page to go back to.</summary>
    Auto,

    /// <summary>Always: it asks the form to go back (<see cref="Form.RequestBack"/>).</summary>
    Always,

    /// <summary>Never.</summary>
    Never,
}

/// <summary>
/// The bar across the top of a phone application's screen: a back button — or a button of
/// the application's own, a menu to open a pane —, the title, and buttons for what the
/// screen does, the ones that don't fit behind a "more" button.
/// </summary>
/// <remarks>
/// <para>
/// Given a <see cref="NavigationHost"/> (<see cref="Host"/>) it follows the stack: the
/// current page's <see cref="Page.Title"/> unless a <see cref="Title"/> of its own is set,
/// a back button while there is a page to go back to, and the page's
/// <see cref="Page.Actions"/> before the bar's own <see cref="Actions"/>.
/// </para>
/// <para>
/// Its background runs up under the status bar of a form that draws edge to edge, and its
/// buttons and title keep below it: <see cref="Form.SafeAreaInsets"/>' top.
/// </para>
/// </remarks>
public partial class AppBar : DecoratedPanel
{
    private readonly AppBarButton _leading;
    private readonly Label _title;
    private readonly AppBarButton _overflow;
    private readonly List<AppBarButton> _shownActions = [];

    private Form? _form;
    private Page? _page;

    public AppBar()
    {
        _leading = new AppBarButton { Icon = IconSource.FromPath(AppBarIcons.Back, AppBarIcons.Stroke) };
        _leading.Click += (_, _) => OnLeadingClicked();

        _title = new Label
        {
            TextStyle = TextStyle.Subtitle,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            VerticalContentAlign = VerticalContentAlignment.Center,
            IsHitTestVisible = false,
        };

        _title.AccessibleRole = AccessibilityRole.Heading;
        _title.HeadingLevel = 1;

        _overflow = new AppBarButton { Icon = IconSource.FromPath(AppBarIcons.More), Text = Localization.Get(ZfText.MoreOptions) };
        _overflow.Click += (_, _) => ShowOverflow();

        Children.Add(_leading);
        Children.Add(_title);
        Children.Add(_overflow);

        Actions.CollectionChanged += (_, _) => Sync();

        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        SetControlDefault(VerticalAlignmentProperty, VerticalAlignment.Top);

        Sync();
    }

    // ===== what it shows =====

    /// <summary>The title. Null — the current page's of <see cref="Host"/>.</summary>
    public string? Title
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Sync();
        }
    }

    /// <summary>The navigation stack the bar follows: its current page's title and
    /// actions, and a back button while it can go back.</summary>
    public NavigationHost? Host
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            if (field is not null) field.Navigated -= OnNavigated;

            field = value;

            if (value is not null) value.Navigated += OnNavigated;

            FollowPage(value?.CurrentPage);
            Sync();
        }
    }

    public BackButtonMode BackButtonMode
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Sync();
        }
    }

    /// <summary>The icon of the leading button while there is no back button: a menu
    /// that opens a navigation pane, say. Null — no button there.</summary>
    public IconSource? NavigationIcon
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Sync();
        }
    }

    /// <summary>The leading button's name, read and shown as its tooltip, while it shows
    /// <see cref="NavigationIcon"/>. Null — "Open navigation".</summary>
    public string? NavigationText
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Sync();
        }
    }

    /// <summary>The leading button was pressed while it showed <see cref="NavigationIcon"/>.</summary>
    public event EventHandler? NavigationClicked;

    /// <summary>The bar's own buttons, after the current page's.</summary>
    public ObservableCollection<AppBarButton> Actions { get; } = [];

    /// <summary>What the "more" button at the end opens: items the bar has no room for.
    /// Empty — no such button.</summary>
    public List<MenuItem> OverflowItems
    {
        get;
        set
        {
            field = value ?? [];
            Sync();
        }
    } = [];

    /// <summary>Whether the bar keeps out of the top of <see cref="Form.SafeAreaInsets"/>,
    /// its background running up under the status bar. True by default; a bar that isn't
    /// at the top of the screen turns it off.</summary>
    public bool UsesSafeArea
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = true;

    /// <summary>The height of the bar's row of buttons, without the safe area above it.</summary>
    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial float BarHeight { get; set; }
    private static float BarHeightDefault => 56f;

    /// <summary>The line along the bar's bottom edge. Transparent — none.</summary>
    [Styled(Category = "Appearance")]
    public partial Color DividerColor { get; set; }
    private static Color DividerColorDefault => Colors.Transparent;

    /// <summary>The title as shown.</summary>
    public string? ShownTitle => Title ?? _page?.Title;

    // ===== following the stack =====

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        FollowPage(e.To);
        Sync();
    }

    private void FollowPage(Page? page)
    {
        if (ReferenceEquals(_page, page)) return;

        if (_page is not null)
        {
            _page.TitleChanged -= OnPageChanged;
            _page.Actions.CollectionChanged -= OnPageActionsChanged;
        }

        _page = page;

        if (page is not null)
        {
            page.TitleChanged += OnPageChanged;
            page.Actions.CollectionChanged += OnPageActionsChanged;
        }
    }

    private void OnPageChanged(object? sender, EventArgs e) => Sync();

    private void OnPageActionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Sync();

    private bool ShowsBack => BackButtonMode switch
    {
        BackButtonMode.Always => true,
        BackButtonMode.Never => false,
        _ => Host is { CanGoBack: true },
    };

    /// <summary>Lay the parts out again for what the bar shows now.</summary>
    private void Sync()
    {
        bool back = ShowsBack;

        _leading.IsVisible = back || NavigationIcon is not null;
        _leading.Icon = back ? IconSource.FromPath(AppBarIcons.Back, AppBarIcons.Stroke) : NavigationIcon;
        _leading.Text = back ? Localization.Get(ZfText.Back) : NavigationText ?? Localization.Get(ZfText.OpenNavigation);

        _title.Text = ShownTitle;

        _overflow.IsVisible = OverflowItems.Count > 0;

        // the actions: the page's, then the bar's own, between the title and "more"
        foreach (AppBarButton old in _shownActions)
            Children.Remove(old);

        _shownActions.Clear();

        if (_page is { HasActions: true } page) _shownActions.AddRange(page.Actions);
        _shownActions.AddRange(Actions);

        int at = Children.IndexOf(_overflow);

        foreach (AppBarButton action in _shownActions)
        {
            // a button can stand in one bar at a time: a page shown by two bars keeps
            // its buttons in the first
            if (action.Parent is not null) continue;

            Children.Insert(at++, action);
        }

        Invalidate();
    }

    private void OnLeadingClicked()
    {
        if (!ShowsBack)
        {
            NavigationClicked?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (Host is { CanGoBack: true } host)
            _ = host.PopAsync();
        else
            FindOwner()?.RequestBack(BackSource.Code);
    }

    private void ShowOverflow()
    {
        if (FindOwner() is not { } form || OverflowItems.Count == 0) return;

        Point at = _overflow.GetAbsolutePosition();

        // under the button, its right edge at the button's
        form.ShowContextMenu(OverflowItems, new Point(at.X, at.Y + _overflow.ActualSize.Height));
    }

    // ===== the safe area =====

    protected override void OnAttached()
    {
        base.OnAttached();

        _form = FindOwner();

        if (_form is not null) _form.SafeAreaInsetsChanged += OnInsetsChanged;
    }

    protected override void OnDetached()
    {
        base.OnDetached();

        if (_form is not null) _form.SafeAreaInsetsChanged -= OnInsetsChanged;

        _form = null;
    }

    private void OnInsetsChanged(object? sender, EventArgs e) => Invalidate();

    private Thickness Insets => UsesSafeArea && _form is not null
        ? SafeArea.Pick(_form.SafeAreaInsets, SafeAreaEdges.Left | SafeAreaEdges.Top | SafeAreaEdges.Right)
        : Thickness.Zero;

    // ===== layout =====

    private const float SidePadding = 4f;
    private const float TitleGap = 12f;

    protected override Size MeasureContentOverride(Size availableSize)
    {
        Thickness inset = Insets;
        float row = BarHeight;
        var button = new Size(row, row);

        foreach (UIElement child in Children)
        {
            if (!child.IsVisible || ReferenceEquals(child, _title)) continue;

            child.Measure(button);
        }

        _title.Measure(new Size(float.PositiveInfinity, row));

        float width = float.IsFinite(availableSize.Width) ? availableSize.Width : 360f;

        return ResolveSize(new Size(width, row + inset.Top + Padding.Vertical), availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        Thickness inset = Insets;

        float row = BarHeight;
        float top = inset.Top + Padding.Top;
        float left = inset.Left + Padding.Left + SidePadding;
        float right = contentSize.Width - inset.Right - Padding.Right - SidePadding;

        float buttonSize = Math.Min(48f, row);
        float buttonTop = top + (row - buttonSize) / 2f;

        bool rtl = IsRightToLeft;

        // from the start: the leading button
        if (_leading.IsVisible)
        {
            PlaceButton(_leading, rtl ? right - buttonSize : left);

            if (rtl) right -= buttonSize;
            else left += buttonSize;
        }

        // from the end: "more", then the actions backwards
        if (_overflow.IsVisible)
        {
            PlaceButton(_overflow, rtl ? left : right - buttonSize);

            if (rtl) left += buttonSize;
            else right -= buttonSize;
        }

        for (int i = _shownActions.Count - 1; i >= 0; i--)
        {
            AppBarButton action = _shownActions[i];

            if (!action.IsVisible || !ReferenceEquals(action.Parent, this)) continue;

            PlaceButton(action, rtl ? left : right - buttonSize);

            if (rtl) left += buttonSize;
            else right -= buttonSize;
        }

        // the title gets what is left, a little away from the leading button
        float titleLeft = left + (_leading.IsVisible ? 4f : TitleGap);
        float titleRight = right - 4f;

        _title.HorizontalContentAlign = rtl ? HorizontalContentAlignment.Right : HorizontalContentAlignment.Left;
        _title.Arrange(new Rectangle(new Point(titleLeft, top), new Size(Math.Max(0, titleRight - titleLeft), row)));

        void PlaceButton(AppBarButton button, float x) =>
            button.Arrange(new Rectangle(new Point(x, buttonTop), new Size(buttonSize, buttonSize)));
    }

    protected override void DrawDecoration(Graphics g)
    {
        Color divider = DividerColor;

        if (divider.A == 0 || ActualSize.Width <= 0) return;

        g.FillRectangle(new Rectangle(new Point(0, ActualSize.Height - 1f), new Size(ActualSize.Width, 1f)), divider);
    }
}

/// <summary>A round button with an icon, for an <see cref="AppBar"/>: its
/// <see cref="Text"/> is its name and tooltip, shown in place of the icon when it has none.</summary>
public partial class AppBarButton : ButtonBase
{
    public AppBarButton()
    {
        AccessibleRole = AccessibilityRole.Button;

        SetControlDefault(PaddingProperty, new Thickness(0));
        SetControlDefault(BorderWidthProperty, 0f);
        SetControlDefault(CornerRadiusProperty, new CornerRadius(24f));
    }

    public AppBarButton(IconSource icon, string text, EventHandler? click = null) : this()
    {
        Icon = icon;
        Text = text;

        if (click is not null) Click += (sender, e) => click(sender, EventArgs.Empty);
    }

    public IconSource? Icon
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    }

    /// <summary>What the button does, in a few words: its accessible name and tooltip.</summary>
    public string? Text
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            AccessibleName = value;
            Invalidate();
        }
    }

    /// <summary>The icon's size.</summary>
    [Styled(Category = "Layout")]
    public partial float IconSize { get; set; }
    private static float IconSizeDefault => 20f;

    protected internal override string? GetToolTip(Point location) => Text;

    protected override void DrawButtonContent(Graphics g)
    {
        Rectangle bounds = LocalBounds;

        if (Icon is { } icon)
        {
            float size = IconSize;
            var rect = new Rectangle(
                new Point(bounds.X + (bounds.Width - size) / 2f, bounds.Y + (bounds.Height - size) / 2f),
                new Size(size, size));

            icon.Draw(g, rect, CurrentTextColor);
            return;
        }

        if (!string.IsNullOrEmpty(Text))
            g.DrawText(Text, bounds, CurrentTextColor, EffectiveFont);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Icon is null && !string.IsNullOrEmpty(Text))
        {
            Size text = TextMeasurer.MeasureLine(Text, EffectiveFont);
            return ResolveSize(new Size(text.Width + 24f, 40f), availableSize);
        }

        return ResolveSize(new Size(40f, 40f), availableSize);
    }
}

/// <summary>The glyphs of the app bar and the navigation bar: strokes of a 24-pixel grid.</summary>
internal static class AppBarIcons
{
    public const float Stroke = 2f;

    /// <summary>An arrow pointing back: to the left.</summary>
    public const string Back = "M20 12H4M10 6l-6 6 6 6";

    /// <summary>Three lines: a navigation pane.</summary>
    public const string Menu = "M3 6h18M3 12h18M3 18h18";

    /// <summary>Three dots one over another: more.</summary>
    public const string More =
        "M10 5a2 2 0 1 0 4 0a2 2 0 1 0 -4 0Z" +
        "M10 12a2 2 0 1 0 4 0a2 2 0 1 0 -4 0Z" +
        "M10 19a2 2 0 1 0 4 0a2 2 0 1 0 -4 0Z";

    /// <summary>A cross: close.</summary>
    public const string Close = "M6 6l12 12M18 6L6 18";
}
