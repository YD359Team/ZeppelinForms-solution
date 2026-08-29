namespace ZeppelinForms.Forms.Controls.Tools;

public static class PropertyCatalog
{
    private static readonly Dictionary<Type, PropertyDescriptor[]> Registry = [];

    // генератор дописывает сюда регистрацию через ModuleInitializer
    public static void Register(Type type, PropertyDescriptor[] properties) =>
        Registry[type] = properties;

    public static PropertyDescriptor[] For(Type type) =>
        Registry.TryGetValue(type, out var props) ? props : [];
}