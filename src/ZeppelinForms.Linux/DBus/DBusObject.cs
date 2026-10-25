namespace ZeppelinForms.Linux.DBus;

/// <summary>An object the application exports on the bus, at a path of its connection:
/// a tray icon, its menu. It answers the calls made to it.</summary>
internal interface IDBusObject
{
    /// <summary>Answer a call — on the UI thread, as the loop reads the bus. Throw
    /// <see cref="DBusErrorException"/> for an error reply; for an unknown member,
    /// <see cref="DBusErrorException.UnknownMethod"/>.</summary>
    DBusReply Call(DBusMessage call);
}

/// <summary>The values of a reply, by their signature.</summary>
internal readonly record struct DBusReply(string Signature, IReadOnlyList<object?> Values)
{
    /// <summary>A reply without values.</summary>
    public static DBusReply Empty { get; } = new(string.Empty, []);

    /// <summary>A reply of one value.</summary>
    public static DBusReply Of(string signature, object? value) => new(signature, [value]);
}

/// <summary>An error reply to a call: its D-Bus name and a message.</summary>
internal sealed class DBusErrorException(string name, string message) : Exception(message)
{
    public string Name { get; } = name;

    public static DBusErrorException UnknownMethod(DBusMessage call) =>
        new("org.freedesktop.DBus.Error.UnknownMethod",
            $"No method {call.Member} with signature '{call.Signature}' in {call.Interface} at {call.Path}.");

    public static DBusErrorException UnknownProperty(string @interface, string name) =>
        new("org.freedesktop.DBus.Error.UnknownProperty", $"No property {name} in {@interface}.");

    public static DBusErrorException ReadOnly(string name) =>
        new("org.freedesktop.DBus.Error.PropertyReadOnly", $"The property {name} is read-only.");
}