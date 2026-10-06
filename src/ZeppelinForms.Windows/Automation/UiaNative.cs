using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ZeppelinForms.Windows.Automation;

/// <summary>The numbers and entry points of UI Automation: UIAutomationCore.dll on
/// the server side, and the few OLE helpers its signatures need.</summary>
/// <remarks>The identifiers are those of UIAutomationClient.h; only the ones the
/// bridge uses are listed.</remarks>
internal static class UiaNative
{
    private const string Core = "UIAutomationCore.dll";

    public const uint WM_GETOBJECT = 0x003D;

    /// <summary>The object id a UIA client asks a window for.</summary>
    public const int UiaRootObjectId = -25;

    /// <summary>The first element of a runtime id that UIA completes with the window's own.</summary>
    public const int UiaAppendRuntimeId = 3;

    // ProviderOptions
    public const int ProviderOptions_ServerSideProvider = 0x2;

    // NavigateDirection
    public const int Navigate_Parent = 0;
    public const int Navigate_NextSibling = 1;
    public const int Navigate_PreviousSibling = 2;
    public const int Navigate_FirstChild = 3;
    public const int Navigate_LastChild = 4;

    // patterns
    public const int InvokePatternId = 10000;
    public const int ValuePatternId = 10002;
    public const int RangeValuePatternId = 10003;
    public const int ExpandCollapsePatternId = 10005;
    public const int SelectionItemPatternId = 10010;
    public const int TogglePatternId = 10015;
    public const int ScrollItemPatternId = 10017;

    // properties
    public const int BoundingRectanglePropertyId = 30001;
    public const int ProcessIdPropertyId = 30002;
    public const int ControlTypePropertyId = 30003;
    public const int LocalizedControlTypePropertyId = 30004;
    public const int NamePropertyId = 30005;
    public const int AccessKeyPropertyId = 30007;
    public const int HasKeyboardFocusPropertyId = 30008;
    public const int IsKeyboardFocusablePropertyId = 30009;
    public const int IsEnabledPropertyId = 30010;
    public const int AutomationIdPropertyId = 30011;
    public const int ClassNamePropertyId = 30012;
    public const int HelpTextPropertyId = 30013;
    public const int IsControlElementPropertyId = 30016;
    public const int IsContentElementPropertyId = 30017;
    public const int LabeledByPropertyId = 30018;
    public const int IsPasswordPropertyId = 30019;
    public const int IsOffscreenPropertyId = 30022;
    public const int FrameworkIdPropertyId = 30024;
    public const int ValueValuePropertyId = 30045;
    public const int RangeValueValuePropertyId = 30047;
    public const int ExpandCollapseStatePropertyId = 30070;
    public const int SelectionItemIsSelectedPropertyId = 30079;
    public const int ToggleStatePropertyId = 30086;
    public const int LiveSettingPropertyId = 30135;
    public const int PositionInSetPropertyId = 30152;
    public const int SizeOfSetPropertyId = 30153;
    public const int LevelPropertyId = 30154;
    public const int HeadingLevelPropertyId = 30173;
    public const int IsDialogPropertyId = 30174;

    /// <summary>HeadingLevel_None; levels 1 to 9 follow it.</summary>
    public const int HeadingLevelNone = 80050;

    // events
    public const int StructureChangedEventId = 20002;
    public const int AutomationFocusChangedEventId = 20005;
    public const int LiveRegionChangedEventId = 20024;

    public const int StructureChangeType_ChildrenInvalidated = 2;

    public const int NotificationKind_Other = 4;
    public const int NotificationProcessing_ImportantMostRecent = 1;
    public const int NotificationProcessing_MostRecent = 3;

