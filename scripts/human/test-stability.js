#!/usr/bin/env node
// Runs the test suite repeatedly to detect flaky tests (`just test-stability`).
// Builds once, then runs `dotnet test --no-build` per iteration with a TEST_SEED of its own (the input seed of the
// tests); the extra arguments go to `dotnet test` only, not to the build.
// Writes one log per iteration and summary.json under TestResults/<os>/<time>/.
//
// Usage:
//   node scripts/human/test-stability.js [--count <n|duration>] [--continue-on-error] [--no-shuffle]
//       [--seed <n>] [--help] [-- <dotnet-test-args>...]
//   just test-stability [count] [continue_on_error] [shuffle] [seed] [<dotnet-test-args>...]
// count is an iteration count (`100`) or a duration (`5h15m`, `1h`, `45s`).
'use strict';

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const repoRoot = path.resolve(__dirname, '..', '..');
// Same names as GSharpHostOs in Directory.Build.props.
const hostOs = { win32: 'win', linux: 'linux', darwin: 'osx' }[process.platform] || 'unix';
const cpuProject = path.join('tests', 'Compiler', 'Kassyi.GSharp.Tests.Cpu', 'Kassyi.GSharp.Tests.Cpu.csproj');
const normalFilter = 'Category!=Stress&Category!=Benchmark';

function printHelp() {
    console.log([
        'Usage: node scripts/human/test-stability.js [options] [-- <dotnet-test-args>...]',
        '',
        'Builds once, then runs the normal tests (no Stress, no Benchmark) repeatedly with a TEST_SEED per iteration.',
        'Logs and summary.json go to TestResults/<os>/<time>/.',
        '',
        'Options:',
        '  --count <n|duration>    iteration count (100) or duration (5h15m, 1h, 45s); default 10',
        '  --continue-on-error     keep going after a failed iteration (default: stop at the first failure)',
        '  --no-shuffle            do not set TEST_SEED',
        '  --seed <n>              TEST_SEED of the first iteration; iteration i uses n + i - 1 (default: random)',
        '  --help                  show this help',
        '',
        'Arguments after -- are passed to dotnet test (not to the build).',
    ].join('\n'));
}

function parseArgs(argv) {
    const o = { count: '10', continueOnError: false, shuffle: true, seed: '', extra: [], help: false };
    for (let i = 0; i < argv.length; i++) {
        const a = argv[i];
        switch (a) {
            case '--':
                o.extra = argv.slice(i + 1);
                return o;
            case '--help':
            case '-h':
                o.help = true;
                break;
            case '--count':
                o.count = String(argv[++i] ?? '');
                break;
            case '--continue-on-error':
                o.continueOnError = true;
                break;
            case '--no-shuffle':
                o.shuffle = false;
                break;
            case '--seed':
                o.seed = String(argv[++i] ?? '').trim();
                if (!/^\d+$/.test(o.seed) || Number(o.seed) > 2147483646) {
                    console.error(`Invalid --seed: '${o.seed}'. Use an integer from 0 to 2147483646.`);
                    process.exit(2);
                }
                break;
            default:
                console.error(`Unknown option: ${a}`);
                process.exit(2);
        }
    }
    return o;
}

function parseCount(text) {
    const t = text.replace(/\s+/g, '');
    if (/^\d+$/.test(t)) { return { timeMode: false, maxCount: parseInt(t, 10), limitSec: 0, text: t }; }
    const m = /^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$/.exec(t);
    const sec = m ? (+(m[1] || 0)) * 3600 + (+(m[2] || 0)) * 60 + (+(m[3] || 0)) : 0;
    if (!m || sec <= 0) {
        console.error(`Invalid count/duration format: '${text}'. Use numbers (e.g. 100) or duration (e.g. 5h30m, 1h, 45s).`);
        process.exit(1);
    }
    return { timeMode: true, maxCount: 0, limitSec: sec, text: t };
}

const pad = (n) => String(n).padStart(2, '0');
function clock(sec) {
    sec = Math.floor(sec);
    const d = Math.floor(sec / 86400), r = sec % 86400;
    const s = `${pad(Math.floor(r / 3600))}:${pad(Math.floor((r % 3600) / 60))}:${pad(r % 60)}`;
    return d > 0 ? `${d}d ${s}` : s;
}
function lap(sec) {
    const ms = Math.round(sec * 1000);
    return `${pad(Math.floor(ms / 60000))}:${pad(Math.floor(ms / 1000) % 60)}.${String(ms % 1000).padStart(3, '0')} (${sec.toFixed(2)}s)`;
}
const color = (code, text) => (process.stdout.isTTY ? `\x1b[${code}m${text}\x1b[0m` : text);

function stamp() {
    const d = new Date();
    return `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}_${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`;
}

function run(args, env, logFile) {
    // The child writes straight into the log, so it fills while the tests run.
    const fd = fs.openSync(logFile, 'a');
    try {
        const r = spawnSync('dotnet', args, { cwd: repoRoot, env, stdio: ['ignore', fd, fd] });
        return r.status === null ? 1 : r.status;
    } finally {
        fs.closeSync(fd);
    }
}

// Creates the result folder and the environment of the children.
function prepareEnvironment(ts) {
    const resultDir = path.join('TestResults', hostOs, ts);
    fs.mkdirSync(path.join(repoRoot, resultDir), { recursive: true });
    const env = { ...process.env };
    // Without nvcc on Unix, run the GpuExecution tests on the CPU project only.
    const noCuda = process.platform !== 'win32' && !env.GSHARP_TEST_NO_CUDA
        && spawnSync('nvcc', ['--version'], { stdio: 'ignore' }).status !== 0;
    if (noCuda) { env.GSHARP_TEST_NO_CUDA = '1'; }
    return { resultDir, env, noCuda };
}

