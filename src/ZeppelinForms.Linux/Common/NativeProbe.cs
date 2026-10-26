using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace ZeppelinForms.Linux;

/// <summary>Whether an optional native library is on the machine, asked once.</summary>
/// <remarks>A call into a missing library throws DllNotFoundException on every call:
/// caught, it cost nothing in the result but filled the debugger's output — a cursor
/// set or a screen listed on every move threw again. Asked here first, a missing one
/// throws nowhere.</remarks>
internal static class NativeProbe
{
    private static readonly ConcurrentDictionary<string, bool> s_found = new(StringComparer.Ordinal);

    /// <summary>The library loads by this name, as a DllImport of it would load it.
    /// The handle stays loaded: the DllImports use it right after.</summary>
    public static bool Has(string library) =>
        s_found.GetOrAdd(library, static name => NativeLibrary.TryLoad(name, out _));
}