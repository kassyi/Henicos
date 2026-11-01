#!/usr/bin/env node
// Prints where test time goes, from the .trx files that `dotnet test --logger trx` writes.
// Usage: node scripts/human/ci-test-durations.js <file.trx|dir>... [--top <n>] [--source <dir>] [--no-source]
//   Hybrid CI uploads the .trx files of every job as the artifacts test-results-<os>-<suite>; download them with
//   `gh run download <run id> --pattern "test-results-windows-*" --dir tmp/trx` and pass the directory. Pass the files
//   of one OS at a time: the totals add up every test result given.
//   Prints the time per test assembly, per xunit collection, per test class, and the slowest tests. A trx does not name
//   the xunit collection, so the collections are read from the [Collection(...)] attributes of the test sources
//   (default: tests/ of this repository); a class without one is its own collection and runs in parallel with others.
//   xunit runs the tests of one collection one after another, so the total of a collection is about its wall time.
'use strict';

const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..', '..');

const helpText = [
    'Usage: node scripts/human/ci-test-durations.js <file.trx|dir>... [--top <n>] [--source <dir>] [--no-source]',
    '',
    '  <file.trx|dir>   .trx files, or directories searched recursively for them.',
    '  --top            Rows to print for the classes and the slowest tests. Default: 20.',
    '  --source         Directory of test sources searched for [Collection(...)] attributes. Default: tests/.',
    '  --no-source      Do not read the sources; the per-collection totals are left out.',
    '  --help           Show this help.',
].join('\n');

function fail(message) {
    process.stderr.write(message + '\n');
    process.exit(1);
}

function parseArgs(argv) {
    const options = { inputs: [], top: 20, source: path.join(repoRoot, 'tests'), useSource: true };
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === '--help' || arg === '-h') {
            process.stdout.write(helpText + '\n');
            process.exit(0);
        } else if (arg === '--top' || arg === '--source') {
            const value = argv[++i];
            if (value === undefined) {
                fail(`${arg} needs a value`);
            }
            if (arg === '--top') {
                options.top = Number.parseInt(value, 10);
                if (!Number.isInteger(options.top) || options.top <= 0) {
                    fail(`--top needs a positive integer, got ${value}`);
                }
            } else {
                options.source = path.resolve(value);
            }
        } else if (arg === '--no-source') {
            options.useSource = false;
        } else if (arg.startsWith('--')) {
            fail(`Unknown option: ${arg}\n\n${helpText}`);
        } else {
            options.inputs.push(path.resolve(arg));
        }
    }
    if (options.inputs.length === 0) {
        fail(`No .trx file or directory given.\n\n${helpText}`);
    }
    return options;
}

function findFiles(entry, extension, found) {
    const stat = fs.statSync(entry);
    if (stat.isFile()) {
        if (entry.toLowerCase().endsWith(extension)) {
            found.push(entry);
        }
        return found;
    }
    for (const child of fs.readdirSync(entry, { withFileTypes: true })) {
        if (child.isDirectory() && (child.name === 'bin' || child.name === 'obj' || child.name === '.git')) {
            continue;
        }
        findFiles(path.join(entry, child.name), extension, found);
    }
    return found;
}

