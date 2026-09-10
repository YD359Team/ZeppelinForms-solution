using System.Runtime.InteropServices.JavaScript;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Browser;

/// <summary>
/// Единственное место, где проект встречается с JavaScript. Всё остальное
/// работает с обычными типами, чтобы браузерная специфика не расползалась.
/// </summary>
internal static partial class Interop
{
    internal const string ModuleName = "zf";

    // ==== C# -> JS ====

    /// <summary>Привязаться к canvas и развесить обработчики событий.</summary>
    [JSImport("init", ModuleName)]
    internal static partial void Init(string canvasId);

    /// <summary>Скопировать готовый кадр на canvas. Span приходит в JS
    /// как представление прямо на память WASM, без копии на этой стороне.</summary>
    [JSImport("present", ModuleName)]
    internal static partial void Present(
        [JSMarshalAs<JSType.MemoryView>] Span<byte> pixels,
        int width,
        int height);

    /// <summary>Запросить один кадр через requestAnimationFrame.</summary>
    [JSImport("requestFrame", ModuleName)]
    internal static partial void RequestFrame();

    /// <summary>Поставить в очередь микрозадачу для разбора очереди Invoke.
    /// Через кадр нельзя: Invoke обязан сработать и когда кадров не просят.</summary>
    [JSImport("scheduleDrain", ModuleName)]
    internal static partial void ScheduleDrain();

    [JSImport("setCursor", ModuleName)]
    internal static partial void SetCursor(string cssCursor);

    [JSImport("setTitle", ModuleName)]
    internal static partial void SetTitle(string title);

    /// <summary>Размер области просмотра в физических пикселях и devicePixelRatio.</summary>
    [JSImport("viewportWidth", ModuleName)]
    internal static partial int ViewportWidth();

    [JSImport("viewportHeight", ModuleName)]
    internal static partial int ViewportHeight();

    [JSImport("devicePixelRatio", ModuleName)]
    internal static partial double DevicePixelRatio();

    [JSImport("readClipboard", ModuleName)]
    internal static partial Task<string> ReadClipboardAsync();

    [JSImport("writeClipboard", ModuleName)]
    internal static partial void WriteClipboard(string text);

    // ==== JS -> C# ====
    // Canvas один, платформа тоже, поэтому идентификатор окна из JS
    // не приходит: кому отдать ввод, решает сама платформа.

    internal static BrowserPlatform? Platform;

    [JSExport]
    internal static void OnFrame(double timestampMs) => Platform?.HandleFrame(timestampMs);

    [JSExport]
    internal static void OnDrain() => Platform?.DrainInvokes();

    [JSExport]
    internal static void OnResize(int physicalWidth, int physicalHeight, double scale) =>
        Platform?.HandleResize(physicalWidth, physicalHeight, (float)scale);

    [JSExport]
    internal static void OnPointerMove(double x, double y, int modifiers) =>
        Platform?.InputTarget()?.HandlePointerMove(x, y, modifiers);

    [JSExport]
    internal static void OnPointerDown(double x, double y, int button, int modifiers) =>
        Platform?.InputTarget()?.HandlePointerDown(x, y, button, modifiers);

    [JSExport]
    internal static void OnPointerUp(double x, double y, int button, int modifiers) =>
        Platform?.InputTarget()?.HandlePointerUp(x, y, button, modifiers);

    [JSExport]
    internal static void OnPointerLeave() => Platform?.InputTarget()?.HandlePointerLeave();

    [JSExport]
    internal static void OnWheel(double x, double y, double deltaY, double deltaX) =>
        Platform?.InputTarget()?.HandleWheel(x, y, deltaY, deltaX);

    [JSExport]
    internal static void OnContextMenu(double x, double y) =>
        Platform?.InputTarget()?.HandleContextMenu(x, y);

    /// <summary>code — физическая клавиша, key — символ с учётом раскладки.
    /// Первое нужно для навигации, второе для ввода текста.</summary>
    [JSExport]
    internal static void OnKeyDown(string code, string key, int modifiers, bool isRepeat) =>
        Platform?.InputTarget()?.HandleKeyDown(code, key, modifiers, isRepeat);

    [JSExport]
    internal static void OnKeyUp(string code, int modifiers) =>
        Platform?.InputTarget()?.HandleKeyUp(code, modifiers);

    [JSExport]
    internal static void OnFocusLost() => Platform?.InputTarget()?.HandleFocusLost();

    [JSExport]
    internal static void OnVisibilityChange(bool visible) =>
        Platform?.HandleVisibilityChange(visible);

    [JSExport]
    internal static void OnPageHide() => Platform?.HandlePageHide();

    /// <summary>CSS-имя курсора для каждого вида. Default и Arrow в браузере
    /// одно и то же: своей стрелки у нас нет, рисует её система.</summary>
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