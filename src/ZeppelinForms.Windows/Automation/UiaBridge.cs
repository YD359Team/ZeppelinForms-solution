using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;

namespace ZeppelinForms.Windows.Automation;

/// <summary>UI Automation for one window: the providers over the form's peers,
/// the answer to WM_GETOBJECT, and the core's events turned into UIA events.</summary>
/// <remarks>
/// <para>
/// Created when the first UIA client asks the window — Narrator, NVDA, JAWS,
/// Inspect, a test tool — and not before: until then no provider and no peer
/// exists, and the events of the core have no listener.
/// </para>
/// <para>
/// UIA may call a provider from its own threads; the peers read the element tree,
/// which belongs to the UI thread. Every call is therefore carried over to it with
/// SendMessage — synchronous, as the caller expects an answer — unless it already
/// runs there.
/// </para>
/// </remarks>
internal sealed class UiaBridge : IDisposable
{
    /// <summary>A UIA call carried over to the UI thread; lParam is a GCHandle of the call.</summary>
    internal const uint WM_UIA_CALL = 0x0400 + 2;

    private static readonly StrategyBasedComWrappers Wrappers = new();

    private static int s_nextRuntimeId;

    private readonly nint _hwnd;
    private readonly Form _form;
    private readonly Func<float> _scale;
    private readonly int _uiThread = Environment.CurrentManagedThreadId;

    /// <summary>One provider per peer, as long as the peer lives: UIA identifies
    /// elements by their runtime id, which belongs to the provider.</summary>
    private readonly ConditionalWeakTable<AccessibilityPeer, UiaProvider> _providers = new();

    private UiaRootProvider? _root;
    private bool _listening;

    public UiaBridge(nint hwnd, Form form, Func<float> scale)
    {
        _hwnd = hwnd;
        _form = form;
        _scale = scale;
    }

    public nint Hwnd => _hwnd;

    public Form Form => _form;

    /// <summary>WM_GETOBJECT with the UIA object id: hand over the root provider.</summary>
    public nint HandleGetObject(nint wParam, nint lParam)
    {
        _root ??= new UiaRootProvider(this, _form.GetAccessibilityPeer());

        StartListening();

        nint provider = ComPointer(_root, typeof(IRawElementProviderSimple).GUID);

        try
        {
            return UiaNative.UiaReturnRawElementProvider(_hwnd, wParam, lParam, provider);
        }
        finally
        {
            Marshal.Release(provider);
        }
    }

    // ===== providers =====

    internal static int NextRuntimeId() => Interlocked.Increment(ref s_nextRuntimeId);

    internal UiaProvider ProviderFor(AccessibilityPeer peer)
    {
        if (_root is not null && ReferenceEquals(peer, _root.Peer)) return _root;

        return _providers.GetValue(peer, p => new UiaProvider(this, p));
    }

    /// <summary>The peer's provider as the interface asked for, AddRef'd; 0 for no peer.</summary>
    internal nint PointerFor(AccessibilityPeer? peer, Guid iid) =>
        peer is null ? 0 : ComPointer(ProviderFor(peer), iid);

    internal nint RootPointer(Guid iid) => _root is null ? 0 : ComPointer(_root, iid);

