using System.Runtime.InteropServices.JavaScript;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Browser;

/// <summary>
/// The only place where the project meets JavaScript. Everything else works
/// with ordinary types, so that browser specifics don't spread around.
/// </summary>
internal static partial class Interop
{
    internal const string ModuleName = "zf";

    // ==== C# -> JS ====

    [JSImport("prefersReducedMotion", ModuleName)]
    internal static partial bool PrefersReducedMotion();

    [JSImport("prefersDarkColorScheme", ModuleName)]
    internal static partial bool PrefersDarkColorScheme();

    /// <summary>The CSS system color AccentColor as computed, "rgb(r, g, b)";
    /// an empty string where the browser doesn't support it.</summary>
    [JSImport("systemAccentColor", ModuleName)]
    internal static partial string SystemAccentColor();

    /// <summary>Apply a snapshot of the accessibility tree to the ARIA mirror,
    /// see AriaSnapshot.</summary>
    [JSImport("updateAccessibilityTree", ModuleName)]
    internal static partial void UpdateAccessibilityTree(string json);

    /// <summary>Have a screen reader say the text: into the polite live region,
    /// or the assertive one.</summary>
    [JSImport("announce", ModuleName)]
    internal static partial void Announce(string text, bool assertive);

    [JSImport("forcedColorsActive", ModuleName)]
    internal static partial bool ForcedColorsActive();

    /// <summary>The forced-colors system colors, "|"-separated, in the order of
    /// HighContrastPalette.</summary>
    [JSImport("systemColors", ModuleName)]
    internal static partial string SystemColors();

    /// <summary>The page address. HttpClient in a browser doesn't know its origin
    /// by itself, and it doesn't accept relative addresses without a BaseAddress.</summary>
    [JSImport("baseUri", ModuleName)]
    internal static partial string BaseUri();

    /// <summary>Bind to the canvas and hang the event handlers.</summary>
    [JSImport("init", ModuleName)]
    internal static partial void Init(string canvasId);

    /// <summary>Copy a finished frame onto the canvas. The Span arrives in JS
    /// as a view right onto the WASM memory, with no copy on this side.</summary>
    [JSImport("present", ModuleName)]
    internal static partial void Present(
        [JSMarshalAs<JSType.MemoryView>] Span<byte> pixels,
        int width,
        int height);

    /// <summary>Request one frame through requestAnimationFrame.</summary>
    [JSImport("requestFrame", ModuleName)]
    internal static partial void RequestFrame();

    /// <summary>Queue a microtask to drain the Invoke queue.
    /// Not through a frame: Invoke must work even when no frames are requested.</summary>
    [JSImport("scheduleDrain", ModuleName)]
    internal static partial void ScheduleDrain();

    [JSImport("setCursor", ModuleName)]
    internal static partial void SetCursor(string cssCursor);

    [JSImport("setTitle", ModuleName)]
    internal static partial void SetTitle(string title);

    [JSImport("setFavicon", ModuleName)]
    internal static partial void SetFavicon(string dataUrl);

    /// <summary>The viewport size in physical pixels and devicePixelRatio.</summary>
    [JSImport("viewportWidth", ModuleName)]
    internal static partial int ViewportWidth();

    [JSImport("viewportHeight", ModuleName)]
    internal static partial int ViewportHeight();

    [JSImport("devicePixelRatio", ModuleName)]
    internal static partial double DevicePixelRatio();

    /// <summary>Open the system file picker. Returns the names separated by line
    /// breaks, and puts the files themselves into /uploads of the virtual FS.
    /// An empty string — cancelled.</summary>
    [JSImport("pickFiles", ModuleName)]
    internal static partial Task<string> PickFilesAsync(string accept, bool multiple);

    /// <summary>Hand a file to the user as a download.</summary>
    [JSImport("downloadFile", ModuleName)]
    internal static partial void DownloadFile(string fileName, string base64);

    [JSImport("readClipboard", ModuleName)]
    internal static partial Task<string> ReadClipboardAsync();

    [JSImport("writeClipboard", ModuleName)]
    internal static partial void WriteClipboard(string text);

    /// <summary>The contents of the picked files: JSON of the form [{name, data}],
    /// data in base64. Written into /uploads, where an ordinary File reads them.</summary>
    [JSExport]
    internal static void OnFilesPicked(string json) => BrowserFilePicker.Save(json);

    // ==== JS -> C# ====
    // There is one canvas and one platform, so no window identifier comes from JS:
    // the platform itself decides whom to give the input to.

