using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZF_SharedLib.Showcase;

namespace ZF_SharedLib;

/// <summary>
/// The showcase: every control of the framework, its styles and effects, one page
/// per subject, chosen in the menu of a <see cref="SplitView"/>.
/// </summary>
/// <remarks>
/// <para>
/// The pages are made when they are first opened, and kept: coming back to a page
/// finds it as it was left. Each lives in a file of its own under Showcase/ and is
/// self-contained — the place to copy a control's use from.
/// </para>
/// <para>
/// On a narrow window — a phone — the menu closes to its strip of icons and opens
/// over the pages; on a wide one it stays open beside them.
/// </para>
/// </remarks>
public partial class ExampleMainForm : Form
{
    /// <summary>Narrower than this, the menu lies over the pages rather than beside them.</summary>
    private const float NarrowWidth = 720f;

    private SplitView _showcase;

    public ExampleMainForm()
    {
        Title = "ZeppelinForms";
        Size = new Size(1200, 800);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AllowDrop = true;

        // the cards and captions every page is made of
        Styles.Add(StyleSheet.Parse(Demo.Sheet, "showcase.zss"));

        _showcase = CreateShowcase();
        Content = _showcase;

        ClientSizeChanged += (_, _) => FitToWidth();
        FitToWidth();
    }

    private SplitView CreateShowcase()
    {
        var view = new SplitView
        {
            DisplayMode = SplitViewDisplayMode.CompactInline,
            PaneTitle = "ZeppelinForms",
            IsBackButtonVisible = true,
            OpenPaneLength = 248,
            IsPaneOpen = true,
        };

        view.Items.Add(new SplitViewItem("Home", Icons.Home, () => HomePage.Create()));

        view.Items.Add(SplitViewItem.Header("Input"));
        view.Items.Add(new SplitViewItem("Buttons", Icons.Button, ButtonsPage.Create));
        view.Items.Add(new SplitViewItem("Choices", Icons.Check, ChoicesPage.Create));
        view.Items.Add(new SplitViewItem("Text input", Icons.Text, TextInputPage.Create));
        view.Items.Add(new SplitViewItem("Date and color", Icons.Calendar, PickersPage.Create));
        view.Items.Add(new SplitViewItem("Ranges and progress", Icons.Slider, RangesPage.Create));

        view.Items.Add(SplitViewItem.Header("Collections"));
        view.Items.Add(new SplitViewItem("Lists", Icons.List, ListsPage.Create));
        view.Items.Add(new SplitViewItem("Grids and tables", Icons.Table, DataPage.Create));
        view.Items.Add(new SplitViewItem("Tree", Icons.Tree, TreePage.Create));
        view.Items.Add(new SplitViewItem("Gallery", Icons.Image, GalleryPage.Create));
        view.Items.Add(new SplitViewItem("Drag and drop", Icons.Drag, DragDropPage.Create));

        view.Items.Add(SplitViewItem.Header("Layout"));
        view.Items.Add(new SplitViewItem("Panels", Icons.Panels, PanelsPage.Create));
        view.Items.Add(new SplitViewItem("Splitters and adaptive", Icons.Split, SplittersPage.Create));
        view.Items.Add(new SplitViewItem("Containers", Icons.Box, ContainersPage.Create));
        view.Items.Add(new SplitViewItem("Navigation", Icons.Tabs, NavigationPage.Create));

        view.Items.Add(SplitViewItem.Header("Text and graphics"));
        view.Items.Add(new SplitViewItem("Text", Icons.Font, TextPage.Create));
        view.Items.Add(new SplitViewItem("Icons and shapes", Icons.Shapes, GraphicsPage.Create));
        view.Items.Add(new SplitViewItem("Charts", Icons.Chart, ChartsPage.Create));
        view.Items.Add(new SplitViewItem("Map", Icons.Map, MapPage.Create));

        view.Items.Add(SplitViewItem.Header("Look and feel"));
        view.Items.Add(new SplitViewItem("Styles", Icons.Brush, StylesPage.Create));
        view.Items.Add(new SplitViewItem("Effects", Icons.Sparkle, EffectsPage.Create));
        view.Items.Add(new SplitViewItem("Animation", Icons.Motion, AnimationPage.Create));

        view.Items.Add(SplitViewItem.Header("Interaction"));
        view.Items.Add(new SplitViewItem("Dialogs and overlays", Icons.Window, () => OverlaysPage.Create(this)));
        view.Items.Add(new SplitViewItem("Gestures", Icons.Hand, GesturesPage.Create));
        view.Items.Add(new SplitViewItem("Calculator", Icons.Calculator, CalculatorPage.Create));

        view.FooterItems.Add(new SplitViewItem("Settings", Icons.Gear, () => SettingsPage.Create(this)));

        return view;
    }

    /// <summary>Hot Reload. The pages need nothing here: they come from the menu's
    /// factories, and the split view makes the chosen one again after any edit of this
    /// assembly. An edit of this file builds the menu itself again — a new entry shows
    /// at once — keeping the page that was chosen.</summary>
    protected override void OnHotReload(HotReloadEventArgs e)
    {
        if (!e.Changed(typeof(ExampleMainForm))) return;

        string? chosen = _showcase.SelectedItem?.Text;

        _showcase = CreateShowcase();

        if (_showcase.Items.Concat(_showcase.FooterItems).FirstOrDefault(i => i.Text == chosen) is { } item)
            _showcase.SelectedItem = item;

        Content = _showcase;
        FitToWidth();
    }

    /// <summary>A phone gets a menu over the pages, closed until it is wanted; a desktop
    /// window keeps it open beside them.</summary>
    private void FitToWidth()
    {
        bool narrow = ClientSize.Width > 0 && ClientSize.Width < NarrowWidth;

        SplitViewDisplayMode mode = narrow ? SplitViewDisplayMode.CompactOverlay : SplitViewDisplayMode.CompactInline;

        if (_showcase.DisplayMode == mode) return;

        _showcase.DisplayMode = mode;
        _showcase.IsPaneOpen = !narrow;
    }
}