    /// <summary>A COM pointer to a managed object, queried for the interface:
    /// what every provider method that returns an interface hands over.</summary>
    internal static nint ComPointer(object instance, Guid iid)
    {
        nint unknown = Wrappers.GetOrCreateComInterfaceForObject(instance, CreateComInterfaceFlags.None);

        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out nint pointer));
            return pointer;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    /// <summary>The managed provider behind a pointer this bridge handed out —
    /// for tests, which walk the tree the way a client would.</summary>
    internal static object ManagedObject(nint pointer) =>
        Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.Unwrap);

    // ===== threads =====

    /// <summary>Run a provider call on the UI thread and give its result back.</summary>
    internal T OnUiThread<T>(Func<T> call)
    {
        if (Environment.CurrentManagedThreadId == _uiThread) return call();

        T result = default!;
        Exception? error = null;

        Action action = () =>
        {
            try
            {
                result = call();
            }
            catch (Exception exception)
            {
                error = exception;
            }
        };

        GCHandle handle = GCHandle.Alloc(action);

        try
        {
            UiaNative.SendMessage(_hwnd, WM_UIA_CALL, 0, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        if (error is not null) ExceptionDispatchInfo.Throw(error);

        return result;
    }

    internal void OnUiThread(Action call) => OnUiThread(() =>
    {
        call();
        return 0;
    });

    /// <summary>WM_UIA_CALL arrived on the UI thread: run what it carries.</summary>
    internal static void RunCall(nint lParam)
    {
        if (GCHandle.FromIntPtr(lParam).Target is Action action)
            action();
    }

    // ===== coordinates =====

    /// <summary>Client DIPs to screen pixels.</summary>
    internal UiaRect ToScreen(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return default;

        var origin = new UiaPoint();
        UiaNative.ClientToScreen(_hwnd, ref origin);

        float scale = _scale();

        return new UiaRect
        {
            Left = origin.X + bounds.X * scale,
            Top = origin.Y + bounds.Y * scale,
            Width = bounds.Width * scale,
            Height = bounds.Height * scale,
        };
    }

    /// <summary>Screen pixels to client DIPs.</summary>
    internal Point FromScreen(double x, double y)
    {
        var origin = new UiaPoint();
        UiaNative.ClientToScreen(_hwnd, ref origin);

        float scale = _scale();

        return new Point((float)((x - origin.X) / scale), (float)((y - origin.Y) / scale));
    }

    // ===== events =====

    private void StartListening()
    {
        if (_listening) return;

        _listening = true;

        AccessibilityEvents.FocusChanged += OnFocusChanged;
        AccessibilityEvents.PropertyChanged += OnPropertyChanged;
        AccessibilityEvents.StructureChanged += OnStructureChanged;
        AccessibilityEvents.Announcement += OnAnnouncement;
    }

    private void StopListening()
    {
        if (!_listening) return;

        _listening = false;

        AccessibilityEvents.FocusChanged -= OnFocusChanged;
        AccessibilityEvents.PropertyChanged -= OnPropertyChanged;
        AccessibilityEvents.StructureChanged -= OnStructureChanged;
        AccessibilityEvents.Announcement -= OnAnnouncement;
    }

    /// <summary>Only this window's peers, and only while some client listens:
    /// raising costs a cross-process call per event.</summary>
    private bool Concerns(AccessibilityPeer peer) =>
        ReferenceEquals(peer.Form, _form) && UiaNative.UiaClientsAreListening();

    private void OnFocusChanged(AccessibilityPeer peer)
    {
        if (!Concerns(peer)) return;

        Raise(peer, provider => UiaNative.UiaRaiseAutomationEvent(provider, UiaNative.AutomationFocusChangedEventId));
    }

    private void OnPropertyChanged(AccessibilityPeer peer, AccessibilityProperty property)
    {
        if (!Concerns(peer)) return;

        UiaProvider uia = ProviderFor(peer);

        switch (property)
        {
            case AccessibilityProperty.Name:
                RaiseProperty(peer, UiaNative.NamePropertyId, UiaVariant.From(peer.Name));
                break;

            case AccessibilityProperty.Description:
                RaiseProperty(peer, UiaNative.HelpTextPropertyId, UiaVariant.From(peer.Description ?? string.Empty));
                break;

            case AccessibilityProperty.Value when peer.Value is { } value:
                RaiseProperty(peer, UiaNative.ValueValuePropertyId, UiaVariant.From(value));
                break;

            case AccessibilityProperty.Range or AccessibilityProperty.Value when peer.Range is { } range:
                RaiseProperty(peer, UiaNative.RangeValueValuePropertyId,
                    UiaVariant.From(ComVariant.Create(range.Value)));
                break;

            case AccessibilityProperty.States:
                // which state changed is not said: the ones the patterns report
                // are raised all, a client compares them with what it knew
                if (uia.SupportsToggle)
                    RaiseProperty(peer, UiaNative.ToggleStatePropertyId, UiaVariant.From(uia.CurrentToggleState));

                if (uia.SupportsExpandCollapse)
                    RaiseProperty(peer, UiaNative.ExpandCollapseStatePropertyId, UiaVariant.From(uia.CurrentExpandCollapseState));

                if (uia.SupportsSelectionItem)
                    RaiseProperty(peer, UiaNative.SelectionItemIsSelectedPropertyId,
                        UiaVariant.From(peer.States.HasFlag(AccessibilityStates.Selected)));
                break;
        }

        // a live element is read out on its own when its name or value changes
        if (peer.LiveSetting != AccessibilityLiveSetting.Off &&
            property is AccessibilityProperty.Name or AccessibilityProperty.Value)
        {
            Raise(peer, provider => UiaNative.UiaRaiseAutomationEvent(provider, UiaNative.LiveRegionChangedEventId));
        }
    }

    private unsafe void OnStructureChanged(AccessibilityPeer peer)
    {
        if (!Concerns(peer)) return;

        int[] runtimeId = [UiaNative.UiaAppendRuntimeId, ProviderFor(peer).RuntimeId];

        Raise(peer, provider =>
        {
            fixed (int* id = runtimeId)
            {
                return UiaNative.UiaRaiseStructureChangedEvent(
                    provider, UiaNative.StructureChangeType_ChildrenInvalidated, id, runtimeId.Length);
            }
        });
    }

    /// <summary>Form.Announce: a notification on the window. Windows before 1709 has
    /// no such event, and the announcement is simply not made there.</summary>
    private void OnAnnouncement(Form form, string text, AccessibilityLiveSetting politeness)
    {
        if (!ReferenceEquals(form, _form) || _root is null || !UiaNative.UiaClientsAreListening()) return;

        int processing = politeness == AccessibilityLiveSetting.Assertive
            ? UiaNative.NotificationProcessing_ImportantMostRecent
            : UiaNative.NotificationProcessing_MostRecent;

        nint display = Marshal.StringToBSTR(text);
        nint activity = Marshal.StringToBSTR("ZeppelinForms.Announce");

        try
        {
            Raise(_root.Peer, provider => UiaNative.UiaRaiseNotificationEvent(
                provider, UiaNative.NotificationKind_Other, processing, display, activity));
        }
        catch (EntryPointNotFoundException)
        {
        }
        finally
        {
            Marshal.FreeBSTR(display);
            Marshal.FreeBSTR(activity);
        }
    }

    /// <summary>The value stays the caller's: a string in it is freed after the raise.</summary>
    private void RaiseProperty(AccessibilityPeer peer, int propertyId, UiaVariant value)
    {
        try
        {
            Raise(peer, provider =>
                UiaNative.UiaRaiseAutomationPropertyChangedEvent(provider, propertyId, UiaVariant.Empty, value));
        }
        finally
        {
            UiaNative.VariantClear(ref value);
        }
    }

    /// <summary>Raise with the peer's provider as IRawElementProviderSimple. A failed
    /// raise is not the application's problem: the event is lost, nothing else.</summary>
    private void Raise(AccessibilityPeer peer, Func<nint, int> raise)
    {
        nint provider = PointerFor(peer, typeof(IRawElementProviderSimple).GUID);

        try
        {
            raise(provider);
        }
        finally
        {
            Marshal.Release(provider);
        }
    }

    /// <summary>The window is going: UIA lets go of its providers, the core's
    /// events of this bridge.</summary>
    public void Dispose()
    {
        StopListening();

        if (_root is not null)
            UiaNative.UiaReturnRawElementProvider(_hwnd, 0, 0, 0);

        _root = null;
    }
}