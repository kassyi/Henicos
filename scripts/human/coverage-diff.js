#!/usr/bin/env node
// Lists the lines added or changed since a base ref that no test executed.
// Usage: node scripts/human/coverage-diff.js [--run] [--base <ref>] [--coverage <file|dir>]
//   --run clears the coverage directory and runs the tests with coverage.runsettings first (`just coverage-diff`).
//   The diff is taken from the merge base with <ref> (default main) to the working tree, plus untracked files.
//   Coverage comes from every coverage.cobertura.xml under <dir> (default TestResults/coverage), merged across the
//   test projects. A changed line counts only when cobertura lists it; lines it does not list (declarations, comments,
//   generated or unmeasured files) are left out.
'use strict';

const fs = require('fs');
const path = require('path');
const { execFileSync, spawnSync } = require('child_process');

const repoRoot = path.resolve(__dirname, '..', '..');

const helpText = [
    'Usage: node scripts/human/coverage-diff.js [--run] [--base <ref>] [--coverage <file|dir>]',
    '',
    '  --run        Clear the coverage directory and run the tests (no Stress / Benchmark) with coverage first.',
    '  --base       Ref to diff against, through its merge base with HEAD. Default: main.',
    '  --coverage   A cobertura file, or a directory searched for coverage.cobertura.xml. Default: TestResults/coverage.',
    '  --help       Show this help.',
].join('\n');

function fail(message) {
    process.stderr.write(message + '\n');
    process.exit(1);
}

function parseArgs(argv) {
    const options = { run: false, base: 'main', coverage: path.join(repoRoot, 'TestResults', 'coverage') };
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === '--help' || arg === '-h') {
            process.stdout.write(helpText + '\n');
            process.exit(0);
        } else if (arg === '--run') {
            options.run = true;
        } else if (arg === '--base' || arg === '--coverage') {
            const value = argv[++i];
            if (value === undefined) {
                fail(`${arg} needs a value.\n\n${helpText}`);
            }
            options[arg.slice(2)] = arg === '--coverage' ? path.resolve(value) : value;
        } else {
            fail(`Unknown argument: ${arg}\n\n${helpText}`);
        }
    }
    return options;
}

/** Runs the tests into a fresh coverage directory; returns the dotnet test exit code. */
function runTests(directory) {
    fs.rmSync(directory, { recursive: true, force: true });
    const args = [
        'test',
        '-s', path.join(repoRoot, 'coverage.runsettings'),
        '--filter', 'Category!=Stress&Category!=Benchmark',
        '--results-directory', directory,
    ];
    const env = { ...process.env, DiffEngine_Disabled: 'true' };
    const result = spawnSync('dotnet', args, { cwd: repoRoot, env, stdio: 'inherit' });
    if (result.error) {
        fail(`Cannot run dotnet test: ${result.error.message}`);
    }
    // A null status means the process was killed by a signal.
    return result.status ?? 1;
}

function git(args) {
    return execFileSync('git', args, { cwd: repoRoot, encoding: 'utf8', maxBuffer: 256 * 1024 * 1024 });
}

// Paths are compared repository-relative, with forward slashes, case-insensitively on Windows.
function key(file) {
    const relative = path.relative(repoRoot, path.resolve(repoRoot, file)).split(path.sep).join('/');
    return process.platform === 'win32' ? relative.toLowerCase() : relative;
}

