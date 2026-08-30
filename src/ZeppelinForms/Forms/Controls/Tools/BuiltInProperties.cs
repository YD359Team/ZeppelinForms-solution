using System.Runtime.CompilerServices;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.Tools;

/// <summary>
/// Временная ручная регистрация свойств — до появления генератора.
/// Служит образцом того, что генератор должен выпускать.
/// </summary>
internal static class BuiltInProperties
{
    [ModuleInitializer]
    internal static void Register()
    {
        // общие для всех UIElement — регистрируем на каждый конкретный тип,
        // потому что PropertyCatalog ищет по точному типу, без обхода иерархии
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

        // фактический размер после раскладки — только для чтения
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

        // только чтение — Position выставляет layout, руками менять бессмысленно
        new("Position", typeof(Point), o => ((T)o).Position),
        new("DesiredSize", typeof(Size), o => ((T)o).DesiredSize),
    ];
}