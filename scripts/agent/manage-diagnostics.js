#!/usr/bin/env node
// Maintenance helper for the GSHxxxx diagnostic registry (diagnostics.yml).
// Usage (from anywhere in the repository):
//   node scripts/agent/manage-diagnostics.js [--next-id] [--check-gaps] [--check-docs] [--catalog-path <p>] [--docs-dir <p>] [--help]
//   --next-id     print the next free diagnostic ID
//   --check-gaps  warn about missing numbers between GSH0001 and the highest ID
//   --check-docs  fail (exit 1) when a diagnostic has no <ID>.md in the docs directory
//   --catalog-path / --docs-dir  override the defaults; relative paths are resolved against the repository root
// Exit code: 0 on success, 1 when diagnostics.yml is missing/empty or documentation is missing.
'use strict';

const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..', '..');

const usage = 'Usage: manage-diagnostics.js [--check-gaps] [--next-id] [--check-docs] [--catalog-path <p>] [--docs-dir <p>]';

// Same defaults as the .ps1 (forward slashes are fine on every platform)
let catalogPath = 'src/Compiler/Kassyi.GSharp.Compiler/Diagnostics/diagnostics.yml';
let docsDir = 'docs/spec/en/03_bcl_api/diagnostics';
let checkGaps = false;
let nextId = false;
let checkDocs = false;

const args = process.argv.slice(2);
for (let i = 0; i < args.length; i++) {
    switch (args[i]) {
        case '--check-gaps': checkGaps = true; break;
        case '--next-id': nextId = true; break;
        case '--check-docs': checkDocs = true; break;
        case '--catalog-path':
        case '--docs-dir': {
            const value = args[++i];
            if (value === undefined) {
                console.error(`${args[i - 1]} requires a value.`);
                process.exit(1);
            }
            if (args[i - 1] === '--catalog-path') catalogPath = value; else docsDir = value;
            break;
        }
        case '--help':
        case '-h':
            console.log(usage);
            process.exit(0);
            break;
        default:
            console.error(`Unknown argument: ${args[i]}`);
            console.error(usage);
            process.exit(1);
    }
}

// Resolve paths (absolute overrides are kept as is)
const fullCatalogPath = path.resolve(repoRoot, catalogPath);
const fullDocsDir = path.resolve(repoRoot, docsDir);

if (!fs.existsSync(fullCatalogPath)) {
    console.error(`Could not find diagnostics.yml at ${fullCatalogPath}`);
    process.exit(1);
}

// The id of each entry: diagnostics.yml writes it as a plain `id: GSHxxxx` line of a list item (the compiler's YAML
// parser reads the whole file at build time; only the IDs are needed here)
const diagnostics = [...fs.readFileSync(fullCatalogPath, 'utf8').matchAll(/^(?:- | {2})id:\s*"?([A-Z]{3}\d{4})"?\s*$/gm)]
    .map((match) => ({ id: match[1] }));

// Extract numbers
const numbers = [];
for (const diag of diagnostics) {
    const m = /GSH(\d+)/.exec(String(diag && diag.id !== undefined && diag.id !== null ? diag.id : ''));
    if (m) numbers.push(parseInt(m[1], 10));
}
numbers.sort((a, b) => a - b);

if (numbers.length === 0) {
    console.error('No valid GSHxxxx IDs found.');
    process.exit(1);
}

const max = numbers[numbers.length - 1];
const min = 1;
const formatId = n => 'GSH' + String(n).padStart(4, '0');

if (nextId) {
    console.log(`Next available ID: ${formatId(max + 1)}`);
}

if (checkGaps) {
    const present = new Set(numbers);
    const gaps = [];
    for (let i = min; i < max; i++) {
        if (!present.has(i)) gaps.push(i);
    }
    if (gaps.length > 0) {
        // PowerShell's Write-Warning goes to the warning stream, so use stderr as well
        console.error('WARNING: Found gaps in diagnostic IDs:');
        for (const gap of gaps) {
            console.error(`WARNING:  - ${formatId(gap)} is missing.`);
        }
    } else {
        console.log('No gaps found in diagnostic IDs.');
    }
}

if (checkDocs) {
    const missingDocs = [];
    for (const diag of diagnostics) {
        const docPath = path.join(fullDocsDir, `${diag.id}.md`);
        if (!fs.existsSync(docPath)) missingDocs.push(diag.id);
    }

    if (missingDocs.length > 0) {
        console.error(`The following diagnostics are missing documentation in ${docsDir}:`);
        for (const id of missingDocs) {
            console.error(` - ${id}`);
        }
        process.exit(1);
    } else {
        console.log('All diagnostics have corresponding documentation.');
    }
}

if (!checkGaps && !nextId && !checkDocs) {
    console.log(usage);
}