/** Changed lines per file: Map<key, { file, lines: Set<number> }>. */
function changedLines(base) {
    let mergeBase;
    try {
        mergeBase = git(['merge-base', base, 'HEAD']).trim();
    } catch (error) {
        fail(`Cannot find the merge base of ${base} and HEAD: ${error.message}`);
    }

    const changes = new Map();
    const add = (file, line) => {
        const k = key(file);
        if (!changes.has(k)) {
            changes.set(k, { file, lines: new Set() });
        }
        changes.get(k).lines.add(line);
    };

    let file = null;
    for (const line of git(['diff', '-U0', '--no-color', '--no-ext-diff', mergeBase]).split('\n')) {
        if (line.startsWith('+++ ')) {
            const target = line.slice(4).trim();
            file = target === '/dev/null' ? null : target.replace(/^b\//, '');
        } else if (file !== null && line.startsWith('@@')) {
            // @@ -a,b +c,d @@: d lines starting at c are new; d = 0 is a pure deletion.
            const match = /^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@/.exec(line);
            if (match) {
                const start = Number(match[1]);
                const count = match[2] === undefined ? 1 : Number(match[2]);
                for (let n = start; n < start + count; n++) {
                    add(file, n);
                }
            }
        }
    }

    for (const untracked of git(['ls-files', '--others', '--exclude-standard']).split('\n')) {
        if (!untracked) {
            continue;
        }
        const lineCount = fs.readFileSync(path.join(repoRoot, untracked), 'utf8').split('\n').length;
        for (let n = 1; n <= lineCount; n++) {
            add(untracked, n);
        }
    }
    return changes;
}

function findCoberturaFiles(target) {
    if (!fs.existsSync(target)) {
        fail(`No coverage found at ${target}. Run \`just coverage-diff\` or pass --coverage.`);
    }
    if (fs.statSync(target).isFile()) {
        return [target];
    }
    const found = [];
    const walk = dir => {
        for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
            const full = path.join(dir, entry.name);
            if (entry.isDirectory()) {
                walk(full);
            } else if (entry.name === 'coverage.cobertura.xml') {
                found.push(full);
            }
        }
    };
    walk(target);
    if (found.length === 0) {
        fail(`No coverage.cobertura.xml under ${target}.`);
    }
    return found;
}

/** Hits per line, summed over every report and class: Map<key, Map<line, hits>>. */
function readCoverage(files) {
    const coverage = new Map();
    for (const file of files) {
        const xml = fs.readFileSync(file, 'utf8');
        const sources = [...xml.matchAll(/<source>([^<]*)<\/source>/g)].map(m => m[1]);
        const classes = /<class\b[^>]*\bfilename="([^"]*)"[^>]*>([\s\S]*?)<\/class>/g;
        for (const [, filename, body] of xml.matchAll(classes)) {
            const absolute = path.isAbsolute(filename) || sources.length === 0
                ? filename
                : path.join(sources[0], filename);
            const k = key(absolute);
            if (!coverage.has(k)) {
                coverage.set(k, new Map());
            }
            const hits = coverage.get(k);
            for (const [, number, count] of body.matchAll(/<line\b[^>]*\bnumber="(\d+)"[^>]*\bhits="(\d+)"/g)) {
                const n = Number(number);
                hits.set(n, (hits.get(n) || 0) + Number(count));
            }
        }
    }
    return coverage;
}

// 3,4,5,9 -> "3-5,9"
function ranges(lines) {
    const parts = [];
    for (let i = 0; i < lines.length; i++) {
        let j = i;
        while (j + 1 < lines.length && lines[j + 1] === lines[j] + 1) {
            j++;
        }
        parts.push(i === j ? `${lines[i]}` : `${lines[i]}-${lines[j]}`);
        i = j;
    }
    return parts.join(',');
}

function main() {
    const options = parseArgs(process.argv.slice(2));
    const testStatus = options.run ? runTests(options.coverage) : 0;
    const changes = changedLines(options.base);
    const reports = findCoberturaFiles(options.coverage);
    const coverage = readCoverage(reports);

    let measured = 0;
    let uncovered = 0;
    const report = [];
    const unmeasured = [];
    for (const [k, { file, lines }] of [...changes].sort((a, b) => a[0].localeCompare(b[0]))) {
        const hits = coverage.get(k);
        if (hits === undefined) {
            // Test assemblies are never instrumented, so only product sources are worth listing.
            if (file.startsWith('src/') && file.endsWith('.cs')) {
                unmeasured.push(file);
            }
            continue;
        }
        const executable = [...lines].filter(n => hits.has(n)).sort((a, b) => a - b);
        measured += executable.length;
        const zero = executable.filter(n => hits.get(n) === 0);
        uncovered += zero.length;
        if (zero.length > 0) {
            report.push(`${file}:${ranges(zero)}`);
        }
    }

    process.stdout.write(`Coverage reports: ${reports.length}\n`);
    process.stdout.write(`Changed executable lines: ${measured}, not executed: ${uncovered}\n`);
    if (report.length > 0) {
        process.stdout.write('\nNot executed:\n' + report.map(line => `  ${line}\n`).join(''));
    }
    if (unmeasured.length > 0) {
        process.stdout.write('\nChanged .cs files with no coverage data (no executable lines, or an assembly no test loads):\n' + unmeasured.map(f => `  ${f}\n`).join(''));
    }
    if (testStatus !== 0) {
        process.stderr.write(`\ndotnet test failed (exit code ${testStatus}); the coverage covers only the tests that ran.\n`);
        process.exit(testStatus);
    }
}

main();
