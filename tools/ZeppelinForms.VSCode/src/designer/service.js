// @ts-check
'use strict';

const fs = require('fs');
const path = require('path');
const vscode = require('vscode');
const { DesignerConnection } = require('./connection');
const project = require('./project');
const protocol = require('./protocol');

/**
 * The previewer's host for the project being edited: started when somebody needs it,
 * started anew after each build of that project and when a file of another project
 * becomes active — as in Visual Studio.
 *
 * A new process per build rather than a reload in place: the framework's registries in
 * the host would keep every old version of the project's controls. The preview reopens
 * what it showed, so a rebuild looks like a refresh.
 */
class DesignerService {
    /**
     * @param {vscode.ExtensionContext} context
     * @param {vscode.LogOutputChannel} log
     */
    constructor(context, log) {
        this.context = context;
        this.log = log;

        /** @type {DesignerConnection | null} */
        this.connection = null;

        /** @type {string | null} the project file of the active document */
        this.projectFile = null;

        /** @type {string | null} the assembly the host has loaded */
        this.assemblyPath = null;
        this.assemblyStamp = 0;

        this.users = 0;
        this.generation = 0;

        /** @type {vscode.FileSystemWatcher | null} */
        this.watcher = null;

        /** @type {string | null} the bin folder the watcher watches */
        this.watchedDirectory = null;

        /** @type {NodeJS.Timeout | undefined} */
        this.buildTimer = undefined;

        /** @type {vscode.EventEmitter<protocol.Message>} */
        this.onMessageEmitter = new vscode.EventEmitter();

        /** @type {vscode.EventEmitter<void>} */
        this.onHostStartedEmitter = new vscode.EventEmitter();

        /** @type {vscode.EventEmitter<string>} */
        this.onHostFailedEmitter = new vscode.EventEmitter();

        /** @type {vscode.EventEmitter<string>} */
        this.onDocumentActivatedEmitter = new vscode.EventEmitter();

        /** @type {vscode.Event<protocol.Message>} any message of the host */
        this.onMessage = this.onMessageEmitter.event;

        /** @type {vscode.Event<void>} a host started and loads the project: what was shown should be shown again */
        this.onHostStarted = this.onHostStartedEmitter.event;

        /** @type {vscode.Event<string>} the host could not be started or has gone; the text says why */
        this.onHostFailed = this.onHostFailedEmitter.event;

        /** @type {vscode.Event<string>} a file became active: the preview shows its previews */
        this.onDocumentActivated = this.onDocumentActivatedEmitter.event;

        context.subscriptions.push(
            vscode.window.onDidChangeActiveTextEditor(editor => {
                if (!editor || editor.document.uri.scheme !== 'file') return;

                this.refresh(false);
                this.onDocumentActivatedEmitter.fire(editor.document.uri.fsPath);
            }),
            vscode.workspace.onDidChangeConfiguration(e => {
                if (e.affectsConfiguration('zeppelinforms.preview') || e.affectsConfiguration('zeppelinforms.dotnetPath'))
                    this.refresh(true);
            }),
            vscode.workspace.onDidGrantWorkspaceTrust(() => this.refresh(true)),
            { dispose: () => this.dispose() });
    }

    /** The source file of the active editor, for choosing its previews. */
    get activeDocumentPath() {
        const editor = vscode.window.activeTextEditor;
        return editor && editor.document.uri.scheme === 'file' ? editor.document.uri.fsPath : null;
    }

    /** Somebody needs the host; the first one starts it, the last release stops it. */
    acquire() {
        if (this.users++ === 0) this.refresh(false);
    }

    release() {
        if (--this.users > 0) return;

        this.users = 0;
        this.stop();
        this.watch(null);
    }

    /** @param {protocol.Message} message */
    send(message) {
        this.connection?.send(message);
    }

    /**
     * Start a host for the active project if there is none for it, or if the project was
     * built since. force — start a new one regardless.
     * @param {boolean} force
     */
    refresh(force) {
        if (this.users === 0) return;

        const target = this.target();

        // another file of the same project, or a file of none — a settings.json, the
        // output — keeps what is shown
        if (!force && target.projectFile === null && this.projectFile !== null) return;

        const stamp = target.assembly ? stampOf(target.assembly) : 0;

        if (!force && this.connection?.isAlive &&
            target.assembly === this.assemblyPath && stamp === this.assemblyStamp)
            return;

        this.projectFile = target.projectFile;
        this.assemblyPath = target.assembly;
        this.assemblyStamp = stamp;

        this.watch(target.projectFile);
        this.restart(target.assembly);
    }

