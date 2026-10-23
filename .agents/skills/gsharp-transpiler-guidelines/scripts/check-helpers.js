#!/usr/bin/env node
// Checks that every `Type.Member` and `.Method` in references/helpers.md still exists in the compiler, and that a method
// called on a receiver (extension or instance) is written `.Method` rather than `Type.Method`.
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

// `Name(this T x` or `Name<T>([Attr] this T x`, possibly across lines
function extensionPattern(member) {
    return new RegExp(`\\b${member}\\s*(<[^>]*>)?\\(\\s*(\\[[^\\]]*\\]\\s*)?this\\b`);
}

// A public or internal method declaration of `member`, static or not
function methodPattern(member, isStatic) {
    const modifier = isStatic ? '(?=[^(\\n]*\\bstatic\\b)' : '(?![^(\\n]*\\bstatic\\b)';
    return new RegExp(`^\\s*(public|internal)\\b${modifier}[^(\\n=]*\\b${member}\\s*(<[^>]*>)?\\(`, 'm');
}

// Called on a receiver: an extension method or an instance method
function isReceiverMethod(source, member) {
    return extensionPattern(member).test(source) || methodPattern(member, false).test(source);
}

const sources = listSources(compiler);
const text = fs.readFileSync(catalog, 'utf8');
const unique = matches => [...new Set(matches)].sort();
const qualified = unique([...text.matchAll(/`([A-Z][A-Za-z0-9]*)\.([A-Z][A-Za-z0-9]*)/g)].map(m => `${m[1]}.${m[2]}`));
const extensions = unique([...text.matchAll(/`\.([A-Z][A-Za-z0-9]*)/g)].map(m => m[1]));

let checked = 0;
let problems = 0;
for (const token of qualified) {
    const [type, member] = token.split('.');
    const declaration = new RegExp(`\\b(class|struct|interface|enum|record) ${type}\\b`);
    const declaringFiles = sources.filter(source => declaration.test(source));
    if (declaringFiles.length === 0) {
        continue;
    }
    checked++;
    const memberPattern = new RegExp(`\\b${member}\\b`);
    if (!declaringFiles.some(source => memberPattern.test(source))) {
        console.log(`missing: ${token}`);
        problems++;
    } else if (declaringFiles.some(source => isReceiverMethod(source, member)) &&
        !declaringFiles.some(source => methodPattern(member, true).test(source) && !extensionPattern(member).test(source))) {
        console.log(`receiver method written as a static call: ${token} (write \`.${member}\`)`);
        problems++;
    }
}

for (const member of extensions) {
    checked++;
    if (!sources.some(source => isReceiverMethod(source, member))) {
        console.log(`missing method: .${member}`);
        problems++;
    }
}

console.log(`checked ${checked} references, ${problems} problems`);
process.exitCode = problems === 0 ? 0 : 1;
