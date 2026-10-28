// @ts-check
'use strict';

const fs = require('fs');
const path = require('path');

/**
 * Which project a file belongs to, and where the project's build puts its assembly.
 * VS Code has no project system of its own, so the answer comes from the files: the
 * nearest .csproj above the file, its AssemblyName, and the newest assembly of that name
 * under its bin folder — the configuration and the framework last built.
 */

/**
 * The project file the file belongs to: the nearest *.csproj in its folder or above,
 * up to the workspace folder (or the file system's root without one).
 *
 * @param {string} file
 * @param {string | undefined} root
 * @returns {string | null}
 */
function projectOf(file, root) {
    let directory = path.dirname(file);

    for (;;) {
        const project = projectIn(directory);
        if (project) return project;

        const parent = path.dirname(directory);

        if (parent === directory) return null;
        if (root && !isInside(directory, root)) return null;
        if (root && samePath(directory, root)) return null;

        directory = parent;
    }
}

/**
 * The folder's project file; of several, the one named as the folder.
 * @param {string} directory
 * @returns {string | null}
 */
function projectIn(directory) {
    let entries;

    try {
        entries = fs.readdirSync(directory).filter(name => name.toLowerCase().endsWith('.csproj'));
    } catch {
        return null;
    }

    if (entries.length === 0) return null;

    const named = entries.find(name => path.parse(name).name === path.basename(directory));
    return path.join(directory, named ?? entries[0]);
}

/**
 * The project's assembly name: its AssemblyName, or the file's name. An AssemblyName built
 * from properties — $(...) — can't be evaluated here: the file's name is the guess.
 *
 * @param {string} project
 * @returns {string}
 */
function assemblyNameOf(project) {
    try {
        const text = fs.readFileSync(project, 'utf8');
        const match = /<AssemblyName>\s*([^<]+?)\s*<\/AssemblyName>/.exec(text);

        if (match && !match[1].includes('$(')) return match[1];
    } catch {
        // unreadable: the file's name
    }

    return path.parse(project).name;
}

/**
 * The newest build of the project's assembly under its bin folder; null — never built.
 * Reference assemblies (bin/.../ref/) have no code to run and are skipped.
 *
 * @param {string} project
 * @returns {{ name: string, binDirectory: string, assembly: string | null }}
 */
function outputOf(project) {
    const name = assemblyNameOf(project);
    const binDirectory = path.join(path.dirname(project), 'bin');
    const wanted = (name + '.dll').toLowerCase();

    const newest = { file: '', time: -Infinity };

    /** @param {string} directory @param {number} depth */
    const walk = (directory, depth) => {
        let entries;

        try {
            entries = fs.readdirSync(directory, { withFileTypes: true });
        } catch {
            return;
        }

        for (const entry of entries) {
            const full = path.join(directory, entry.name);

            if (entry.isDirectory()) {
                // bin/Debug/net10.0/ — and a runtime's folder or publish/ under it at most
                if (depth < 5 && entry.name !== 'ref' && entry.name !== 'refint') walk(full, depth + 1);
            } else if (entry.name.toLowerCase() === wanted) {
                const time = fs.statSync(full).mtimeMs;

                if (time > newest.time) {
                    newest.file = full;
                    newest.time = time;
                }
            }
        }
    };

    walk(binDirectory, 0);

    return { name, binDirectory, assembly: newest.file || null };
}

/**
 * The type names a C# file declares, to find its previews among the project's.
 * @param {string} file
 * @returns {Set<string>}
 */
function declaredTypes(file) {
    if (!file.toLowerCase().endsWith('.cs')) return new Set();

    try {
        const text = fs.readFileSync(file, 'utf8');
        return new Set([...text.matchAll(/\b(?:class|record|struct)\s+([A-Za-z_]\w*)/g)].map(m => m[1]));
    } catch {
        return new Set();
    }
}

/**
 * @param {string} child
 * @param {string} parent
 */
function isInside(child, parent) {
    const relative = path.relative(parent, child);
    return !relative.startsWith('..') && !path.isAbsolute(relative);
}

/**
 * @param {string} a
 * @param {string} b
 */
function samePath(a, b) {
    const left = path.resolve(a);
    const right = path.resolve(b);

    return process.platform === 'win32' || process.platform === 'darwin'
        ? left.toLowerCase() === right.toLowerCase()
        : left === right;
}

module.exports = { projectOf, outputOf, assemblyNameOf, declaredTypes, samePath };
