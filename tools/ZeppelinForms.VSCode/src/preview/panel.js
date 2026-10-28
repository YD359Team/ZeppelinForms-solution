// @ts-check
'use strict';

const crypto = require('crypto');
const path = require('path');
const vscode = require('vscode');
const project = require('../designer/project');
const protocol = require('../designer/protocol');

/** Hidden this long, the panel lets the host go; shown again, it starts one. */
const RELEASE_AFTER_MS = 2 * 60 * 1000;

/**
 * The preview: a webview with the list of the project's previews, the settings to show
 * them with, and the live picture, which takes the mouse and the keyboard. The page
 * draws; this side speaks to the host.
 *
 * Frames come often and are large. The page gets one at a time: the next is posted when
 * it says it drew the last, and frames that came in between are dropped — what a screen
 * does with frames it had no time for.
 */
class PreviewPanel {
    /** @type {PreviewPanel | undefined} */
    static current;

    /**
     * Show the preview, creating it beside the editor if there is none.
     * @param {vscode.ExtensionContext} context
     * @param {import('../designer/service').DesignerService} designer
     */
    static show(context, designer) {
        if (PreviewPanel.current) {
            PreviewPanel.current.panel.reveal(undefined, true);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'zeppelinforms.preview',
            'ZeppelinForms Preview',
            { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true },
            {
                enableScripts: true,
                // the page keeps its picture and choices while in a background tab
                retainContextWhenHidden: true,
                localResourceRoots: [vscode.Uri.joinPath(context.extensionUri, 'media')],
            });

        PreviewPanel.current = new PreviewPanel(context, designer, panel);
    }

    /** Start the host anew: the Reload button and command. */
    static reload() {
        PreviewPanel.current?.designer.refresh(true);
    }

    /**
     * @param {vscode.ExtensionContext} context
     * @param {import('../designer/service').DesignerService} designer
     * @param {vscode.WebviewPanel} panel
     */
    constructor(context, designer, panel) {
        this.context = context;
        this.designer = designer;
        this.panel = panel;

        /** @type {protocol.PreviewInfo[]} */
        this.previews = [];

        /** @type {string | undefined} */
        this.lastStatus = undefined;

        /** @type {any} the last list, for a page that loads after it came */
        this.lastCatalog = undefined;

        /** @type {protocol.Message | null} the newest frame the page hasn't been given */
        this.pendingFrame = null;
        this.frameInFlight = false;
        this.holdsHost = false;

        /** @type {NodeJS.Timeout | undefined} */
        this.releaseTimer = undefined;

        /** @type {vscode.Disposable[]} */
        this.disposables = [];

        panel.iconPath = vscode.Uri.joinPath(context.extensionUri, 'media', 'icon.png');
        panel.webview.html = this.html();

        this.disposables.push(
            panel.webview.onDidReceiveMessage(message => this.onPageMessage(message)),
            panel.onDidChangeViewState(e => this.onVisibility(e.webviewPanel.visible)),
            panel.onDidDispose(() => this.dispose()),
            designer.onMessage(message => this.onHostMessage(message)),
            designer.onHostStarted(() => this.status(designer.assemblyPath
                ? `Loading ${path.basename(designer.assemblyPath)}…`
                : 'No project.')),
            designer.onHostFailed(reason => this.status(reason)),
            designer.onDocumentActivated(file => this.post({ type: 'prefer', ids: this.previewsOf(file) })));

        vscode.commands.executeCommand('setContext', 'zeppelinforms.previewOpen', true);

        this.acquire();
    }

    acquire() {
        clearTimeout(this.releaseTimer);

        if (this.holdsHost) return;

        this.holdsHost = true;
        this.designer.acquire();
    }

    release() {
        if (!this.holdsHost) return;

        this.holdsHost = false;
        this.designer.release();
    }

    /** @param {boolean} visible */
    onVisibility(visible) {
        if (visible) {
            this.acquire();
            return;
        }

        clearTimeout(this.releaseTimer);
        this.releaseTimer = setTimeout(() => this.release(), RELEASE_AFTER_MS);
    }

    /** @param {any} message */
    post(message) {
        this.panel.webview.postMessage(message);
    }

    /** @param {string} text */
    status(text) {
        this.lastStatus = text;
        this.post({ type: 'status', text });
    }

    // ===== the host =====

    /** @param {protocol.Message} message */
    onHostMessage(message) {
        switch (message.kind) {
            case protocol.Kind.Frame:
                this.pendingFrame = message;
                this.sendFrame();
                break;

            case protocol.Kind.Catalog:
                this.previews = message.previews;
                this.post(this.lastCatalog = {
                    type: 'catalog',
                    previews: message.previews,
                    error: message.error,
                    warning: message.warning,
                    assembly: this.designer.assemblyPath ? path.basename(this.designer.assemblyPath) : '',
                    prefer: this.designer.activeDocumentPath ? this.previewsOf(this.designer.activeDocumentPath) : [],
                });
                break;

            case protocol.Kind.PreviewError:
                this.post({ type: 'error', message: message.message, details: message.details });
                break;

            case protocol.Kind.Hello:
                if (message.protocolVersion !== protocol.VERSION)
                    this.status(`The previewer speaks protocol ${message.protocolVersion}, the extension ${protocol.VERSION}: reinstall the extension.`);
                break;
        }
    }

