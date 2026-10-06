using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Windows.Automation;

namespace ZeppelinForms.UnitTests;

/// <summary>
/// The UI Automation providers of the Windows bridge, walked the way a client walks
/// them — through the COM pointers they hand out — but without a window: what is
/// checked is the translation of peers into UIA, which needs no Win32. Calls that
/// do need it (screen coordinates, runtime id arrays, the host provider) are left
/// to the platform tests on Windows.
/// </summary>
[Collection("Platform")]
public class UiaProviderTests
{
    private static (Form Form, UiaRootProvider Root) Create(UIElement content, string title = "Window")
    {
        var form = new Form { Size = new Size(400, 300), Content = content, Title = title };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        var bridge = new UiaBridge(0, form, () => 1f);

        return (form, new UiaRootProvider(bridge, form.GetAccessibilityPeer()));
    }

    private static StackPanel Panel(params UIElement[] children)
    {
        var panel = new StackPanel();

        foreach (UIElement child in children)
            panel.Children.Add(child);

        return panel;
    }

    private static UiaProvider Provider(nint pointer) => (UiaProvider)UiaBridge.ManagedObject(pointer);

    private static UiaProvider ProviderOf(UiaRootProvider root, UIElement element)
    {
        // the client's way: down from the root, by the tree
        var stack = new Stack<UiaProvider>([root]);

        while (stack.Count > 0)
        {
            UiaProvider current = stack.Pop();

            if (ReferenceEquals(current.Peer.Element, element)) return current;

            for (nint child = current.Navigate(UiaNative.Navigate_FirstChild); child != 0;)
            {
                UiaProvider next = Provider(child);
                stack.Push(next);
                child = next.Navigate(UiaNative.Navigate_NextSibling);
            }
        }

        throw new InvalidOperationException("Not in the tree.");
    }

    private static ComVariant Value(UiaProvider provider, int propertyId)
    {
        UiaVariant value = provider.GetPropertyValue(propertyId);
        return Unsafe.As<UiaVariant, ComVariant>(ref value);
    }

    private static string? Text(UiaProvider provider, int propertyId) =>
        Value(provider, propertyId) is { VarType: VarEnum.VT_BSTR } variant ? variant.As<string>() : null;

    private static int Number(UiaProvider provider, int propertyId) => Value(provider, propertyId).As<int>();

    private static bool Flag(UiaProvider provider, int propertyId) => Value(provider, propertyId).As<bool>();

    private static bool Has(UiaProvider provider, int patternId) => provider.GetPatternProvider(patternId) != 0;

    // ===== the tree =====

    [Fact]
    public void RootIsTheWindow()
    {
        (_, UiaRootProvider root) = Create(new Label { Text = "x" }, "Settings");

        Assert.Equal("Settings", Text(root, UiaNative.NamePropertyId));
        Assert.Equal(UiaNative.WindowControlTypeId, Number(root, UiaNative.ControlTypePropertyId));
        Assert.Equal(0, root.Navigate(UiaNative.Navigate_Parent));
    }

    [Fact]
    public void LayoutContainersStayInTheRawView()
    {
        var panel = Panel(new Button { Text = "OK" });
        (_, UiaRootProvider root) = Create(panel);

        UiaProvider panelProvider = ProviderOf(root, panel);

        Assert.Equal(UiaNative.PaneControlTypeId, Number(panelProvider, UiaNative.ControlTypePropertyId));
        Assert.False(Flag(panelProvider, UiaNative.IsControlElementPropertyId));
    }

    [Fact]
    public void ControlsHaveTheirTypesAndNames()
    {
        var check = new CheckBox { Text = "Remember" };
        var button = new Button { Text = "OK" };
        var box = new TextBox();
        var label = new Label { Text = "Name", Target = box };
        (_, UiaRootProvider root) = Create(Panel(label, box, check, button));

        Assert.Equal(UiaNative.CheckBoxControlTypeId, Number(ProviderOf(root, check), UiaNative.ControlTypePropertyId));
        Assert.Equal(UiaNative.ButtonControlTypeId, Number(ProviderOf(root, button), UiaNative.ControlTypePropertyId));
        Assert.Equal(UiaNative.EditControlTypeId, Number(ProviderOf(root, box), UiaNative.ControlTypePropertyId));
        Assert.Equal(UiaNative.TextControlTypeId, Number(ProviderOf(root, label), UiaNative.ControlTypePropertyId));

        Assert.Equal("Name", Text(ProviderOf(root, box), UiaNative.NamePropertyId));
    }

    [Fact]
    public void NavigationIsConsistent()
    {
        var a = new Button { Text = "A" };
        var b = new Button { Text = "B" };
        var c = new Button { Text = "C" };
        var panel = Panel(a, b, c);
        (_, UiaRootProvider root) = Create(panel);

        UiaProvider panelProvider = ProviderOf(root, panel);
        UiaProvider middle = ProviderOf(root, b);

        Assert.Same(ProviderOf(root, c), Provider(middle.Navigate(UiaNative.Navigate_NextSibling)));
        Assert.Same(ProviderOf(root, a), Provider(middle.Navigate(UiaNative.Navigate_PreviousSibling)));
        Assert.Same(panelProvider, Provider(middle.Navigate(UiaNative.Navigate_Parent)));
        Assert.Same(ProviderOf(root, c), Provider(panelProvider.Navigate(UiaNative.Navigate_LastChild)));

        Assert.Equal(0, ProviderOf(root, c).Navigate(UiaNative.Navigate_NextSibling));
    }

