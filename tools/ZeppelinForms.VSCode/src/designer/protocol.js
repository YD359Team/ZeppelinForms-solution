// @ts-check
'use strict';

/**
 * The previewer's wire protocol — tools/ZeppelinForms.Design/Protocol/DesignerProtocol.cs
 * spoken from JavaScript. A message is [int32 length][byte kind][payload], little-endian,
 * the length counting the kind and the payload. Strings are those of .NET's BinaryWriter:
 * a 7-bit encoded byte count, then UTF-8. A nullable string is a bool, then the string
 * when the bool is true.
 *
 * Kept in step with the C# file by hand: when a message there changes shape, its
 * Version rises, and so does VERSION here.
 */

const VERSION = 1;

/** Anything longer is a corrupted stream, not a message: DesignerProtocol.MaxMessageLength. */
const MAX_MESSAGE_LENGTH = 256 * 1024 * 1024;

/** @enum {number} */
const Kind = Object.freeze({
    Hello: 1,
    Load: 2,
    Catalog: 3,
    Open: 4,
    Settings: 5,
    Frame: 6,
    PreviewError: 7,
    Pointer: 8,
    Key: 9,
    Text: 10,
    CheckSheet: 11,
    SheetDiagnostics: 12,
    Log: 13,
    Shutdown: 14,
});

/** @enum {number} */
const PointerAction = Object.freeze({ Move: 0, Down: 1, Up: 2, Wheel: 3, Leave: 4 });

/** @enum {number} */
const MouseButton = Object.freeze({ Left: 0, Middle: 1, Right: 2 });

/** The protocol's own modifier bits. */
const Modifiers = Object.freeze({ None: 0, Shift: 1, Control: 2, Alt: 4 });

/**
 * @typedef {object} PreviewSettings
 * @property {number} width   device-independent pixels; 0 — the preview's own
 * @property {number} height
 * @property {number} scale   device pixels per device-independent pixel
 * @property {string | null} theme
 * @property {string | null} culture
 * @property {boolean} rightToLeft
 * @property {number} textScale  0 — 1
 */

/**
 * @typedef {object} PreviewInfo
 * @property {string} id
 * @property {string} name
 * @property {string} group
 * @property {string} typeName
 * @property {string | null} memberName
 * @property {boolean} isForm
 * @property {PreviewSettings} defaults
 */

/** @returns {PreviewSettings} */
function defaultSettings() {
    return { width: 0, height: 0, scale: 1, theme: null, culture: null, rightToLeft: false, textScale: 0 };
}

class Writer {
    constructor() {
        this.buffer = Buffer.allocUnsafe(256);
        this.length = 0;
    }

    /** @param {number} count */
    ensure(count) {
        if (this.length + count <= this.buffer.length) return;

        const grown = Buffer.allocUnsafe(Math.max(this.buffer.length * 2, this.length + count));
        this.buffer.copy(grown, 0, 0, this.length);
        this.buffer = grown;
    }

    /** @param {number} value */
    byte(value) {
        this.ensure(1);
        this.buffer[this.length++] = value & 0xff;
    }

    /** @param {boolean} value */
    bool(value) {
        this.byte(value ? 1 : 0);
    }

    /** @param {number} value */
    int32(value) {
        this.ensure(4);
        this.buffer.writeInt32LE(value | 0, this.length);
        this.length += 4;
    }

    /** @param {number} value */
    float(value) {
        this.ensure(4);
        this.buffer.writeFloatLE(value, this.length);
        this.length += 4;
    }

    /** @param {string} value */
    string(value) {
        const bytes = Buffer.from(value, 'utf8');
        let count = bytes.length;

        // BinaryWriter's 7-bit encoded length: seven bits a byte, the high bit set on all but the last
        while (count >= 0x80) {
            this.byte((count & 0x7f) | 0x80);
            count >>>= 7;
        }

        this.byte(count);
        this.bytes(bytes);
    }

    /** @param {string | null | undefined} value */
    nullable(value) {
        this.bool(value != null);
        if (value != null) this.string(value);
    }

    /** @param {Uint8Array} value */
    bytes(value) {
        this.ensure(value.length);
        this.buffer.set(value, this.length);
        this.length += value.length;
    }

    /** @param {PreviewSettings} settings */
    settings(settings) {
        this.float(settings.width ?? 0);
        this.float(settings.height ?? 0);
        this.float(settings.scale ?? 1);
        this.nullable(settings.theme);
        this.nullable(settings.culture);
        this.bool(!!settings.rightToLeft);
        this.float(settings.textScale ?? 0);
    }

