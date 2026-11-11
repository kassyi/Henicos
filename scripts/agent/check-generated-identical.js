#!/usr/bin/env node
'use strict';

// Compares the generated output of two builds byte for byte, to show that a change (a refactoring, a cleanup, a
// change of tests or docs only) leaves every generated file as it was.
//
// Both checkouts must be built the same way first (same projects, same configuration, same OS), for example:
//   git worktree add --detach tmp/worktree/base origin/main
//   dotnet build tests/Compiler/Kassyi.GSharp.Tests.Cpu -nodeReuse:false   (in both checkouts)
//
// Compared: the AOT sources (*.g.cu, *.g.cpp, *.g.hpp, *.g.h), the symbol files (*.gsym.json) and the generated C#
// (*.g.cs) under every obj/ directory of the two checkouts (bin/, tmp/, .git/, node_modules/ and TestResults/ are not
// searched). A file present in only one checkout counts as a difference. GSharpSharedTypeKeyedSites.g.cs is skipped:
// its keys hash the absolute source path, so it always differs between two checkouts.
//
// Usage:
//   node scripts/agent/check-generated-identical.js --base <checkout> --head <checkout> [--under <dir>] [--show <n>]
//
// Options:
//   --base <dir>   The checkout of the base (for example origin/main)
//   --head <dir>   The checkout of the change
//   --under <dir>  Compare only below this directory, relative to each checkout (default: the whole checkout)
//   --show <n>     Print at most n differences (default 20)
//   --help         Show this help
// Relative checkout paths resolve against the repository root.
// Exit code: 0 when the outputs are identical, 1 on a difference or when a checkout has no generated files,
// 2 on a wrong command line.

const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..', '..');
const generatedPatterns = [/\.g\.cu$/, /\.g\.cpp$/, /\.g\.hpp$/, /\.g\.h$/, /\.gsym\.json$/, /\.g\.cs$/];
const skippedFiles = new Set(['GSharpSharedTypeKeyedSites.g.cs']);
const skippedDirs = new Set(['.git', 'node_modules', 'TestResults', 'tmp', 'bin']);

function usage() {
    const lines = fs.readFileSync(__filename, 'utf8').split(/\r?\n/);
    const start = lines.findIndex(line => line.startsWith('// Usage:'));
    const end = lines.findIndex((line, index) => index > start && !line.startsWith('//'));
    return lines.slice(start, end).map(line => line.replace(/^\/\/ ?/, '')).join('\n');
}

function fail(message) {
    console.error(`check-generated-identical: ${message}\n\n${usage()}`);
    process.exit(2);
}

function parseArgs(argv) {
    const options = { base: null, head: null, under: '.', show: 20 };
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        const value = () => {
            if (i + 1 >= argv.length) {
                fail(`${arg} needs a value`);
            }
            return argv[++i];
        };
        if (arg === '--help' || arg === '-h') {
            console.log(usage());
            process.exit(0);
        } else if (arg === '--base') {
            options.base = path.resolve(repoRoot, value());
        } else if (arg === '--head') {
            options.head = path.resolve(repoRoot, value());
        } else if (arg === '--under') {
            options.under = value();
        } else if (arg === '--show') {
            options.show = Number(value());
            if (!Number.isInteger(options.show) || options.show < 0) {
                fail('--show needs a non-negative integer');
            }
        } else {
            fail(`unknown argument ${arg}`);
        }
    }
    if (!options.base || !options.head) {
        fail('--base and --head are required');
    }
    for (const dir of [options.base, options.head]) {
        if (!fs.existsSync(dir) || !fs.statSync(dir).isDirectory()) {
            fail(`${dir} is not a directory`);
        }
    }
    return options;
}

// The generated files below <root>/<under> that sit inside an obj/ directory, keyed by their path relative to root.
function collectGenerated(root, under) {
    const files = new Map();
    const walk = (dir, insideObj) => {
        for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
            const full = path.join(dir, entry.name);
            if (entry.isDirectory()) {
                if (!skippedDirs.has(entry.name)) {
                    walk(full, insideObj || entry.name === 'obj');
                }
            } else if (insideObj && !skippedFiles.has(entry.name) && generatedPatterns.some(p => p.test(entry.name))) {
                files.set(path.relative(root, full).split(path.sep).join('/'), full);
            }
        }
    };
    const start = path.join(root, under);
    if (fs.existsSync(start)) {
        walk(start, false);
    }
    return files;
}

// The 1-based line of the first differing byte of two buffers that are known to differ.
function firstDifferentLine(a, b) {
    const length = Math.min(a.length, b.length);
    let line = 1;
    for (let i = 0; i < length; i++) {
        if (a[i] !== b[i]) {
            return line;
        }
        if (a[i] === 0x0a) {
            line++;
        }
    }
    return line;
}

function main() {
    const options = parseArgs(process.argv.slice(2));
    const base = collectGenerated(options.base, options.under);
    const head = collectGenerated(options.head, options.under);
    if (base.size === 0 || head.size === 0) {
        console.log(`FAILED: no generated files found (base ${base.size}, head ${head.size}); build both checkouts first`);
        process.exit(1);
    }
    const differences = [];
    for (const [relative, baseFile] of base) {
        const headFile = head.get(relative);
        if (headFile === undefined) {
            differences.push(`only in base: ${relative}`);
            continue;
        }
        const a = fs.readFileSync(baseFile);
        const b = fs.readFileSync(headFile);
        if (!a.equals(b)) {
            differences.push(`differs: ${relative} (first difference on line ${firstDifferentLine(a, b)})`);
        }
    }
    for (const relative of head.keys()) {
        if (!base.has(relative)) {
            differences.push(`only in head: ${relative}`);
        }
    }
    differences.slice(0, options.show).forEach(d => console.log(`FAIL ${d}`));
    if (differences.length > options.show) {
        console.log(`... ${differences.length - options.show} more`);
    }
    const verdict = differences.length === 0 ? 'OK' : 'FAILED';
    console.log(`${verdict}: ${differences.length} difference(s); compared ${base.size} base and ${head.size} head file(s)`);
    process.exit(differences.length === 0 ? 0 : 1);
}

main();