    // ===== patterns =====

    [Fact]
    public void PatternsFollowTheControl()
    {
        var check = new CheckBox { Text = "x" };
        var button = new Button { Text = "x" };
        var combo = new ComboBox { Items = ["a", "b"] };
        var slider = new TrackBar();
        var box = new TextBox();
        (_, UiaRootProvider root) = Create(Panel(check, button, combo, slider, box));

        Assert.True(Has(ProviderOf(root, check), UiaNative.TogglePatternId));
        Assert.False(Has(ProviderOf(root, check), UiaNative.InvokePatternId));
        Assert.True(Has(ProviderOf(root, button), UiaNative.InvokePatternId));
        Assert.True(Has(ProviderOf(root, combo), UiaNative.ExpandCollapsePatternId));
        Assert.True(Has(ProviderOf(root, slider), UiaNative.RangeValuePatternId));
        Assert.True(Has(ProviderOf(root, box), UiaNative.ValuePatternId));
        Assert.False(Has(ProviderOf(root, box), UiaNative.RangeValuePatternId));
    }

    [Fact]
    public void TogglePatternTogglesTheControl()
    {
        var check = new CheckBox { Text = "x" };
        (_, UiaRootProvider root) = Create(Panel(check));

        var toggle = (IToggleProvider)UiaBridge.ManagedObject(
            ProviderOf(root, check).GetPatternProvider(UiaNative.TogglePatternId));

        toggle.Toggle();

        Assert.True(check.IsChecked);
        Assert.Equal(1, toggle.get_ToggleState());
    }

    [Fact]
    public void ValueAndRangePatternsSetValues()
    {
        var box = new TextBox();
        var slider = new TrackBar { Minimum = 0, Maximum = 10 };
        (_, UiaRootProvider root) = Create(Panel(box, slider));

        var value = (IValueProvider)UiaBridge.ManagedObject(
            ProviderOf(root, box).GetPatternProvider(UiaNative.ValuePatternId));

        value.SetValue("typed by a client");
        Assert.Equal("typed by a client", box.Text);

        var range = (IRangeValueProvider)UiaBridge.ManagedObject(
            ProviderOf(root, slider).GetPatternProvider(UiaNative.RangeValuePatternId));

        range.SetValue(7);
        Assert.Equal(7f, slider.Value);
        Assert.Equal(10, range.get_Maximum());
    }

    [Fact]
    public void ListItemsAreSelectable()
    {
        var list = new ListBox();
        list.Items.Add("One");
        list.Items.Add("Two");
        (_, UiaRootProvider root) = Create(list);

        UiaProvider second = Provider(Provider(ProviderOf(root, list).Navigate(UiaNative.Navigate_FirstChild))
            .Navigate(UiaNative.Navigate_NextSibling));

        Assert.Equal(UiaNative.ListItemControlTypeId, Number(second, UiaNative.ControlTypePropertyId));
        Assert.Equal(2, Number(second, UiaNative.PositionInSetPropertyId));
        Assert.Equal(2, Number(second, UiaNative.SizeOfSetPropertyId));

        var item = (ISelectionItemProvider)UiaBridge.ManagedObject(
            second.GetPatternProvider(UiaNative.SelectionItemPatternId));

        item.Select();

        Assert.Equal(1, list.SelectedIndex);
        Assert.True(item.get_IsSelected());
    }

    [Fact]
    public void HeadingLevelIsUiasOwnNumber()
    {
        var heading = new Label { Text = "Account", HeadingLevel = 2 };
        (_, UiaRootProvider root) = Create(Panel(heading));

        Assert.Equal(UiaNative.HeadingLevelNone + 2, Number(ProviderOf(root, heading), UiaNative.HeadingLevelPropertyId));
    }

    // ===== focus and lifetime =====

    [Fact]
    public void RootReportsTheFocus()
    {
        var button = new Button { Text = "OK" };
        (Form form, UiaRootProvider root) = Create(Panel(new Label { Text = "x" }, button));

        Assert.Equal(0, root.GetFocus());

        HeadlessInput.PressKey(form, Key.Tab);

        Assert.Same(ProviderOf(root, button), Provider(root.GetFocus()));
    }

    [Fact]
    public void ElementThatLeftTheFormIsNotAvailable()
    {
        var check = new CheckBox { Text = "x" };
        var panel = Panel(check);
        (_, UiaRootProvider root) = Create(panel);

        UiaProvider provider = ProviderOf(root, check);

        panel.Children.Remove(check);

        COMException error = Assert.Throws<COMException>(() => provider.GetPropertyValue(UiaNative.NamePropertyId));
        Assert.Equal(unchecked((int)0x80040201), error.HResult);
    }
}