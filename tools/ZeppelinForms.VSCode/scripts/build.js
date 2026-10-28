// @ts-check
'use strict';

// Puts into the extension's folder what it ships besides its own code:
//   server/    the language server, published from tools/ZeppelinForms.LanguageServer
//   designer/  the previewer's host, published from tools/ZeppelinForms.Designer
//   syntaxes/zss.tmLanguage.json, language-configuration/zss.json — the Visual Studio
//              extension's, one grammar for both
//   LICENSE.txt — the repository's
//
//   node scripts/build.js                  everything
//   node scripts/build.js --version 0.19.0 everything, the .NET programs of that version
//   node scripts/build.js --assets-only    the files, without publishing
//   node scripts/build.js --check          the files, and fail if the programs are missing
//                                          (before `vsce package`)
//
// Both programs are published framework-dependent and without a runtime identifier: one
// folder runs on every system with .NET 10, and the previewer's folder carries Skia's
// native modules of all of them.

const { execFileSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const extension = path.resolve(__dirname, '..');
const tools = path.resolve(extension, '..');
const repository = path.resolve(tools, '..');

const args = process.argv.slice(2);
const assetsOnly = args.includes('--assets-only');
const check = args.includes('--check');
const versionIndex = args.indexOf('--version');
const version = versionIndex >= 0 ? args[versionIndex + 1] : undefined;

/** @param {string} from @param {string} to */
function copy(from, to) {
    fs.mkdirSync(path.dirname(to), { recursive: true });

    // without the byte order mark the Visual Studio files have: VS Code's JSON readers
    // for grammars don't all skip it
    const text = fs.readFileSync(from, 'utf8');
    fs.writeFileSync(to, text.charCodeAt(0) === 0xfeff ? text.slice(1) : text);

    console.log(`copied ${path.relative(repository, from)} -> ${path.relative(extension, to)}`);
}

/** @param {string} project @param {string} output */
function publish(project, output) {
    fs.rmSync(output, { recursive: true, force: true });

    const command = ['publish', project, '-c', 'Release', '-o', output, '-nologo', '-v', 'minimal'];

    // the previewer's host must be of the framework's version: a host of another one
    // can't bind a project built against it
    if (version) command.push(`-p:Version=${version}`);

    console.log(`dotnet ${command.join(' ')}`);
    execFileSync('dotnet', command, { stdio: 'inherit' });

    // symbols are for debugging the programs, not for running them
    for (const file of fs.readdirSync(output))
        if (file.endsWith('.pdb')) fs.rmSync(path.join(output, file));
}

const grammars = path.join(tools, 'ZeppelinForms.VisualStudio', 'Grammars');

copy(path.join(grammars, 'zss.tmLanguage.json'), path.join(extension, 'syntaxes', 'zss.tmLanguage.json'));
copy(path.join(grammars, 'language-configuration.json'), path.join(extension, 'language-configuration', 'zss.json'));
copy(path.join(repository, 'LICENSE.txt'), path.join(extension, 'LICENSE.txt'));

if (check) {
    const missing = [
        path.join(extension, 'server', 'ZeppelinForms.LanguageServer.dll'),
        path.join(extension, 'designer', 'ZeppelinForms.Designer.dll'),
    ].filter(file => !fs.existsSync(file));

    if (missing.length > 0) {
        console.error(`Run 'npm run build' first; missing:\n  ${missing.join('\n  ')}`);
        process.exit(1);
    }
} else if (!assetsOnly) {
    publish(path.join(tools, 'ZeppelinForms.LanguageServer', 'ZeppelinForms.LanguageServer.csproj'), path.join(extension, 'server'));
    publish(path.join(tools, 'ZeppelinForms.Designer', 'ZeppelinForms.Designer.csproj'), path.join(extension, 'designer'));
}
