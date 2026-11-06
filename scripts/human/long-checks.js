#!/usr/bin/env node
// Runs the long checks that need no one at the keyboard, one after another, and writes one summary:
// benchmark baselines (generator, compute, GPU Pi), the differential fuzzer, the sanitized native tests (on Windows
// also inside WSL, for UBSan), the test-stability loop, the tests with coverage and the Stryker.NET mutation runs.
// The whole set takes about a day; pick a subset with --steps. Steps that use the GPU run under
// scripts/agent/gpu-lock.js (the fuzzer takes the lock itself). A failing step is recorded and the run goes on.
//
// Outputs elsewhere: fuzzer findings in tmp/differential-fuzz, sanitizer logs in tmp/test-sanitized.log, test-stability
// logs in TestResults/<os>/<time>, coverage reports in TestResults, mutation reports in tmp/stryker/<phase>.
//
// Output: tmp/long-checks/<yyyyMMdd-HHmmss>-<commit>/
//   <step>.log            full output of the step
//   bench/generator/      BenchmarkDotNet reports of the generator benchmark (input of `just bench-compare`)
//   summary.md            one row per step: exit code, duration, log
//
// Usage:
//   node scripts/human/long-checks.js [--steps <list>] [--fuzz-minutes <n>] [--stability <count|duration>]
//                                        [--pi-digits <n>] [--dry-run] [--help]
//   just long-checks [args...]

'use strict';

const fs = require('fs');
const path = require('path');
const { spawn, spawnSync } = require('child_process');

const repoRoot = path.resolve(__dirname, '..', '..');
const gpuLock = path.join(repoRoot, 'scripts', 'agent', 'gpu-lock.js');
const benchmarksProject = path.join('src', 'Apps', 'Henicos.Benchmarks');

// The comments give the measured time of each step, for reference only: one Windows 11 machine with an NVIDIA GPU,
// default options, 2026-11. The whole set took about 22h20m.
const allSteps = [
  'bench-generator', // 1m56s
  'bench-compute', // 1m05s
  'bench-pi', // 34s (1M digits)
  'fuzz', // 2h00m (--fuzz-minutes 120)
  'sanitized', // 3m10s to 6m35s, depending on the build
  'sanitized-wsl', // 7m44s (stopped at the first UBSan report, #1058)
  'stability', // 3h04m (--stability 3h)
  'coverage', // 3m11s
  'mutate-compiler', // 16h50m (measured at concurrency 6, now 10; about 830 mutants an hour)
  'mutate-gpumath', // not measured: the run stops within minutes (#1191)
  'mutate-compiler-cuda', // 1h15m (build 2m13s)
];

// P:\a\b -> /mnt/p/a/b, for running the same checkout inside WSL.
function toWslPath(windowsPath) {
  const match = /^([A-Za-z]):[\\/](.*)$/.exec(windowsPath);
  if (!match) throw new Error(`cannot map ${windowsPath} to a WSL path`);
  return `/mnt/${match[1].toLowerCase()}/${match[2].replace(/\\/g, '/')}`;
}

const usage = [
  'Usage: node scripts/human/long-checks.js [options]',
  '',
  `  --steps <list>                Comma-separated steps to run, in this order (default: ${allSteps.join(',')}).`,
  '  --fuzz-minutes <n>            Minutes of differential fuzzing on all paths, CUDA included (default: 120).',
  '  --stability <count|duration>  Argument of `just test-stability`: a count (10) or a duration (3h, 1h30m) (default: 3h).',
  '  --pi-digits <n>               Digits for the GPU Pi benchmark (default: 1M).',
  '  --dry-run                     Print the commands without running them.',
  '  --help, -h                    Show this help.',
].join('\n');

