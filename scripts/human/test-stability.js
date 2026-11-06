#!/usr/bin/env node
// Runs the test suite repeatedly to detect flaky tests (`just test-stability`).
// Builds once, then runs `dotnet test --no-build` per iteration with a TEST_SEED of its own (the input seed of the tests),
// writes one log per iteration and summary.json under TestResults/<os>/<time>/.
//
// Usage:
//   node scripts/human/test-stability.js [--count <n|duration>] [--continue-on-error] [--no-shuffle] [--seed <n>] [--help] [-- <dotnet-test-args>...]
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
        'Arguments after -- are passed to dotnet build and dotnet test.',
    ].join('\n'));
}

function parseArgs(argv) {
    const o = { count: '10', continueOnError: false, shuffle: true, seed: '', extra: [], help: false };
    for (let i = 0; i < argv.length; i++) {
        const a = argv[i];
        if (a === '--') { o.extra = argv.slice(i + 1); break; }
        else if (a === '--help' || a === '-h') { o.help = true; }
        else if (a === '--count') { o.count = String(argv[++i] ?? ''); }
        else if (a === '--continue-on-error') { o.continueOnError = true; }
        else if (a === '--no-shuffle') { o.shuffle = false; }
        else if (a === '--seed') { o.seed = String(argv[++i] ?? ''); }
        else { console.error(`Unknown option: ${a}`); process.exit(2); }
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
    const r = spawnSync('dotnet', args, { cwd: repoRoot, env, encoding: 'utf8', maxBuffer: 1 << 30 });
    fs.appendFileSync(logFile, `${r.stdout || ''}${r.stderr || ''}`);
    return r.status === null ? 1 : r.status;
}

function main() {
    const opts = parseArgs(process.argv.slice(2));
    if (opts.help) { printHelp(); return 0; }
    const target = parseCount(opts.count);
    const ts = stamp();
    const resultDir = path.join('TestResults', hostOs, ts);
    fs.mkdirSync(path.join(repoRoot, resultDir), { recursive: true });

    const env = { ...process.env };
    // Without nvcc on Unix, run the GpuExecution tests on the CPU project only.
    const noCuda = process.platform !== 'win32' && !env.GSHARP_TEST_NO_CUDA
        && spawnSync('nvcc', ['--version'], { stdio: 'ignore' }).status !== 0;
    if (noCuda) { env.GSHARP_TEST_NO_CUDA = '1'; }

    const modeDesc = target.timeMode ? `Time-based (${target.text} / ${clock(target.limitSec)})` : `${target.maxCount} iterations`;
    console.log(color(36, `Stability test mode: ${modeDesc}`));
    console.log(color(36, `Stability test output directory: ${resultDir}`));
    console.log(color(33, 'Building project once before stability tests...'));
    const build = spawnSync('dotnet', ['build', ...opts.extra], { cwd: repoRoot, env, stdio: 'inherit' });
    if (build.status !== 0) { return build.status || 1; }

    const failed = [];
    const start = Date.now();
    let executed = 0;
    const writeSummary = () => {
        const summary = {
            timestamp: ts,
            mode: target.timeMode ? 'duration' : 'count',
            target: target.text,
            executed,
            passed: executed - failed.length,
            failed: failed.length,
            totalElapsedSeconds: Math.round((Date.now() - start) / 10) / 100,
            failedIterations: failed,
        };
        fs.writeFileSync(path.join(repoRoot, resultDir, 'summary.json'), JSON.stringify(summary, null, 2) + '\n');
    };

    for (let i = 1; ; i++) {
        const elapsed = (Date.now() - start) / 1000;
        if (!target.timeMode && i > target.maxCount) { break; }
        if (target.timeMode && i > 1 && elapsed >= target.limitSec) { break; }

        let seed = null;
        const iterEnv = { ...env };
        delete iterEnv.TEST_SEED;
        if (opts.shuffle) {
            seed = opts.seed !== '' ? parseInt(opts.seed, 10) + i - 1 : Math.floor(Math.random() * 2147483646) + 1;
            iterEnv.TEST_SEED = String(seed);
        }
        const seedInfo = seed === null ? '' : ` (Seed: ${seed})`;
        const progress = target.timeMode ? `[${i}] (Elapsed: ${clock(elapsed)} / ${clock(target.limitSec)})` : `[${i}/${target.maxCount}]`;
        console.log(color(36, `${progress} Running test iteration${seedInfo}...`));

        const logName = `stability_iter_${i}.log`;
        const logFile = path.join(repoRoot, resultDir, logName);
        fs.writeFileSync(logFile, seed === null
            ? `=== Stability Test Iteration ${i} (Shuffle: disabled) ===\n`
            : `=== Stability Test Iteration ${i} (Seed: ${seed}) ===\n`);

        const lapStart = Date.now();
        let status;
        if (noCuda && opts.extra.length === 0) {
            status = run(['test', '--no-build', '--filter', `${normalFilter}&Category!=GpuExecution`], iterEnv, logFile);
            if (status === 0) {
                status = run(['test', cpuProject, '--no-build', '--filter', `Category=GpuExecution&${normalFilter}`], iterEnv, logFile);
            }
        } else {
            status = run(['test', '--no-build', '--filter', normalFilter, ...opts.extra], iterEnv, logFile);
        }
        const lapSec = (Date.now() - lapStart) / 1000;
        executed = i;

        if (status !== 0) {
            failed.push({ iteration: i, seed: seed === null ? null : String(seed), elapsedSeconds: Math.round(lapSec * 100) / 100, log: logName });
            writeSummary();
            console.log(color(31, `Test failed on iteration ${i}${seedInfo} after ${lap(lapSec)}! See ${path.join(resultDir, logName)} for details.`));
            if (!opts.continueOnError) { return status; }
            console.log(color(33, 'Continuing to next iteration (--continue-on-error is set)...'));
        } else {
            console.log(color(32, `Iteration ${i}${seedInfo} passed in ${lap(lapSec)}`));
        }
    }

    writeSummary();
    const total = lap((Date.now() - start) / 1000);
    if (failed.length > 0) {
        console.log(color(31, `Finished in ${total} (${executed} iterations), but some iterations failed. See ${resultDir} for logs & summary.json.`));
        return 1;
    }
    console.log(color(32, `All ${executed} iterations passed successfully in ${total}. Results saved to ${resultDir}.`));
    return 0;
}

process.exit(main());
