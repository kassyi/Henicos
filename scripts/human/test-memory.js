#!/usr/bin/env node
// Runs one test project once to see where its test host memory goes (`just test-memory`).
// Usage: node scripts/human/test-memory.js <project> [--filter <expr>] [-c <configuration>] [--no-build]
//                                          [--keep-budget] [-- <dotnet test args>...]
//   The run sets GSHARP_TEST_MEMORY_REPORT (peaks and the tests that raised the peak, written by the final memory check
//   of the assembly), GSHARP_TEST_MEMORY_DUMP_AT_END=1 (a dotnet-gcdump of what the run left on the heap) and, unless
//   --keep-budget is given, a budget too large to breach. While the tests run, dotnet-counters samples the test host
//   into counters.csv. The diagnostic tools run one-shot through `dotnet dnx` at a pinned version.
//   Output: tmp/test-memory/run-<project>-<time>/ (test.log, <assembly>.tsv, <assembly>.top.tsv, counters.csv, the
//   gcdump and its report). A summary is printed at the end. GPU suites: run it under scripts/agent/gpu-lock.js.
'use strict';

const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const repoRoot = path.resolve(__dirname, '..', '..');
const toolVersion = '10.0.750501';
const pidWaitMilliseconds = 500;
const countersStopMilliseconds = 30000;

const helpText = [
    'Usage: node scripts/human/test-memory.js <project> [--filter <expr>] [-c <configuration>] [--no-build]',
    '                                         [--keep-budget] [-- <dotnet test args>...]',
    '',
    '  <project>       A test project: a path to its .csproj or its directory, or its name (found under tests/).',
    '  --filter        The dotnet test filter. Default: Category!=Stress&Category!=Benchmark&Category!=GpuTrap (as `just test`).',
    '  -c              Build configuration. Default: Debug.',
    '  --no-build      Pass --no-build to dotnet test.',
    '  --keep-budget   Keep the budget of tests/memory-budgets.yml (a breach then fails tests and writes a snapshot).',
    '  --help          Show this help.',
].join('\n');

function fail(message) {
    process.stderr.write(message + '\n');
    process.exit(1);
}

function parseArgs(argv) {
    const options = {
        project: null,
        filter: 'Category!=Stress&Category!=Benchmark&Category!=GpuTrap',
        configuration: 'Debug',
        noBuild: false,
        keepBudget: false,
        extra: [],
    };
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === '--help' || arg === '-h') {
            process.stdout.write(helpText + '\n');
            process.exit(0);
        } else if (arg === '--') {
            options.extra = argv.slice(i + 1);
            break;
        } else if (arg === '--filter' || arg === '-c') {
            const value = argv[++i];
            if (value === undefined) {
                fail(`${arg} needs a value.\n\n${helpText}`);
            }
            options[arg === '-c' ? 'configuration' : 'filter'] = value;
        } else if (arg === '--no-build') {
            options.noBuild = true;
        } else if (arg === '--keep-budget') {
            options.keepBudget = true;
        } else if (!arg.startsWith('-') && options.project === null) {
            options.project = arg;
        } else {
            fail(`Unknown argument: ${arg}\n\n${helpText}`);
        }
    }
    if (options.project === null) {
        fail(`A test project is required.\n\n${helpText}`);
    }
    return options;
}

/** Resolves a .csproj path, a project directory or a project name found under tests/. */
function resolveProject(project) {
    const direct = path.resolve(project);
    if (fs.existsSync(direct) && fs.statSync(direct).isFile()) {
        return direct;
    }
    if (fs.existsSync(direct) && fs.statSync(direct).isDirectory()) {
        const name = path.basename(direct);
        const csproj = path.join(direct, `${name}.csproj`);
        if (fs.existsSync(csproj)) {
            return csproj;
        }
    }
    const found = [];
    const walk = (dir) => {
        for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
            if (entry.isDirectory() && entry.name !== 'bin' && entry.name !== 'obj') {
                walk(path.join(dir, entry.name));
            } else if (entry.isFile() && entry.name === `${project}.csproj`) {
                found.push(path.join(dir, entry.name));
            }
        }
    };
    walk(path.join(repoRoot, 'tests'));
    if (found.length !== 1) {
        fail(`No single test project named '${project}' under tests/ (found ${found.length}).`);
    }
    return found[0];
}

function timestamp() {
    const d = new Date();
    const two = (n) => String(n).padStart(2, '0');
    const date = `${d.getFullYear()}${two(d.getMonth() + 1)}${two(d.getDate())}`;
    return `${date}-${two(d.getHours())}${two(d.getMinutes())}${two(d.getSeconds())}`;
}

/** Starts dotnet-counters on the test host once its process id file appears; returns a handle to stop it. */
function startCounters(pidFile, csv, log) {
    const state = { child: null, timer: null };
    const tryStart = () => {
        if (!fs.existsSync(pidFile)) {
            state.timer = setTimeout(tryStart, pidWaitMilliseconds);
            return;
        }
        const pid = fs.readFileSync(pidFile, 'utf8').trim();
        const args = [
            'dnx', `dotnet-counters@${toolVersion}`, '--yes', '--',
            'collect', '-p', pid, '--format', 'csv', '-o', csv,
            '--refresh-interval', '1', '--counters', 'System.Runtime',
        ];
        state.child = spawn('dotnet', args, { cwd: repoRoot, stdio: ['ignore', log, log] });
    };
    tryStart();
    return state;
}

