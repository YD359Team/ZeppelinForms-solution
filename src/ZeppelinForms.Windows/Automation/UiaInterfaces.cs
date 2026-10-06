using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ZeppelinForms.Windows.Automation;

// The provider interfaces of UIAutomationCore.idl, in their vtable order.
//
// Source-generated COM rather than [ComImport]: it works with trimming and native
// AOT, and needs no runtime marshalling. Properties are declared as get_ methods —
// that is what they are in the vtable. Interface pointers going out are returned as
// raw nint: the bridge creates them itself with the right IID, already AddRef'd, and
// a null pointer is the ordinary "none" of UIA.

[GeneratedComInterface]
[Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
internal partial interface IRawElementProviderSimple
{
    int get_ProviderOptions();

    nint GetPatternProvider(int patternId);

    UiaVariant GetPropertyValue(int propertyId);

    nint get_HostRawElementProvider();
}

[GeneratedComInterface]
[Guid("f7063da8-8359-439c-9297-bbc5299a7d87")]
internal partial interface IRawElementProviderFragment
{
    nint Navigate(int direction);

    /// <summary>A SAFEARRAY of int.</summary>
    nint GetRuntimeId();

    UiaRect get_BoundingRectangle();

    /// <summary>A SAFEARRAY of fragment roots; none here.</summary>
    nint GetEmbeddedFragmentRoots();

    void SetFocus();

    nint get_FragmentRoot();
}

[GeneratedComInterface]
[Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58")]
internal partial interface IRawElementProviderFragmentRoot
{
    nint ElementProviderFromPoint(double x, double y);

    nint GetFocus();
}

[GeneratedComInterface]
[Guid("54fcb24b-e18e-47a2-b4d3-eccbe77599a2")]
internal partial interface IInvokeProvider
{
    void Invoke();
}

[GeneratedComInterface]
[Guid("56d00bd0-c4f4-433c-a836-1a52a57e0892")]
internal partial interface IToggleProvider
{
    void Toggle();

    int get_ToggleState();
}

[GeneratedComInterface]
[Guid("d847d3a5-cab0-4a98-8c32-ecb45c59ad24")]
internal partial interface IExpandCollapseProvider
{
    void Expand();

    void Collapse();

    int get_ExpandCollapseState();
}

[GeneratedComInterface]
[Guid("2acad808-b2d4-452d-a407-91ff1ad167b2")]
internal partial interface ISelectionItemProvider
{
    void Select();

    void AddToSelection();

    void RemoveFromSelection();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool get_IsSelected();

    nint get_SelectionContainer();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("c7935180-6fb3-4201-b174-7df73adbf64a")]
internal partial interface IValueProvider
{
    void SetValue(string value);

    [return: MarshalAs(UnmanagedType.BStr)]
    string get_Value();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool get_IsReadOnly();
}

[GeneratedComInterface]
[Guid("36dc7aef-33e6-4691-afe1-2be7274b3d33")]
internal partial interface IRangeValueProvider
{
    void SetValue(double value);

    double get_Value();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool get_IsReadOnly();

    double get_Maximum();

    double get_Minimum();

    double get_LargeChange();

    double get_SmallChange();
}

[GeneratedComInterface]
[Guid("2360c714-4bf1-4b26-ba65-9b21316127eb")]
internal partial interface IScrollItemProvider
{
    void ScrollIntoView();
}