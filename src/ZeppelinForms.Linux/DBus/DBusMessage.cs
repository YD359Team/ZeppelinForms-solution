using ZeppelinForms.Linux.DBus;

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
    IReadOnlyList<object?> Body)
{
    /// <summary>The header's flags; <see cref="DBusWire.FlagNoReplyExpected"/> — a call
    /// that wants no answer.</summary>
    public byte Flags { get; init; }

    /// <summary>The name the message was sent to: a well-known name or the unique one.</summary>
    public string? Destination { get; init; }

    /// <summary>The caller waits for a reply.</summary>
    public bool ExpectsReply => (Flags & DBusWire.FlagNoReplyExpected) == 0;
}