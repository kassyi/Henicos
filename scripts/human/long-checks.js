#!/usr/bin/env node
// Runs the long checks that need no one at the keyboard through scripts/human/job-queue.js and writes one summary:
//   - Serial benchmarks (noise-free): generator, compute, GPU Pi.
//   - Serial local sanitizer (`sanitized`, Windows MSVC ASan): runs before parallel lanes so main-worktree
//     IncludeEmbeddedStackLinq writes never collide with GPU-lane builds.
//   - Parallel check lanes (default; pass --serial for sequential execution):
//     - Remote Linux sanitizer (`sanitized-linux`, legacy alias `sanitized-wsl`): runs `just test-sanitized` on a
//       configured remote Linux worker via `job-queue.js run --remote`, falling back to WSL on Windows if remote
//       offload is unconfigured or unreachable before execution.
//     - GPU lane (main worktree): differential fuzzer, test-stability loop, coverage, mutate-gpumath,
//       mutate-compiler-cuda.
//     - Heavy CPU lane (isolated temporary git worktree when clean): `mutate-compiler`, avoiding MSBuild bin/obj
//       file-lock collisions with the GPU lane.
// Each step is queued as its own job-queue.js job, so live progress appears in `just queue-watch` and other chats can
// interleave between steps. A failing step is recorded and the run goes on.
//
// Outputs elsewhere: fuzzer findings in tmp/differential-fuzz, sanitizer logs in tmp/test-sanitized.log, test-stability
// logs in TestResults/<os>/<time>, coverage reports in TestResults, mutation reports in tmp/stryker/<phase>.
//
// Output: tmp/long-checks/<yyyyMMdd-HHmmss>-<commit>/
//   <step>.log            full output of the step
//   bench/generator/      BenchmarkDotNet reports of the generator benchmark (input of `just bench-compare`)
//   summary.md            one row per step: GPU, target, exit code, duration, log; on Windows also the kernel pools
//                         after the step and the nvlddmkm Event 153 entries during it (gpu-fault-watch.js)
//
// Usage:
//   node scripts/human/long-checks.js [--steps <list>] [--fuzz-minutes <n>] [--stability <count|duration>]
//                                        [--pi-digits <n>] [--serial] [--dry-run] [--help]
//   just long-checks [args...]

'use strict';

const fs = require('fs');
const path = require('path');
const { spawn, spawnSync } = require('child_process');
const faultWatch = require('./gpu-fault-watch.js');

const repoRoot = path.resolve(__dirname, '..', '..');
const jobQueueScript = path.join(repoRoot, 'scripts', 'human', 'job-queue.js');
const benchmarksProject = path.join('src', 'Apps', 'Henicos.Benchmarks');

const ParallelMutateCompilerWeight = 4;
const ParallelMutateCompilerConcurrency = '8';
const QueueEnvVarsToStrip = [
  'GSHARP_JOB_QUEUE_JOB',
  'GSHARP_JOB_QUEUE_CPUS',
  'GSHARP_JOB_QUEUE_IN_GPU_LOCK',
  'GSHARP_GPU_LOCK_HELD',
  'GSHARP_JOB_QUEUE_GPU_TURN',
  'GSHARP_JOB_QUEUE_COMMAND',
  'GSHARP_JOB_QUEUE_MSBUILD_ARGS',
];

// Measured serial times on a Windows 11 host with an NVIDIA GPU (default options, 2026-11).
// Running the parallel check lanes cuts wall-clock time from ~23h to ~17h.
const allSteps = [
  'bench-generator', // 1m56s
  'bench-compute', // 1m05s
  'bench-pi', // 34s (1M digits)
  'fuzz', // 2h00m (--fuzz-minutes 120)
  'sanitized', // 3m10s to 6m35s, depending on the build
  'sanitized-linux', // ~4m on remote Ubuntu worker, ~7m44s in WSL (ASan + UBSan)
  'stability', // 3h04m (--stability 3h)
  'coverage', // 3m11s
  'mutate-compiler', // 16h50m (CPU-only; runs in parallel with GPU lane in an isolated worktree)
  'mutate-gpumath', // not measured: the run stops within minutes (#1191)
  'mutate-compiler-cuda', // 1h15m (build 2m13s)
];

