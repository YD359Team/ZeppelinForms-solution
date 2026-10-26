using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Browser;

/// <summary>
/// An element of the page among the form's elements: an <c>iframe</c> with a map or a
/// video service, a <c>video</c>, an editor a JavaScript library builds into a
/// <c>div</c>. The element is made from <see cref="TagName"/> and
/// <see cref="Attributes"/>; <see cref="Element"/> gives it to JavaScript for the rest.
/// </summary>
/// <remarks>
/// <para>
/// The element is laid out with the host — placed, sized, cut by a scrolled panel round
/// it, hidden with a hidden page — and sits over the canvas: it takes its own clicks
/// and keys, and the form's popups open under it.
/// </para>
/// <para>
/// See <see cref="NativeControlHost"/> for what any native control can't do in a form.
/// </para>
/// </remarks>
public class HtmlElementHost : NativeControlHost
{
    /// <summary>The element's tag: <c>iframe</c>, <c>video</c>, <c>div</c>.</summary>
    public string TagName { get; init; } = "div";

    /// <summary>The element's attributes, set when it is made: <c>src</c>,
    /// <c>allow</c>, <c>controls</c>.</summary>
    public Dictionary<string, string> Attributes { get; init; } = [];

    /// <summary>The element, once made: a JavaScript object to pass to a library, or to
    /// set properties of; null before and after.</summary>
    public JSObject? Element { get; private set; }

    /// <summary>The element was made, before it is first placed: the moment to fill it.</summary>
    protected virtual void OnElementCreated(JSObject element) { }

    protected sealed override PlatformHandle CreateNativeControlCore(PlatformHandle parent)
    {
        if (parent.Kind != PlatformHandle.HtmlElement)
            throw new PlatformNotSupportedException($"An HtmlElementHost lives on a page, not in a {parent.Kind} window.");

        string attributes = JsonSerializer.Serialize(Attributes, BrowserJsonContext.Default.DictionaryStringString);

        Element = Interop.CreateSlotElement((int)parent.Handle, TagName, attributes)
            ?? throw new InvalidOperationException("The page has no slot for the element.");

        OnElementCreated(Element);

        return parent;
    }

    protected override void DestroyNativeControlCore(PlatformHandle control)
    {
        Element?.Dispose();
        Element = null;

        base.DestroyNativeControlCore(control);
    }
}

/// <summary>The attributes as JSON without reflection: the trimmer keeps what a
/// generated serializer needs.</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class BrowserJsonContext : System.Text.Json.Serialization.JsonSerializerContext;