// Runs the dotnet test commands of one iteration; returns the first non-zero status, or 0.
function runTests(env, noCuda, extra, logFile) {
    if (noCuda && extra.length === 0) {
        const first = ['test', '--no-build', '--filter', `${normalFilter}&Category!=GpuExecution`];
        const status = run(first, env, logFile);
        if (status !== 0) { return status; }
        const second = ['test', cpuProject, '--no-build', '--filter', `Category=GpuExecution&${normalFilter}`];
        return run(second, env, logFile);
    }
    return run(['test', '--no-build', '--filter', normalFilter, ...extra], env, logFile);
}

// Runs iteration i; returns { status, seed, seedInfo, lapSec, logName }.
function runIteration(i, ctx) {
    const { opts, target, env, noCuda, resultDir, start } = ctx;
    const elapsed = (Date.now() - start) / 1000;
    let seed = null;
    const iterEnv = { ...env };
    delete iterEnv.TEST_SEED;
    if (opts.shuffle) {
        seed = opts.seed !== '' ? parseInt(opts.seed, 10) + i - 1 : Math.floor(Math.random() * 2147483646) + 1;
        iterEnv.TEST_SEED = String(seed);
    }
    const seedInfo = seed === null ? '' : ` (Seed: ${seed})`;
    const progress = target.timeMode
        ? `[${i}] (Elapsed: ${clock(elapsed)} / ${clock(target.limitSec)})`
        : `[${i}/${target.maxCount}]`;
    console.log(color(36, `${progress} Running test iteration${seedInfo}...`));

    const logName = `stability_iter_${i}.log`;
    const logFile = path.join(repoRoot, resultDir, logName);
    fs.writeFileSync(logFile, seed === null
        ? `=== Stability Test Iteration ${i} (Shuffle: disabled) ===\n`
        : `=== Stability Test Iteration ${i} (Seed: ${seed}) ===\n`);

    const lapStart = Date.now();
    const status = runTests(iterEnv, noCuda, opts.extra, logFile);
    return { status, seed, seedInfo, lapSec: (Date.now() - lapStart) / 1000, logName };
}

function writeSummary(ctx) {
    const { target, failed, executed } = ctx;
    const summary = {
        timestamp: ctx.ts,
        mode: target.timeMode ? 'duration' : 'count',
        target: target.text,
        executed,
        passed: executed - failed.length,
        failed: failed.length,
        totalElapsedSeconds: Math.round((Date.now() - ctx.start) / 10) / 100,
        failedIterations: failed,
    };
    fs.writeFileSync(path.join(repoRoot, ctx.resultDir, 'summary.json'), JSON.stringify(summary, null, 2) + '\n');
}

function recordFailure(ctx, i, result) {
    const { seed, seedInfo, lapSec, logName } = result;
    ctx.failed.push({
        iteration: i,
        seed: seed === null ? null : String(seed),
        elapsedSeconds: Math.round(lapSec * 100) / 100,
        log: logName,
    });
    writeSummary(ctx);
    const logPath = path.join(ctx.resultDir, logName);
    const message = `Test failed on iteration ${i}${seedInfo} after ${lap(lapSec)}! See ${logPath} for details.`;
    console.log(color(31, message));
}

function printTotal(ctx) {
    const { failed, executed, resultDir } = ctx;
    const total = lap((Date.now() - ctx.start) / 1000);
    if (failed.length > 0) {
        console.log(color(31, `Finished in ${total} (${executed} iterations), but some iterations failed. `
            + `See ${resultDir} for logs & summary.json.`));
        return 1;
    }
    console.log(color(32, `All ${executed} iterations passed successfully in ${total}. `
        + `Results saved to ${resultDir}.`));
    return 0;
}

function main() {
    const opts = parseArgs(process.argv.slice(2));
    if (opts.help) { printHelp(); return 0; }
    const target = parseCount(opts.count);
    const ts = stamp();
    const { resultDir, env, noCuda } = prepareEnvironment(ts);

    const modeDesc = target.timeMode
        ? `Time-based (${target.text} / ${clock(target.limitSec)})`
        : `${target.maxCount} iterations`;
    console.log(color(36, `Stability test mode: ${modeDesc}`));
    console.log(color(36, `Stability test output directory: ${resultDir}`));
    console.log(color(33, 'Building project once before stability tests...'));
    const build = spawnSync('dotnet', ['build'], { cwd: repoRoot, env, stdio: 'inherit' });
    if (build.status !== 0) { return build.status || 1; }

    const ctx = { opts, target, env, noCuda, ts, resultDir, failed: [], executed: 0, start: Date.now() };
    for (let i = 1; ; i++) {
        const elapsed = (Date.now() - ctx.start) / 1000;
        if (!target.timeMode && i > target.maxCount) { break; }
        if (target.timeMode && i > 1 && elapsed >= target.limitSec) { break; }

        const result = runIteration(i, ctx);
        ctx.executed = i;
        if (result.status === 0) {
            console.log(color(32, `Iteration ${i}${result.seedInfo} passed in ${lap(result.lapSec)}`));
            continue;
        }
        recordFailure(ctx, i, result);
        if (!opts.continueOnError) { return result.status; }
        console.log(color(33, 'Continuing to next iteration (--continue-on-error is set)...'));
    }

    writeSummary(ctx);
    return printTotal(ctx);
}

process.exit(main());