const stepAliases = { 'sanitized-wsl': 'sanitized-linux' };
const benchStepNames = new Set(['bench-generator', 'bench-compute', 'bench-pi']);
const gpuLaneStepNames = new Set(['fuzz', 'stability', 'coverage', 'mutate-gpumath', 'mutate-compiler-cuda']);

// P:\a\b -> /mnt/p/a/b, for running the same checkout inside WSL.
function toWslPath(windowsPath) {
  const match = /^([A-Za-z]):[\\/](.*)$/.exec(windowsPath);
  if (!match) throw new Error(`cannot map ${windowsPath} to a WSL path`);
  return `/mnt/${match[1].toLowerCase()}/${match[2].replace(/\\/g, '/')}`;
}

const usage = [
  'Usage: node scripts/human/long-checks.js [options]',
  '',
  `  --steps <list>                Comma-separated steps to run (default: ${allSteps.join(',')}).`,
  '                                Legacy alias: sanitized-wsl -> sanitized-linux.',
  '  --fuzz-minutes <n>            Minutes of differential fuzzing on all paths, CUDA included (default: 120).',
  '  --stability <count|duration>  Argument of `just test-stability`: count (10) or duration (3h) (default: 3h).',
  '  --pi-digits <n>               Digits for the GPU Pi benchmark (default: 1M).',
  '  --serial                      Run every selected step sequentially in the main worktree (no parallel lanes).',
  '  --dry-run                     Print the planned order and queued commands without running them.',
  '  --help, -h                    Show this help.',
].join('\n');

function parseArgs(argv) {
  const options = {
    steps: allSteps,
    fuzzMinutes: '120',
    stability: '3h',
    piDigits: '1M',
    serial: false,
    dryRun: false,
  };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    const value = () => {
      if (i + 1 >= argv.length) throw new Error(`${arg} needs a value`);
      return argv[++i];
    };
    if (arg === '--help' || arg === '-h') options.help = true;
    else if (arg === '--steps') {
      const raw = value().split(',').map((s) => s.trim()).filter(Boolean);
      const normalized = [];
      for (const item of raw) {
        const mapped = stepAliases[item] || item;
        if (!normalized.includes(mapped)) normalized.push(mapped);
      }
      if (normalized.length === 0) throw new Error('--steps must name at least one step');
      options.steps = normalized;
    } else if (arg === '--fuzz-minutes') options.fuzzMinutes = value();
    else if (arg === '--stability') options.stability = value();
    else if (arg === '--pi-digits') options.piDigits = value();
    else if (arg === '--serial') options.serial = true;
    else if (arg === '--dry-run') options.dryRun = true;
    else throw new Error(`unknown option ${arg}`);
  }
  const unknown = options.steps.filter((s) => !allSteps.includes(s));
  if (unknown.length > 0) throw new Error(`unknown step(s): ${unknown.join(', ')}`);
  if (!/^\d+$/.test(options.fuzzMinutes)) throw new Error('--fuzz-minutes must be a whole number');
  return options;
}

function isWorktreeClean() {
  const res = spawnSync('git', ['status', '--porcelain'], { cwd: repoRoot, encoding: 'utf8' });
  return res.status === 0 && res.stdout.trim() === '';
}

function queuedStep(queueArgs, command, args, meta) {
  return {
    ...meta,
    command: process.execPath,
    args: [jobQueueScript, 'run', ...queueArgs, '--', command, ...args],
  };
}

function buildWslSanitizedStep() {
  return queuedStep(
    ['--local'],
    'wsl.exe',
    process.platform === 'win32'
      ? ['--', 'bash', '-lc', `cd '${toWslPath(repoRoot)}' && node scripts/human/test-sanitized.js`]
      : [],
    { gpu: false, lane: 'remote-sanitizer', windowsOnly: true, target: 'local:wsl' },
  );
}