function parseArgs(argv) {
  const options = { steps: allSteps, fuzzMinutes: '120', stability: '3h', piDigits: '1M', dryRun: false };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    const value = () => {
      if (i + 1 >= argv.length) throw new Error(`${arg} needs a value`);
      return argv[++i];
    };
    if (arg === '--help' || arg === '-h') {
      options.help = true;
    } else if (arg === '--steps') {
      options.steps = value().split(',').map((s) => s.trim()).filter(Boolean);
    } else if (arg === '--fuzz-minutes') {
      options.fuzzMinutes = value();
    } else if (arg === '--stability') {
      options.stability = value();
    } else if (arg === '--pi-digits') {
      options.piDigits = value();
    } else if (arg === '--dry-run') {
      options.dryRun = true;
    } else {
      throw new Error(`unknown option ${arg}`);
    }
  }
  const unknown = options.steps.filter((s) => !allSteps.includes(s));
  if (unknown.length > 0) throw new Error(`unknown step(s): ${unknown.join(', ')}`);
  if (!/^\d+$/.test(options.fuzzMinutes)) throw new Error('--fuzz-minutes must be a whole number');
  return options;
}

function underGpuLock(command, args) {
  return { command: process.execPath, args: [gpuLock, '--', command, ...args] };
}

// Each step: the command it runs and whether it needs the GPU (shown in the summary).
function buildSteps(options, outDir) {
  const benchRun = (...args) => ['run', '--project', benchmarksProject, '-c', 'Release', '--', ...args];
  return {
    'bench-generator': {
      gpu: false,
      command: 'dotnet',
      args: benchRun('generator-bench', '--artifacts', path.join(outDir, 'bench', 'generator')),
    },
    'bench-compute': { gpu: true, ...underGpuLock('dotnet', benchRun('compute-bench', 'all', '--json')) },
    'bench-pi': { gpu: true, ...underGpuLock('dotnet', benchRun('gpu-pi', options.piDigits)) },
    fuzz: {
      gpu: true,
      command: process.execPath,
      args: [
        path.join('scripts', 'human', 'fuzz-differential.js'),
        '--profile', 'all',
        '--minutes', options.fuzzMinutes,
        '--metamorphic',
      ],
    },
    sanitized: { gpu: false, command: process.execPath, args: [path.join('scripts', 'human', 'test-sanitized.js')] },
    // Linux adds UndefinedBehaviorSanitizer, which MSVC does not have. Windows only; the checkout is shared through /mnt.
    'sanitized-wsl': {
      gpu: false,
      windowsOnly: true,
      command: 'wsl.exe',
      args: process.platform === 'win32'
        ? ['--', 'bash', '-lc', `cd '${toWslPath(repoRoot)}' && node scripts/human/test-sanitized.js`]
        : [],
    },
    stability: { gpu: true, ...underGpuLock('just', ['test-stability', options.stability, 'true']) },
    coverage: { gpu: true, ...underGpuLock('just', ['test-coverage']) },
    // Stryker.NET reports land in tmp/stryker/<phase>; each phase runs for hours.
    'mutate-compiler': { gpu: false, command: 'just', args: ['mutate', 'compiler'] },
    'mutate-gpumath': { gpu: true, ...underGpuLock('just', ['mutate', 'gpumath']) },
    'mutate-compiler-cuda': { gpu: true, ...underGpuLock('just', ['mutate', 'compiler-cuda']) },
  };
}

function timestamp(date) {
  const pad = (n) => String(n).padStart(2, '0');
  return `${date.getFullYear()}${pad(date.getMonth() + 1)}${pad(date.getDate())}-` +
    `${pad(date.getHours())}${pad(date.getMinutes())}${pad(date.getSeconds())}`;
}

function formatDuration(ms) {
  const totalSeconds = Math.round(ms / 1000);
  const h = Math.floor(totalSeconds / 3600);
  const m = Math.floor((totalSeconds % 3600) / 60);
  const s = totalSeconds % 60;
  return h > 0 ? `${h}h${String(m).padStart(2, '0')}m` : `${m}m${String(s).padStart(2, '0')}s`;
}

