using System.Runtime.CompilerServices;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.Tools;

/// <summary>
/// Temporary manual registration of properties — until the generator arrives.
/// Serves as a model of what the generator should produce.
/// </summary>
internal static class BuiltInProperties
{
    // CA2255: the module initializer here is intentional. The property catalog must
    // be filled before the first access to any control, and the library has no entry
    // point where this could be done explicitly. It will go away together with the
    // move to the generator, which will emit registration where the properties are declared
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "The property catalog must be ready before the first control")]
    [ModuleInitializer]
    internal static void Register()
    {
        // common to all UIElements — registered for each concrete type, because
        // PropertyCatalog looks up by the exact type, without walking the hierarchy
        RegisterFor<Button>();
        RegisterFor<Label>();
        RegisterFor<CheckBox>();
        RegisterFor<Panel>();
        RegisterFor<StackPanel>();
        RegisterFor<Grid>();
        RegisterFor<TextBox>();
    }

    private static void RegisterFor<T>() where T : UIElement =>
        PropertyCatalog.Register(typeof(T), CommonProperties<T>());

    private static PropertyDescriptor[] CommonProperties<T>() where T : UIElement =>
    [
        new("Name", typeof(string),
            o => ((T)o).Name,
            (o, v) => ((T)o).Name = (string)(v ?? string.Empty)),

        new("IsVisible", typeof(bool),
            o => ((T)o).IsVisible,
            (o, v) => ((T)o).IsVisible = (bool)(v ?? true)),

        new("IsEnabled", typeof(bool),
            o => ((T)o).IsEnabled,
            (o, v) => ((T)o).IsEnabled = (bool)(v ?? true)),

        new("Width", typeof(float),
            o => float.IsFinite(((T)o).Size.Width) ? ((T)o).Size.Width : 0f,
            (o, v) => ((T)o).Size = new Size((float)(v ?? 0f), ((T)o).Size.Height)),

        new("Height", typeof(float),
            o => float.IsFinite(((T)o).Size.Height) ? ((T)o).Size.Height : 0f,
            (o, v) => ((T)o).Size = new Size(((T)o).Size.Width, (float)(v ?? 0f))),

        // the actual size after layout — read-only
        new("ActualWidth", typeof(float), o => ((T)o).ActualSize.Width),
        new("ActualHeight", typeof(float), o => ((T)o).ActualSize.Height),

        new("Opacity", typeof(float),
            o => ((T)o).Opacity,
            (o, v) => ((T)o).Opacity = (float)(v ?? 1f)),

        new("Background", typeof(Color),
            o => ((T)o).Background,
            (o, v) => ((T)o).Background = (Color)(v ?? Colors.Transparent)),

        new("Docking", typeof(Dock),
            o => ((T)o).Docking,
            (o, v) => ((T)o).Docking = (Dock)(v ?? Dock.None)),

        new("HorizontalAlignment", typeof(HorizontalAlignment),
            o => ((T)o).HorizontalAlignment,
            (o, v) => ((T)o).HorizontalAlignment = (HorizontalAlignment)(v ?? HorizontalAlignment.Stretch)),

        new("VerticalAlignment", typeof(VerticalAlignment),
            o => ((T)o).VerticalAlignment,
            (o, v) => ((T)o).VerticalAlignment = (VerticalAlignment)(v ?? VerticalAlignment.Stretch)),

        // read-only — Position is set by layout, changing it by hand makes no sense
        new("Position", typeof(Point), o => ((T)o).Position),
        new("DesiredSize", typeof(Size), o => ((T)o).DesiredSize),
    ];
}