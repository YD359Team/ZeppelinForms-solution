namespace ZeppelinForms.Forms.Controls.Tools;

public sealed class PropertyDescriptor(
    string name, Type type,
    Func<object, object?> get,
    Action<object, object?>? set = null)
{
    public string Name { get; } = name;
    public string Category { get; init; } = "Прочее";
    public Type Type { get; } = type;
    public bool IsReadOnly => set is null;

    public object? GetValue(object target) => get(target);
    public void SetValue(object target, object? value) => set?.Invoke(target, value);
}