    /**
     * The project of the active file and its built assembly — or the one the settings name.
     * @returns {{ projectFile: string | null, assembly: string | null }}
     */
    target() {
        const document = this.activeDocumentPath;
        const folder = document ? vscode.workspace.getWorkspaceFolder(vscode.Uri.file(document)) : vscode.workspace.workspaceFolders?.[0];

        const configured = vscode.workspace.getConfiguration('zeppelinforms', folder?.uri).get('preview.assembly', '');

        if (configured) {
            const assembly = path.isAbsolute(configured) || !folder ? configured : path.join(folder.uri.fsPath, configured);
            return { projectFile: this.projectFile ?? assembly, assembly };
        }

        if (!document) return { projectFile: this.projectFile, assembly: this.assemblyPath };

        const projectFile = project.projectOf(document, folder?.uri.fsPath);

        if (!projectFile) return { projectFile: null, assembly: null };

        // a walk of bin, at most a few folders deep: cheap enough for every switch of a tab
        const output = project.outputOf(projectFile);

        return { projectFile, assembly: output.assembly ?? path.join(output.binDirectory, output.name + '.dll') };
    }

    /**
     * Watch the project's bin for builds of its assembly: a build writes it, and a moment
     * later the host is started anew. A build writes many files — and some twice —, so
     * the restart waits for the writes to settle.
     * @param {string | null} projectFile
     */
    watch(projectFile) {
        const wanted = projectFile ? path.join(path.dirname(projectFile), 'bin') : null;

        if (this.watcher && this.watchedDirectory === wanted) return;

        this.watcher?.dispose();
        this.watcher = null;
        this.watchedDirectory = wanted;

        if (!projectFile || !wanted) return;

        const name = project.assemblyNameOf(projectFile);
        const watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(vscode.Uri.file(wanted), `**/${name}.dll`));

        const changed = () => {
            clearTimeout(this.buildTimer);
            this.buildTimer = setTimeout(() => this.refresh(false), 800);
        };

        watcher.onDidCreate(changed);
        watcher.onDidChange(changed);

        this.watcher = watcher;
    }

    /** @param {string | null} assembly */
    restart(assembly) {
        this.stop();

        const generation = ++this.generation;

        if (!vscode.workspace.isTrusted) {
            this.onHostFailedEmitter.fire('The previewer runs the project\'s code: trust the workspace to see its previews.');
            return;
        }

        const configuration = vscode.workspace.getConfiguration('zeppelinforms');
        const dotnet = configuration.get('dotnetPath', 'dotnet') || 'dotnet';
        const hostPath = configuration.get('preview.hostPath', '') ||
            path.join(this.context.extensionPath, 'designer', 'ZeppelinForms.Designer.dll');

        DesignerConnection.start({ dotnet, hostPath, log: line => this.log.info(line) }).then(connection => {
            // a newer start has begun meanwhile: this one is not wanted any more
            if (generation !== this.generation || this.users === 0) {
                connection.dispose();
                return;
            }

            this.connection = connection;

            connection.on('message', (/** @type {protocol.Message} */ message) => {
                if (message.kind === protocol.Kind.Log) this.log.info(message.text);
                this.onMessageEmitter.fire(message);
            });

            connection.on('close', () => {
                if (generation === this.generation && !connection.disposed)
                    this.onHostFailedEmitter.fire("The previewer's host has stopped; see the ZeppelinForms output.");
            });

            connection.send({ kind: protocol.Kind.Hello, protocolVersion: protocol.VERSION, frameworkVersion: '' });

            const loading = assembly !== null && fs.existsSync(assembly);

            if (loading) {
                this.log.info(`Loading ${assembly}`);
                connection.send({ kind: protocol.Kind.Load, assemblyPath: assembly });
            }

            this.onHostStartedEmitter.fire();

            // after onHostStarted, whose listeners report "loading…": the reason there is
            // nothing to load must be the last word
            if (!loading) {
                this.onHostFailedEmitter.fire(assembly === null
                    ? 'Open a file of a ZeppelinForms project to see its previews.'
                    : `Build the project to see its previews: ${path.basename(assembly)} is not there yet.`);
            }
        }, error => {
            const text = error instanceof Error ? error.message : String(error);
            this.log.error(text);

            if (generation === this.generation) this.onHostFailedEmitter.fire(text);
        });
    }

    stop() {
        const old = this.connection;
        this.connection = null;
        old?.dispose();
    }

    dispose() {
        this.users = 0;
        clearTimeout(this.buildTimer);
        this.watcher?.dispose();
        this.watcher = null;
        this.stop();
    }
}

/** @param {string} file */
function stampOf(file) {
    try {
        return fs.statSync(file).mtimeMs;
    } catch {
        return 0;
    }
}

module.exports = { DesignerService };