    // control types
    public const int ButtonControlTypeId = 50000;
    public const int CalendarControlTypeId = 50001;
    public const int CheckBoxControlTypeId = 50002;
    public const int ComboBoxControlTypeId = 50003;
    public const int EditControlTypeId = 50004;
    public const int HyperlinkControlTypeId = 50005;
    public const int ImageControlTypeId = 50006;
    public const int ListItemControlTypeId = 50007;
    public const int ListControlTypeId = 50008;
    public const int MenuControlTypeId = 50009;
    public const int MenuBarControlTypeId = 50010;
    public const int MenuItemControlTypeId = 50011;
    public const int ProgressBarControlTypeId = 50012;
    public const int RadioButtonControlTypeId = 50013;
    public const int ScrollBarControlTypeId = 50014;
    public const int SliderControlTypeId = 50015;
    public const int SpinnerControlTypeId = 50016;
    public const int TabControlTypeId = 50018;
    public const int TabItemControlTypeId = 50019;
    public const int TextControlTypeId = 50020;
    public const int ToolTipControlTypeId = 50022;
    public const int TreeControlTypeId = 50023;
    public const int TreeItemControlTypeId = 50024;
    public const int GroupControlTypeId = 50026;
    public const int DataGridControlTypeId = 50028;
    public const int DataItemControlTypeId = 50029;
    public const int SplitButtonControlTypeId = 50031;
    public const int WindowControlTypeId = 50032;
    public const int PaneControlTypeId = 50033;
    public const int HeaderItemControlTypeId = 50035;
    public const int SeparatorControlTypeId = 50038;

    [DllImport(Core)]
    public static extern nint UiaReturnRawElementProvider(nint hwnd, nint wParam, nint lParam, nint provider);

    [DllImport(Core)]
    public static extern int UiaHostProviderFromHwnd(nint hwnd, out nint provider);

    [DllImport(Core)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UiaClientsAreListening();

    [DllImport(Core)]
    public static extern int UiaRaiseAutomationEvent(nint provider, int eventId);

    [DllImport(Core)]
    public static extern int UiaRaiseAutomationPropertyChangedEvent(
        nint provider, int propertyId, UiaVariant oldValue, UiaVariant newValue);

    [DllImport(Core)]
    public static extern unsafe int UiaRaiseStructureChangedEvent(
        nint provider, int structureChangeType, int* runtimeId, int runtimeIdLength);

    /// <summary>Windows 10 1709 and later; earlier the entry point is missing,
    /// and an announcement is simply not made.</summary>
    [DllImport(Core)]
    public static extern int UiaRaiseNotificationEvent(
        nint provider, int notificationKind, int notificationProcessing, nint displayString, nint activityId);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    public static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(nint hwnd, ref UiaPoint point);

    [DllImport("oleaut32.dll")]
    public static extern nint SafeArrayCreateVector(ushort variantType, int lowerBound, uint count);

    [DllImport("oleaut32.dll")]
    public static extern int SafeArrayPutElement(nint safeArray, ref int index, ref int value);

    public const ushort VT_I4 = 3;

    [DllImport("oleaut32.dll")]
    public static extern int VariantClear(ref UiaVariant variant);
}

/// <summary>A VARIANT, as a plain blittable struct: the type tag, three reserved
/// words, and two pointer-sized words of value — 16 bytes in a 32-bit process,
/// 24 in a 64-bit one, exactly as the native union.</summary>
/// <remarks>
/// ComVariant has the same layout but can't cross a source-generated COM boundary
/// without disabling runtime marshalling for the whole assembly, which the other
/// P/Invokes of the platform rely on. So values are built as ComVariant — it knows
/// how to make a BSTR or a VARIANT_BOOL — and reinterpreted as this.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct UiaVariant
{
    public ushort VarType;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public nint Value1;
    public nint Value2;

    public static UiaVariant Empty => default;

    public static UiaVariant From(ComVariant variant) =>
        System.Runtime.CompilerServices.Unsafe.As<ComVariant, UiaVariant>(ref variant);

    public static UiaVariant From(string value) => From(ComVariant.Create(value));

    public static UiaVariant From(int value) => From(ComVariant.Create(value));

    public static UiaVariant From(bool value) => From(ComVariant.Create(value));

    /// <summary>VT_UNKNOWN: an interface pointer, AddRef'd — the receiver releases it.</summary>
    public static UiaVariant FromUnknown(nint unknown) => new()
    {
        VarType = (ushort)VarEnum.VT_UNKNOWN,
        Value1 = unknown,
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct UiaPoint
{
    public int X;
    public int Y;
}

/// <summary>A rectangle in screen pixels, as UIA passes it: left, top, width, height.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct UiaRect
{
    public double Left;
    public double Top;
    public double Width;
    public double Height;
}