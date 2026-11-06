#!/usr/bin/env node
// Lists references to local planning material in tracked files: lane and scenario labels (`D1 (R1)`), roadmap IDs
// (`A-21`), design-document sections (`design §4.3`, `design D3 4.1`), plan stages (`stage 5`, `P5 tests`) and paths
// under `tmp/`. None of these exists in the repository, so a reader cannot follow it; say what is meant, or cite an
// issue number.
// Usage: node scripts/human/check-work-ids.js [--github] [--changed <base>] [--strict] [--root <dir>] [--path <prefix>]...
//   Warnings only by default (exit 0 even when matches exist); --strict exits 1 when any match remains.
'use strict';

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const defaultRoots = ['src/', 'tests/', 'tools/', 'docs/', '.agents/', 'scripts/', '.github/', 'justfile'];

// This script lists the patterns in its header and help, so it is the one file that is never scanned.
const selfPath = 'scripts/human/check-work-ids.js';

const helpText = [
    'Usage: node scripts/human/check-work-ids.js [--github] [--changed <base>] [--strict] [--root <dir>] [--path <prefix>]...',
    '',
    '  --github    Print GitHub Actions annotations (::warning file=...,line=...::) for the reported matches, and',
    '              write the full list to the log and to $GITHUB_STEP_SUMMARY when it is set.',
    '  --changed   Annotate only matches on lines added since the merge base with <base> (git diff <base>...HEAD);',
    '              the full list is still printed. Without --github, print only those matches.',
    '  --strict    Exit 1 when a reported match remains (default: always exit 0 after a successful scan).',
    '  --root      Repository root to scan (default: the root of this script\'s checkout).',
    '  --path      Only scan tracked files under this prefix (repeatable; default: the scanned paths below).',
    '  --help      Show this help.',
    '',
    'Scanned paths: ' + defaultRoots.join(', ') + ' (except ' + selfPath + ')',
].join('\n');

// Text files only; binaries, snapshots of generated output and lock files carry no comments of ours.
const scannedExtension = /(\.(cs|cu|cpp|h|hpp|md|yml|yaml|json|js|ts|tsx|props|targets|csproj|slnx|txt|prompt)|^justfile)$/i;
const skippedFile = /(\.verified\.[a-z]+|\.received\.[a-z]+|package-lock\.json|\.gsym\.json)$/i;

