using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace ZeppelinForms.Linux.DBus;

/// <summary>A minimal connection to the session bus: enough to call a method
/// and to receive signals. No dependency — the protocol is a socket, a line of
/// text to authenticate, and binary messages after it.</summary>
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
/// Every failure — no bus, a refused authentication, a broken connection —
/// ends in a closed connection rather than an exception: the system appearance
/// is a nicety, and its absence must not keep the application from starting.
/// </para>
/// </remarks>
internal sealed class DBusConnection : IDisposable
{
    private readonly Socket _socket;
    private readonly List<byte> _incoming = [];
    private uint _serial;

    /// <summary>A signal arrived — on the thread that called <see cref="ProcessIncoming"/>.</summary>
    public event Action<DBusMessage>? Signal;

    public bool IsOpen { get; private set; } = true;

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
                    connection.Call("org.freedesktop.DBus", "/org/freedesktop/DBus",
                        "org.freedesktop.DBus", "Hello") is { Type: DBusMessageType.MethodReturn })
                {
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

                if (key == "path")
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

    /// <summary>Call a method and wait for its reply or error, up to the socket's
    /// timeout. Signals that arrive meanwhile are dispatched as usual. Null — no
    /// reply in time, or the connection broke.</summary>
    public DBusMessage? Call(string destination, string path, string @interface, string member, params string[] arguments)
    {
        if (!IsOpen) return null;

        uint serial = ++_serial;

        try
        {
            _socket.Send(DBusWire.MethodCall(serial, destination, path, @interface, member, arguments));

            while (IsOpen)
            {
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

        return null;
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
        // replies to calls nobody waits for anymore are dropped
        if (message.Type == DBusMessageType.Signal)
            Signal?.Invoke(message);
    }

    private void Close()
    {
        if (!IsOpen) return;

        IsOpen = false;
        _socket.Dispose();
    }

    public void Dispose() => Close();
}