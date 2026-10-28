// @ts-check
'use strict';

// The protocol's codec against bytes .NET's DesignerChannel wrote: the vectors below are
// what tools/ZeppelinForms.Design/Protocol/DesignerProtocol.cs sends for these messages.
// When a message changes shape there, these change with it.
//
//   npm test

const assert = require('node:assert/strict');
const { test } = require('node:test');
const protocol = require('../src/designer/protocol');

const K = protocol.Kind;

/** Written by DesignerChannel.Send, in hex. @type {Record<string, string>} */
const dotnet = {
    hello:
        '06000000010100000000',
    open:
        '34000000041a5a465f5368617265644c69622e53686f77636173652e44656d6f0000b443000020440000c03f01044461726b00010000a03f',
    pointer:
        '13000000080300002841000040c00288ffffff03000000',
    key:
        '0c0000000901000d0000000400000001',
    text:
        'dd0000000ada01d09fd180d0b8d0b2d0b5d1822c20e29c88207878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878',
    catalog:
        '58000000030200000003412e420142014103412e42000100000000000000000000803f0000000000000005412e432e4d014d014103412e4301014d000000c842000048420000803f01054c696768740000000000000001047761726e',
    frame:
        '1900000006020000000100000000000040080000000102030405060708',
    error:
        '0b0000000704626f6f6d0461742058',
};

/** The messages of the vectors, as the extension builds them. */
const messages = {
    hello: { kind: K.Hello, protocolVersion: 1, frameworkVersion: '' },
    open: {
        kind: K.Open,
        previewId: 'ZF_SharedLib.Showcase.Demo',
        settings: { width: 360, height: 640, scale: 1.5, theme: 'Dark', culture: null, rightToLeft: true, textScale: 1.25 },
    },
    pointer: { kind: K.Pointer, action: protocol.PointerAction.Wheel, x: 10.5, y: -3, button: protocol.MouseButton.Right, wheelDelta: -120, modifiers: 3 },
    key: { kind: K.Key, isDown: true, key: '', code: 13, modifiers: protocol.Modifiers.Alt, isRepeat: true },
    text: { kind: K.Text, text: 'Привет, ✈ ' + 'x'.repeat(200) },
    catalog: {
        kind: K.Catalog,
        previews: [
            { id: 'A.B', name: 'B', group: 'A', typeName: 'A.B', memberName: null, isForm: true, defaults: protocol.defaultSettings() },
            {
                id: 'A.C.M', name: 'M', group: 'A', typeName: 'A.C', memberName: 'M', isForm: false,
                defaults: { width: 100, height: 50, scale: 1, theme: 'Light', culture: null, rightToLeft: false, textScale: 0 },
            },
        ],
        error: null,
        warning: 'warn',
    },
    frame: { kind: K.Frame, width: 2, height: 1, scale: 2, pixels: Buffer.from([1, 2, 3, 4, 5, 6, 7, 8]) },
    error: { kind: K.PreviewError, message: 'boom', details: 'at X' },
};

/** @param {any} message */
function plain(message) {
    return JSON.parse(JSON.stringify(message, (_, value) =>
        value && value.type === 'Buffer' ? value.data : value));
}

for (const [name, message] of Object.entries(messages)) {
    test(`${name}: written as .NET writes it`, () => {
        assert.equal(protocol.encode(message).toString('hex'), dotnet[name]);
    });

    test(`${name}: read as .NET wrote it`, () => {
        const decoded = new protocol.Decoder().push(Buffer.from(dotnet[name], 'hex'));
        assert.equal(decoded.length, 1);

        const read = plain(decoded[0]);
        if (read.pixels) read.pixels = Object.values(read.pixels);

        assert.deepEqual(read, plain(message));
    });
}

test('a stream cut anywhere gives the same messages', () => {
    const stream = Buffer.concat(Object.values(dotnet).map(hex => Buffer.from(hex, 'hex')));

    for (const size of [1, 2, 3, 5, 7, 64]) {
        const decoder = new protocol.Decoder();
        const kinds = [];

        for (let at = 0; at < stream.length; at += size)
            for (const message of decoder.push(stream.subarray(at, at + size))) kinds.push(message.kind);

        assert.deepEqual(kinds, Object.values(messages).map(m => m.kind), `chunks of ${size}`);
        assert.equal(decoder.available, 0);
    }
});

test('a long string takes a length of several bytes', () => {
    const text = 'й'.repeat(70000);
    const [read] = new protocol.Decoder().push(protocol.encode({ kind: K.Log, text }));

    assert.equal(read.text, text);
});

test('a corrupted length is an error, not a hang', () => {
    assert.throws(() => new protocol.Decoder().push(Buffer.from([0, 0, 0, 0, 1])), RangeError);
    assert.throws(() => new protocol.Decoder().push(Buffer.from([0xff, 0xff, 0xff, 0x7f, 1])), RangeError);
});
