#!/usr/bin/env node
// Lists references to local planning material in tracked files: lane and scenario labels (`D1 (R1)`), roadmap IDs
// (`A-21`), design-document sections (`design §4.3`, `design D3 4.1`), plan stages (`stage 5`, `P5 tests`) and paths
// under `tmp/`. None of these exists in the repository, so a reader cannot follow it; say what is meant, or cite an
// issue number.
// Usage: node scripts/human/check-work-ids.js [--github] [--strict] [--root <dir>] [--path <prefix>]...
//   Warnings only by default (exit 0 even when matches exist); --strict exits 1 when any match remains.
'use strict';

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const helpText = [
    'Usage: node scripts/human/check-work-ids.js [--github] [--strict] [--root <dir>] [--path <prefix>]...',
    '',
    '  --github   Print GitHub Actions annotations (::warning file=...,line=...::) instead of path:line text.',
    '  --strict   Exit 1 when a match remains (default: always exit 0 after a successful scan).',
    '  --root     Repository root to scan (default: the root of this script\'s checkout).',
    '  --path     Only scan tracked files under this prefix (repeatable; default: the scanned folders below).',
    '  --help     Show this help.',
    '',
    'Scanned folders: ' + 'src/, tests/, tools/, docs/, .agents/',
].join('\n');

const defaultRoots = ['src/', 'tests/', 'tools/', 'docs/', '.agents/'];

// Text files only; binaries, snapshots of generated output and lock files carry no comments of ours.
const scannedExtension = /\.(cs|cu|cpp|h|hpp|md|yml|yaml|json|js|ts|tsx|props|targets|csproj|slnx|txt|prompt)$/i;
const skippedFile = /(\.verified\.[a-z]+|\.received\.[a-z]+|package-lock\.json|\.gsym\.json)$/i;

const patterns = [
    { name: 'tmp-path', regex: /(?<![\w.\-])tmp[\\/]\S*/g },
    { name: 'design-section', regex: /(?<![\w\-])design\s+(?:§|D\d|\d+\.\d)\S*/gi },
    { name: 'roadmap-id', regex: /\bA-\d{1,3}\b/g },
    { name: 'plan-stage', regex: /\b(?:stage\s+\d+(?!\s+of\s+#\d)|P\d+\s+tests?|test\s+plan\s+\d+)\b/gi },
    { name: 'scenario-label', regex: /\bD[1-8]\s*\(R[1-6]\)|\bD[1-8]\s+to\s+D[1-8]\b/g },
];

// A match is dropped when its text is in this list. Each entry is a real name, not a reference.
const allowed = [
    // The agent rules and workflows tell agents where to write scratch output; tmp/ is that documented folder.
    { name: 'tmp-path', file: /^\.agents\//, reason: 'scratch-output locations named by agent instructions' },
    // Default output folder of the differential fuzzer (a real path the tool writes).
    { name: 'tmp-path', regex: /^tmp[\\/]differential-fuzz/, reason: 'default output path of tools/fuzz-diff' },
];

function parseArgs(argv) {
    const options = { github: false, strict: false, root: null, paths: [] };
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === '--help' || arg === '-h') {
            process.stdout.write(helpText + '\n');
            process.exit(0);
        } else if (arg === '--github') {
            options.github = true;
        } else if (arg === '--strict') {
            options.strict = true;
        } else if (arg === '--root' || arg === '--path') {
            const value = argv[++i];
            if (value === undefined) {
                process.stderr.write(`${arg} needs a value\n`);
                process.exit(1);
            }
            if (arg === '--root') {
                options.root = value;
            } else {
                options.paths.push(value.split('\\').join('/'));
            }
        } else {
            process.stderr.write(`Unknown argument: ${arg}\n\n${helpText}\n`);
            process.exit(1);
        }
    }
    return options;
}

function main() {
    const options = parseArgs(process.argv.slice(2));
    const root = options.root ? path.resolve(options.root) : path.resolve(__dirname, '..', '..');
    const prefixes = options.paths.length > 0 ? options.paths : defaultRoots;

    const files = execFileSync('git', ['ls-files', '-z'], { cwd: root, maxBuffer: 256 * 1024 * 1024 })
        .toString('utf8')
        .split('\0')
        .filter(f => f.length > 0
            && prefixes.some(p => f.startsWith(p))
            && scannedExtension.test(f)
            && !skippedFile.test(f));

    let count = 0;
    for (const file of files) {
        let text;
        try {
            text = fs.readFileSync(path.join(root, file), 'utf8');
        } catch (error) {
            if (error.code === 'ENOENT') {
                continue; // tracked but deleted in the working tree
            }
            throw error;
        }
        const lines = text.split(/\r?\n/);
        for (let i = 0; i < lines.length; i++) {
            for (const pattern of patterns) {
                pattern.regex.lastIndex = 0;
                for (const match of lines[i].matchAll(pattern.regex)) {
                    const found = match[0].replace(/[`'")\],.;:]+$/, '');
                    if (allowed.some(a => a.name === pattern.name
                        && (a.file === undefined || a.file.test(file))
                        && (a.regex === undefined || a.regex.test(found)))) {
                        continue;
                    }
                    count++;
                    if (options.github) {
                        process.stdout.write(`::warning file=${file},line=${i + 1},title=Work ID (${pattern.name})::${found} refers to planning material that is not in the repository; say what is meant or cite an issue\n`);
                    } else {
                        process.stdout.write(`${file}:${i + 1}: [${pattern.name}] ${found}\n`);
                    }
                }
            }
        }
    }
    process.stderr.write(`${count} match(es) in ${files.length} file(s)\n`);
    process.exit(options.strict && count > 0 ? 1 : 0);
}

main();