    internal static BrowserPlatform? Platform;

    [JSExport]
    internal static void OnFrame(double timestampMs) => Platform?.HandleFrame(timestampMs);

    [JSExport]
    internal static void OnDrain() => Platform?.DrainInvokes();

    /// <summary>A screen reader acted on a node of the ARIA mirror: "click" — the
    /// default action, "focus" — moved its own focus there.</summary>
    [JSExport]
    internal static void OnAccessibilityAction(string id, string action) =>
        Platform?.AriaMirror?.Act(id, action);

    [JSExport]
    internal static void OnResize(int physicalWidth, int physicalHeight, double scale) =>
        Platform?.HandleResize(physicalWidth, physicalHeight, (float)scale);

    /// <summary>kind: 0 — mouse, 1 — touch, 2 — pen. timestampMs — browser time
    /// since the page started loading, not TickCount64: BrowserWindow converts it,
    /// here it goes as is.</summary>
    [JSExport]
    internal static void OnPointerMove(
        double x, double y, int pointerId, int kind, double pressure, double timestampMs, int modifiers) =>
        Platform?.InputTarget()?.HandlePointerMove(x, y, pointerId, kind, pressure, timestampMs, modifiers);

    [JSExport]
    internal static void OnPointerDown(
        double x, double y, int pointerId, int kind, int button, double pressure, double timestampMs, int modifiers) =>
        Platform?.InputTarget()?.HandlePointerDown(x, y, pointerId, kind, button, pressure, timestampMs, modifiers);

    [JSExport]
    internal static void OnPointerUp(
        double x, double y, int pointerId, int kind, int button, double pressure, double timestampMs, int modifiers) =>
        Platform?.InputTarget()?.HandlePointerUp(x, y, pointerId, kind, button, pressure, timestampMs, modifiers);

    [JSExport]
    internal static void OnPointerCancel(int pointerId) =>
        Platform?.InputTarget()?.HandlePointerCancel(pointerId);

    [JSExport]
    internal static void OnPointerLeave() => Platform?.InputTarget()?.HandlePointerLeave();

    /// <summary>The deltas are already in pixels: zf.js brings lines and pages
    /// to pixels by deltaMode before calling.</summary>
    [JSExport]
    internal static void OnWheel(double x, double y, double deltaY, double deltaX) =>
        Platform?.InputTarget()?.HandleWheel(x, y, deltaY, deltaX);

    [JSExport]
    internal static void OnContextMenu(double x, double y) =>
        Platform?.InputTarget()?.HandleContextMenu(x, y);

    /// <summary>code — the physical key, key — the character according to the layout.
    /// The former is needed for navigation, the latter for text input.</summary>
    [JSExport]
    internal static void OnKeyDown(string code, string key, int modifiers, bool isRepeat) =>
        Platform?.InputTarget()?.HandleKeyDown(code, key, modifiers, isRepeat);

    [JSExport]
    internal static void OnKeyUp(string code, int modifiers) =>
        Platform?.InputTarget()?.HandleKeyUp(code, modifiers);

    [JSExport]
    internal static void OnFocusLost() => Platform?.InputTarget()?.HandleFocusLost();

    [JSExport]
    internal static void OnVisibilityChange(bool visible)
    {
        // back on the tab: the accent may have been changed in the system meanwhile,
        // and the browser has no event for it
        if (visible) Platform?.RefreshAppearance();

        Platform?.HandleVisibilityChange(visible);
    }

    [JSExport]
    internal static void OnColorSchemeChange() => Platform?.RefreshAppearance();

    [JSExport]
    internal static void OnReducedMotionChange(bool reduced) =>
    Platform?.HandleReducedMotionChange(reduced);

    [JSExport]
    internal static void OnPageHide() => Platform?.HandlePageHide();

    /// <summary>The CSS name of the cursor for each kind. Default and Arrow are the
    /// same thing in a browser: we have no arrow of our own, the system draws it.</summary>
    internal static string ToCssCursor(CursorKind cursor) => cursor switch
    {
        CursorKind.Hand => "pointer",
        CursorKind.IBeam => "text",
        CursorKind.Wait => "wait",
        CursorKind.SizeWestEast => "ew-resize",
        CursorKind.SizeNorthSouth => "ns-resize",
        CursorKind.SizeAll => "move",
        CursorKind.Cross => "crosshair",
        CursorKind.No => "not-allowed",
        _ => "default",
    };
}