    toBuffer() {
        return this.buffer.subarray(0, this.length);
    }
}

class Reader {
    /** @param {Buffer} buffer */
    constructor(buffer) {
        this.buffer = buffer;
        this.offset = 0;
    }

    /** @param {number} count */
    need(count) {
        if (this.offset + count > this.buffer.length)
            throw new RangeError('The message ended before its payload did.');
    }

    byte() {
        this.need(1);
        return this.buffer[this.offset++];
    }

    bool() {
        return this.byte() !== 0;
    }

    int32() {
        this.need(4);
        const value = this.buffer.readInt32LE(this.offset);
        this.offset += 4;
        return value;
    }

    float() {
        this.need(4);
        const value = this.buffer.readFloatLE(this.offset);
        this.offset += 4;
        return value;
    }

    string() {
        let count = 0;
        let shift = 0;

        for (;;) {
            const b = this.byte();
            count |= (b & 0x7f) << shift;
            if ((b & 0x80) === 0) break;

            shift += 7;
            if (shift > 28) throw new RangeError('A string length of more than five bytes.');
        }

        this.need(count);
        const value = this.buffer.toString('utf8', this.offset, this.offset + count);
        this.offset += count;
        return value;
    }

    /** @returns {string | null} */
    nullable() {
        return this.bool() ? this.string() : null;
    }

    /** @param {number} count */
    bytes(count) {
        this.need(count);
        // a view, not a copy: frames are megabytes, and the message's buffer is its own
        const value = this.buffer.subarray(this.offset, this.offset + count);
        this.offset += count;
        return value;
    }

    /** @returns {PreviewSettings} */
    settings() {
        return {
            width: this.float(),
            height: this.float(),
            scale: this.float(),
            theme: this.nullable(),
            culture: this.nullable(),
            rightToLeft: this.bool(),
            textScale: this.float(),
        };
    }
}

/**
 * A message as an object: `{ kind, ...fields }`, the fields named as the C# properties
 * in camelCase.
 *
 * @typedef {{ kind: number, [field: string]: any }} Message
 */

/**
 * The payload of a message.
 * @param {Message} message
 * @returns {Buffer}
 */
function writePayload(message) {
    const w = new Writer();

    switch (message.kind) {
        case Kind.Hello:
            w.int32(message.protocolVersion ?? VERSION);
            w.string(message.frameworkVersion ?? '');
            break;

        case Kind.Load:
            w.string(message.assemblyPath);
            break;

        case Kind.Catalog:
            w.int32(message.previews.length);
            for (const p of /** @type {PreviewInfo[]} */ (message.previews)) {
                w.string(p.id);
                w.string(p.name);
                w.string(p.group);
                w.string(p.typeName);
                w.nullable(p.memberName);
                w.bool(p.isForm);
                w.settings(p.defaults ?? defaultSettings());
            }
            w.nullable(message.error);
            w.nullable(message.warning);
            break;

        case Kind.Open:
            w.string(message.previewId);
            w.settings(message.settings);
            break;

        case Kind.Settings:
            w.settings(message.settings);
            break;

        case Kind.Frame:
            w.int32(message.width);
            w.int32(message.height);
            w.float(message.scale);
            w.int32(message.pixels.length);
            w.bytes(message.pixels);
            break;

        case Kind.PreviewError:
            w.string(message.message);
            w.string(message.details ?? '');
            break;

        case Kind.Pointer:
            w.byte(message.action);
            w.float(message.x);
            w.float(message.y);
            w.byte(message.button ?? MouseButton.Left);
            w.int32(message.wheelDelta ?? 0);
            w.int32(message.modifiers ?? 0);
            break;

        case Kind.Key:
            w.bool(message.isDown);
            w.string(message.key ?? '');
            w.int32(message.code ?? 0);
            w.int32(message.modifiers ?? 0);
            w.bool(!!message.isRepeat);
            break;

        case Kind.Text:
            w.string(message.text);
            break;

        case Kind.CheckSheet:
            w.string(message.path);
            break;

        case Kind.SheetDiagnostics:
            w.string(message.path);
            w.int32(message.items.length);
            for (const item of message.items) {
                w.bool(item.isError);
                w.string(item.message);
                w.string(item.source);
                w.int32(item.line);
                w.int32(item.column);
            }
            break;

        case Kind.Log:
            w.string(message.text);
            break;

        case Kind.Shutdown:
            break;

        default:
            throw new TypeError(`Unknown message kind ${message.kind}.`);
    }

    return w.toBuffer();
}