    sendFrame() {
        if (this.frameInFlight || !this.pendingFrame) return;

        const frame = this.pendingFrame;
        this.pendingFrame = null;
        this.frameInFlight = true;

        // a typed array goes to the page as binary, not as JSON numbers
        this.post({
            type: 'frame',
            width: frame.width,
            height: frame.height,
            scale: frame.scale,
            pixels: new Uint8Array(frame.pixels.buffer, frame.pixels.byteOffset, frame.pixels.byteLength),
        });
    }

    /**
     * The previews whose types the file declares, by name.
     * @param {string} file
     * @returns {string[]}
     */
    previewsOf(file) {
        const declared = project.declaredTypes(file);

        return this.previews
            .filter(p => declared.has(p.typeName.slice(Math.max(p.typeName.lastIndexOf('.'), p.typeName.lastIndexOf('+')) + 1)))
            .map(p => p.id);
    }

    // ===== the page =====

    /** @param {any} message */
    onPageMessage(message) {
        const K = protocol.Kind;

        switch (message.type) {
            case 'ready':
                // what came before the page could listen: the host may well be quicker
                this.frameInFlight = false;
                this.status(this.lastStatus ?? 'Starting the previewer…');
                if (this.lastCatalog) this.post(this.lastCatalog);
                break;

            case 'frameShown':
                this.frameInFlight = false;
                this.sendFrame();
                break;

            case 'open':
                this.designer.send({ kind: K.Open, previewId: message.id, settings: message.settings });
                break;

            case 'settings':
                this.designer.send({ kind: K.Settings, settings: message.settings });
                break;

            case 'pointer':
                this.designer.send({
                    kind: K.Pointer,
                    action: message.action,
                    x: message.x,
                    y: message.y,
                    button: message.button,
                    wheelDelta: message.wheelDelta,
                    modifiers: message.modifiers,
                });
                break;

            case 'key':
                this.designer.send({
                    kind: K.Key,
                    isDown: message.isDown,
                    key: message.key,
                    code: message.code,
                    modifiers: message.modifiers,
                    isRepeat: message.isRepeat,
                });
                break;

            case 'text':
                this.designer.send({ kind: K.Text, text: message.text });
                break;

            case 'reload':
                this.designer.refresh(true);
                break;

            case 'reveal':
                this.reveal(message.id);
                break;
        }
    }

    /**
     * Open the source of a preview: the file that declares its type.
     * @param {string} id
     */
    async reveal(id) {
        const preview = this.previews.find(p => p.id === id);
        if (!preview) return;

        const typeName = preview.typeName.slice(Math.max(preview.typeName.lastIndexOf('.'), preview.typeName.lastIndexOf('+')) + 1);
        const symbols = /** @type {vscode.SymbolInformation[] | undefined} */ (
            await vscode.commands.executeCommand('vscode.executeWorkspaceSymbolProvider', preview.memberName ?? typeName));

        const match = symbols?.find(s => preview.memberName ? s.containerName?.endsWith(typeName) : s.name === typeName);

        if (match) await vscode.window.showTextDocument(match.location.uri, { selection: match.location.range, viewColumn: vscode.ViewColumn.One });
    }

    html() {
        const webview = this.panel.webview;
        const media = (/** @type {string} */ file) => webview.asWebviewUri(vscode.Uri.joinPath(this.context.extensionUri, 'media', file));
        const nonce = crypto.randomBytes(16).toString('base64');

        return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${webview.cspSource} data:; style-src ${webview.cspSource}; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<link href="${media('preview.css')}" rel="stylesheet">
<title>ZeppelinForms Preview</title>
</head>
<body>
<div id="toolbar">
  <select id="list" title="Previews"></select>
  <select id="size" title="The size the preview is laid out at"></select>
  <select id="zoom" title="Zoom: the picture only, the layout stays"></select>
  <span id="zoomInfo"></span>
  <select id="theme" title="Theme"></select>
  <input id="culture" list="cultures" placeholder="Culture" title="Culture" spellcheck="false">
  <datalist id="cultures"></datalist>
  <select id="textScale" title="Text scale"></select>
  <label title="Right-to-left layout"><input id="rtl" type="checkbox"> RTL</label>
  <button id="source" title="Go to the preview's source">Source</button>
  <button id="reload" title="Start the previewer anew">Reload</button>
</div>
<div id="center">
  <div id="scroll"><div id="stage"><canvas id="picture" tabindex="0"></canvas></div></div>
  <pre id="error" hidden></pre>
</div>
<div id="status"></div>
<script nonce="${nonce}" src="${media('preview.js')}"></script>
</body>
</html>`;
    }

    dispose() {
        PreviewPanel.current = undefined;

        clearTimeout(this.releaseTimer);
        this.release();

        vscode.commands.executeCommand('setContext', 'zeppelinforms.previewOpen', false);

        for (const disposable of this.disposables) disposable.dispose();
        this.disposables = [];
    }
}

module.exports = { PreviewPanel };