function stopCounters(state) {
    clearTimeout(state.timer);
    const child = state.child;
    if (child === null || child.exitCode !== null) {
        return Promise.resolve();
    }
    // dotnet-counters ends by itself when the test host exits; stop it only when it does not
    return new Promise((resolve) => {
        const timer = setTimeout(() => {
            child.kill();
            resolve();
        }, countersStopMilliseconds);
        child.on('exit', () => {
            clearTimeout(timer);
            resolve();
        });
    });
}

function readLines(file) {
    return fs.existsSync(file) ? fs.readFileSync(file, 'utf8').split(/\r?\n/).filter((l) => l.length > 0) : [];
}

/** Largest value per counter whose name looks like memory, from a dotnet-counters csv. */
function counterMaxima(csv) {
    const maxima = new Map();
    for (const line of readLines(csv).slice(1)) {
        const cells = line.split(',');
        const name = cells[2];
        const value = Number(cells[cells.length - 1]);
        if (name !== undefined && /memory|heap|working|committed|fragment/i.test(name) && Number.isFinite(value)) {
            maxima.set(name, Math.max(maxima.get(name) ?? 0, value));
        }
    }
    return maxima;
}

function printSummary(outDir, assembly) {
    const out = [];
    const summary = readLines(path.join(outDir, `${assembly}.tsv`)).pop();
    if (summary === undefined) {
        out.push('No peak report: the final memory check did not run (filtered out, or the host died).');
    } else {
        const [peak, gcPeak, budget, tests, profile, metric] = summary.split('\t');
        out.push(`Peak: ${metric} ${peak} MB, GC heap peak ${gcPeak} MB, `
            + `budget ${budget} MB (${profile} profile), ${tests} tests`);
    }
    const top = readLines(path.join(outDir, `${assembly}.top.tsv`)).filter((l) => !l.startsWith('#'));
    if (top.length > 0) {
        out.push('', 'Tests that raised the peak most (growth MB, GC heap MB at their end):');
        for (const line of top.slice(0, 10)) {
            const [growth, gc, name] = line.split('\t');
            out.push(`  +${growth.padStart(5)}  ${gc.padStart(5)}  ${name}`);
        }
    }
    const maxima = counterMaxima(path.join(outDir, 'counters.csv'));
    if (maxima.size > 0) {
        out.push('', 'dotnet-counters maxima:');
        for (const [name, value] of maxima) {
            out.push(`  ${name}: ${value}`);
        }
    }
    const snapshot = readLines(path.join(outDir, `${assembly}.snapshot.txt`)).join(' ');
    if (snapshot.length > 0) {
        out.push('', snapshot);
        const report = /types by size: (.+\.report\.txt)\./.exec(snapshot);
        if (report !== null) {
            out.push('', 'Heap at the end of the run (dotnet-gcdump report, first lines):');
            for (const line of readLines(report[1]).slice(0, 25)) {
                out.push(`  ${line}`);
            }
        }
    }
    out.push('', `Files: ${outDir}`);
    process.stdout.write(out.join('\n') + '\n');
}

async function main() {
    const options = parseArgs(process.argv.slice(2));
    const project = resolveProject(options.project);
    const assembly = path.basename(project, '.csproj');
    const outDir = path.join(repoRoot, 'tmp', 'test-memory', `run-${assembly}-${timestamp()}`);
    fs.mkdirSync(outDir, { recursive: true });

    const env = {
        ...process.env,
        DiffEngine_Disabled: 'true',
        GSHARP_TEST_MEMORY_REPORT: outDir,
        GSHARP_TEST_MEMORY_DUMP_AT_END: '1',
    };
    if (!options.keepBudget) {
        env.GSHARP_TEST_MEMORY_BUDGET_MB = '1000000';
    }

    const args = ['test', project, '-c', options.configuration, '--filter', options.filter];
    if (options.noBuild) {
        args.push('--no-build');
    }
    args.push(...options.extra);

    const log = fs.openSync(path.join(outDir, 'test.log'), 'w');
    const countersLog = fs.openSync(path.join(outDir, 'counters.log'), 'w');
    process.stdout.write(`dotnet ${args.join(' ')}\n(log: ${path.join(outDir, 'test.log')})\n`);
    const pidFile = path.join(outDir, `${assembly}.pid`);
    const counters = startCounters(pidFile, path.join(outDir, 'counters.csv'), countersLog);
    const exitCode = await new Promise((resolve) => {
        const child = spawn('dotnet', args, { cwd: repoRoot, env, stdio: ['ignore', log, log] });
        child.on('exit', (code) => resolve(code ?? 1));
    });
    await stopCounters(counters);
    fs.closeSync(log);
    fs.closeSync(countersLog);

    process.stdout.write(`dotnet test exited with ${exitCode}\n\n`);
    printSummary(outDir, assembly);
    process.exit(exitCode);
}

main();
