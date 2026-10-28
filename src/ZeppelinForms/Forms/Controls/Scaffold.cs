using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Navigation;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// The frame of a phone application's screen: an <see cref="AppBar"/> at the top, the
/// body, a <see cref="NavigationBar"/> at the bottom and a floating button over the body's
/// corner — each optional, each in its place, and the screen's insets taken care of.
/// </summary>
/// <remarks>
/// <para>
/// The navigation bar's destinations carry the body: choosing one shows its content (made
/// once, then kept as it was left), and a <see cref="NavigationHost"/> as that content
/// gives each destination its own stack. The app bar follows the stack of the destination
/// shown, unless it was given one of its own; choosing the shown destination again takes
/// its stack back to the root.
/// </para>
/// <para>
/// The app bar keeps out of the top of the safe area, the navigation bar out of its
/// bottom; the body keeps out of whatever of the safe area they don't cover. A snackbar of
/// the form stands above the navigation bar.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Content = new Scaffold
/// {
///     AppBar = new AppBar(),
///     NavigationBar = new NavigationBar
///     {
///         Items =
///         {
///             new NavigationBarItem("Home", homeIcon, () => new NavigationHost { RootPage = new HomePage() }),
///             new NavigationBarItem("Settings", gearIcon, () => new SettingsView()),
///         },
///     },
/// };
/// </code>
/// </example>
public class Scaffold : DecoratedPanel
{
    private Form? _form;

    /// <summary>The body shown: <see cref="Body"/>, or the chosen destination's content.</summary>
    private UIElement? _shownBody;

    /// <summary>The stack the app bar was given by the scaffold, not by the application.</summary>
    private NavigationHost? _autoHost;

    public Scaffold()
    {
        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        SetControlDefault(VerticalAlignmentProperty, VerticalAlignment.Stretch);
    }

    public AppBar? AppBar
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            if (field is not null)
            {
                if (ReferenceEquals(field.Host, _autoHost)) field.Host = null;
                Children.Remove(field);
            }

            field = value;
            _autoHost = null;

            if (value is not null) Children.Add(value);

