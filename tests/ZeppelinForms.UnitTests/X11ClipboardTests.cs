using System.Runtime.InteropServices;
using System.Text;
using Xunit;
using ZeppelinForms.Linux;

namespace ZeppelinForms.UnitTests;

/// <summary>A paste on X11 from another application: an owner on a connection of its own
/// answers as a browser or an IDE does — a whole text, a large one past the size of one
/// read, a large one in INCR parts, a refusal, an answer that isn't text.</summary>
[Collection("Platform")]
public sealed class X11ClipboardTests
{
    private static bool HasDisplay =>
        OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

    private static void SkipWithoutDisplay()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Test for Linux only");
        Assert.SkipUnless(HasDisplay, "No X server is available (DISPLAY is not set)");
    }

    private static string Text(int length)
    {
        var text = new StringBuilder(length);

        // not one repeated character: a part lost or read twice shows up as a mismatch;
        // and not ASCII only: a part may end in the middle of a character
        for (int i = 0; text.Length < length; i++)
            text.Append(i % 97 == 0 ? 'ж' : (char)('a' + i % 26));

        return text.ToString(0, length);
    }

    private static string? Paste(FakeOwner.Mode mode, string text, int part = 64 * 1024)
    {
        nint display = X11.XOpenDisplay(0);
        Assert.NotEqual(0, display);

        nuint window = X11.XCreateSimpleWindow(
            display, X11.XRootWindow(display, X11.XDefaultScreen(display)), 0, 0, 1, 1, 0, 0, 0);

        using var owner = new FakeOwner(mode, Encoding.UTF8.GetBytes(text), part);
        var clipboard = new X11Clipboard(display, window);

        try
        {
            return clipboard.GetText();
        }
        finally
        {
            clipboard.Dispose();
            X11.XDestroyWindow(display, window);
            X11.XCloseDisplay(display);
        }
    }

    [Fact]
    public void ATextComesWhole()
    {
        SkipWithoutDisplay();

        Assert.Equal("Привет, X11", Paste(FakeOwner.Mode.Whole, "Привет, X11"));
    }

    [Fact]
    public void ATextLargerThanOneReadComesWhole()
    {
        SkipWithoutDisplay();

        // past the 4 MB one request reads: the rest is read by what the server says is left
        string text = Text(5 * 1024 * 1024 + 123);

        Assert.Equal(text, Paste(FakeOwner.Mode.Whole, text));
    }

    [Fact]
    public void ALargeTextComesInParts()
    {
        SkipWithoutDisplay();

        string text = Text(1024 * 1024 + 7);

        Assert.Equal(text, Paste(FakeOwner.Mode.Incremental, text, part: 64 * 1024 + 1));
    }

    [Fact]
    public void ARefusalAndAnAnswerThatIsntTextGiveNothing()
    {
        SkipWithoutDisplay();

        Assert.Null(Paste(FakeOwner.Mode.Refuse, "unused"));
        Assert.Null(Paste(FakeOwner.Mode.NotText, "unused"));
    }

    /// <summary>Another application owning the clipboard: its own connection, its own
    /// thread, answering requests as the selection protocol says.</summary>
    private sealed class FakeOwner : IDisposable
    {
        public enum Mode { Whole, Incremental, Refuse, NotText }

        private readonly Mode _mode;
        private readonly byte[] _text;
        private readonly int _part;
        private readonly nint _display;
        private readonly nuint _window;
        private readonly nuint _utf8;
        private readonly nuint _incr;
        private readonly Thread _thread;
        private volatile bool _stop;

        // the transfer in parts going on: where to, and how far
        private nuint _requestor;
        private nuint _property;
        private int _sent = -1;

        public FakeOwner(Mode mode, byte[] text, int part)
        {
            _mode = mode;
            _text = text;
            _part = part;

            _display = X11.XOpenDisplay(0);
            _window = X11.XCreateSimpleWindow(
                _display, X11.XRootWindow(_display, X11.XDefaultScreen(_display)), 0, 0, 1, 1, 0, 0, 0);

            _utf8 = X11.XInternAtom(_display, "UTF8_STRING", false);
            _incr = X11.XInternAtom(_display, "INCR", false);

            X11.XSetSelectionOwner(_display, X11.XInternAtom(_display, "CLIPBOARD", false), _window, 0);
            X11.XFlush(_display);

            _thread = new Thread(Run) { IsBackground = true };
            _thread.Start();
        }

        private void Run()
        {
            nint buffer = Marshal.AllocHGlobal(192);

            try
            {
                while (!_stop)
                {
                    while (X11.XPending(_display) > 0)
                    {
                        X11.XNextEvent(_display, buffer);

                        switch (Marshal.ReadInt32(buffer))
                        {
                            case X11.SelectionRequest:
                                Answer(Marshal.PtrToStructure<X11.XSelectionRequestEvent>(buffer));
                                break;

                            case X11.PropertyNotify:
                                Continue(Marshal.PtrToStructure<X11.XPropertyEvent>(buffer));
                                break;
                        }
                    }

                    Thread.Sleep(1);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private void Answer(X11.XSelectionRequestEvent request)
        {
            nuint property = request.property;

            switch (_mode)
            {
                case Mode.Whole:
                    X11.XChangeProperty(_display, request.requestor, property, _utf8, 8,
                        X11.PropModeReplace, _text, _text.Length);
                    break;

                case Mode.Incremental:
                    // the size instead of the text; the parts follow the deletions
                    X11.XSelectInput(_display, request.requestor, X11.PropertyChangeMask);
                    X11.XChangeProperty(_display, request.requestor, property, _incr, 32,
                        X11.PropModeReplace, BitConverter.GetBytes((long)_text.Length), 1);

                    _requestor = request.requestor;
                    _property = property;
                    _sent = 0;
                    break;

                case Mode.Refuse:
                    property = 0;
                    break;

                case Mode.NotText:
                    X11.XChangeProperty(_display, request.requestor, property, _utf8, 32,
                        X11.PropModeReplace, BitConverter.GetBytes(42L), 1);
                    break;
            }

            var response = new X11.XSelectionEvent
            {
                type = X11.SelectionNotify,
                display = _display,
                requestor = request.requestor,
                selection = request.selection,
                target = request.target,
                property = property,
                time = request.time,
            };

            nint buffer = Marshal.AllocHGlobal(192);

            try
            {
                Marshal.StructureToPtr(response, buffer, false);
                X11.XSendEvent(_display, request.requestor, false, 0, buffer);
                X11.XFlush(_display);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>The reader deleted the property: the next part — or the end, a part
        /// of no length.</summary>
        private void Continue(X11.XPropertyEvent notify)
        {
            if (_sent < 0 || notify.window != _requestor || notify.atom != _property ||
                notify.state != X11.PropertyDelete)
            {
                return;
            }

            int length = Math.Min(_part, _text.Length - _sent);
            byte[] part = _text.AsSpan(_sent, length).ToArray();

            X11.XChangeProperty(_display, _requestor, _property, _utf8, 8, X11.PropModeReplace, part, length);
            X11.XFlush(_display);

            // the part of no length has gone: the transfer is over
            _sent = length == 0 ? -1 : _sent + length;
        }

        public void Dispose()
        {
            _stop = true;
            _thread.Join(2000);

            X11.XDestroyWindow(_display, _window);
            X11.XCloseDisplay(_display);
        }
    }
}