/**
 * A message from its kind and payload.
 * @param {number} kind
 * @param {Buffer} payload
 * @returns {Message}
 */
function readPayload(kind, payload) {
    const r = new Reader(payload);

    switch (kind) {
        case Kind.Hello:
            return { kind, protocolVersion: r.int32(), frameworkVersion: r.string() };

        case Kind.Load:
            return { kind, assemblyPath: r.string() };

        case Kind.Catalog: {
            const count = r.int32();
            /** @type {PreviewInfo[]} */
            const previews = [];

            for (let i = 0; i < count; i++) {
                previews.push({
                    id: r.string(),
                    name: r.string(),
                    group: r.string(),
                    typeName: r.string(),
                    memberName: r.nullable(),
                    isForm: r.bool(),
                    defaults: r.settings(),
                });
            }

            return { kind, previews, error: r.nullable(), warning: r.nullable() };
        }

        case Kind.Open:
            return { kind, previewId: r.string(), settings: r.settings() };

        case Kind.Settings:
            return { kind, settings: r.settings() };

        case Kind.Frame: {
            const width = r.int32();
            const height = r.int32();
            const scale = r.float();
            const length = r.int32();
            return { kind, width, height, scale, pixels: r.bytes(length) };
        }

        case Kind.PreviewError:
            return { kind, message: r.string(), details: r.string() };

        case Kind.Pointer:
            return {
                kind,
                action: r.byte(),
                x: r.float(),
                y: r.float(),
                button: r.byte(),
                wheelDelta: r.int32(),
                modifiers: r.int32(),
            };

        case Kind.Key:
            return { kind, isDown: r.bool(), key: r.string(), code: r.int32(), modifiers: r.int32(), isRepeat: r.bool() };

        case Kind.Text:
            return { kind, text: r.string() };

        case Kind.CheckSheet:
            return { kind, path: r.string() };

        case Kind.SheetDiagnostics: {
            const path = r.string();
            const count = r.int32();
            const items = [];

            for (let i = 0; i < count; i++)
                items.push({ isError: r.bool(), message: r.string(), source: r.string(), line: r.int32(), column: r.int32() });

            return { kind, path, items };
        }

        case Kind.Log:
            return { kind, text: r.string() };

        case Kind.Shutdown:
            return { kind };

        default:
            throw new RangeError(`Unknown message kind ${kind}.`);
    }
}

/**
 * A whole message, framed.
 * @param {Message} message
 * @returns {Buffer}
 */
function encode(message) {
    const payload = writePayload(message);
    const header = Buffer.allocUnsafe(5);

    header.writeInt32LE(payload.length + 1, 0);
    header[4] = message.kind;

    return Buffer.concat([header, payload], 5 + payload.length);
}

/**
 * Cuts a stream's chunks into messages. Chunks are kept as they came until a message is
 * whole, and joined once: a frame of several megabytes arrives in many pieces, and
 * joining at every piece would copy it over and over.
 */
class Decoder {
    constructor() {
        /** @type {Buffer[]} */
        this.chunks = [];
        this.available = 0;
    }

    /**
     * The messages the chunk completes.
     * @param {Buffer} chunk
     * @returns {Message[]}
     */
    push(chunk) {
        this.chunks.push(chunk);
        this.available += chunk.length;

        /** @type {Message[]} */
        const messages = [];

        while (this.available >= 5) {
            const head = this.peek(5);
            const length = head.readInt32LE(0);

            if (length < 1 || length > MAX_MESSAGE_LENGTH)
                throw new RangeError(`A message of ${length} bytes: the stream is corrupted.`);

            if (this.available < 4 + length) break;

            const whole = this.take(4 + length);
            messages.push(readPayload(whole[4], whole.subarray(5)));
        }

        return messages;
    }

    /** @param {number} count */
    peek(count) {
        if (this.chunks[0].length >= count) return this.chunks[0];

        const joined = Buffer.concat(this.chunks, this.available);
        this.chunks = [joined];
        return joined;
    }

    /** @param {number} count */
    take(count) {
        let first = this.chunks[0];

        if (first.length < count) {
            first = Buffer.concat(this.chunks, this.available);
            this.chunks = [first];
        }

        const taken = first.subarray(0, count);
        const rest = first.subarray(count);

        if (rest.length > 0) this.chunks[0] = rest;
        else this.chunks.shift();

        this.available -= count;
        return taken;
    }
}

module.exports = {
    VERSION,
    Kind,
    PointerAction,
    MouseButton,
    Modifiers,
    defaultSettings,
    encode,
    readPayload,
    writePayload,
    Decoder,
};
