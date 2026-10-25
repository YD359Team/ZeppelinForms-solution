using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace ZeppelinForms.Linux.DBus;

/// <summary>A minimal connection to the session bus: enough to call a method,
/// to receive signals, and to export objects that answer calls and send signals of
/// their own. No dependency — the protocol is a socket, a line of text to
/// authenticate, and binary messages after it.</summary>
/// <remarks>
/// <para>
/// It doesn't run a thread of its own. Calls made at startup wait for their reply
/// on the socket, with a timeout. After that the X11 loop polls the socket next to
/// the X connection: <see cref="FileDescriptor"/> goes into the same select, and
/// <see cref="ProcessIncoming"/> reads what has arrived. A signal is thus handled
/// on the UI thread, where ISystemAppearance promises to raise its event, and
/// nothing has to be marshaled.
/// </para>
/// <para>
/// An exported object (<see cref="Register"/>) is called the same way: a call to it
/// is read by <see cref="ProcessIncoming"/> on the UI thread, answered by the object,
/// and the reply goes out at once. A call that comes while the application waits for a
/// reply of its own is answered too — the other side may need the answer to reply.
/// </para>
/// <para>
/// Every failure — no bus, a refused authentication, a broken connection —
/// ends in a closed connection rather than an exception: the system appearance
/// is a nicety, and its absence must not keep the application from starting.
/// </para>
/// </remarks>
internal sealed class DBusConnection : IDisposable
{
    private const string BusName = "org.freedesktop.DBus";
    private const string BusPath = "/org/freedesktop/DBus";
    private const string IntrospectableInterface = "org.freedesktop.DBus.Introspectable";

    private readonly Socket _socket;
    private readonly List<byte> _incoming = [];
    private uint _serial;

    // the objects the application exports, by path
    private readonly Dictionary<string, IDBusObject> _objects = new(StringComparer.Ordinal);

    // the serials of the calls waiting for a reply, and the replies a nested wait read
    // for an outer one
    private readonly HashSet<uint> _waiting = [];
    private readonly Dictionary<uint, DBusMessage> _replies = [];

    /// <summary>A signal arrived — on the thread that called <see cref="ProcessIncoming"/>.</summary>
    public event Action<DBusMessage>? Signal;

    public bool IsOpen { get; private set; } = true;

    /// <summary>The name the bus gave the connection at Hello: ":1.42".</summary>
    public string? UniqueName { get; private set; }

    /// <summary>The socket's descriptor, for the select of the event loop; −1 once closed.</summary>
    public int FileDescriptor => IsOpen ? (int)_socket.Handle : -1;

    private DBusConnection(Socket socket) => _socket = socket;

    [DllImport("libc")]
    private static extern uint getuid();

    /// <summary>Connect to the session bus and say Hello; null if there is none.</summary>
    public static DBusConnection? ConnectSession(TimeSpan timeout)
    {
        foreach (UnixDomainSocketEndPoint endpoint in SessionAddresses())
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
            {
                ReceiveTimeout = (int)timeout.TotalMilliseconds,
                SendTimeout = (int)timeout.TotalMilliseconds,
            };

            try
            {
                socket.Connect(endpoint);

                var connection = new DBusConnection(socket);

                if (connection.Authenticate() &&
                    connection.Call(BusName, BusPath, BusName, "Hello") is
                    { Type: DBusMessageType.MethodReturn } hello)
                {
                    connection.UniqueName = hello.Body.Count > 0 ? hello.Body[0] as string : null;

                    return connection;
                }

                connection.Dispose();
            }
            catch (Exception exception) when (exception is SocketException or IOException or InvalidDataException)
            {
                socket.Dispose();
            }
        }