function decodeXml(text) {
    return text
        .replace(/&lt;/g, '<')
        .replace(/&gt;/g, '>')
        .replace(/&quot;/g, '"')
        .replace(/&apos;/g, "'")
        .replace(/&#x([0-9a-fA-F]+);/g, (_, hex) => String.fromCodePoint(Number.parseInt(hex, 16)))
        .replace(/&#([0-9]+);/g, (_, dec) => String.fromCodePoint(Number.parseInt(dec, 10)))
        .replace(/&amp;/g, '&');
}

function attributes(tagText) {
    const result = {};
    for (const match of tagText.matchAll(/([\w:]+)="([^"]*)"/g)) {
        result[match[1]] = decodeXml(match[2]);
    }
    return result;
}

// "hh:mm:ss.fffffff" -> seconds
function parseDuration(text) {
    const match = /^(\d+):(\d+):(\d+(?:\.\d+)?)$/.exec(text || '');
    return match ? Number(match[1]) * 3600 + Number(match[2]) * 60 + Number(match[3]) : 0;
}

function readTrx(file) {
    const text = fs.readFileSync(file, 'utf8');
    const definitions = new Map();
    for (const match of text.matchAll(/<UnitTest\s([^>]*)>([\s\S]*?)<\/UnitTest>/g)) {
        const unitTest = attributes(match[1]);
        const methodTag = /<TestMethod\s([^>]*?)\/?>/.exec(match[2]);
        const method = methodTag ? attributes(methodTag[1]) : {};
        definitions.set(unitTest.id, {
            // path.win32.basename splits on both separators, so a trx written on Windows reads the same on Linux
            assembly: path.win32.basename(unitTest.storage || method.codeBase || 'unknown'),
            className: method.className || '(unknown class)',
        });
    }

    const results = [];
    for (const match of text.matchAll(/<UnitTestResult\s([^>]*?)\/?>/g)) {
        const result = attributes(match[1]);
        const definition = definitions.get(result.testId) || { assembly: 'unknown', className: '(unknown class)' };
        results.push({
            name: result.testName || '(unnamed)',
            outcome: result.outcome || 'Unknown',
            seconds: parseDuration(result.duration),
            assembly: definition.assembly,
            className: definition.className,
        });
    }

    const timesTag = /<Times\s([^>]*?)\/?>/.exec(text);
    const times = timesTag ? attributes(timesTag[1]) : {};
    const start = Date.parse(times.start);
    const finish = Date.parse(times.finish);
    const wallSeconds = Number.isNaN(start) || Number.isNaN(finish) ? 0 : (finish - start) / 1000;
    return { file, results, wallSeconds };
}

// Looks up the collection of a test class by the full name the trx gives (Namespace.Class, or Namespace.Outer+Inner for
// a nested class, which the sources know as Namespace.Inner).
function collectionOf(collections, className) {
    const exact = collections.get(className);
    if (exact !== undefined) {
        return exact;
    }
    const nested = className.indexOf('+');
    if (nested < 0) {
        return undefined;
    }
    const outer = className.slice(0, nested);
    const namespace = outer.slice(0, Math.max(outer.lastIndexOf('.'), 0));
    const inner = className.slice(className.lastIndexOf('+') + 1);
    return collections.get(namespace === '' ? inner : `${namespace}.${inner}`);
}

// The namespace in force at an offset of a C# file: the last file-scoped or block namespace declared before it
// (nested block namespaces are not combined; the test sources declare one namespace per file).
function namespaceAt(namespaces, offset) {
    let current = '';
    for (const declaration of namespaces) {
        if (declaration.index > offset) {
            break;
        }
        current = declaration.name;
    }
    return current;
}

// Maps a test class, by its full name (Namespace.Class), to the collection named by its [Collection(...)] attribute.
// Attributes and preprocessor lines may stand between the attribute and the class; an attribute under #if counts in
// every build.
function readCollections(sourceDir) {
    const collections = new Map();
    const pattern = /\[[^\]]*?\bCollection\(\s*([^)]+?)\s*\)[^\]]*\]((?:(?:[ \t]*\[[^\]]*\][ \t]*\r?\n)|(?:[ \t]*#[^\r\n]*[ \t]*\r?\n)|(?:[ \t]*\r?\n))*)\s*(?:(?:public|internal|private|protected|sealed|abstract|static|partial|file)\s+)*class\s+(\w+)/g;
    for (const file of findFiles(sourceDir, '.cs', [])) {
        const text = fs.readFileSync(file, 'utf8');
        const namespaces = [...text.matchAll(/^\s*namespace\s+([\w.]+)/gm)].map((m) => ({ index: m.index, name: m[1] }));
        for (const match of text.matchAll(pattern)) {
            const collection = match[1].replace(/^TestCollections\./, '').replace(/^"(.*)"$/, '$1');
            const namespace = namespaceAt(namespaces, match.index);
            const className = namespace === '' ? match[3] : `${namespace}.${match[3]}`;
            const previous = collections.get(className);
            collections.set(className, previous !== undefined && previous !== collection ? '(ambiguous)' : collection);
        }
    }
    return collections;
}

// Rounds before splitting off the minutes, so 59.96 prints 1m00.0s rather than 60.0s.
function formatSeconds(seconds) {
    const tenths = Math.round(seconds * 10);
    if (tenths >= 600) {
        const minutes = Math.floor(tenths / 600);
        return `${minutes}m${((tenths - minutes * 600) / 10).toFixed(1).padStart(4, '0')}s`;
    }
    if (seconds >= 9.9995) {
        return `${(tenths / 10).toFixed(1)}s`;
    }
    return `${seconds.toFixed(3)}s`;
}

function addTo(groups, key, seconds, extra) {
    let group = groups.get(key);
    if (group === undefined) {
        group = { key, seconds: 0, tests: 0, classes: new Set(), failed: 0, runs: new Set(), wall: 0 };
        groups.set(key, group);
    }
    group.seconds += seconds;
    group.tests += 1;
    if (extra.className !== undefined) {
        group.classes.add(extra.className);
    }
    if (extra.failed) {
        group.failed += 1;
    }
    return group;
}

function printTable(title, headers, rows) {
    process.stdout.write(`\n${title}\n`);
    const widths = headers.map((header, column) => Math.max(header.length, ...rows.map((row) => String(row[column]).length)));
    const line = (cells) => cells
        .map((cell, column) => (column === cells.length - 1 ? String(cell) : String(cell).padStart(widths[column])))
        .join('  ');
    process.stdout.write(line(headers) + '\n');
    for (const row of rows) {
        process.stdout.write(line(row) + '\n');
    }
}

function main() {
    const options = parseArgs(process.argv.slice(2));
    const files = [];
    for (const input of options.inputs) {
        if (!fs.existsSync(input)) {
            fail(`Not found: ${input}`);
        }
        findFiles(input, '.trx', files);
    }
    if (files.length === 0) {
        fail('No .trx file found.');
    }

    const runs = files.map(readTrx);
    const collections = options.useSource && fs.existsSync(options.source) ? readCollections(options.source) : null;

    const byAssembly = new Map();
    const byClass = new Map();
    const byCollection = new Map();
    const tests = [];
    for (const run of runs) {
        const runAssemblies = new Set();
        for (const result of run.results) {
            const failed = result.outcome === 'Failed';
            const assembly = addTo(byAssembly, result.assembly, result.seconds, { className: result.className, failed });
            if (!runAssemblies.has(result.assembly)) {
                runAssemblies.add(result.assembly);
                assembly.runs.add(run.file);
                assembly.wall += run.wallSeconds;
            }
            addTo(byClass, `${result.className}|${result.assembly}`, result.seconds, { failed });
            if (collections !== null) {
                // A collection belongs to one test assembly; the same name in two assemblies is two collections
                const collection = collectionOf(collections, result.className) || '(own collection per class)';
                addTo(byCollection, `${collection} (${result.assembly})`, result.seconds, { className: result.className, failed });
            }
            tests.push(result);
        }
    }

    const total = tests.reduce((sum, t) => sum + t.seconds, 0);
    process.stdout.write(`${files.length} trx file(s), ${tests.length} test results, ${formatSeconds(total)} of test time\n`);

    const descending = (a, b) => b.seconds - a.seconds;
    printTable(
        'Per assembly (test time = sum of test durations; wall = test run start to finish, summed over its trx files)',
        ['test time', 'wall', 'tests', 'failed', 'runs', 'assembly'],
        [...byAssembly.values()].sort(descending).map((g) => [
            formatSeconds(g.seconds), formatSeconds(g.wall), g.tests, g.failed, g.runs.size, g.key,
        ])
    );

    if (collections !== null) {
        printTable(
            'Per xunit collection (from [Collection(...)] in the sources; a collection runs its tests one after another)',
            ['test time', 'tests', 'classes', 'collection'],
            [...byCollection.values()].sort(descending).map((g) => [formatSeconds(g.seconds), g.tests, g.classes.size, g.key])
        );
    }

    printTable(
        `Slowest test classes (top ${options.top})`,
        ['test time', 'tests', 'class (assembly)'],
        [...byClass.values()].sort(descending).slice(0, options.top).map((g) => {
            const [className, assembly] = g.key.split('|');
            const known = collections !== null ? collectionOf(collections, className) : undefined;
            const collection = known !== undefined ? ` [${known}]` : '';
            return [formatSeconds(g.seconds), g.tests, `${className} (${assembly})${collection}`];
        })
    );

    printTable(
        `Slowest tests (top ${options.top})`,
        ['time', 'outcome', 'test'],
        tests.sort(descending).slice(0, options.top).map((t) => [formatSeconds(t.seconds), t.outcome, t.name])
    );
}

if (require.main === module) {
    main();
} else {
    module.exports = { formatSeconds, readCollections, collectionOf };
}
