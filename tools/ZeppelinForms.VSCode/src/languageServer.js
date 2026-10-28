// @ts-check
'use strict';

const fs = require('fs');
const path = require('path');
const vscode = require('vscode');
const { LanguageClient, TransportKind } = require('vscode-languageclient/node');

/**
 * The ZeppelinForms language server — the one Visual Studio and Rider run — for .zfml
 * and .zss: completion, hover and problems. It ships in the extension's server folder and
 * runs on the machine's .NET 10, over stdin and stdout.
 *
 * @param {vscode.ExtensionContext} context
 * @param {vscode.LogOutputChannel} log
 * @returns {LanguageClient | null}
 */
function createLanguageClient(context, log) {
    const configuration = vscode.workspace.getConfiguration('zeppelinforms');
    const dotnet = configuration.get('dotnetPath', 'dotnet') || 'dotnet';

    // a server path from the settings runs a program: only in a trusted workspace
    const configured = vscode.workspace.isTrusted ? configuration.get('languageServer.path', '') : '';
    const server = configured || path.join(context.extensionPath, 'server', 'ZeppelinForms.LanguageServer.dll');

    if (!fs.existsSync(server)) {
        log.error(`The language server is missing: ${server}`);
        return null;
    }

    /** @type {import('vscode-languageclient/node').Executable} */
    const executable = {
        command: dotnet,
        args: [server],
        transport: TransportKind.stdio,
        options: { cwd: path.dirname(server) },
    };

    return new LanguageClient(
        'zeppelinforms',
        'ZeppelinForms',
        { run: executable, debug: executable },
        {
            documentSelector: [
                { scheme: 'file', language: 'zfml' },
                { scheme: 'file', language: 'zss' },
            ],
            // no file events: the server reads the project's C# from the disk by the
            // files' time stamps, at each request
            outputChannel: log,
            initializationFailedHandler: (/** @type {unknown} */ error) => {
                log.error(`The language server did not start: ${error instanceof Error ? error.message : error}. ` +
                    `It needs the .NET 10 runtime; set zeppelinforms.dotnetPath if '${dotnet}' is not it.`);
                return false;
            },
        });
}

module.exports = { createLanguageClient };
