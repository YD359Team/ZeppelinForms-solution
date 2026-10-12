using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using ZeppelinForms.Dispatchers;
using ZeppelinForms.Forms.Styling;

[assembly: MetadataUpdateHandler(typeof(ZeppelinForms.Forms.HotReloadHandler))]

namespace ZeppelinForms.Forms;

/// <summary>
/// .NET Hot Reload for code-first interfaces: what the open forms do after an edit is
/// applied — Visual Studio's Hot Reload, <c>dotnet watch</c>.
/// </summary>
/// <remarks>
/// <para>
/// The runtime replaces the bodies of the edited methods, but a form built in its
/// constructor has already run it, and nothing is redrawn until something asks. After
/// every edit the framework therefore, on the UI thread:
/// </para>
/// <list type="number">
/// <item>calls <see cref="Form.OnHotReload"/> of every open form — the place to build
/// the content again;</item>
/// <item>remakes the content made by factories whose assembly changed: the entries of a
/// <c>SplitView</c>, the pages of a <c>PageControl</c>;</item>
/// <item>calls <see cref="Controls.Base.UIElement.OnHotReload"/> of every element;</item>
/// <item>applies the theme and the styles again, measures everything anew and redraws:
/// an edited <c>Draw</c>, <c>MeasureOverride</c> or theme rule shows at once;</item>
/// <item>raises <see cref="Applied"/>.</item>
/// </list>
/// <para>
/// Style sheets loaded from files follow their saves by themselves, see
/// <c>StyleSheetLink</c>. In a trimmed or natively compiled application there is no
/// Hot Reload, and none of this runs.
/// </para>
/// </remarks>
public static class HotReload
{
    /// <summary>Whether edits can be applied to this process at all: a debug run under
    /// Visual Studio or <c>dotnet watch</c>.</summary>
    public static bool IsSupported => MetadataUpdater.IsSupported;

    /// <summary>An edit was applied and the open forms have caught up with it. Raised on
    /// the UI thread, after <see cref="Form.OnHotReload"/> of every form.</summary>
    public static event EventHandler<HotReloadEventArgs>? Applied;

    /// <summary>Bring the open forms up to date with an edit. On the UI thread.</summary>
    /// <param name="updatedTypes">The types the edit changed; null — the runtime didn't
    /// say, and everything counts as changed.</param>
    internal static void Apply(Type[]? updatedTypes)
    {
        var e = new HotReloadEventArgs(updatedTypes);

        foreach (Form form in Form.OpenForms.ToArray())
            form.ApplyHotReload(e);

        Run(() => Applied?.Invoke(null, e), "HotReload.Applied");
    }

    /// <summary>Run a reaction to an edit, and survive its failure: code in the middle
    /// of being edited throws easily, and one broken page must not close the
    /// application the edit was meant to keep alive.</summary>
    internal static void Run(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Trace.TraceError($"[ZF] Hot Reload: {what} failed: {exception}");
        }
    }
}

/// <summary>An edit applied by Hot Reload: which types it changed.</summary>
/// <remarks>
/// The runtime names the types whose members the edit touched, compiler-made ones
/// among them — the closure of a lambda, the state machine of an async method. They
/// count as their outermost type: an edit inside a lambda of <c>MainForm</c> changed
/// <c>MainForm</c>. When the runtime doesn't name them, every question here answers yes.
/// </remarks>
public sealed class HotReloadEventArgs : EventArgs
{
    private readonly HashSet<Type>? _changed;
    private readonly HashSet<Assembly>? _assemblies;

    internal HotReloadEventArgs(Type[]? updatedTypes)
    {
        UpdatedTypes = updatedTypes ?? [];

        if (updatedTypes is null) return;

        _changed = [];
        _assemblies = [];

        foreach (Type type in updatedTypes)
        {
            _changed.Add(Outermost(type));
            _assemblies.Add(type.Assembly);
        }
    }

    /// <summary>The types the edit changed, as the runtime named them. Empty when it
    /// didn't name them — see <see cref="AreTypesKnown"/>.</summary>
    public IReadOnlyList<Type> UpdatedTypes { get; }

    /// <summary>Whether the runtime named the changed types. When it didn't, everything
    /// counts as changed.</summary>
    public bool AreTypesKnown => _changed is not null;

    /// <summary>Whether the edit changed the type or a type nested in it — a lambda's
    /// closure, an iterator, an async method's state machine.</summary>
    public bool Changed(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return _changed is null || _changed.Contains(Outermost(type));
    }

    /// <summary>Whether the edit changed anything in the assembly.</summary>
    public bool Changed(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return _assemblies is null || _assemblies.Contains(assembly);
    }

    /// <summary>Whether the edit changed the assembly the code of a delegate lives in.
    /// It is what decides whether content made by a factory is made again: a page built
    /// by <c>OrdersPage.Create</c> is built from helpers of its assembly as well, and an
    /// edit of a helper must show too.</summary>
    public bool Changed(Delegate code)
    {
        ArgumentNullException.ThrowIfNull(code);

        return code.Method.DeclaringType is not { } type || Changed(type.Assembly);
    }

    private static Type Outermost(Type type)
    {
        if (type.IsGenericType && !type.IsGenericTypeDefinition)
            type = type.GetGenericTypeDefinition();

        while (type.DeclaringType is { } outer)
            type = outer;

        return type;
    }
}

/// <summary>Where the runtime reports an applied edit: found by the
/// <see cref="MetadataUpdateHandlerAttribute"/> of the assembly and called by name.</summary>
internal static class HotReloadHandler
{
    /// <summary>Before the application hears of the edit: caches that describe types.</summary>
    public static void ClearCache(Type[]? updatedTypes)
    {
        if (updatedTypes is not null)
            HotReload.Run(() => StyleTypes.NoteUpdated(updatedTypes), "registering new controls");
    }

    /// <summary>Called on the thread that applied the edit — not the UI thread.</summary>
    public static void UpdateApplication(Type[]? updatedTypes) =>
        Dispatcher.UIThread.BeginInvoke(() => HotReload.Apply(updatedTypes));
}