        return null;
    }

    /// <summary>The addresses of DBUS_SESSION_BUS_ADDRESS, in order: a list
    /// separated by semicolons, each "unix:path=..." or "unix:abstract=...",
    /// with %-escapes. Without the variable — the socket systemd puts in
    /// $XDG_RUNTIME_DIR/bus.</summary>
    private static IEnumerable<UnixDomainSocketEndPoint> SessionAddresses()
    {
        string? addresses = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");

        if (string.IsNullOrEmpty(addresses))
        {
            string? runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");

            if (!string.IsNullOrEmpty(runtime) && File.Exists(Path.Combine(runtime, "bus")))
                yield return new UnixDomainSocketEndPoint(Path.Combine(runtime, "bus"));

            yield break;
        }

        foreach (string address in addresses.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!address.StartsWith("unix:", StringComparison.Ordinal)) continue;

            foreach (string pair in address["unix:".Length..].Split(','))
            {
                int equals = pair.IndexOf('=');
                if (equals < 0) continue;

                string key = pair[..equals];
                string value = Uri.UnescapeDataString(pair[(equals + 1)..]);

                // a socket file that isn't there is no bus: a failed connect to it
                // threw a SocketException on every start, under WSL among others
                if (key == "path" && File.Exists(value))
                    yield return new UnixDomainSocketEndPoint(value);

                // the abstract namespace: a name with a leading zero byte
                else if (key == "abstract")
                    yield return new UnixDomainSocketEndPoint("\0" + value);
            }
        }
    }

    /// <summary>EXTERNAL: the bus checks the peer's credentials on the socket
    /// itself, the client only names its uid — in hex of its decimal digits.</summary>
    private bool Authenticate()
    {
        string uid = getuid().ToString(System.Globalization.CultureInfo.InvariantCulture);
        string hex = Convert.ToHexString(Encoding.ASCII.GetBytes(uid)).ToLowerInvariant();

        _socket.Send([0]);
        _socket.Send(Encoding.ASCII.GetBytes($"AUTH EXTERNAL {hex}\r\n"));

        if (!ReadLine().StartsWith("OK ", StringComparison.Ordinal))
            return false;

        _socket.Send(Encoding.ASCII.GetBytes("BEGIN\r\n"));

        return true;
    }

    private string ReadLine()
    {
        var line = new StringBuilder();
        byte[] one = new byte[1];

        while (_socket.Receive(one) == 1)
        {
            line.Append((char)one[0]);

            if (line.Length >= 2 && line[^2] == '\r' && line[^1] == '\n')
                return line.ToString(0, line.Length - 2);
        }

        return string.Empty;
    }

    /// <summary>Call a method with string arguments and wait for its reply or error,
    /// up to the socket's timeout. Signals that arrive meanwhile are dispatched as
    /// usual. Null — no reply in time, or the connection broke.</summary>
    public DBusMessage? Call(string destination, string path, string @interface, string member, params string[] arguments) =>
        CallMethod(destination, path, @interface, member, new string('s', arguments.Length), [.. arguments]);

    /// <summary>Call a method with arguments of any type, by their signature, and wait
    /// for its reply or error, up to the socket's timeout. Signals and calls to the
    /// exported objects that arrive meanwhile are dispatched as usual. Null — no reply
    /// in time, or the connection broke.</summary>
    public DBusMessage? CallMethod(
        string destination, string path, string @interface, string member,
        string signature, IReadOnlyList<object?> arguments)
    {
        if (!IsOpen) return null;

        uint serial = ++_serial;
        _waiting.Add(serial);

        try
        {
            _socket.Send(DBusWire.MethodCall(serial, destination, path, @interface, member, signature, arguments));

            while (IsOpen)
            {
                // a call answered while waiting may have waited for one of its own,
                // and read this reply on the way
                if (_replies.Remove(serial, out DBusMessage? early)) return early;

                if (TakeMessage() is { } message)
                {
                    if (message.ReplySerial == serial) return message;

                    Dispatch(message);
                    continue;
                }

                // a blocking read: the socket's ReceiveTimeout bounds the wait
                if (!ReceiveSome(blocking: true)) break;
            }
        }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.TimedOut)
        {
            // no answer in time is not a broken connection: the reply, if it
            // ever comes, finds no waiter and is dropped
        }
        catch (Exception exception) when (exception is SocketException or InvalidDataException)
        {
            Close();
        }
        finally
        {
            _waiting.Remove(serial);
            _replies.Remove(serial);
        }

        return null;
    }

    /// <summary>Ask the bus for a well-known name, without queueing for it. True — the
    /// connection owns it now.</summary>
    public bool RequestName(string name)
    {
        // DBUS_NAME_FLAG_DO_NOT_QUEUE; the reply 1 is DBUS_REQUEST_NAME_REPLY_PRIMARY_OWNER
        DBusMessage? reply = CallMethod(BusName, BusPath, BusName, "RequestName", "su", [name, 4u]);

        return reply is { Type: DBusMessageType.MethodReturn, Body: [uint result, ..] } && result == 1;
    }

    /// <summary>Some connection owns the name now.</summary>
    public bool NameHasOwner(string name)
    {
        DBusMessage? reply = CallMethod(BusName, BusPath, BusName, "NameHasOwner", "s", [name]);

        return reply is { Type: DBusMessageType.MethodReturn, Body: [true, ..] };
    }

    /// <summary>Have the bus send the signals the rule matches to this connection.</summary>
    public void AddMatch(string rule) => Call(BusName, BusPath, BusName, "AddMatch", rule);

    /// <summary>Export an object at a path: the calls to it come to
    /// <see cref="IDBusObject.Call"/>.</summary>
    public void Register(string path, IDBusObject target) => _objects[path] = target;

    public void Unregister(string path) => _objects.Remove(path);

    /// <summary>Send a signal from an exported object; nothing waits for it.</summary>
    public void Emit(string path, string @interface, string member, string signature, IReadOnlyList<object?> values) =>
        Send(DBusWire.Signal(++_serial, path, @interface, member, signature, values));

    private void Send(byte[] message)
    {
        if (!IsOpen) return;

        try
        {
            _socket.Send(message);
        }
        catch (SocketException)
        {
            Close();
        }
    }

    /// <summary>Read what has arrived, without waiting, and dispatch the signals.
    /// For the event loop, after select reported the descriptor readable — or
    /// simply on each turn: with nothing to read it returns at once.</summary>
    public void ProcessIncoming()
    {
        if (!IsOpen) return;

        try
        {
            if (!ReceiveSome(blocking: false)) return;

            while (TakeMessage() is { } message)
                Dispatch(message);
        }
        catch (Exception exception) when (exception is SocketException or InvalidDataException)
        {
            Close();
        }
    }

    /// <summary>Move the bytes waiting in the socket into the buffer.
    /// False — the peer closed the connection, or a blocking read timed out.</summary>
    private bool ReceiveSome(bool blocking)
    {
        int available = _socket.Available;

        if (available == 0)
        {
            if (!blocking)
            {
                // readable with nothing to read is how a socket reports
                // that the other side has gone
                if (_socket.Poll(0, SelectMode.SelectRead))
                {
                    Close();
                    return false;
                }

                return true;
            }

            available = 4096;
        }

        byte[] buffer = new byte[available];
        int read = _socket.Receive(buffer);

        if (read == 0)
        {
            Close();
            return false;
        }

        _incoming.AddRange(buffer.AsSpan(0, read));
        return true;
    }

    /// <summary>A whole message from the buffer, if one has arrived.</summary>
    private DBusMessage? TakeMessage()
    {
        if (DBusWire.MessageLength(CollectionsMarshal.AsSpan(_incoming)) is not { } length ||
            _incoming.Count < length)
        {
            return null;
        }

        byte[] bytes = _incoming.GetRange(0, length).ToArray();
        _incoming.RemoveRange(0, length);

        return DBusWire.Parse(bytes);
    }

    private void Dispatch(DBusMessage message)
    {
        switch (message.Type)
        {
            case DBusMessageType.Signal:
                Signal?.Invoke(message);
                break;

            case DBusMessageType.MethodCall:
                Answer(message);
                break;

            // the reply of an outer wait, read by a nested one; replies to calls
            // nobody waits for anymore are dropped
            case DBusMessageType.MethodReturn or DBusMessageType.Error
                when message.ReplySerial is { } serial && _waiting.Contains(serial):
                _replies[serial] = message;
                break;
        }
    }

    /// <summary>A call to an exported object: its reply, or an error — also for a path
    /// nothing is exported at. An exception of the application's own code goes on to
    /// the loop's handler after the caller got an error.</summary>
    private void Answer(DBusMessage call)
    {
        DBusReply reply;

        try
        {
            reply = Resolve(call);
        }
        catch (DBusErrorException error)
        {
            if (call.ExpectsReply)
                Send(DBusWire.Error(++_serial, call, error.Name, error.Message));

            return;
        }
        catch (Exception exception) when (exception is not (SocketException or InvalidDataException))
        {
            if (call.ExpectsReply)
                Send(DBusWire.Error(++_serial, call, "org.freedesktop.DBus.Error.Failed", exception.Message));

            throw;
        }

        if (call.ExpectsReply)
            Send(DBusWire.MethodReturn(++_serial, call, reply.Signature, reply.Values));
    }

    private DBusReply Resolve(DBusMessage call)
    {
        // every connection answers Peer, whatever the path
        if (call.Interface == "org.freedesktop.DBus.Peer")
        {
            return call.Member switch
            {
                "Ping" => DBusReply.Empty,
                "GetMachineId" => DBusReply.Of("s", MachineId()),
                _ => throw DBusErrorException.UnknownMethod(call),
            };
        }

        string path = call.Path ?? string.Empty;

        if (_objects.TryGetValue(path, out IDBusObject? target))
            return target.Call(call);

        // a path above the exported objects: a node that lists its children,
        // so that a tool walking the tree from "/" finds them
        if (call.Member == "Introspect" && call.Interface is null or IntrospectableInterface &&
            Children(path) is { Count: > 0 } children)
        {
            var xml = new StringBuilder(IntrospectHeader).Append("<node>");

            foreach (string child in children)
                xml.Append("<node name=\"").Append(child).Append("\"/>");

            return DBusReply.Of("s", xml.Append("</node>").ToString());
        }

        throw new DBusErrorException("org.freedesktop.DBus.Error.UnknownObject", $"Nothing is exported at {path}.");
    }

    /// <summary>The doctype every introspection document starts with.</summary>
    internal const string IntrospectHeader =
        "<!DOCTYPE node PUBLIC \"-//freedesktop//DTD D-BUS Object Introspection 1.0//EN\" " +
        "\"http://www.freedesktop.org/standards/dbus/1.0/introspect.dtd\">";

    /// <summary>The names of the nodes right under <paramref name="path"/> that lead to
    /// exported objects.</summary>
    private List<string> Children(string path)
    {
        string prefix = path == "/" ? "/" : path + "/";
        var children = new List<string>();

        foreach (string exported in _objects.Keys)
        {
            if (!exported.StartsWith(prefix, StringComparison.Ordinal)) continue;

            string rest = exported[prefix.Length..];
            int slash = rest.IndexOf('/');
            string child = slash < 0 ? rest : rest[..slash];

            if (child.Length > 0 && !children.Contains(child))
                children.Add(child);
        }

        return children;
    }

    private static string MachineId()
    {
        foreach (string file in (string[])["/etc/machine-id", "/var/lib/dbus/machine-id"])
        {
            try
            {
                if (File.Exists(file)) return File.ReadAllText(file).Trim();
            }
            catch (IOException)
            {
                // the next one
            }
        }

        return string.Empty;
    }

    private void Close()
    {
        if (!IsOpen) return;

        IsOpen = false;
        _socket.Dispose();
    }

    public void Dispose() => Close();
}