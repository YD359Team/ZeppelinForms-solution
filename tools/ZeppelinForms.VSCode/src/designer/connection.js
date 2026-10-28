// @ts-check
'use strict';

const childProcess = require('child_process');
const crypto = require('crypto');
const { EventEmitter } = require('events');
const fs = require('fs');
const net = require('net');
const os = require('os');
const path = require('path');
const protocol = require('./protocol');

const CONNECT_TIMEOUT_MS = 20000;

/**
 * Where the host connects back to. On Windows a named pipe, as Visual Studio's; elsewhere a
 * Unix domain socket at a path of our own: .NET's named pipes there are sockets too, but in
 * a folder and under a name .NET picks, and its --socket takes the path as it is.
 *
 * The path is short on purpose: a socket's path has room for about a hundred bytes, and
 * macOS's temporary folder takes half of that already.
 *
 * @returns {{ listenOn: string, args: string[], cleanup: () => void }}
 */
function endpoint() {
    const id = crypto.randomBytes(6).toString('hex');

    if (process.platform === 'win32') {
        const name = `zeppelinforms-preview-${id}`;
        return { listenOn: `\\\\.\\pipe\\${name}`, args: ['--pipe', name], cleanup: () => { } };
    }

    const socket = path.join(os.tmpdir(), `zfp-${id}.sock`);

    return {
        listenOn: socket,
        args: ['--socket', socket],
        cleanup: () => fs.rm(socket, { force: true }, () => { }),
    };
}

/**
 * One previewer host process and the connection to it: the extension listens, starts
 * `dotnet ZeppelinForms.Designer.dll --socket <path>` (or `--pipe <name>`), and the host
 * connects back.
 *
 * Events: 'message' (a decoded message), 'close' (once: the host exited or the
 * connection broke).
 */
class DesignerConnection extends EventEmitter {
    /**
     * @param {childProcess.ChildProcess} process
     * @param {net.Socket} socket
     * @param {() => void} cleanup
     * @param {(line: string) => void} log
     */
    constructor(process, socket, cleanup, log) {
        super();

        this.process = process;
        this.socket = socket;
        this.cleanup = cleanup;
        this.log = log;
        this.disposed = false;
        this.closed = false;

        const decoder = new protocol.Decoder();

        socket.on('data', chunk => {
            let messages;

            try {
                messages = decoder.push(chunk);
            } catch (e) {
                log(`The connection to the previewer broke: ${e instanceof Error ? e.message : e}`);
                socket.destroy();
                return;
            }

            for (const message of messages) this.emit('message', message);
        });

        socket.on('error', e => {
            if (!this.disposed) log(`The connection to the previewer broke: ${e.message}`);
        });

        socket.on('close', () => this.onClosed());
        process.on('exit', () => this.onClosed());
    }

    get isAlive() {
        return !this.disposed && !this.closed && this.process.exitCode === null && !this.socket.destroyed;
    }

    onClosed() {
        if (this.closed) return;

        this.closed = true;
        this.cleanup();
        this.emit('close');
    }

    /**
     * Send a message; a dead host is not an error here — 'close' reports it once.
     * @param {protocol.Message} message
     */
    send(message) {
        if (!this.isAlive) return;
        this.socket.write(protocol.encode(message));
    }

    /** Ask the host to exit; kill it if it doesn't in a moment. */
    dispose() {
        if (this.disposed) return;

        this.send({ kind: protocol.Kind.Shutdown });
        this.disposed = true;

        const kill = setTimeout(() => {
            if (this.process.exitCode === null) this.process.kill();
        }, 1500);

        this.process.once('exit', () => clearTimeout(kill));

        this.socket.end();
        this.cleanup();
    }

    /**
     * Start a host and wait for it to connect.
     * @param {{ dotnet: string, hostPath: string, log: (line: string) => void }} options
     * @returns {Promise<DesignerConnection>}
     */
    static start({ dotnet, hostPath, log }) {
        return new Promise((resolve, reject) => {
            if (!fs.existsSync(hostPath)) {
                reject(new Error(`The previewer's host is missing: ${hostPath}`));
                return;
            }

            const { listenOn, args, cleanup } = endpoint();
            const server = net.createServer();

            /** @type {childProcess.ChildProcess | undefined} */
            let host;
            let settled = false;

            /** @param {Error} error */
            const fail = error => {
                if (settled) return;
                settled = true;

                clearTimeout(timer);
                server.close();
                cleanup();

                if (host && host.exitCode === null) host.kill();
                reject(error);
            };

            const timer = setTimeout(() => fail(new Error("The previewer's host did not connect in time.")), CONNECT_TIMEOUT_MS);

            server.on('error', e => fail(e));

            server.once('connection', socket => {
                if (settled || !host) {
                    socket.destroy();
                    return;
                }

                settled = true;
                clearTimeout(timer);

                // one host, one connection: nobody else may come in on this path
                server.close();

                socket.setNoDelay(true);

                log(`Previewer host started, process ${host.pid}.`);
                resolve(new DesignerConnection(host, socket, cleanup, log));
            });

            server.listen(listenOn, () => {
                try {
                    host = childProcess.spawn(dotnet, [hostPath, ...args], {
                        cwd: path.dirname(hostPath),
                        stdio: ['ignore', 'pipe', 'pipe'],
                        windowsHide: true,
                    });
                } catch (e) {
                    fail(new Error(`'${dotnet}' did not start: ${e instanceof Error ? e.message : e}`));
                    return;
                }

                const started = host;

                for (const stream of [started.stdout, started.stderr]) {
                    stream?.setEncoding('utf8');
                    let rest = '';

                    stream?.on('data', (/** @type {string} */ text) => {
                        const lines = (rest + text).split(/\r?\n/);
                        rest = lines.pop() ?? '';
                        for (const line of lines) if (line.length > 0) log(`host: ${line}`);
                    });
                }

                started.on('error', e => fail(new Error(
                    `The previewer needs the .NET 10 runtime, and '${dotnet}' did not start: ${e.message}. ` +
                    'Install .NET 10 or set zeppelinforms.dotnetPath.')));

                started.on('exit', code => fail(new Error(
                    `The previewer's host exited with code ${code} before connecting; see the ZeppelinForms output.`)));
            });
        });
    }
}

module.exports = { DesignerConnection };
