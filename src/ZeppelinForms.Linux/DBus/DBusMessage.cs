namespace ZeppelinForms.Linux.DBus;

/// <summary>The kind of a D-Bus message: the second byte of its header.</summary>
internal enum DBusMessageType : byte
{
    MethodCall = 1,
    MethodReturn = 2,
    Error = 3,
    Signal = 4,
}

/// <summary>A received D-Bus message: the header fields that matter here
/// and the body, unmarshaled by its signature.</summary>
/// <remarks>
/// Values come as plain objects: byte, bool, short, ushort, int, uint, long, ulong,
/// double and string for the basic types, <see cref="DBusVariant"/> for a variant,
/// object[] for a struct or a dict entry, List&lt;object?&gt; for an array. That is
/// enough to read a setting and to skip whatever else arrives — the client asks
/// for little, but the bus may still send anything.
/// </remarks>
internal sealed record DBusMessage(
    DBusMessageType Type,
    uint Serial,
    uint? ReplySerial,
    string? Path,
    string? Interface,
    string? Member,
    string? ErrorName,
    string? Sender,
    string Signature,
    IReadOnlyList<object?> Body);

/// <summary>A variant: a value together with its own signature.</summary>
internal sealed record DBusVariant(string Signature, object? Value)
{
    /// <summary>The value under all the variants wrapped around it. The deprecated
    /// Settings.Read puts the setting into one variant more than ReadOne does.</summary>
    public object? Unwrap()
    {
        object? value = Value;

        while (value is DBusVariant inner)
            value = inner.Value;

        return value;
    }
}