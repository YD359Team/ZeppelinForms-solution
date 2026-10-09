using ZeppelinForms.Accessibility.Peers;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Charts;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Controls.Shapes;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Controls.Tree;

namespace ZeppelinForms.Accessibility;

/// <summary>The peer of each built-in control, in one place.</summary>
/// <remarks>
/// <para>
/// A table rather than an override in every control: the peers read the controls
/// through their public API, and the controls needed no change for it. A control
/// of an application overrides <c>CreateAccessibilityPeer</c> instead; this table
/// is only the default.
/// </para>
/// <para>
/// The order matters: a derived type before its base — ToggleButton before Button,
/// LinkLabel before RichLabel, CheckedListBox before ListBox.
/// </para>
/// </remarks>
internal static class AccessibilityPeerFactory
{
    public static AccessibilityPeer? Create(UIElement element) => element switch
    {
        // buttons
        ToggleButton toggle => new ToggleButtonPeer(toggle),
        SplitButton split => new SplitButtonPeer(split),
        ButtonBase button => new ButtonPeer(button),
        CheckBox box => new CheckBoxPeer(box),
        RadioButton radio => new RadioButtonPeer(radio),
        ToggleSwitch toggleSwitch => new ToggleSwitchPeer(toggleSwitch),

        // text
        LinkLabel link => new LinkPeer(link),
        HintLabel hint => new HintLabelPeer(hint),
        Label or RichLabel => new TextPeer(element),
        TextBox textBox => new TextBoxPeer(textBox),
        MarkdownViewer markdown => new MarkdownViewerPeer(markdown),
        MaskedTextBox masked => new MaskedTextBoxPeer(masked),
        NumericUpDown numeric => new SpinButtonPeer(numeric),

        // ranges
        TrackBar trackBar => new SliderPeer(trackBar),
        RangeSlider range => new RangeSliderPeer(range),
        ProgressBar progress => new ProgressBarPeer(progress),
        CircularProgressBar circular => new ProgressBarPeer(circular),
        Loader loader => new LoaderPeer(loader),
        ScrollBar scrollBar => new ScrollBarPeer(scrollBar),

        // drop-downs
        ComboBox combo => new ComboBoxPeer(combo),
        CheckedComboBox checkedCombo => new ComboBoxPeer(checkedCombo),
        DateTimePicker date => new ComboBoxPeer(date),
        TimePicker time => new ComboBoxPeer(time),
        ColorPicker color => new ComboBoxPeer(color),

        // collections
        ListBox or DragList => new ListPeer((ItemsControl)element),
        TreeView tree => new TreeViewPeer(tree),
        TabControl tabs => new TabControlPeer(tabs),
        TabStrip strip => new TabStripPeer(strip),
        DataGridView grid => new DataGridPeer(grid),
        MenuBar menuBar => new MenuBarPeer(menuBar),
        MenuList menu => new MenuListPeer(menu),
        Calendar calendar => new CalendarPeer(calendar),

        // containers and pictures
        GroupBox group => new GroupPeer(group),
        Spoiler spoiler => new SpoilerPeer(spoiler),
        SplitView split => new SplitViewPeer(split),
        SplitViewPaneHost pane => new SplitViewPanePeer(pane),
        RefreshContainer refresh => new RefreshContainerPeer(refresh),
        ImageGallery gallery => new ImageGalleryPeer(gallery),
        ImageViewer viewer => new ImageViewerPeer(viewer),
        ChartBase chart => new ChartPeer(chart),
        PictureBox or SvgIcon => new ImagePeer(element),
        GridSplitter => new SeparatorPeer(element),

        // a shape is decoration: drawn, never read
        Shape => null,

        // layout and everything else: a container with no meaning of its own
        _ => new UIElementPeer(element),
    };
}