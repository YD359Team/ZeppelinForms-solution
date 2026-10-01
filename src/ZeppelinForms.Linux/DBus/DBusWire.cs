using System.Buffers.Binary;
using System.Text;

namespace ZeppelinForms.Linux.DBus;

/// <summary>The D-Bus wire format: writing method calls, reading any message.</summary>
/// <remarks>
/// <para>
/// Every value is aligned to its own size from the start of the message: 4 for
/// integers and the length of a string or an array, 8 for 64-bit values, structs
/// and dict entries, 1 for bytes and signatures. A string is its length, UTF-8 and
/// a terminating zero; a signature the same with a one-byte length. An array's
/// length counts its elements only — not the padding before the first one.
/// </para>
/// <para>
/// Only what the client sends is written: a call with string arguments. Reading
/// covers every type except Unix file descriptors, which the client never asks
/// for and the bus never sends unasked.
/// </para>
/// </remarks>
internal static class DBusWire
{
    /// <summary>The fixed part of a header: endianness, type, flags, version,
    /// body length, serial — and the length of the header fields array.</summary>
    internal const int FixedHeaderLength = 16;

    private const byte FieldPath = 1;
    private const byte FieldInterface = 2;
    private const byte FieldMember = 3;
    private const byte FieldErrorName = 4;
    private const byte FieldReplySerial = 5;
    private const byte FieldDestination = 6;
    private const byte FieldSender = 7;
    private const byte FieldSignature = 8;

    // ===== writing =====

    /// <summary>A method call with string arguments, ready to send.</summary>
    internal static byte[] MethodCall(
        uint serial, string destination, string path, string @interface, string member,
        params string[] arguments)
    {
        var body = new Writer();

        foreach (string argument in arguments)
            body.String(argument);

        byte[] bodyBytes = body.ToArray();

        var header = new Writer();

        header.Byte((byte)'l');                       // little-endian
        header.Byte((byte)DBusMessageType.MethodCall);
        header.Byte(0);                               // flags
        header.Byte(1);                               // protocol version
        header.UInt32((uint)bodyBytes.Length);
        header.UInt32(serial);

        int lengthAt = header.Length;
        header.UInt32(0);                             // patched below
        header.Align(8);

        int fieldsStart = header.Length;

        header.Field(FieldPath, "o", w => w.String(path));
        header.Field(FieldDestination, "s", w => w.String(destination));
        header.Field(FieldInterface, "s", w => w.String(@interface));
        header.Field(FieldMember, "s", w => w.String(member));

        if (arguments.Length > 0)
            header.Field(FieldSignature, "g", w => w.Signature(new string('s', arguments.Length)));

        header.PatchUInt32(lengthAt, (uint)(header.Length - fieldsStart));

        // the body starts on an 8-byte boundary
        header.Align(8);

        return [.. header.ToArray(), .. bodyBytes];
    }

    private sealed class Writer
    {
        private readonly List<byte> _bytes = [];

        public int Length => _bytes.Count;

        public void Align(int alignment)
        {
            while (_bytes.Count % alignment != 0)
                _bytes.Add(0);
        }

        public void Byte(byte value) => _bytes.Add(value);

        public void UInt32(uint value)
        {
            Align(4);

            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);

            foreach (byte b in buffer) _bytes.Add(b);
        }

        public void PatchUInt32(int at, uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);

