using System.Buffers.Binary;
using System.Text;
using Xunit;
using ZeppelinForms.Linux.DBus;

namespace ZeppelinForms.UnitTests;

/// <summary>
/// The D-Bus wire format of the Linux portal client, without a bus: a call it
/// writes reads back the same, and a signal laid out by hand — the way the portal
/// sends SettingChanged — unmarshals into the values the client looks for.
/// </summary>
public class DBusWireTests
{
    [Fact]
    public void MethodCallReadsBack()
    {
        byte[] bytes = DBusWire.MethodCall(7, "org.freedesktop.portal.Desktop",
            "/org/freedesktop/portal/desktop", "org.freedesktop.portal.Settings", "ReadOne",
            "org.freedesktop.appearance", "color-scheme");

        Assert.Equal(bytes.Length, DBusWire.MessageLength(bytes));

        DBusMessage message = DBusWire.Parse(bytes);

        Assert.Equal(DBusMessageType.MethodCall, message.Type);
        Assert.Equal(7u, message.Serial);
        Assert.Equal("/org/freedesktop/portal/desktop", message.Path);
        Assert.Equal("org.freedesktop.portal.Settings", message.Interface);
        Assert.Equal("ReadOne", message.Member);
        Assert.Equal("ss", message.Signature);
        Assert.Equal(["org.freedesktop.appearance", "color-scheme"], message.Body);
    }

    [Fact]
    public void CallWithoutArgumentsHasNoBody()
    {
        byte[] bytes = DBusWire.MethodCall(1, "org.freedesktop.DBus", "/org/freedesktop/DBus",
            "org.freedesktop.DBus", "Hello");

        DBusMessage message = DBusWire.Parse(bytes);

        Assert.Equal(string.Empty, message.Signature);
        Assert.Empty(message.Body);

        // the body length in the header is zero, and the header ends on 8 bytes
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(0, bytes.Length % 8);
    }

    [Fact]
    public void LengthIsUnknownUntilFixedHeaderArrives()
    {
        byte[] bytes = DBusWire.MethodCall(1, "a.b", "/a", "a.b", "C", "x");

        Assert.Null(DBusWire.MessageLength(bytes.AsSpan(0, DBusWire.FixedHeaderLength - 1)));
        Assert.Equal(bytes.Length, DBusWire.MessageLength(bytes.AsSpan(0, DBusWire.FixedHeaderLength)));
    }

    [Fact]
    public void SettingChangedSignalUnmarshals()
    {
        byte[] bytes = SettingChangedSignal(0.2, 0.4, 0.8);

        DBusMessage message = DBusWire.Parse(bytes);

        Assert.Equal(DBusMessageType.Signal, message.Type);
        Assert.Equal("org.freedesktop.portal.Settings", message.Interface);
        Assert.Equal("SettingChanged", message.Member);
        Assert.Equal("ssv", message.Signature);

        Assert.Equal("org.freedesktop.appearance", message.Body[0]);
        Assert.Equal("accent-color", message.Body[1]);

        var value = Assert.IsType<DBusVariant>(message.Body[2]);
        Assert.Equal("(ddd)", value.Signature);

        object?[] rgb = Assert.IsType<object?[]>(value.Unwrap());
        Assert.Equal([0.2, 0.4, 0.8], rgb);
    }

    [Fact]
    public void NestedVariantUnwraps()
    {
        var inner = new DBusVariant("u", 1u);
        var outer = new DBusVariant("v", inner);

        Assert.Equal(1u, outer.Unwrap());
    }

    /// <summary>A SettingChanged signal laid out by the specification: a little-endian
    /// header with its fields, then the body (ssv) with a (ddd) variant — the
    /// doubles on an 8-byte boundary counted from the start of the message.</summary>
    private static byte[] SettingChangedSignal(double r, double g, double b)
    {
        var body = new List<byte>();
        int bodyStart = 0;

        void Align(List<byte> to, int start, int n) { while ((to.Count + start) % n != 0) to.Add(0); }

        void UInt32(List<byte> to, int start, uint v)
        {
            Align(to, start, 4);
            byte[] buf = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buf, v);
            to.AddRange(buf);
        }

        void Str(List<byte> to, int start, string s)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(s);
            UInt32(to, start, (uint)utf8.Length);
            to.AddRange(utf8);
            to.Add(0);
        }

        void Sig(List<byte> to, string s)
        {
            to.Add((byte)s.Length);
            to.AddRange(Encoding.ASCII.GetBytes(s));
            to.Add(0);
        }

        void Double(List<byte> to, int start, double v)
        {
            Align(to, start, 8);
            byte[] buf = new byte[8];
            BinaryPrimitives.WriteDoubleLittleEndian(buf, v);
            to.AddRange(buf);
        }

        var header = new List<byte> { (byte)'l', 4, 0, 1 };
        UInt32(header, 0, 0);          // body length, patched below
        UInt32(header, 0, 42);         // serial
        int fieldsLengthAt = header.Count;
        UInt32(header, 0, 0);          // fields length, patched below
        Align(header, 0, 8);
        int fieldsStart = header.Count;

        void Field(byte code, string sig, string value)
        {
            Align(header, 0, 8);
            header.Add(code);
            Sig(header, sig);

            if (sig == "g") Sig(header, value);
            else Str(header, 0, value);
        }

        Field(1, "o", "/org/freedesktop/portal/desktop");
        Field(2, "s", "org.freedesktop.portal.Settings");
        Field(3, "s", "SettingChanged");
        Field(8, "g", "ssv");
        Field(7, "s", ":1.13");

        BinaryPrimitives.WriteUInt32LittleEndian(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(header)[fieldsLengthAt..],
            (uint)(header.Count - fieldsStart));

        Align(header, 0, 8);
        bodyStart = header.Count;

        Str(body, bodyStart, "org.freedesktop.appearance");
        Str(body, bodyStart, "accent-color");
        Sig(body, "(ddd)");
        Align(body, bodyStart, 8);     // the struct
        Double(body, bodyStart, r);
        Double(body, bodyStart, g);
        Double(body, bodyStart, b);

        BinaryPrimitives.WriteUInt32LittleEndian(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(header)[4..],
            (uint)body.Count);

        return [.. header, .. body];
    }
}