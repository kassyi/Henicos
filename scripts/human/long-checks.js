#!/usr/bin/env node
// Runs the long checks that need no one at the keyboard through scripts/human/job-queue.js and writes one summary:
//   Stage 1 (serial, for clean measurements): benchmark baselines (generator, compute, GPU Pi).
//   Stage 2 (parallel lanes by default; pass --serial for sequential execution):
//     - Sanitizer lane: Windows MSVC ASan (`sanitized`) + Linux GCC ASan/UBSan (`sanitized-linux`, offloaded to a
//       healthy configured remote Linux worker via `job-queue.js run --remote`, falling back to WSL on Windows).
//     - GPU lane (main worktree): differential fuzzer, test-stability loop, coverage, mutate-gpumath,
//       mutate-compiler-cuda.
//     - Heavy CPU lane (isolated temporary git worktree when clean and running alongside other lanes):
//       `mutate-compiler`, avoiding MSBuild bin/obj file-lock collisions with the GPU lane.
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
const { mainCheckout } = require('./job-queue/lib/main-checkout.js');

const repoRoot = path.resolve(__dirname, '..', '..');
const jobQueueScript = path.join(repoRoot, 'scripts', 'human', 'job-queue.js');
const benchmarksProject = path.join('src', 'Apps', 'Henicos.Benchmarks');

// Default memory budget required for `just test-sanitized` (Kassyi.GSharp.Tests.Cpu).
const SanitizedMemoryMb = 2880;
const ParallelMutateCompilerWeight = 4;
const ParallelMutateCompilerConcurrency = '8';

// The comments give the measured serial time of each step, for reference only: one Windows 11 machine with an NVIDIA
// GPU, default options, 2026-11. Running Stage 2 lanes in parallel cuts wall-clock time from ~23h to ~17h.
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

const stepAliases = {
  'sanitized-wsl': 'sanitized-linux',
};

const benchStepNames = new Set(['bench-generator', 'bench-compute', 'bench-pi']);

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
  '  --stability <count|duration>  Argument of `just test-stability`: a count (10) or a duration (3h, 1h30m) (default: 3h).',
  '  --pi-digits <n>               Digits for the GPU Pi benchmark (default: 1M).',
  '  --serial                      Run every selected step sequentially in the main worktree (no parallel lanes).',
  '  --dry-run                     Print the planned stages and queued commands without running them.',
  '  --help, -h                    Show this help.',
].join('\n');

