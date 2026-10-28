// @ts-check
'use strict';

const vscode = require('vscode');
const { DesignerService } = require('./designer/service');
const { createLanguageClient } = require('./languageServer');
const { PreviewPanel } = require('./preview/panel');

/** @type {import('vscode-languageclient/node').LanguageClient | null} */
let client = null;

/**
 * ZeppelinForms in VS Code: the language server for .zfml and .zss, and the previewer
 * — the same host process Visual Studio and Rider start, drawn in a webview.
 *
 * @param {vscode.ExtensionContext} context
 */
async function activate(context) {
    const log = vscode.window.createOutputChannel('ZeppelinForms', { log: true });
    context.subscriptions.push(log);

    const designer = new DesignerService(context, log);

    context.subscriptions.push(
        vscode.commands.registerCommand('zeppelinforms.openPreview', () => PreviewPanel.show(context, designer)),
        vscode.commands.registerCommand('zeppelinforms.reloadPreview', () => PreviewPanel.reload()),
        vscode.commands.registerCommand('zeppelinforms.restartLanguageServer', async () => {
            await stopClient();
            await startClient(context, log);
        }),
        vscode.workspace.onDidChangeConfiguration(async e => {
            if (e.affectsConfiguration('zeppelinforms.dotnetPath') || e.affectsConfiguration('zeppelinforms.languageServer.path')) {
                await stopClient();
                await startClient(context, log);
            }
        }),
        // a trusted workspace may run the server its settings name
        vscode.workspace.onDidGrantWorkspaceTrust(async () => {
            await stopClient();
            await startClient(context, log);
        }));

    await startClient(context, log);
}

/**
 * @param {vscode.ExtensionContext} context
 * @param {vscode.LogOutputChannel} log
 */
async function startClient(context, log) {
    client = createLanguageClient(context, log);

    try {
        await client?.start();
    } catch (e) {
        // the handler logged why; completion is gone, the rest of the extension stays
        client = null;
        log.error(`ZeppelinForms language server: ${e instanceof Error ? e.message : e}`);
    }
}

async function stopClient() {
    const old = client;
    client = null;

    try {
        await old?.stop();
    } catch {
        // gone already
    }
}

function deactivate() {
    return stopClient();
}

module.exports = { activate, deactivate };