const patterns = [
    { name: 'tmp-path', regex: /(?<![\w.\-/])tmp[\\/]\S*/g },
    { name: 'design-section', regex: /(?<![\w\-])design\s+(?:§|D\d|\d+\.\d)\S*/gi },
    { name: 'roadmap-id', regex: /\bA-\d{1,3}\b/g },
    { name: 'plan-stage', regex: /\b(?:stage\s+\d+(?!\s+of\s+#\d)|P\d+\s+tests?|test\s+plan\s+\d+)\b/gi },
    { name: 'scenario-label', regex: /\bD[1-8]\s*\(R[1-6]\)|\bD[1-8]\s+to\s+D[1-8]\b/g },
];

// A match is dropped when its text is in this list. Each entry is a real name, not a reference.
const allowed = [
    // The agent rules and workflows tell agents where to write scratch output; tmp/ is that documented folder.
    { name: 'tmp-path', file: /^\.agents\//, reason: 'scratch-output locations named by agent instructions' },
    // Scripts, recipes and workflows write their output under tmp/ (the git-ignored scratch folder) and name the
    // paths they write; the other patterns still apply to them.
    { name: 'tmp-path', file: /^(scripts\/|\.github\/|justfile$)/, reason: 'output paths written by scripts, recipes and workflows' },
    // Default output folder of the differential fuzzer (a real path the tool writes).
    { name: 'tmp-path', regex: /^tmp[\\/]differential-fuzz/, reason: 'default output path of tools/fuzz-diff' },
];

function parseArgs(argv) {
    const options = { github: false, strict: false, changed: null, root: null, paths: [] };
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === '--help' || arg === '-h') {
            process.stdout.write(helpText + '\n');
            process.exit(0);
        } else if (arg === '--github') {
            options.github = true;
        } else if (arg === '--strict') {
            options.strict = true;
        } else if (arg === '--root' || arg === '--path' || arg === '--changed') {
            const value = argv[++i];
            if (value === undefined) {
                process.stderr.write(`${arg} needs a value\n`);
                process.exit(1);
            }
            if (arg === '--root') {
                options.root = value;
            } else if (arg === '--changed') {
                options.changed = value;
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

function git(root, args) {
    return execFileSync('git', args, { cwd: root, maxBuffer: 256 * 1024 * 1024 }).toString('utf8');
}

// The tracked text files under the given prefixes, without this script.
function listFiles(root, prefixes) {
    return git(root, ['ls-files', '-z'])
        .split('\0')
        .filter(f => f.length > 0
            && f !== selfPath
            && prefixes.some(p => f.startsWith(p))
            && scannedExtension.test(f)
            && !skippedFile.test(f));
}

// The text of a tracked file, or null when it is deleted in the working tree; other read errors are thrown.
function readTracked(root, file) {
    try {
        return fs.readFileSync(path.join(root, file), 'utf8');
    } catch (error) {
        if (error.code === 'ENOENT') {
            return null;
        }
        throw error;
    }
}

// The matches in one file's text, as { file, line, name, found }; allowed matches are left out.
function scanFile(file, text) {
    const matches = [];
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
                matches.push({ file, line: i + 1, name: pattern.name, found });
            }
        }
    }
    return matches;
}

// The lines added since the merge base with `base`, as a map from file to a set of 1-based line numbers.
function addedLines(root, base) {
    const diff = git(root, ['diff', '-U0', '--no-color', '--no-ext-diff', `${base}...HEAD`]);
    const added = new Map();
    let current = null;
    for (const line of diff.split('\n')) {
        if (line.startsWith('+++ ')) {
            const target = line.slice(4).trimEnd();
            current = target.startsWith('b/') ? target.slice(2) : null; // null for /dev/null (a deleted file)
            if (current !== null && !added.has(current)) {
                added.set(current, new Set());
            }
            continue;
        }
        const hunk = /^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@/.exec(line);
        if (hunk && current !== null) {
            const start = Number(hunk[1]);
            const count = hunk[2] === undefined ? 1 : Number(hunk[2]);
            for (let n = start; n < start + count; n++) {
                added.get(current).add(n);
            }
        }
    }
    return added;
}

// Workflow command data: `%`, CR and LF are escaped; a property value also escapes `:` and `,`.
function escapeData(text) {
    return text.replace(/%/g, '%25').replace(/\r/g, '%0D').replace(/\n/g, '%0A');
}

function escapeProperty(text) {
    return escapeData(text).replace(/:/g, '%3A').replace(/,/g, '%2C');
}

function formatPlain(m) {
    return `${m.file}:${m.line}: [${m.name}] ${m.found}`;
}

// Prints the matches. `reported` is the subset that gets an annotation (all of them without --changed).
function report(matches, reported, options) {
    if (!options.github) {
        for (const m of reported) {
            process.stdout.write(formatPlain(m) + '\n');
        }
        return;
    }
    // GitHub shows only the first 10 warning annotations of a step, so the full list goes to the log and the summary.
    for (const m of reported) {
        const title = escapeProperty(`Work ID (${m.name})`);
        const message = escapeData(`${m.found} refers to planning material that is not in the repository; say what is meant or cite an issue`);
        process.stdout.write(`::warning file=${escapeProperty(m.file)},line=${m.line},title=${title}::${message}\n`);
    }
    process.stdout.write(`::group::All ${matches.length} work ID(s) in the scanned files\n`);
    for (const m of matches) {
        process.stdout.write(escapeData(formatPlain(m)) + '\n');
    }
    process.stdout.write('::endgroup::\n');

    const summaryPath = process.env.GITHUB_STEP_SUMMARY;
    if (summaryPath) {
        const cell = text => text.replace(/\|/g, '\\|');
        const lines = [
            '## Work IDs',
            '',
            options.changed === null
                ? `${matches.length} reference(s) to planning material that is not in the repository.`
                : `${reported.length} on lines added since \`${cell(options.changed)}\`, ${matches.length} in all.`,
            '',
        ];
        if (matches.length > 0) {
            lines.push('| File | Line | Kind | Text |', '| ----- | ----- | ----- | ----- |');
            for (const m of matches) {
                lines.push(`| ${cell(m.file)} | ${m.line} | ${m.name} | \`${cell(m.found)}\` |`);
            }
            lines.push('');
        }
        fs.appendFileSync(summaryPath, lines.join('\n') + '\n');
    }
}

function main() {
    const options = parseArgs(process.argv.slice(2));
    const root = options.root ? path.resolve(options.root) : path.resolve(__dirname, '..', '..');
    const files = listFiles(root, options.paths.length > 0 ? options.paths : defaultRoots);

    const matches = [];
    for (const file of files) {
        const text = readTracked(root, file);
        if (text !== null) {
            matches.push(...scanFile(file, text));
        }
    }

    const added = options.changed === null ? null : addedLines(root, options.changed);
    const reported = added === null
        ? matches
        : matches.filter(m => added.has(m.file) && added.get(m.file).has(m.line));
    report(matches, reported, options);

    const scope = added === null ? '' : `, ${reported.length} on added lines`;
    process.stderr.write(`${matches.length} match(es) in ${files.length} file(s)${scope}\n`);
    process.exit(options.strict && reported.length > 0 ? 1 : 0);
}

main();