            Sync();
        }
    }

    /// <summary>The body while no destination of the navigation bar gives one.</summary>
    public UIElement? Body
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            field = value;
            Sync();
        }
    }

    public NavigationBar? NavigationBar
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            if (field is not null)
            {
                field.SelectionChanged -= OnSelectionChanged;
                field.Reselected -= OnReselected;
                Children.Remove(field);
            }

            field = value;

            if (value is not null)
            {
                value.SelectionChanged += OnSelectionChanged;
                value.Reselected += OnReselected;
                Children.Add(value);
            }

            Sync();
        }
    }

    /// <summary>A button over the body's bottom corner — the screen's main action.</summary>
    public UIElement? FloatingActionButton
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            if (field is not null) Children.Remove(field);

            field = value;

            if (value is not null) Children.Add(value);

            Sync();
        }
    }

    /// <summary>The body shown now.</summary>
    public UIElement? ShownBody => _shownBody;

    /// <summary>How far above the bottom edge a snackbar stands: over the navigation bar.</summary>
    internal float BottomObstruction =>
        NavigationBar is { IsVisible: true } bar ? bar.ActualSize.Height : _form?.SafeAreaInsets.Bottom ?? 0f;

    private void OnSelectionChanged(object? sender, NavigationBarSelectionChangedEventArgs e) => Sync();

    private void OnReselected(object? sender, NavigationBarItem item)
    {
        if (_shownBody is NavigationHost { CanGoBack: true } host)
            _ = host.PopToRootAsync();
    }

    /// <summary>Show the body of the moment: the chosen destination's, or <see cref="Body"/>.</summary>
    private void Sync()
    {
        UIElement? body = NavigationBar?.SelectedItem?.ResolveContent() ?? Body;

        if (!ReferenceEquals(body, _shownBody))
        {
            // the bodies of other destinations stay, hidden: they come back as they were
            if (_shownBody is not null && !IsDestinationContent(_shownBody) && !ReferenceEquals(_shownBody, body))
                Children.Remove(_shownBody);
            else if (_shownBody is not null)
                _shownBody.IsVisible = false;

            _shownBody = body;

            if (body is not null)
            {
                body.IsVisible = true;

                // under the bars and the floating button
                if (!Children.Contains(body)) Children.Insert(0, body);
            }
        }

        // the app bar follows the shown destination's stack, unless the application
        // gave it a stack of its own
        if (AppBar is { } bar && (bar.Host is null || ReferenceEquals(bar.Host, _autoHost)))
        {
            _autoHost = body as NavigationHost;
            bar.Host = _autoHost;
        }

        Invalidate();
    }

    private bool IsDestinationContent(UIElement element) =>
        NavigationBar?.Items.Any(item => ReferenceEquals(item.Content, element) || ReferenceEquals(item.MadeContent, element)) == true;

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

    // ===== layout =====

    /// <summary>The space between the floating button and the body's edges.</summary>
    private const float FloatingMargin = 16f;

    protected override Size MeasureContentOverride(Size availableSize)
    {
        float width = float.IsFinite(availableSize.Width) ? availableSize.Width : 360f;
        float height = float.IsFinite(availableSize.Height) ? availableSize.Height : 640f;

        var whole = new Size(Math.Max(0, width - Padding.Horizontal), Math.Max(0, height - Padding.Vertical));

        float top = 0f, bottom = 0f;

        if (AppBar is { IsVisible: true } bar)
        {
            bar.Measure(whole);
            top = bar.DesiredSize.Height;
        }

        if (NavigationBar is { IsVisible: true } navigation)
        {
            navigation.Measure(whole);
            bottom = navigation.DesiredSize.Height;
        }

        Thickness body = BodyInsets(top, bottom);

        _shownBody?.Measure(new Size(
            Math.Max(0, whole.Width - body.Horizontal),
            Math.Max(0, whole.Height - body.Vertical)));

        FloatingActionButton?.Measure(whole);

        return ResolveSize(new Size(width, height), availableSize);
    }

    /// <summary>What the body keeps out of: the bars, and the safe area they don't cover.</summary>
    private Thickness BodyInsets(float top, float bottom)
    {
        Thickness safe = _form?.SafeAreaInsets ?? Thickness.Zero;

        bool hasTop = AppBar is { IsVisible: true };
        bool hasBottom = NavigationBar is { IsVisible: true };

        return new Thickness(
            safe.Left,
            hasTop ? top : safe.Top,
            safe.Right,
            hasBottom ? bottom : safe.Bottom);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        float left = Padding.Left;
        float top = Padding.Top;
        float width = Math.Max(0, contentSize.Width - Padding.Horizontal);
        float height = Math.Max(0, contentSize.Height - Padding.Vertical);

        float barHeight = 0f, navigationHeight = 0f;

        if (AppBar is { IsVisible: true } bar)
        {
            barHeight = bar.DesiredSize.Height;
            bar.Arrange(new Rectangle(new Point(left, top), new Size(width, barHeight)));
        }

        if (NavigationBar is { IsVisible: true } navigation)
        {
            navigationHeight = navigation.DesiredSize.Height;
            navigation.Arrange(new Rectangle(new Point(left, top + height - navigationHeight), new Size(width, navigationHeight)));
        }

        Thickness inset = BodyInsets(barHeight, navigationHeight);

        var body = new Rectangle(
            new Point(left + inset.Left, top + inset.Top),
            new Size(Math.Max(0, width - inset.Horizontal), Math.Max(0, height - inset.Vertical)));

        _shownBody?.Arrange(body);

        if (FloatingActionButton is { IsVisible: true } fab)
        {
            Size size = fab.DesiredSize;

            float x = IsRightToLeft ? body.X + FloatingMargin : body.Right - size.Width - FloatingMargin;
            float y = body.Bottom - size.Height - FloatingMargin;

            fab.Arrange(new Rectangle(new Point(x, y), size));
        }
    }
}