            for (int i = 0; i < 4; i++) _bytes[at + i] = buffer[i];
        }

        public void String(string value)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(value);

            UInt32((uint)utf8.Length);
            _bytes.AddRange(utf8);
            _bytes.Add(0);
        }

        public void Signature(string value)
        {
            _bytes.Add((byte)value.Length);
            _bytes.AddRange(Encoding.ASCII.GetBytes(value));
            _bytes.Add(0);
        }

        /// <summary>A header field: a (yv) struct — the code and a variant.</summary>
        public void Field(byte code, string signature, Action<Writer> value)
        {
            Align(8);
            Byte(code);
            Signature(signature);
            value(this);
        }

        public byte[] ToArray() => [.. _bytes];
    }

    // ===== reading =====

    /// <summary>The full length of the message whose start is in
    /// <paramref name="data"/>, once its fixed header has arrived; null before.</summary>
    internal static int? MessageLength(ReadOnlySpan<byte> data)
    {
        if (data.Length < FixedHeaderLength) return null;

        bool big = data[0] == (byte)'B';

        uint body = big ? BinaryPrimitives.ReadUInt32BigEndian(data[4..])
            : BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);

        uint fields = big ? BinaryPrimitives.ReadUInt32BigEndian(data[12..])
            : BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);

        long header = FixedHeaderLength + (long)fields;
        header = (header + 7) & ~7L;

        long total = header + body;

        // the specification caps a message at 128 MiB; anything above is garbage
        if (total > 128L * 1024 * 1024)
            throw new InvalidDataException("A D-Bus message longer than the protocol allows.");

        return (int)total;
    }

    /// <summary>Unmarshal one whole message.</summary>
    internal static DBusMessage Parse(byte[] data)
    {
        var reader = new Reader(data, bigEndian: data[0] == (byte)'B');

        reader.Skip(1);                                 // endianness, already known
        var type = (DBusMessageType)reader.Byte();
        reader.Skip(2);                                 // flags, version
        uint bodyLength = reader.UInt32();
        uint serial = reader.UInt32();

        string? path = null, @interface = null, member = null, errorName = null, sender = null;
        string signature = string.Empty;
        uint? replySerial = null;

        // the header fields: an array of (yv)
        uint fieldsLength = reader.UInt32();
        reader.Align(8);
        int fieldsEnd = reader.Position + (int)fieldsLength;

        while (reader.Position < fieldsEnd)
        {
            reader.Align(8);

            byte code = reader.Byte();
            object? value = ((DBusVariant)reader.Value("v")!).Value;

            switch (code)
            {
                case FieldPath: path = value as string; break;
                case FieldInterface: @interface = value as string; break;
                case FieldMember: member = value as string; break;
                case FieldErrorName: errorName = value as string; break;
                case FieldReplySerial: replySerial = value as uint?; break;
                case FieldSender: sender = value as string; break;
                case FieldSignature: signature = value as string ?? string.Empty; break;

                // the destination and unknown fields are not needed: the bus
                // routed the message here, and new fields must be skipped
                default: break;
            }
        }

        reader.Align(8);

        var body = new List<object?>();
        int bodyEnd = reader.Position + (int)bodyLength;

        for (int i = 0; i < signature.Length && reader.Position < bodyEnd;)
        {
            int length = TypeLength(signature, i);
            body.Add(reader.Value(signature.Substring(i, length)));
            i += length;
        }

        return new DBusMessage(type, serial, replySerial, path, @interface, member,
            errorName, sender, signature, body);
    }

    /// <summary>The length of the single complete type at <paramref name="start"/>.</summary>
    private static int TypeLength(string signature, int start)
    {
        char c = signature[start];

        if (c == 'a') return 1 + TypeLength(signature, start + 1);

        if (c is '(' or '{')
        {
            char close = c == '(' ? ')' : '}';
            int i = start + 1;

            while (signature[i] != close)
                i += TypeLength(signature, i);

            return i - start + 1;
        }

        return 1;
    }

    private static int Alignment(char type) => type switch
    {
        'y' or 'g' or 'v' => 1,
        'n' or 'q' => 2,
        'x' or 't' or 'd' or '(' or '{' => 8,
        _ => 4,   // b i u h s o a
    };

    private sealed class Reader(byte[] data, bool bigEndian)
    {
        public int Position { get; private set; }

        public void Skip(int count) => Position += count;

        public void Align(int alignment) =>
            Position = (Position + alignment - 1) / alignment * alignment;

        private ReadOnlySpan<byte> Take(int count)
        {
            if (Position + count > data.Length)
                throw new InvalidDataException("A D-Bus message shorter than its own header says.");

            ReadOnlySpan<byte> span = data.AsSpan(Position, count);
            Position += count;

            return span;
        }

        public byte Byte() => Take(1)[0];

        public uint UInt32()
        {
            Align(4);
            ReadOnlySpan<byte> s = Take(4);

            return bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(s) : BinaryPrimitives.ReadUInt32LittleEndian(s);
        }

        private ulong UInt64()
        {
            Align(8);
            ReadOnlySpan<byte> s = Take(8);

            return bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(s) : BinaryPrimitives.ReadUInt64LittleEndian(s);
        }

        private ushort UInt16()
        {
            Align(2);
            ReadOnlySpan<byte> s = Take(2);

            return bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(s) : BinaryPrimitives.ReadUInt16LittleEndian(s);
        }

        private string String()
        {
            int length = (int)UInt32();
            string value = Encoding.UTF8.GetString(Take(length));
            Skip(1);   // the terminating zero

            return value;
        }

        private string Signature()
        {
            int length = Byte();
            string value = Encoding.ASCII.GetString(Take(length));
            Skip(1);

            return value;
        }

        /// <summary>One value of a single complete type.</summary>
        public object? Value(string type)
        {
            switch (type[0])
            {
                case 'y': return Byte();
                case 'b': return UInt32() != 0;
                case 'n': return (short)UInt16();
                case 'q': return UInt16();
                case 'i': return (int)UInt32();
                case 'u': return UInt32();
                case 'h': return UInt32();
                case 'x': return (long)UInt64();
                case 't': return UInt64();
                case 'd': return BitConverter.UInt64BitsToDouble(UInt64());
                case 's': case 'o': return String();
                case 'g': return Signature();

                case 'v':
                    {
                        string signature = Signature();

                        return new DBusVariant(signature, Value(signature));
                    }

                case 'a':
                    {
                        int length = (int)UInt32();
                        string element = type[1..];

                        // the padding before the first element is not counted in the length
                        Align(Alignment(element[0]));

                        int end = Position + length;
                        var items = new List<object?>();

                        while (Position < end)
                            items.Add(Value(element));

                        return items;
                    }

                case '(':
                case '{':
                    {
                        Align(8);

                        var fields = new List<object?>();
                        string inner = type[1..^1];

                        for (int i = 0; i < inner.Length;)
                        {
                            int length = TypeLength(inner, i);
                            fields.Add(Value(inner.Substring(i, length)));
                            i += length;
                        }

                        return fields.ToArray();
                    }

                default:
                    throw new InvalidDataException($"Unknown D-Bus type '{type[0]}'.");
            }
        }
    }
}