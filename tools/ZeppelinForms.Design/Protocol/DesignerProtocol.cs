// The previewer's wire protocol. This file is compiled twice: into ZeppelinForms.Design
// (.NET 10, the previewer's host) and into the Visual Studio extension (.NET Framework
// 4.7.2). It therefore uses nothing newer than netstandard2.0 has: no records, no init
// accessors, no Stream.ReadExactly, no ArgumentNullException.ThrowIfNull.
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZeppelinForms.Design.Protocol
{
    /// <summary>
    /// Messages between an IDE and the previewer's host process, over any duplex
    /// stream — a named pipe in practice. A message is framed as
    /// <c>[int32 length][byte kind][payload]</c>, little-endian, the length counting
    /// the kind and the payload. Strings are those of <see cref="BinaryWriter"/>:
    /// a 7-bit encoded byte count and UTF-8.
    /// </summary>
    /// <remarks>
    /// The format is deliberately plain: a Rider plugin, written in Kotlin, has to
    /// speak it without a .NET serializer at hand.
    /// </remarks>
    public static class DesignerProtocol
    {
        /// <summary>Raised whenever a message changes shape. Both sides send it in
        /// <see cref="HelloMessage"/>; a mismatch is reported rather than guessed through.</summary>
        public const int Version = 1;

        /// <summary>A frame of 4K at scale 2 with room to spare: anything longer is
        /// a corrupted stream, not a message.</summary>
        public const int MaxMessageLength = 256 * 1024 * 1024;
    }

    public enum MessageKind : byte
    {
        Hello = 1,
        Load = 2,
        Catalog = 3,
        Open = 4,
        Settings = 5,
        Frame = 6,
        PreviewError = 7,
        Pointer = 8,
        Key = 9,
        Text = 10,
        CheckSheet = 11,
        SheetDiagnostics = 12,
        Log = 13,
        Shutdown = 14,
    }

    /// <summary>Modifier keys, the protocol's own bits.</summary>
    [Flags]
    public enum DesignerModifiers
    {
        None = 0,
        Shift = 1,
        Control = 2,
        Alt = 4,
    }

    public enum PointerAction : byte
    {
        Move = 0,
        Down = 1,
        Up = 2,
        Wheel = 3,

        /// <summary>The pointer left the preview: hover ends.</summary>
        Leave = 4,
    }

    public enum DesignerMouseButton : byte
    {
        Left = 0,
        Middle = 1,
        Right = 2,
    }

    public abstract class DesignerMessage
    {
        public abstract MessageKind Kind { get; }

        internal abstract void Write(BinaryWriter writer);

        internal static DesignerMessage Read(MessageKind kind, BinaryReader reader)
        {
            switch (kind)
            {
                case MessageKind.Hello: return HelloMessage.ReadPayload(reader);
                case MessageKind.Load: return new LoadMessage(reader.ReadString());
                case MessageKind.Catalog: return CatalogMessage.ReadPayload(reader);
                case MessageKind.Open: return new OpenMessage(reader.ReadString(), PreviewSettings.Read(reader));
                case MessageKind.Settings: return new SettingsMessage(PreviewSettings.Read(reader));
                case MessageKind.Frame: return FrameMessage.ReadPayload(reader);
                case MessageKind.PreviewError: return new PreviewErrorMessage(reader.ReadString(), reader.ReadString());
                case MessageKind.Pointer: return PointerMessage.ReadPayload(reader);
                case MessageKind.Key: return KeyMessage.ReadPayload(reader);
                case MessageKind.Text: return new TextMessage(reader.ReadString());
                case MessageKind.CheckSheet: return new CheckSheetMessage(reader.ReadString());
                case MessageKind.SheetDiagnostics: return SheetDiagnosticsMessage.ReadPayload(reader);
                case MessageKind.Log: return new LogMessage(reader.ReadString());
                case MessageKind.Shutdown: return new ShutdownMessage();
                default: throw new InvalidDataException("Unknown message kind " + (int)kind + ".");
            }
        }

        internal static void WriteNullable(BinaryWriter writer, string? value)
        {
            writer.Write(value != null);
            if (value != null) writer.Write(value);
        }

        internal static string? ReadNullable(BinaryReader reader) =>
            reader.ReadBoolean() ? reader.ReadString() : null;
    }

    /// <summary>The first message of each side: the protocol version, and the
    /// framework version of the host (empty from the IDE).</summary>
    public sealed class HelloMessage : DesignerMessage
    {
        public HelloMessage(int protocolVersion, string frameworkVersion)
        {
            ProtocolVersion = protocolVersion;
            FrameworkVersion = frameworkVersion;
        }

        public override MessageKind Kind => MessageKind.Hello;

        public int ProtocolVersion { get; }

        public string FrameworkVersion { get; }

        internal override void Write(BinaryWriter writer)
        {
            writer.Write(ProtocolVersion);
            writer.Write(FrameworkVersion);
        }

        internal static HelloMessage ReadPayload(BinaryReader reader) =>
            new HelloMessage(reader.ReadInt32(), reader.ReadString());
    }

    /// <summary>IDE → host: load the project's built assembly. The host answers
    /// with <see cref="CatalogMessage"/>.</summary>
    public sealed class LoadMessage : DesignerMessage
    {
        public LoadMessage(string assemblyPath) { AssemblyPath = assemblyPath; }

        public override MessageKind Kind => MessageKind.Load;

        public string AssemblyPath { get; }

        internal override void Write(BinaryWriter writer) => writer.Write(AssemblyPath);
    }

    /// <summary>One entry of the previewer's list.</summary>
    public sealed class PreviewInfo
    {
        public PreviewInfo(string id, string name, string group, string typeName, string? memberName, bool isForm)
        {
            Id = id;
            Name = name;
            Group = group;
            TypeName = typeName;
            MemberName = memberName;
            IsForm = isForm;
        }

        /// <summary>Stable across builds while the code doesn't move: the full type
        /// name, and for a method the method's name after a dot.</summary>
        public string Id { get; }

        public string Name { get; }

        public string Group { get; }

        /// <summary>The full name of the declaring type: the IDE finds the source by it.</summary>
        public string TypeName { get; }

        /// <summary>The method's name; null for a form.</summary>
        public string? MemberName { get; }

        public bool IsForm { get; }

        /// <summary>What the preview asks for itself, see <c>PreviewAttribute</c>.</summary>
        public PreviewSettings Defaults { get; set; } = new PreviewSettings();
    }

    /// <summary>Host → IDE: the previews of the loaded assembly.</summary>
    public sealed class CatalogMessage : DesignerMessage
    {
        public CatalogMessage(IList<PreviewInfo> previews, string? error, string? warning)
        {
            Previews = previews;
            Error = error;
            Warning = warning;
        }

        public override MessageKind Kind => MessageKind.Catalog;

        public IList<PreviewInfo> Previews { get; }

        /// <summary>The assembly could not be loaded; the list is empty.</summary>
        public string? Error { get; }

        /// <summary>Loaded, but something may not work — a framework version that is not
        /// the host's, a setup method that threw.</summary>
        public string? Warning { get; }

        internal override void Write(BinaryWriter writer)
        {
            writer.Write(Previews.Count);

            foreach (PreviewInfo preview in Previews)
            {
                writer.Write(preview.Id);
                writer.Write(preview.Name);
                writer.Write(preview.Group);
                writer.Write(preview.TypeName);
                WriteNullable(writer, preview.MemberName);
                writer.Write(preview.IsForm);
                preview.Defaults.Write(writer);
            }

            WriteNullable(writer, Error);
            WriteNullable(writer, Warning);
        }

        internal static CatalogMessage ReadPayload(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var previews = new List<PreviewInfo>(count);

            for (int i = 0; i < count; i++)
            {
                var preview = new PreviewInfo(
                    reader.ReadString(), reader.ReadString(), reader.ReadString(),
                    reader.ReadString(), ReadNullable(reader), reader.ReadBoolean());

                preview.Defaults = PreviewSettings.Read(reader);
                previews.Add(preview);
            }

            return new CatalogMessage(previews, ReadNullable(reader), ReadNullable(reader));
        }
    }

    /// <summary>How a preview is shown. Zero sizes and null names mean "the preview's
    /// own choice, or the host's default".</summary>
    public sealed class PreviewSettings
    {
        /// <summary>The size in device-independent pixels.</summary>
        public float Width { get; set; }

        public float Height { get; set; }

        /// <summary>Device pixels per device-independent pixel: the IDE's DPI scale.</summary>
        public float Scale { get; set; } = 1f;

        public string? Theme { get; set; }

        public string? Culture { get; set; }

        public bool RightToLeft { get; set; }

        /// <summary>The text scale; 0 — 1.</summary>
        public float TextScale { get; set; }

        public PreviewSettings Clone() => (PreviewSettings)MemberwiseClone();

        internal void Write(BinaryWriter writer)
        {
            writer.Write(Width);
            writer.Write(Height);
            writer.Write(Scale);
            DesignerMessage.WriteNullable(writer, Theme);
            DesignerMessage.WriteNullable(writer, Culture);
            writer.Write(RightToLeft);
            writer.Write(TextScale);
        }

        internal static PreviewSettings Read(BinaryReader reader) => new PreviewSettings
        {
            Width = reader.ReadSingle(),
            Height = reader.ReadSingle(),
            Scale = reader.ReadSingle(),
            Theme = DesignerMessage.ReadNullable(reader),
            Culture = DesignerMessage.ReadNullable(reader),
            RightToLeft = reader.ReadBoolean(),
            TextScale = reader.ReadSingle(),
        };
    }

    /// <summary>IDE → host: show a preview. The host answers with frames or an error.</summary>
    public sealed class OpenMessage : DesignerMessage
    {
        public OpenMessage(string previewId, PreviewSettings settings)
        {
            PreviewId = previewId;
            Settings = settings;
        }

        public override MessageKind Kind => MessageKind.Open;

        public string PreviewId { get; }

        /// <summary>The IDE's choices; they override the preview's own where set.</summary>
        public PreviewSettings Settings { get; }

        internal override void Write(BinaryWriter writer)
        {
            writer.Write(PreviewId);
            Settings.Write(writer);
        }
    }

    /// <summary>IDE → host: the window was resized, the theme or another setting changed.</summary>
    public sealed class SettingsMessage : DesignerMessage
    {
        public SettingsMessage(PreviewSettings settings) { Settings = settings; }

        public override MessageKind Kind => MessageKind.Settings;

        public PreviewSettings Settings { get; }

        internal override void Write(BinaryWriter writer) => Settings.Write(writer);
    }

    /// <summary>Host → IDE: a picture of the preview. Pixels are RGBA, 8 bits per
    /// channel, premultiplied alpha, rows top to bottom without padding.</summary>
    public sealed class FrameMessage : DesignerMessage
    {
        public FrameMessage(int width, int height, float scale, byte[] pixels)
        {
            Width = width;
            Height = height;
            Scale = scale;
            Pixels = pixels;
        }

        public override MessageKind Kind => MessageKind.Frame;

        /// <summary>In device pixels.</summary>
        public int Width { get; }

        public int Height { get; }

        public float Scale { get; }

        public byte[] Pixels { get; }

        internal override void Write(BinaryWriter writer)
        {
            writer.Write(Width);
            writer.Write(Height);
            writer.Write(Scale);
            writer.Write(Pixels.Length);
            writer.Write(Pixels);
        }

        internal static FrameMessage ReadPayload(BinaryReader reader)
        {
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            float scale = reader.ReadSingle();
            int length = reader.ReadInt32();

            return new FrameMessage(width, height, scale, reader.ReadBytes(length));
        }
    }

    /// <summary>Host → IDE: the preview could not be built or threw while running.</summary>
    public sealed class PreviewErrorMessage : DesignerMessage
    {
        public PreviewErrorMessage(string message, string details)
        {
            Message = message;
            Details = details;
        }

        public override MessageKind Kind => MessageKind.PreviewError;

        public string Message { get; }

        /// <summary>The exception with its stack trace.</summary>
        public string Details { get; }

        internal override void Write(BinaryWriter writer)
        {
            writer.Write(Message);
            writer.Write(Details);
        }
    }

    /// <summary>IDE → host: the mouse over the preview, in device-independent pixels.</summary>
    public sealed class PointerMessage : DesignerMessage
    {
        public PointerMessage(PointerAction action, float x, float y)
        {
            Action = action;
            X = x;
            Y = y;
        }

        public override MessageKind Kind => MessageKind.Pointer;

        public PointerAction Action { get; }

        public float X { get; }

        public float Y { get; }

        public DesignerMouseButton Button { get; set; }

        /// <summary>For <see cref="PointerAction.Wheel"/>: 120 per notch, positive up.</summary>
        public int WheelDelta { get; set; }

        public DesignerModifiers Modifiers { get; set; }

        internal override void Write(BinaryWriter writer)
        {
            writer.Write((byte)Action);
            writer.Write(X);
            writer.Write(Y);
            writer.Write((byte)Button);
            writer.Write(WheelDelta);
            writer.Write((int)Modifiers);
        }

        internal static PointerMessage ReadPayload(BinaryReader reader) =>
            new PointerMessage((PointerAction)reader.ReadByte(), reader.ReadSingle(), reader.ReadSingle())
            {
                Button = (DesignerMouseButton)reader.ReadByte(),
                WheelDelta = reader.ReadInt32(),
                Modifiers = (DesignerModifiers)reader.ReadInt32(),
            };
    }

    /// <summary>IDE → host: a key went down or up. The key is a Windows virtual-key
    /// code in <see cref="Code"/> — ZeppelinForms' <c>Key</c> enum uses those values —
    /// or, where the IDE has no such codes, the name of a member of that enum in
    /// <see cref="Key"/>: "Enter", "Tab", "A", "D1", "F5", "Left".</summary>
    public sealed class KeyMessage : DesignerMessage
    {
        public KeyMessage(bool isDown, string key)
        {
            IsDown = isDown;
            Key = key;
        }

        public override MessageKind Kind => MessageKind.Key;

        public bool IsDown { get; }

        /// <summary>The key's name; empty when <see cref="Code"/> is given.</summary>
        public string Key { get; }

        /// <summary>The Windows virtual-key code; 0 — use <see cref="Key"/>.</summary>
        public int Code { get; set; }

        public DesignerModifiers Modifiers { get; set; }

        public bool IsRepeat { get; set; }

        internal override void Write(BinaryWriter writer)
        {
            writer.Write(IsDown);
            writer.Write(Key);
            writer.Write(Code);
            writer.Write((int)Modifiers);
            writer.Write(IsRepeat);
        }

        internal static KeyMessage ReadPayload(BinaryReader reader) =>
            new KeyMessage(reader.ReadBoolean(), reader.ReadString())
            {
                Code = reader.ReadInt32(),
                Modifiers = (DesignerModifiers)reader.ReadInt32(),
                IsRepeat = reader.ReadBoolean(),
            };
    }

    /// <summary>IDE → host: typed text, after keyboard layouts and dead keys.</summary>
    public sealed class TextMessage : DesignerMessage
    {
        public TextMessage(string text) { Text = text; }

        public override MessageKind Kind => MessageKind.Text;

        public string Text { get; }

        internal override void Write(BinaryWriter writer) => writer.Write(Text);
    }

    /// <summary>IDE → host: read a style sheet and report its problems. The host knows
    /// the project's controls, so their types and pseudo-classes are checked too.</summary>
    public sealed class CheckSheetMessage : DesignerMessage
    {
        public CheckSheetMessage(string path) { Path = path; }

        public override MessageKind Kind => MessageKind.CheckSheet;

        public string Path { get; }

        internal override void Write(BinaryWriter writer) => writer.Write(Path);
    }

    public sealed class SheetDiagnosticInfo
    {
        public SheetDiagnosticInfo(bool isError, string message, string source, int line, int column)
        {
            IsError = isError;
            Message = message;
            Source = source;
            Line = line;
            Column = column;
        }

        public bool IsError { get; }

        public string Message { get; }

        /// <summary>The file the problem is in: the sheet or one of its imports.</summary>
        public string Source { get; }

        /// <summary>From one.</summary>
        public int Line { get; }

        /// <summary>From one.</summary>
        public int Column { get; }
    }

    /// <summary>Host → IDE: the problems of a sheet; an empty list clears earlier ones.</summary>
    public sealed class SheetDiagnosticsMessage : DesignerMessage
    {
        public SheetDiagnosticsMessage(string path, IList<SheetDiagnosticInfo> items)
        {
            Path = path;
            Items = items;
        }

        public override MessageKind Kind => MessageKind.SheetDiagnostics;

        public string Path { get; }

        public IList<SheetDiagnosticInfo> Items { get; }

        internal override void Write(BinaryWriter writer)
        {
            writer.Write(Path);
            writer.Write(Items.Count);

            foreach (SheetDiagnosticInfo item in Items)
            {
                writer.Write(item.IsError);
                writer.Write(item.Message);
                writer.Write(item.Source);
                writer.Write(item.Line);
                writer.Write(item.Column);
            }
        }

        internal static SheetDiagnosticsMessage ReadPayload(BinaryReader reader)
        {
            string path = reader.ReadString();
            int count = reader.ReadInt32();
            var items = new List<SheetDiagnosticInfo>(count);

            for (int i = 0; i < count; i++)
                items.Add(new SheetDiagnosticInfo(
                    reader.ReadBoolean(), reader.ReadString(), reader.ReadString(),
                    reader.ReadInt32(), reader.ReadInt32()));

            return new SheetDiagnosticsMessage(path, items);
        }
    }

    /// <summary>Host → IDE: a line for the IDE's output window.</summary>
    public sealed class LogMessage : DesignerMessage
    {
        public LogMessage(string text) { Text = text; }

        public override MessageKind Kind => MessageKind.Log;

        public string Text { get; }

        internal override void Write(BinaryWriter writer) => writer.Write(Text);
    }

    /// <summary>IDE → host: exit.</summary>
    public sealed class ShutdownMessage : DesignerMessage
    {
        public override MessageKind Kind => MessageKind.Shutdown;

        internal override void Write(BinaryWriter writer) { }
    }

    /// <summary>
    /// One end of the conversation: sends and receives framed messages over a stream.
    /// Sending is safe from any thread; receiving belongs to one reader.
    /// </summary>
    public sealed class DesignerChannel : IDisposable
    {
        private readonly Stream _stream;
        private readonly object _writeLock = new object();
        private readonly byte[] _header = new byte[5];

        public DesignerChannel(Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        public void Send(DesignerMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            byte[] payload;

            using (var buffer = new MemoryStream())
            {
                using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
                    message.Write(writer);

                payload = buffer.ToArray();
            }

            byte[] header = new byte[5];
            WriteInt32(header, payload.Length + 1);
            header[4] = (byte)message.Kind;

            lock (_writeLock)
            {
                _stream.Write(header, 0, header.Length);
                _stream.Write(payload, 0, payload.Length);
                _stream.Flush();
            }
        }

        /// <summary>The next message; null when the other side closed the stream.</summary>
        public DesignerMessage? Receive()
        {
            if (!ReadExactly(_header, 0, 5)) return null;

            int length = ReadInt32(_header);

            if (length < 1 || length > DesignerProtocol.MaxMessageLength)
                throw new InvalidDataException("A message of " + length + " bytes: the stream is corrupted.");

            var kind = (MessageKind)_header[4];
            byte[] payload = new byte[length - 1];

            if (!ReadExactly(payload, 0, payload.Length))
                throw new EndOfStreamException("The stream ended in the middle of a message.");

            using (var reader = new BinaryReader(new MemoryStream(payload), Encoding.UTF8))
                return DesignerMessage.Read(kind, reader);
        }

        private bool ReadExactly(byte[] buffer, int offset, int count)
        {
            int read = 0;

            while (read < count)
            {
                int n = _stream.Read(buffer, offset + read, count - read);

                if (n == 0)
                {
                    if (read == 0) return false;
                    throw new EndOfStreamException("The stream ended in the middle of a message.");
                }

                read += n;
            }

            return true;
        }

        private static void WriteInt32(byte[] buffer, int value)
        {
            buffer[0] = (byte)value;
            buffer[1] = (byte)(value >> 8);
            buffer[2] = (byte)(value >> 16);
            buffer[3] = (byte)(value >> 24);
        }

        private static int ReadInt32(byte[] buffer) =>
            buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24);

        public void Dispose() => _stream.Dispose();
    }
}