function parseArgs(argv) {
  const options = {
    steps: allSteps,
    explicitSteps: false,
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
    if (arg === '--help' || arg === '-h') {
      options.help = true;
    } else if (arg === '--steps') {
      options.explicitSteps = true;
      const raw = value().split(',').map((s) => s.trim()).filter(Boolean);
      const normalized = [];
      for (const item of raw) {
        const mapped = stepAliases[item] || item;
        if (!normalized.includes(mapped)) normalized.push(mapped);
      }
      options.steps = normalized;
    } else if (arg === '--fuzz-minutes') {
      options.fuzzMinutes = value();
    } else if (arg === '--stability') {
      options.stability = value();
    } else if (arg === '--pi-digits') {
      options.piDigits = value();
    } else if (arg === '--serial') {
      options.serial = true;
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

/**
 * Inspects `<mainCheckout>/tmp/job-queue/remotes.local.json` and runs the configured probe for the first enabled
 * remote worker whose memory budget fits `SanitizedMemoryMb`. Returns `{ name }` when healthy, or `null` when
 * offload is disabled, no worker fits, or every fitting worker is unreachable.
 */
function probeHealthyRemoteWorker() {
  if (process.env.GSHARP_JOB_QUEUE_OFFLOAD === '0') {
    return null;
  }
  const mainRoot = mainCheckout(repoRoot, repoRoot);
  const remotesPath = path.join(mainRoot, 'tmp', 'job-queue', 'remotes.local.json');
  if (!fs.existsSync(remotesPath)) {
    return null;
  }
  let parsed;
  try {
    parsed = JSON.parse(fs.readFileSync(remotesPath, 'utf8'));
  } catch {
    return null;
  }
  const remotes = parsed && typeof parsed.remotes === 'object' && parsed.remotes !== null ? parsed.remotes : null;
  if (!remotes) {
    return null;
  }
  for (const [name, entry] of Object.entries(remotes)) {
    if (!entry || typeof entry !== 'object') continue;
    if (entry.mode === 'off') continue;
    const mem = typeof entry.memoryMb === 'number' ? entry.memoryMb : 0;
    if (mem < SanitizedMemoryMb) continue;
    const probe = entry.probe;
    if (!probe || !Array.isArray(probe.argv) || probe.argv.length === 0) {
      return { name };
    }
    const timeoutMs = typeof probe.timeoutMs === 'number' && probe.timeoutMs > 0 ? probe.timeoutMs : 5000;
    const res = spawnSync(String(probe.argv[0]), probe.argv.slice(1).map(String), {
      cwd: repoRoot,
      encoding: 'utf8',
      timeout: timeoutMs,
    });
    if (res.status !== 0) {
      continue;
    }
    const min = typeof probe.min === 'number' ? probe.min : 0;
    const match = /-?\d+(?:\.\d+)?/.exec(res.stdout || '');
    const value = match ? Number(match[0]) : NaN;
    if (Number.isFinite(value) && value >= min) {
      return { name };
    }
  }
  return null;
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

// Each step: how it is queued through job-queue.js, its lane, GPU usage, and target label for summary.md.
function buildSteps(options, outDir, envInfo) {
  const benchRun = (...args) => ['run', '--project', benchmarksProject, '-c', 'Release', '--', ...args];
  const useIsolatedMutate = envInfo.isolateMutateCompiler;

  const sanitizedLinuxStep = envInfo.remoteWorker
    ? queuedStep(['--remote', '--timeout-minutes', '30'], 'just', ['test-sanitized'], {
        gpu: false,
        lane: 'sanitizer',
        target: `remote:${envInfo.remoteWorker.name}`,
      })
    : queuedStep(
        ['--local'],
        'wsl.exe',
        process.platform === 'win32'
          ? ['--', 'bash', '-lc', `cd '${toWslPath(repoRoot)}' && node scripts/human/test-sanitized.js`]
          : [],
        {
          gpu: false,
          lane: 'sanitizer',
          windowsOnly: true,
          target: 'local:wsl',
        },
      );

  const mutateCompilerArgs = useIsolatedMutate
    ? [path.join('scripts', 'human', 'mutate.js'), '--phase', 'compiler', '--', '-c', ParallelMutateCompilerConcurrency]
    : [path.join('scripts', 'human', 'mutate.js'), '--phase', 'compiler'];

  return {
    'bench-generator': queuedStep(
      ['--local'],
      'dotnet',
      benchRun('generator-bench', '--artifacts', path.join(outDir, 'bench', 'generator')),
      { gpu: false, lane: 'bench', target: 'local' },
    ),
    'bench-compute': queuedStep(['--gpu-exclusive'], 'dotnet', benchRun('compute-bench', 'all', '--json'), {
      gpu: true,
      lane: 'bench',
      target: 'local:gpu',
    }),
    'bench-pi': queuedStep(['--gpu-exclusive'], 'dotnet', benchRun('gpu-pi', options.piDigits), {
      gpu: true,
      lane: 'bench',
      target: 'local:gpu',
    }),
    fuzz: queuedStep(
      ['--local', '--weight', '2'],
      'node',
      [
        path.join('scripts', 'human', 'fuzz-differential.js'),
        '--profile',
        'all',
        '--minutes',
        options.fuzzMinutes,
        '--metamorphic',
      ],
      { gpu: true, lane: 'gpu', target: 'local:gpu' },
    ),
    sanitized: queuedStep(['--local'], 'node', [path.join('scripts', 'human', 'test-sanitized.js')], {
      gpu: false,
      lane: 'sanitizer',
      target: 'local',
    }),
    'sanitized-linux': sanitizedLinuxStep,
    stability: queuedStep(['--gpu'], 'just', ['test-stability', options.stability, 'true'], {
      gpu: true,
      lane: 'gpu',
      target: 'local:gpu',
    }),
    coverage: queuedStep(['--gpu'], 'just', ['test-coverage'], {
      gpu: true,
      lane: 'gpu',
      target: 'local:gpu',
    }),
    'mutate-compiler': queuedStep(
      useIsolatedMutate ? ['--local', '--weight', String(ParallelMutateCompilerWeight)] : ['--local'],
      'node',
      mutateCompilerArgs,
      {
        gpu: false,
        lane: 'cpu',
        isolatedWorktree: useIsolatedMutate,
        target: useIsolatedMutate ? 'local:worktree' : 'local',
      },
    ),
    'mutate-gpumath': queuedStep(['--gpu'], 'just', ['mutate', 'gpumath'], {
      gpu: true,
      lane: 'gpu',
      target: 'local:gpu',
    }),
    'mutate-compiler-cuda': queuedStep(['--gpu'], 'just', ['mutate', 'compiler-cuda'], {
      gpu: true,
      lane: 'gpu',
      target: 'local:gpu',
    }),
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

/** Strips outer job-queue session variables so child steps always schedule cleanly through job-queue.js. */
function cleanChildEnv() {
  const env = { ...process.env, DiffEngine_Disabled: 'true', CI: 'true' };
  delete env.GSHARP_JOB_QUEUE_JOB;
  delete env.GSHARP_JOB_QUEUE_CPUS;
  delete env.GSHARP_JOB_QUEUE_IN_GPU_LOCK;
  delete env.GSHARP_GPU_LOCK_HELD;
  return env;
}

const activeChildren = new Set();

function runStep(name, step, logPath, options = {}) {
  const cwd = options.cwd || repoRoot;
  const prefixOutput = Boolean(options.prefixOutput);
  return new Promise((resolve) => {
    const log = fs.createWriteStream(logPath);
    log.write(`$ ${describe(step)}\n`);
    if (cwd !== repoRoot) {
      log.write(`# cwd: ${cwd}\n`);
    }
    log.write('\n');
    const started = Date.now();
    const child = spawn(step.command, step.args, {
      cwd,
      env: cleanChildEnv(),
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    activeChildren.add(child);
    let partial = '';
    const forward = (chunk) => {
      log.write(chunk);
      if (!prefixOutput) {
        process.stdout.write(chunk);
        return;
      }
      partial += chunk.toString('utf8');
      const lines = partial.split(/\r?\n/);
      partial = lines.pop() ?? '';
      for (const line of lines) {
        process.stdout.write(`[${name}] ${line}\n`);
      }
    };
    child.stdout.on('data', forward);
    child.stderr.on('data', forward);
    const finish = (code, error) => {
      activeChildren.delete(child);
      if (prefixOutput && partial !== '') {
        process.stdout.write(`[${name}] ${partial}\n`);
      }
      if (error) log.write(`\n[long-checks] ${error.message}\n`);
      log.end(() => resolve({ name, code, ms: Date.now() - started, target: step.target }));
    };
    child.on('error', (error) => finish(-1, error));
    child.on('close', (code, signal) => finish(code ?? (signal ? `signal ${signal}` : -1)));
  });
}

function poolCells(before, after) {
  if (!after.supported) return '';
  const mb = (bytes) => (bytes === null ? 'unknown' : String(Math.round(bytes / (1024 * 1024))));
  const entries = faultWatch.compare(before, after).event153Entries;
  const why = after.errors.length === 0 ? '' : ` (${after.errors.join('; ').replace(/\|/g, '/')})`;
  return ` ${mb(after.nonpagedBytes)} / ${mb(after.pagedBytes)}${why} | ${entries === null ? 'unknown' : entries} |`;
}

// The pool lines under the table: the reading before the first step and, on Windows, the change over the whole run.
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
    fs.rmSync(worktreeDir, { recursive: true, force: true });
  }
  spawnSync('git', ['worktree', 'prune'], { cwd: repoRoot, encoding: 'utf8' });
}

function copyMutateCompilerReport(worktreeDir) {
  const srcDir = path.join(worktreeDir, 'tmp', 'stryker', 'compiler');
  if (!fs.existsSync(srcDir)) return;
  const dstDir = path.join(repoRoot, 'tmp', 'stryker', 'compiler');
  fs.rmSync(dstDir, { recursive: true, force: true });
  fs.mkdirSync(path.dirname(dstDir), { recursive: true });
  fs.cpSync(srcDir, dstDir, { recursive: true });
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

  const remoteWorker = options.steps.includes('sanitized-linux') ? probeHealthyRemoteWorker() : null;
  const wantsParallel = !options.serial;
  const hasMutateCompiler = options.steps.includes('mutate-compiler');
  const hasGpuSteps = options.steps.some((name) => !benchStepNames.has(name) && name !== 'mutate-compiler' && name !== 'sanitized' && name !== 'sanitized-linux');
  const clean = hasMutateCompiler && wantsParallel && hasGpuSteps ? isWorktreeClean() : true;
  const isolateMutateCompiler = hasMutateCompiler && wantsParallel && hasGpuSteps && clean;

  const steps = buildSteps(options, outDir, { remoteWorker, isolateMutateCompiler });
  const benchSteps = options.steps.filter((name) => benchStepNames.has(name));
  const postBenchSteps = options.steps.filter((name) => !benchStepNames.has(name));

  if (options.dryRun) {
    console.log(`mode: ${wantsParallel ? 'parallel' : 'serial'}`);
    if (hasMutateCompiler && wantsParallel && hasGpuSteps && !clean) {
      console.log('note: working tree has uncommitted changes; mutate-compiler will run serially after GPU steps.');
    }
    if (benchSteps.length > 0) {
      console.log('\n[stage 1: benchmarks (serial)]');
      for (const name of benchSteps) console.log(`  ${name} [${steps[name].target}]: ${describe(steps[name])}`);
    }
    if (postBenchSteps.length > 0) {
      console.log(`\n[stage 2: ${wantsParallel ? 'parallel lanes' : 'serial'}]`);
      for (const name of postBenchSteps) {
        console.log(`  ${name} (${steps[name].lane}) [${steps[name].target}]: ${describe(steps[name])}`);
      }
    }
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
    `- Generator benchmark reports: bench/generator (compare with \`just bench-compare <base dir> <head dir>\`)`,
    '- Fuzzer findings: tmp/differential-fuzz; test-stability logs: TestResults/<os>/<time>; sanitizer log: tmp/test-sanitized.log',
  ];

  let activeWorktreeDir = null;
  let stopping = false;
  const stopAll = (signal) => {
    if (stopping) return;
    stopping = true;
    console.error(`\n[long-checks] received ${signal}; stopping active steps...`);
    for (const child of activeChildren) {
      try {
        child.kill('SIGTERM');
      } catch {
        // Ignore already-exited child processes
      }
    }
    if (activeWorktreeDir) {
      removeIsolatedWorktree(activeWorktreeDir);
      activeWorktreeDir = null;
    }
    process.exit(signal === 'SIGINT' ? 130 : 143);
  };
  process.on('SIGINT', () => stopAll('SIGINT'));
  process.on('SIGTERM', () => stopAll('SIGTERM'));

  const resultsByName = new Map();
  const poolsBefore = faultWatch.measure({ slowFallback: true });

  const executeOne = async (name, execOptions = {}) => {
    const step = steps[name];
    if (step.windowsOnly && process.platform !== 'win32') {
      console.log(`\n[long-checks] ${name}: skipped (Windows only; no remote Linux worker configured)`);
      return;
    }
    if (name === 'sanitized-linux' && !remoteWorker && process.platform !== 'win32' && !options.explicitSteps) {
      console.log(`\n[long-checks] ${name}: skipped (already covered by sanitized on ${process.platform})`);
      return;
    }
    console.log(`\n[long-checks] ${name} [${step.target}]: ${describe(step)}`);
    const stepPoolsBefore = faultWatch.measure({ slowFallback: true });
    const result = await runStep(name, step, path.join(outDir, `${name}.log`), execOptions);
    result.poolsBefore = stepPoolsBefore;
    result.poolsAfter = faultWatch.measure({ slowFallback: true });
    resultsByName.set(name, result);
    writeSummary(summaryPath, header, resultsByName, steps, poolsBefore, result.poolsAfter);
  };

  if (options.serial) {
    for (const name of options.steps) {
      await executeOne(name, { prefixOutput: false });
    }
  } else {
    // Stage 1: Benchmarks serially to keep measurements free of background CPU/GPU noise.
    for (const name of benchSteps) {
      await executeOne(name, { prefixOutput: false });
    }

    // Stage 2: Run sanitizer, GPU, and heavy-CPU lanes in parallel.
    const sanitizerSteps = postBenchSteps.filter((name) => steps[name].lane === 'sanitizer');
    const gpuSteps = postBenchSteps.filter((name) => steps[name].lane === 'gpu');
    const cpuSteps = postBenchSteps.filter((name) => steps[name].lane === 'cpu');

    const deferMutateCompiler = hasMutateCompiler && hasGpuSteps && !isolateMutateCompiler;
    if (deferMutateCompiler) {
      console.log(
        '\n[long-checks] working tree has uncommitted changes; deferring mutate-compiler until GPU lane finishes to avoid bin/obj collisions.',
      );
    }

    const activeLaneCount =
      (sanitizerSteps.length > 0 ? 1 : 0) +
      (gpuSteps.length > 0 ? 1 : 0) +
      (cpuSteps.length > 0 && !deferMutateCompiler ? 1 : 0);
    const prefixOutput = activeLaneCount > 1;

    const lanes = [];
    if (sanitizerSteps.length > 0) {
      lanes.push(
        (async () => {
          for (const name of sanitizerSteps) {
            await executeOne(name, { prefixOutput });
          }
        })(),
      );
    }
    if (gpuSteps.length > 0) {
      lanes.push(
        (async () => {
          for (const name of gpuSteps) {
            await executeOne(name, { prefixOutput });
          }
        })(),
      );
    }
    if (cpuSteps.length > 0 && !deferMutateCompiler) {
      lanes.push(
        (async () => {
          for (const name of cpuSteps) {
            if (steps[name].isolatedWorktree) {
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
            } else {
              await executeOne(name, { prefixOutput });
            }
          }
        })(),
      );
    }

    await Promise.all(lanes);

    if (deferMutateCompiler) {
      for (const name of cpuSteps) {
        await executeOne(name, { prefixOutput: false });
      }
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