function describe(step) {
  return [step.command === process.execPath ? 'node' : step.command, ...step.args].join(' ');
}

function runStep(name, step, logPath) {
  return new Promise((resolve) => {
    const log = fs.createWriteStream(logPath);
    log.write(`$ ${describe(step)}\n\n`);
    const started = Date.now();
    const child = spawn(step.command, step.args, {
      cwd: repoRoot,
      env: { ...process.env, DiffEngine_Disabled: 'true', CI: 'true' },
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    const forward = (chunk) => {
      log.write(chunk);
      process.stdout.write(chunk);
    };
    child.stdout.on('data', forward);
    child.stderr.on('data', forward);
    const finish = (code, error) => {
      if (error) log.write(`\n[long-checks] ${error.message}\n`);
      log.end(() => resolve({ name, code, ms: Date.now() - started }));
    };
    child.on('error', (error) => finish(-1, error));
    child.on('close', (code, signal) => finish(code ?? (signal ? `signal ${signal}` : -1)));
  });
}

function writeSummary(summaryPath, header, results, steps) {
  const rows = results.map((r) => {
    const status = r.code === 0 ? 'ok' : `failed (${r.code})`;
    return `| ${r.name} | ${steps[r.name].gpu ? 'yes' : 'no'} | ${status} | ${formatDuration(r.ms)} | ${r.name}.log |`;
  });
  const text = [
    ...header,
    '',
    '| Step | GPU | Result | Time | Log |',
    '| :- | :- | :- | -: | :- |',
    ...rows,
    '',
  ].join('\n');
  fs.writeFileSync(summaryPath, text);
}

async function main() {
  let options;
  try {
    options = parseArgs(process.argv.slice(2));
  } catch (error) {
    console.error(`long-checks: ${error.message}\n\n${usage}`);
    return 2;
  }
  if (options.help) {
    console.log(usage);
    return 0;
  }

  const head = spawnSync('git', ['rev-parse', '--short', 'HEAD'], { cwd: repoRoot, encoding: 'utf8' });
  const commit = head.status === 0 ? head.stdout.trim() : 'unknown';
  const started = new Date();
  const outDir = path.join(repoRoot, 'tmp', 'long-checks', `${timestamp(started)}-${commit}`);
  const steps = buildSteps(options, outDir);

  if (options.dryRun) {
    for (const name of options.steps) console.log(`${name}: ${describe(steps[name])}`);
    return 0;
  }

  fs.mkdirSync(outDir, { recursive: true });
  const summaryPath = path.join(outDir, 'summary.md');
  const header = [
    `# Long checks ${timestamp(started)}`,
    '',
    `- Commit: ${commit}`,
    `- Steps: ${options.steps.join(', ')}`,
    `- Fuzz minutes: ${options.fuzzMinutes}; stability: ${options.stability}; Pi digits: ${options.piDigits}`,
    `- Generator benchmark reports: bench/generator (compare with \`just bench-compare <base dir> <head dir>\`)`,
    '- Fuzzer findings: tmp/differential-fuzz; test-stability logs: TestResults/<os>/<time>; sanitizer log: tmp/test-sanitized.log',
  ];

  const results = [];
  for (const name of options.steps) {
    if (steps[name].windowsOnly && process.platform !== 'win32') {
      console.log(`\n[long-checks] ${name}: skipped (Windows only)`);
      continue;
    }
    console.log(`\n[long-checks] ${name}: ${describe(steps[name])}`);
    results.push(await runStep(name, steps[name], path.join(outDir, `${name}.log`)));
    writeSummary(summaryPath, header, results, steps);
  }

  console.log(`\n[long-checks] summary: ${summaryPath}`);
  console.log(fs.readFileSync(summaryPath, 'utf8'));
  return results.every((r) => r.code === 0) ? 0 : 1;
}

main().then((code) => {
  process.exitCode = code;
});