// Each step: how it is queued through job-queue.js, its lane, GPU usage, and target label for summary.md.
function buildSteps(options, outDir, isolateMutateCompiler) {
  const benchRun = (...args) => ['run', '--project', benchmarksProject, '-c', 'Release', '--', ...args];
  const wslFallback = buildWslSanitizedStep();
  const sanitizedLinuxStep =
    process.env.GSHARP_JOB_QUEUE_OFFLOAD !== '0'
      ? queuedStep(['--remote', '--timeout-minutes', '30'], 'just', ['test-sanitized'], {
          gpu: false,
          lane: 'remote-sanitizer',
          target: 'remote',
          wslFallback: process.platform === 'win32' ? wslFallback : null,
        })
      : wslFallback;
  const mutateScript = path.join('scripts', 'human', 'mutate.js');
  const mutateCompilerArgs = isolateMutateCompiler
    ? [mutateScript, '--phase', 'compiler', '--', '-c', ParallelMutateCompilerConcurrency]
    : [mutateScript, '--phase', 'compiler'];
  const fuzzArgs = [
    path.join('scripts', 'human', 'fuzz-differential.js'),
    '--profile', 'all', '--minutes', options.fuzzMinutes, '--metamorphic',
  ];

  return {
    'bench-generator': queuedStep(
      ['--local'], 'dotnet', benchRun('generator-bench', '--artifacts', path.join(outDir, 'bench', 'generator')),
      { gpu: false, lane: 'bench', target: 'local' },
    ),
    'bench-compute': queuedStep(
      ['--gpu-exclusive'], 'dotnet', benchRun('compute-bench', 'all', '--json'),
      { gpu: true, lane: 'bench', target: 'local:gpu' },
    ),
    'bench-pi': queuedStep(
      ['--gpu-exclusive'], 'dotnet', benchRun('gpu-pi', options.piDigits),
      { gpu: true, lane: 'bench', target: 'local:gpu' },
    ),
    fuzz: queuedStep(['--local', '--weight', '2'], 'node', fuzzArgs, { gpu: true, lane: 'gpu', target: 'local:gpu' }),
    sanitized: queuedStep(
      ['--local'], 'node', [path.join('scripts', 'human', 'test-sanitized.js')],
      { gpu: false, lane: 'local-pre', target: 'local' },
    ),
    'sanitized-linux': sanitizedLinuxStep,
    stability: queuedStep(
      ['--gpu'], 'just', ['test-stability', options.stability, 'true'],
      { gpu: true, lane: 'gpu', target: 'local:gpu' },
    ),
    coverage: queuedStep(['--gpu'], 'just', ['test-coverage'], { gpu: true, lane: 'gpu', target: 'local:gpu' }),
    'mutate-compiler': queuedStep(
      isolateMutateCompiler ? ['--local', '--weight', String(ParallelMutateCompilerWeight)] : ['--local'],
      'node',
      mutateCompilerArgs,
      {
        gpu: false,
        lane: 'cpu',
        isolatedWorktree: isolateMutateCompiler,
        target: isolateMutateCompiler ? 'local:worktree' : 'local',
      },
    ),
    'mutate-gpumath': queuedStep(
      ['--gpu'], 'just', ['mutate', 'gpumath'],
      { gpu: true, lane: 'gpu', target: 'local:gpu' },
    ),
    'mutate-compiler-cuda': queuedStep(
      ['--gpu'], 'just', ['mutate', 'compiler-cuda'],
      { gpu: true, lane: 'gpu', target: 'local:gpu' },
    ),
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

function cleanChildEnv() {
  const env = { ...process.env, DiffEngine_Disabled: 'true', CI: 'true' };
  for (const key of QueueEnvVarsToStrip) delete env[key];
  return env;
}

const activeChildren = new Set();

function runStep(name, step, logPath, options = {}) {
  const cwd = options.cwd || repoRoot;
  const prefixOutput = Boolean(options.prefixOutput);
  const append = Boolean(options.append);
  return new Promise((resolve) => {
    const log = fs.createWriteStream(logPath, { flags: append ? 'a' : 'w' });
    log.write(`${append ? '\n' : ''}$ ${describe(step)}\n`);
    if (cwd !== repoRoot) log.write(`# cwd: ${cwd}\n`);
    log.write('\n');
    const started = Date.now();
    const child = spawn(step.command, step.args, { cwd, env: cleanChildEnv(), stdio: ['ignore', 'pipe', 'pipe'] });
    activeChildren.add(child);
    let partial = '';
    let captured = '';
    const forward = (chunk) => {
      log.write(chunk);
      const text = chunk.toString('utf8');
      if (captured.length < 8192) captured += text.slice(0, 8192 - captured.length);
      if (!prefixOutput) {
        process.stdout.write(chunk);
        return;
      }
      partial += text;
      const lines = partial.split(/\r?\n/);
      partial = lines.pop() ?? '';
      for (const line of lines) process.stdout.write(`[${name}] ${line}\n`);
    };
    child.stdout.on('data', forward);
    child.stderr.on('data', forward);
    const finish = (code, error) => {
      activeChildren.delete(child);
      if (prefixOutput && partial !== '') process.stdout.write(`[${name}] ${partial}\n`);
      if (error) log.write(`\n[long-checks] ${error.message}\n`);
      log.end(() => resolve({ name, code, ms: Date.now() - started, target: step.target, outputHead: captured }));
    };
    child.on('error', (error) => finish(-1, error));
    child.on('close', (code, signal) => finish(code ?? (signal ? `signal ${signal}` : -1)));
  });
}

function shouldFallbackRemoteToWsl(result) {
  if (result.code === 0) return false;
  if (result.code === 2 || result.code === 255) return true;
  const text = result.outputHead || '';
  return (
    /transport failed before execution/i.test(text) ||
    /--remote requested, but /i.test(text) ||
    /could not run this command; refusing local execution/i.test(text)
  );
}

function extractGrantedRemote(outputHead) {
  const match = /granted j\d+ on remote ([^\s(]+)/.exec(outputHead || '');
  return match ? `remote:${match[1]}` : 'remote';
}

function poolCells(before, after) {
  if (!after.supported) return '';
  const mb = (bytes) => (bytes === null ? 'unknown' : String(Math.round(bytes / (1024 * 1024))));
  const entries = faultWatch.compare(before, after).event153Entries;
  const why = after.errors.length === 0 ? '' : ` (${after.errors.join('; ').replace(/\|/g, '/')})`;
  return ` ${mb(after.nonpagedBytes)} / ${mb(after.pagedBytes)}${why} | ${entries === null ? 'unknown' : entries} |`;
}

function poolLines(poolsBefore, poolsAfter) {
  if (!poolsBefore.supported) return [];
  const lines = ['', `- Before the first step: ${faultWatch.describe(poolsBefore)}`];
  if (faultWatch.isNonpagedHigh(poolsBefore)) lines.push(`- WARNING: ${faultWatch.startWarningText(poolsBefore)}`);
  if (poolsAfter) {
    const comparison = faultWatch.compare(poolsBefore, poolsAfter);
    lines.push(`- After the last step: ${faultWatch.describe(poolsAfter)}`);
    if (comparison.warn) lines.push(`- WARNING: ${faultWatch.warningText(comparison)}`);
  }
  return lines;
}

function writeSummary(summaryPath, header, resultsByName, steps, poolsBefore, poolsAfter) {
  const pools = poolsBefore.supported;
  const ordered = allSteps.filter((name) => resultsByName.has(name)).map((name) => resultsByName.get(name));
  const rows = ordered.map((r) => {
    const status = r.code === 0 ? 'ok' : `failed (${r.code})`;
    const cells = pools ? poolCells(r.poolsBefore, r.poolsAfter) : '';
    const gpu = steps[r.name].gpu ? 'yes' : 'no';
    return `| ${r.name} | ${gpu} | ${r.target} | ${status} | ${formatDuration(r.ms)} |${cells} ${r.name}.log |`;
  });
  const text = [
    ...header,
    '',
    pools
      ? '| Step | GPU | Target | Result | Time | Pools after (nonpaged / paged MB) | Event 153 entries | Log |'
      : '| Step | GPU | Target | Result | Time | Log |',
    pools ? '| :- | :- | :- | :- | -: | -: | -: | :- |' : '| :- | :- | :- | :- | -: | :- |',
    ...rows,
    ...poolLines(poolsBefore, poolsAfter),
    '',
  ].join('\n');
  const tmpPath = `${summaryPath}.tmp`;
  fs.writeFileSync(tmpPath, text);
  fs.renameSync(tmpPath, summaryPath);
}

function createIsolatedWorktree(worktreeDir) {
  fs.mkdirSync(path.dirname(worktreeDir), { recursive: true });
  const res = spawnSync('git', ['worktree', 'add', '--detach', worktreeDir, 'HEAD'], {
    cwd: repoRoot,
    encoding: 'utf8',
  });
  if (res.status !== 0) {
    throw new Error(`git worktree add failed: ${(res.stderr || res.stdout || '').trim()}`);
  }
}

function removeIsolatedWorktree(worktreeDir) {
  if (!worktreeDir) return;
  spawnSync('git', ['worktree', 'remove', '--force', worktreeDir], { cwd: repoRoot, encoding: 'utf8' });
  if (fs.existsSync(worktreeDir)) {
    try {
      fs.rmSync(worktreeDir, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
    } catch (error) {
      console.error(`[long-checks] warning: could not remove ${worktreeDir}: ${error.message}`);
    }
  }
  spawnSync('git', ['worktree', 'prune'], { cwd: repoRoot, encoding: 'utf8' });
}

function copyMutateCompilerReport(worktreeDir) {
  const srcDir = path.join(worktreeDir, 'tmp', 'stryker', 'compiler');
  const marker = path.join(srcDir, 'run-started');
  const report = path.join(srcDir, 'reports', 'mutation-report.json');
  if (!fs.existsSync(report)) return;
  if (fs.existsSync(marker) && fs.statSync(report).mtimeMs < fs.statSync(marker).mtimeMs) return;
  const dstDir = path.join(repoRoot, 'tmp', 'stryker', 'compiler');
  fs.rmSync(dstDir, { recursive: true, force: true });
  fs.mkdirSync(path.dirname(dstDir), { recursive: true });
  fs.cpSync(srcDir, dstDir, { recursive: true });
}

async function stopActiveChildren(signal) {
  const children = [...activeChildren];
  if (children.length === 0) return;
  const closed = (c) => c.exitCode !== null || c.signalCode !== null;
  const onClosed = (c) => (closed(c) ? Promise.resolve() : new Promise((r) => c.once('close', r)));
  const waitClosed = Promise.all(children.map(onClosed));
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
  if (process.platform === 'win32') {
    if (signal === 'SIGINT') await Promise.race([waitClosed, sleep(2000)]);
    for (const c of children) {
      if (!closed(c) && c.pid) spawnSync('taskkill', ['/PID', String(c.pid), '/T', '/F'], { stdio: 'ignore' });
    }
    await Promise.race([waitClosed, sleep(3000)]);
    return;
  }
  for (const c of children) {
    try { c.kill('SIGTERM'); } catch { /* already exited */ }
  }
  await Promise.race([waitClosed, sleep(3000)]);
}

function printDryRun(options, steps, plan) {
  console.log(`mode: ${options.serial ? 'serial' : 'parallel'}`);
  if (plan.deferMutateCompiler) {
    console.log('note: working tree has uncommitted changes; mutate-compiler will run serially after GPU steps.');
  }
  if (plan.benchSteps.length > 0) {
    console.log('\n[benchmarks (serial)]');
    for (const name of plan.benchSteps) console.log(`  ${name} [${steps[name].target}]: ${describe(steps[name])}`);
  }
  if (!options.serial && plan.localPreSteps.length > 0) {
    console.log('\n[local pre-checks (serial)]');
    for (const name of plan.localPreSteps) console.log(`  ${name} [${steps[name].target}]: ${describe(steps[name])}`);
  }
  const remaining = options.serial ? plan.postBenchSteps : plan.parallelSteps;
  if (remaining.length > 0) {
    console.log(`\n[checks (${options.serial ? 'serial' : 'parallel lanes'})]`);
    for (const name of remaining) {
      const target = steps[name].wslFallback ? `${steps[name].target} (fallback: local:wsl)` : steps[name].target;
      console.log(`  ${name} (${steps[name].lane}) [${target}]: ${describe(steps[name])}`);
    }
  }
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

  const wantsParallel = !options.serial;
  const hasMutateCompiler = options.steps.includes('mutate-compiler');
  const hasGpuSteps = options.steps.some((name) => gpuLaneStepNames.has(name));
  const clean = hasMutateCompiler && wantsParallel && hasGpuSteps ? isWorktreeClean() : true;
  const isolateMutateCompiler = hasMutateCompiler && wantsParallel && hasGpuSteps && clean;
  const deferMutateCompiler = hasMutateCompiler && wantsParallel && hasGpuSteps && !isolateMutateCompiler;

  const steps = buildSteps(options, outDir, isolateMutateCompiler);
  const benchSteps = options.steps.filter((name) => benchStepNames.has(name));
  const postBenchSteps = options.steps.filter((name) => !benchStepNames.has(name));
  const localPreSteps = postBenchSteps.filter((name) => steps[name].lane === 'local-pre');
  const parallelSteps = postBenchSteps.filter((name) => steps[name].lane !== 'local-pre');

  if (options.dryRun) {
    printDryRun(options, steps, { benchSteps, postBenchSteps, localPreSteps, parallelSteps, deferMutateCompiler });
    return 0;
  }

  fs.mkdirSync(outDir, { recursive: true });
  const summaryPath = path.join(outDir, 'summary.md');
  const header = [
    `# Long checks ${timestamp(started)}`,
    '',
    `- Commit: ${commit}`,
    `- Mode: ${wantsParallel ? 'parallel' : 'serial'}`,
    `- Steps: ${options.steps.join(', ')}`,
    `- Fuzz minutes: ${options.fuzzMinutes}; stability: ${options.stability}; Pi digits: ${options.piDigits}`,
    '- Generator benchmark reports: bench/generator (compare with `just bench-compare <base dir> <head dir>`)',
    '- Fuzzer: tmp/differential-fuzz; stability: TestResults/<os>/<time>; sanitizer: tmp/test-sanitized.log',
  ];

  let activeWorktreeDir = null;
  let stopping = false;
  const stopAll = async (signal) => {
    if (stopping) return;
    stopping = true;
    console.error(`\n[long-checks] received ${signal}; stopping active steps...`);
    await stopActiveChildren(signal);
    if (activeWorktreeDir) {
      removeIsolatedWorktree(activeWorktreeDir);
      activeWorktreeDir = null;
    }
    process.exit(signal === 'SIGINT' ? 130 : 143);
  };
  process.on('SIGINT', () => void stopAll('SIGINT'));
  process.on('SIGTERM', () => void stopAll('SIGTERM'));

  const resultsByName = new Map();
  const poolsBefore = faultWatch.measure({ slowFallback: true });

  const executeOne = async (name, execOptions = {}) => {
    const step = steps[name];
    if (step.windowsOnly && process.platform !== 'win32') {
      console.log(`\n[long-checks] ${name}: skipped (Windows only; no remote Linux worker configured)`);
      return;
    }
    const logPath = path.join(outDir, `${name}.log`);
    console.log(`\n[long-checks] ${name} [${step.target}]: ${describe(step)}`);
    const stepPoolsBefore = faultWatch.measure({ slowFallback: true });
    let result = await runStep(name, step, logPath, execOptions);
    if (step.target === 'remote') {
      if (step.wslFallback && shouldFallbackRemoteToWsl(result)) {
        console.log(`\n[long-checks] ${name}: remote unavailable; falling back to WSL [${step.wslFallback.target}]`);
        result = await runStep(name, step.wslFallback, logPath, { ...execOptions, append: true });
      } else {
        result.target = extractGrantedRemote(result.outputHead);
      }
    }
    result.poolsBefore = stepPoolsBefore;
    result.poolsAfter = faultWatch.measure({ slowFallback: true });
    resultsByName.set(name, result);
    writeSummary(summaryPath, header, resultsByName, steps, poolsBefore, result.poolsAfter);
  };

  if (options.serial) {
    for (const name of options.steps) await executeOne(name, { prefixOutput: false });
  } else {
    for (const name of benchSteps) await executeOne(name, { prefixOutput: false });
    for (const name of localPreSteps) await executeOne(name, { prefixOutput: false });

    const remoteSanitizerSteps = parallelSteps.filter((name) => steps[name].lane === 'remote-sanitizer');
    const gpuSteps = parallelSteps.filter((name) => steps[name].lane === 'gpu');
    const cpuSteps = parallelSteps.filter((name) => steps[name].lane === 'cpu');

    if (deferMutateCompiler) {
      console.log(
        '\n[long-checks] working tree has uncommitted changes; ' +
          'deferring mutate-compiler until GPU lane finishes to avoid bin/obj collisions.',
      );
    }

    const activeLaneCount =
      (remoteSanitizerSteps.length > 0 ? 1 : 0) +
      (gpuSteps.length > 0 ? 1 : 0) +
      (cpuSteps.length > 0 && !deferMutateCompiler ? 1 : 0);
    const prefixOutput = activeLaneCount > 1;

    const runLane = async (names, runEach) => {
      for (const name of names) await runEach(name);
    };

    const lanes = [];
    if (remoteSanitizerSteps.length > 0) {
      lanes.push(runLane(remoteSanitizerSteps, (name) => executeOne(name, { prefixOutput })));
    }
    if (gpuSteps.length > 0) {
      lanes.push(runLane(gpuSteps, (name) => executeOne(name, { prefixOutput })));
    }
    if (cpuSteps.length > 0 && !deferMutateCompiler) {
      lanes.push(
        runLane(cpuSteps, async (name) => {
          if (!steps[name].isolatedWorktree) {
            await executeOne(name, { prefixOutput });
            return;
          }
          const wtDir = path.join(outDir, 'cpu-worktree');
          try {
            createIsolatedWorktree(wtDir);
            activeWorktreeDir = wtDir;
            await executeOne(name, { cwd: wtDir, prefixOutput });
            copyMutateCompilerReport(wtDir);
          } finally {
            removeIsolatedWorktree(wtDir);
            if (activeWorktreeDir === wtDir) activeWorktreeDir = null;
          }
        }),
      );
    }

    await Promise.all(lanes);

    if (deferMutateCompiler) {
      for (const name of cpuSteps) await executeOne(name, { prefixOutput: false });
    }
  }

  const poolsAfter = faultWatch.measure({ slowFallback: true });
  writeSummary(summaryPath, header, resultsByName, steps, poolsBefore, poolsAfter);
  console.log(`\n[long-checks] summary: ${summaryPath}`);
  console.log(fs.readFileSync(summaryPath, 'utf8'));
  return [...resultsByName.values()].every((r) => r.code === 0) ? 0 : 1;
}

main().then((code) => {
  process.exitCode = code;
});
