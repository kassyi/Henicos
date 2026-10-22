#!/usr/bin/env node
// Checks that every `Type.Member` in references/helpers.md still exists in the compiler.
// A type that the compiler does not declare (a Roslyn type, for example) is skipped.
// Usage: node .agents/skills/gsharp-transpiler-guidelines/scripts/check-helpers.js
'use strict';

const fs = require('fs');
const path = require('path');

const skillDir = path.resolve(__dirname, '..');
const repoRoot = path.resolve(skillDir, '..', '..', '..');
const catalog = path.join(skillDir, 'references', 'helpers.md');
const compiler = path.join(repoRoot, 'src', 'Compiler', 'Kassyi.GSharp.Compiler');

function listSources(dir, out = []) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        if (entry.isDirectory()) {
            if (entry.name !== 'bin' && entry.name !== 'obj') {
                listSources(path.join(dir, entry.name), out);
            }
        } else if (entry.name.endsWith('.cs')) {
            out.push(fs.readFileSync(path.join(dir, entry.name), 'utf8'));
        }
    }
    return out;
}

const sources = listSources(compiler);
const tokens = [...new Set([...fs.readFileSync(catalog, 'utf8').matchAll(/`([A-Z][A-Za-z0-9]*)\.([A-Z][A-Za-z0-9]*)/g)]
    .map(m => `${m[1]}.${m[2]}`))].sort();

let checked = 0;
let missing = 0;
for (const token of tokens) {
    const [type, member] = token.split('.');
    const declaration = new RegExp(`\\b(class|struct|interface|enum|record) ${type}\\b`);
    const declaringFiles = sources.filter(text => declaration.test(text));
    if (declaringFiles.length === 0) {
        continue;
    }
    checked++;
    const memberPattern = new RegExp(`\\b${member}\\b`);
    if (!declaringFiles.some(text => memberPattern.test(text))) {
        console.log(`missing: ${token}`);
        missing++;
    }
}

console.log(`checked ${checked} references, ${missing} missing`);
process.exitCode = missing === 0 ? 0 : 1;
