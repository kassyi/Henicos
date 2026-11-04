#!/usr/bin/env node
'use strict';

/*
 * fuzz-differential.js — Batch differential kernel fuzzer orchestrator for G#.
 * Generates batches of scalar and composite device kernels via tools/fuzz-diff into tmp/differential-fuzz/,
 * compiles and runs them across Profile M, Profile C, or Profile G, and writes findings to <work-dir>/findings/.
 */

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const repoRoot = path.resolve(__dirname, '..', '..');
const hostOs = process.platform === 'win32' ? 'win' : process.platform === 'linux' ? 'linux' : process.platform === 'darwin' ? 'osx' : 'unix';
const DEFAULT_BUILD_TIMEOUT_MS = 240_000;
const DEFAULT_RUN_TIMEOUT_MS = 120_000;
// A reduction runs up to 16 rounds, each with its own batch build (up to DEFAULT_BUILD_TIMEOUT_MS) and run;
// the reducer times out each process itself, so this only stops a reducer that hangs between them.
const DEFAULT_REDUCE_TIMEOUT_MS = 30 * 60_000;

const helpText = [
  'Usage: node scripts/human/fuzz-differential.js [options]',
  '',
  '  --profile <managed|cpu|all|M|C|G>  Execution profile: M/managed, C/cpu (default), or G/all (includes CUDA).',
  '  --minutes <n>                      Run duration in minutes (default: 5 when --batches is not given).',
  '  --batches <n>                      Run a fixed number of batches instead of a time limit.',
  '  --batch-size <n>                   Kernels generated and compiled per batch (default: 64).',
  '  --fuzz-seed <seed>                 Initial 64-bit fuzz seed in decimal or 0x hex (default: 1).',
  '  --input-seed <int>                 Optional explicit runtime data input seed (default: 0..8).',
  '  --allow-issue <n>                  Re-enable generation of shapes avoided for known issue #<n> (repeatable).',
  '  --swarm-focus <mode>               Optional Swarm focus mode (default, coalesce-assign-value, tuple-deconstruct-complex).',
  '  --compare-base <ref>               Compare generated C++/CUDA (.g.cpp and .g.cu) against git <ref> without a GPU.',
  '  --expect-change                    Exit 0 when --compare-base finds generated C++/CUDA differences.',
  '  --work-dir <dir>                   Workspace directory for temporary projects (default: tmp/differential-fuzz).',
  '  --out-dir <dir>                    Output directory for generated sources (default: <work-dir>/Generated).',
  '  --no-reduce                        Skip automatic 2-stage reduction and fingerprint deduplication on failure.',
  '  --no-gpu-lock                      Skip acquiring scripts/agent/gpu-lock.js (use when already holding the lock).',
  '  --help, -h                         Show this help.',
].join('\n');

function fail(message, code = 1) {
  process.stderr.write(`fuzz-differential: ${message}\n`);
  process.exit(code);
}

function normalizeProfile(raw) {
  const value = String(raw || '').trim().toLowerCase();
  if (value === 'm' || value === 'managed') return 'M';
  if (value === 'c' || value === 'cpu' || value === 'native') return 'C';
  if (value === 'g' || value === 'all' || value === 'cuda' || value === 'gpu') return 'G';
  fail(`Invalid --profile '${raw}'. Expected managed|cpu|all|M|C|G.`, 2);
}

function parseSeedBigInt(raw) {
  const text = String(raw ?? '').trim();
  if (!text) fail('--fuzz-seed requires a 64-bit integer.', 2);
  try {
    return BigInt.asUintN(64, BigInt(text));
  } catch {
    fail(`Invalid --fuzz-seed '${raw}'.`, 2);
  }
}

function formatHexSeed(seedBigInt) {
  return `0x${BigInt.asUintN(64, seedBigInt).toString(16).toUpperCase().padStart(16, '0')}`;
}

function parseArgs(argv) {
  const options = {
    profile: 'C',
    minutes: 5,
    batches: null,
    batchSize: 64,
    fuzzSeed: 1n,
    inputSeed: null,
    allowedIssues: [],
    swarmFocus: null,
    compareBase: null,
    expectChange: false,
    compareSingle: null,
    baseWorktree: null,
    compareDialect: 'Cpu,Cuda',
    compareKernel: null,
    compareScope: 'kernel',
    workDir: path.join(repoRoot, 'tmp', 'differential-fuzz'),
    outDir: null,
    noReduce: false,
    noGpuLock: false,
  };

  function takeValue(index, flagName) {
    const eqIdx = argv[index].indexOf('=');
    if (eqIdx >= 0) return { value: argv[index].slice(eqIdx + 1), nextIndex: index };
    if (index + 1 >= argv.length) fail(`${flagName} requires a value.`, 2);
    return { value: argv[index + 1], nextIndex: index + 1 };
  }

  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--help' || arg === '-h') {
      process.stdout.write(`${helpText}\n`);
      process.exit(0);
    } else if (arg === '--profile' || arg.startsWith('--profile=')) {
      const { value, nextIndex } = takeValue(i, '--profile');
      i = nextIndex;
      options.profile = normalizeProfile(value);
    } else if (arg === '--minutes' || arg.startsWith('--minutes=')) {
      const { value, nextIndex } = takeValue(i, '--minutes');
      i = nextIndex;
      const v = Number(value);
      if (!Number.isFinite(v) || v <= 0) fail('--minutes requires a positive number.', 2);
      options.minutes = v;
    } else if (arg === '--batches' || arg.startsWith('--batches=')) {
      const { value, nextIndex } = takeValue(i, '--batches');
      i = nextIndex;
      const v = Number(value);
      if (!Number.isInteger(v) || v <= 0) fail('--batches requires a positive integer.', 2);
      options.batches = v;
    } else if (arg === '--batch-size' || arg.startsWith('--batch-size=')) {
      const { value, nextIndex } = takeValue(i, '--batch-size');
      i = nextIndex;
      const v = Number(value);
      if (!Number.isInteger(v) || v < 1 || v > 256) fail('--batch-size requires an integer in [1, 256].', 2);
      options.batchSize = v;
    } else if (arg === '--fuzz-seed' || arg.startsWith('--fuzz-seed=')) {
      const { value, nextIndex } = takeValue(i, '--fuzz-seed');
      i = nextIndex;
      options.fuzzSeed = parseSeedBigInt(value);
    } else if (arg === '--input-seed' || arg.startsWith('--input-seed=')) {
      const { value, nextIndex } = takeValue(i, '--input-seed');
      i = nextIndex;
      const v = Number(value);
      if (!Number.isInteger(v)) fail('--input-seed requires an integer.', 2);
      options.inputSeed = v;
    } else if (arg === '--allow-issue' || arg.startsWith('--allow-issue=')) {
      const { value, nextIndex } = takeValue(i, '--allow-issue');
      i = nextIndex;
      const v = Number(String(value ?? '').replace(/^#/, ''));
      if (!Number.isInteger(v) || v <= 0) fail('--allow-issue requires a positive integer issue number.', 2);
      if (!options.allowedIssues.includes(v)) options.allowedIssues.push(v);
    } else if (arg === '--swarm-focus' || arg.startsWith('--swarm-focus=')) {
      const { value, nextIndex } = takeValue(i, '--swarm-focus');
      i = nextIndex;
      if (!value) fail('--swarm-focus requires a focus mode.', 2);
      options.swarmFocus = value;
    } else if (arg === '--compare-base' || arg.startsWith('--compare-base=')) {
      const { value, nextIndex } = takeValue(i, '--compare-base');
      i = nextIndex;
      if (!value) fail('--compare-base requires a git ref.', 2);
      options.compareBase = value;
    } else if (arg === '--expect-change') {
      options.expectChange = true;
    } else if (arg === '--compare-single' || arg.startsWith('--compare-single=')) {
      const { value, nextIndex } = takeValue(i, '--compare-single');
      i = nextIndex;
      if (!value) fail('--compare-single requires a file path.', 2);
      options.compareSingle = path.resolve(repoRoot, value);
    } else if (arg === '--base-worktree' || arg.startsWith('--base-worktree=')) {
      const { value, nextIndex } = takeValue(i, '--base-worktree');
      i = nextIndex;
      if (!value) fail('--base-worktree requires a directory path.', 2);
      options.baseWorktree = path.resolve(repoRoot, value);
    } else if (arg === '--compare-dialect' || arg.startsWith('--compare-dialect=')) {
      const { value, nextIndex } = takeValue(i, '--compare-dialect');
      i = nextIndex;
      if (!value) fail('--compare-dialect requires Cpu, Cuda, or Cpu,Cuda.', 2);
      options.compareDialect = value;
    } else if (arg === '--compare-kernel' || arg.startsWith('--compare-kernel=')) {
      const { value, nextIndex } = takeValue(i, '--compare-kernel');
      i = nextIndex;
      options.compareKernel = value || null;
    } else if (arg === '--compare-scope' || arg.startsWith('--compare-scope=')) {
      const { value, nextIndex } = takeValue(i, '--compare-scope');
      i = nextIndex;
      options.compareScope = value || 'kernel';
    } else if (arg === '--work-dir' || arg.startsWith('--work-dir=')) {
      const { value, nextIndex } = takeValue(i, '--work-dir');
      i = nextIndex;
      if (!value) fail('--work-dir requires a directory path.', 2);
      options.workDir = path.resolve(repoRoot, value);
    } else if (arg === '--out-dir' || arg.startsWith('--out-dir=')) {
      const { value, nextIndex } = takeValue(i, '--out-dir');
      i = nextIndex;
      if (!value) fail('--out-dir requires a directory path.', 2);
      options.outDir = path.resolve(repoRoot, value);
    } else if (arg === '--no-reduce') {
      options.noReduce = true;
    } else if (arg === '--no-gpu-lock') {
      options.noGpuLock = true;
    } else {
      fail(`Unknown option '${arg}'.\n\n${helpText}`, 2);
    }
  }

  if (!options.outDir) options.outDir = path.join(options.workDir, 'Generated');
  return options;
}

function loadKnownIssuesCatalog() {
  const jsonPath = path.join(repoRoot, 'tools', 'fuzz-diff', 'known-issues.json');
  if (!fs.existsSync(jsonPath)) fail(`Known-issue catalog not found: ${jsonPath}`);
  let parsed;
  try {
    parsed = JSON.parse(fs.readFileSync(jsonPath, 'utf8'));
  } catch (err) {
    fail(`Failed to parse known-issue catalog '${jsonPath}': ${err.message}`);
  }
  if (!parsed || !Array.isArray(parsed.issues) || parsed.issues.length === 0) {
    fail(`Known-issue catalog '${jsonPath}' must contain a non-empty 'issues' array.`);
  }
  return parsed.issues;
}

function selectActiveKnownIssues(catalogEntries, profile, allowedIssues) {
  const allowedSet = new Set(allowedIssues || []);
  return catalogEntries
    .filter((entry) => !allowedSet.has(entry.issue) && Array.isArray(entry.profiles) && entry.profiles.includes(profile))
    .map((entry) => ({
      issue: entry.issue,
      kind: entry.kind || 'avoid',
      summary: entry.summary,
      paths: entry.paths,
    }));
}

function runCommand(command, args, { cwd = repoRoot, extraEnv = {}, timeoutMs = DEFAULT_BUILD_TIMEOUT_MS } = {}) {
  const result = spawnSync(command, args, {
    cwd,
    env: { ...process.env, DiffEngine_Disabled: 'true', CI: 'true', DOTNET_NOLOGO: '1', ...extraEnv },
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
    timeout: timeoutMs,
  });
  const timedOut = Boolean((result.error && result.error.code === 'ETIMEDOUT') || (result.signal === 'SIGTERM' && timeoutMs > 0));
  return { status: timedOut ? 124 : (result.status ?? 1), timedOut, stdout: result.stdout || '', stderr: result.stderr || '' };
}

function detectHostCudaArch() {
  const res = spawnSync('nvidia-smi', ['--query-gpu=compute_cap', '--format=csv,noheader'], { cwd: repoRoot, encoding: 'utf8', timeout: 15_000 });
  if (res.status !== 0 || !res.stdout) return null;
  const firstLine = res.stdout.split(/\r?\n/).map((s) => s.trim()).find((s) => /^\d+\.\d+$/.test(s));
  if (!firstLine) return null;
  const digits = firstLine.replace('.', '');
  return `compute_${digits},sm_${digits}`;
}

function isAlreadyInsideGpuLock() {
  try {
    const gitCommon = spawnSync('git', ['rev-parse', '--git-common-dir'], { cwd: repoRoot, encoding: 'utf8' });
    const mainRoot = gitCommon.status === 0 && gitCommon.stdout.trim() ? path.resolve(repoRoot, gitCommon.stdout.trim(), '..') : repoRoot;
    const ownerJsonPath = path.join(mainRoot, 'tmp', 'gpu-test-lock', 'owner.json');
    if (!fs.existsSync(ownerJsonPath)) return false;
    const owner = JSON.parse(fs.readFileSync(ownerJsonPath, 'utf8'));
    return Boolean(owner && (owner.pid === process.pid || owner.pid === process.ppid));
  } catch {
    return false;
  }
}

function hasAssetsJson(csprojPath) {
  return fs.existsSync(path.join(path.dirname(csprojPath), 'obj', hostOs, 'project.assets.json'));
}

function ensureCompilerPrereqsBuiltForRoot(targetRoot, forceRebuildCompiler = false) {
  const prereqProjects = [
    { proj: path.join(targetRoot, 'src', 'Core', 'Kassyi.GSharp.Core', 'Kassyi.GSharp.Core.csproj'), dll: path.join(targetRoot, 'src', 'Core', 'Kassyi.GSharp.Core', 'bin', hostOs, 'Debug', 'net10.0', 'Kassyi.GSharp.Core.dll'), force: false },
    { proj: path.join(targetRoot, 'src', 'Runtime', 'Kassyi.GSharp.Runtime', 'Kassyi.GSharp.Runtime.csproj'), dll: path.join(targetRoot, 'src', 'Runtime', 'Kassyi.GSharp.Runtime', 'bin', hostOs, 'Debug', 'net10.0', 'Kassyi.GSharp.Runtime.dll'), force: false },
    { proj: path.join(targetRoot, 'src', 'Compiler', 'Kassyi.GSharp.Compiler', 'Kassyi.GSharp.Compiler.csproj'), dll: path.join(targetRoot, 'src', 'Compiler', 'Kassyi.GSharp.Compiler', 'bin', hostOs, 'Debug', 'netstandard2.0', 'Kassyi.GSharp.Compiler.dll'), force: forceRebuildCompiler },
    { proj: path.join(targetRoot, 'src', 'Compiler', 'Kassyi.GSharp.Compiler.Tasks', 'Kassyi.GSharp.Compiler.Tasks.csproj'), dll: path.join(targetRoot, 'src', 'Compiler', 'Kassyi.GSharp.Compiler.Tasks', 'bin', hostOs, 'Debug', 'netstandard2.0', 'Kassyi.GSharp.Compiler.Tasks.dll'), force: false },
  ];

  for (const item of prereqProjects) {
    if (!item.force && fs.existsSync(item.dll)) continue;
    const buildArgs = ['build', item.proj, '-c', 'Debug', '-m:1', '-nodeReuse:false'];
    if (hasAssetsJson(item.proj)) buildArgs.push('--no-restore');
    const res = runCommand('dotnet', buildArgs, { cwd: targetRoot });
    if (res.status !== 0) {
      process.stderr.write(res.stdout + '\n' + res.stderr + '\n');
      fail(`Failed to build prerequisite project: ${item.proj}`);
    }
  }
}

function ensurePrereqsBuilt(fuzzToolProj, forceRebuildCompiler = false) {
  process.stdout.write('[fuzz-diff] Building tools/fuzz-diff and compiler/runtime prerequisites...\n');
  const toolArgs = ['build', fuzzToolProj, '-c', 'Debug', '-m:1', '-nodeReuse:false'];
  if (hasAssetsJson(fuzzToolProj)) toolArgs.push('--no-restore');
  const toolBuild = runCommand('dotnet', toolArgs);
  if (toolBuild.status !== 0) {
    process.stderr.write(toolBuild.stdout + '\n' + toolBuild.stderr + '\n');
    fail('Failed to build tools/fuzz-diff.');
  }

  ensureCompilerPrereqsBuiltForRoot(repoRoot, forceRebuildCompiler);
}

function classifySuspectedIssue(details, catalogEntries) {
  if (!details || details.status === 'compile_error' || details.status === 'hang') return false;
  const paths = Array.isArray(details.failingPaths) ? details.failingPaths.map((p) => String(p).toLowerCase()) : [];
  const classifyEntries = Array.isArray(catalogEntries) ? catalogEntries.filter((e) => Array.isArray(e.classifyOnlyPaths) && e.classifyOnlyPaths.length > 0) : [];

  for (const entry of classifyEntries) {
    const expected = entry.classifyOnlyPaths.map((p) => String(p).toLowerCase());
    if (paths.length === expected.length && expected.every((p) => paths.includes(p))) {
      details.suspectedIssue = entry.issue;
      details.suspectedIssueNote = `#${entry.issue} suspected (${entry.classifyOnlyPaths.join(',')} only; ${entry.summary})`;
      return true;
    }
  }
  return false;
}

function recordFinding(params) {
  const { findingsDir, batchSeedHex, stage, details, kernelsSource, casesSource, options, fuzzToolDll, reducedKernelSeeds, catalogEntries } = params;
  fs.mkdirSync(findingsDir, { recursive: true });
  classifySuspectedIssue(details, catalogEntries);

  let findingDir = path.join(findingsDir, `${batchSeedHex}_${stage}`);
  let reductionMs = 0;

  const kernelDedupKey = details && details.kernel ? `${batchSeedHex}:${details.kernel}` : null;
  const isKnownClassifiedIssue = details && details.suspectedIssue &&
    (!options || !Array.isArray(options.allowedIssues) || !options.allowedIssues.includes(details.suspectedIssue));
  const shouldReduce = options && !options.noReduce && fuzzToolDll && details && details.kernel &&
    details.status !== 'hang' && !isKnownClassifiedIssue &&
    (!reducedKernelSeeds || !reducedKernelSeeds.has(kernelDedupKey));

  if (shouldReduce) {
    if (reducedKernelSeeds && kernelDedupKey) reducedKernelSeeds.add(kernelDedupKey);
    const redStartMs = Date.now();
    const tmpSourcePath = path.join(findingsDir, `_tmp_${batchSeedHex}_${details.kernel}.cu.cs`);
    const tmpReducedPath = path.join(findingsDir, `_tmp_${batchSeedHex}_${details.kernel}.reduced.cu.cs`);
    try {
      fs.writeFileSync(tmpSourcePath, kernelsSource, 'utf8');
      const failingPathsStr = Array.isArray(details.failingPaths) && details.failingPaths.length > 0
        ? details.failingPaths.join(',')
        : (details.dialect || 'Native,Cuda');
      const failureKindStr = details.failureKind || details.status || 'CellMismatch';
      const columnStr = details.column || 'hash';
      const inputSeedVal = details.inputSeed !== undefined ? String(details.inputSeed) : '0';
      const hasCudaFailure = Array.isArray(details.failingPaths) && details.failingPaths.some((p) => String(p).toLowerCase() === 'cuda');
      const reduceProfile = (stage.startsWith('cuda_') || hasCudaFailure) ? 'G' : 'C';

      const reduceArgs = ['exec', fuzzToolDll, 'reduce', '--source', tmpSourcePath, '--fuzz-seed', batchSeedHex, '--kernel', String(details.kernel), '--input-seed', inputSeedVal, '--profile', reduceProfile, '--work-dir', path.join(options.workDir, '_reduce'), '--repo-root', repoRoot, '--failing-paths', failingPathsStr, '--failure-kind', failureKindStr, '--column', columnStr, '--out', tmpReducedPath];
      if (options.noGpuLock || isAlreadyInsideGpuLock()) reduceArgs.push('--no-gpu-lock');

      const redRes = runCommand('dotnet', reduceArgs, { timeoutMs: DEFAULT_REDUCE_TIMEOUT_MS });
      if (redRes.status === 0 && fs.existsSync(tmpReducedPath)) {
        const redInfo = parseBatchJsonl(redRes.stdout).find((e) => e.status === 'reduced') || {};
        details.fingerprint = redInfo.fingerprint || null;
        details.originalStatements = redInfo.originalStatements;
        details.reducedStatements = redInfo.reducedStatements;

        const promoteArgs = ['exec', fuzzToolDll, 'promote', '--source', tmpReducedPath, '--fuzz-seed', batchSeedHex, '--kernel', String(details.kernel), '--input-seed', inputSeedVal, '--failing-paths', failingPathsStr, '--failure-kind', failureKindStr, '--column', columnStr, '--findings-dir', findingsDir];
        const promRes = runCommand('dotnet', promoteArgs, { timeoutMs: 60_000 });
        const promInfo = parseBatchJsonl(promRes.stdout).find((e) => e.status === 'promoted');
        if (promInfo && promInfo.reproKernelPath) {
          findingDir = path.dirname(promInfo.reproKernelPath);
          details.fingerprint = promInfo.fingerprint || details.fingerprint;
          details.reproKernelPath = promInfo.reproKernelPath;
          details.knownDivergenceEntry = promInfo.knownDivergenceEntry || null;
        }
      }
    } finally {
      if (fs.existsSync(tmpSourcePath)) fs.unlinkSync(tmpSourcePath);
      if (fs.existsSync(tmpReducedPath)) fs.unlinkSync(tmpReducedPath);
      reductionMs = Date.now() - redStartMs;
    }
  }

  fs.mkdirSync(findingDir, { recursive: true });
  fs.writeFileSync(path.join(findingDir, 'FuzzBatchKernels.cu.cs'), kernelsSource, 'utf8');
  fs.writeFileSync(path.join(findingDir, 'FuzzBatchCases.cs'), casesSource, 'utf8');
  fs.writeFileSync(path.join(findingDir, 'finding.json'), JSON.stringify(details, null, 2) + '\n', 'utf8');
  fs.appendFileSync(path.join(findingsDir, 'occurrences.jsonl'), JSON.stringify(details) + '\n', 'utf8');
  return reductionMs;
}

function parseBatchJsonl(stdout) {
  const lines = stdout.split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
  const events = [];
  for (const line of lines) {
    if (line.startsWith('{') && line.endsWith('}')) {
      try {
        events.push(JSON.parse(line));
      } catch {
        // Ignore non-JSON console lines
      }
    }
  }
  return events;
}

function mergeKernelFailureEvents(cpuEvents, cudaEvents) {
  const byKernel = new Map();
  const nonKernelEvents = [];

  function ingest(ev, stagePrefix) {
    if (!ev || (ev.status !== 'failed' && ev.status !== 'harness_error')) return;
    if (!ev.kernel) {
      nonKernelEvents.push({ stage: `${stagePrefix}_batch`, event: ev });
      return;
    }
    const existing = byKernel.get(ev.kernel);
    if (!existing) {
      const copy = { ...ev, failingPaths: Array.isArray(ev.failingPaths) ? [...ev.failingPaths] : [] };
      byKernel.set(ev.kernel, { stage: `${stagePrefix}_${ev.kernel}`, event: copy });
    } else {
      const mergedPaths = new Set(existing.event.failingPaths || []);
      for (const p of (ev.failingPaths || [])) mergedPaths.add(p);
      const order = { managed: 0, native: 1, cuda: 2 };
      existing.event.failingPaths = Array.from(mergedPaths).sort((a, b) => (order[String(a).toLowerCase()] ?? 9) - (order[String(b).toLowerCase()] ?? 9));
      if (stagePrefix === 'cuda') existing.stage = `cuda_${ev.kernel}`;
    }
  }

  for (const ev of cpuEvents) ingest(ev, 'cpu');
  for (const ev of cudaEvents) ingest(ev, 'cuda');
  return [...nonKernelEvents, ...byKernel.values()];
}

function prepareWorkspace(options) {
  const fuzzToolProj = path.join(repoRoot, 'tools', 'fuzz-diff', 'Kassyi.GSharp.Tools.FuzzDiff.csproj');
  const fuzzToolDll = path.join(repoRoot, 'tools', 'fuzz-diff', 'bin', hostOs, 'Debug', 'net10.0', 'Kassyi.GSharp.Tools.FuzzDiff.dll');
  ensurePrereqsBuilt(fuzzToolProj);

  const cudaArch = options.profile === 'G' ? detectHostCudaArch() : null;
  if (options.profile === 'G' && cudaArch) {
    process.stdout.write(`[fuzz-diff] Detected host CUDA architecture: ${cudaArch}\n`);
  }

  const catalogEntries = loadKnownIssuesCatalog();
  const activeKnownIssues = selectActiveKnownIssues(catalogEntries, options.profile, options.allowedIssues);
  if (activeKnownIssues.length > 0) {
    process.stdout.write(`[fuzz-diff] Active known-issue switches on profile ${options.profile}: ${activeKnownIssues.map((x) => `#${x.issue}`).join(', ')}\n`);
  }

  fs.mkdirSync(options.workDir, { recursive: true });
  fs.mkdirSync(options.outDir, { recursive: true });

  return {
    options, fuzzToolDll, cudaArch, catalogEntries, activeKnownIssues,
    findingsDir: path.join(options.workDir, 'findings'),
    startMs: Date.now(), timeLimitMs: options.minutes * 60 * 1000,
    batchIndex: 0, totalKernels: 0, totalFailures: 0,
    suspected1078Count: 0, hangCount: 0, otherFindingsCount: 0,
    totalReductionMs: 0, reducedKernelSeeds: new Set(),
  };
}

function runSingleBatch(ctx, batchIndex, seedHex) {
  const { options, fuzzToolDll, cudaArch } = ctx;
  const genArgs = [
    'exec', fuzzToolDll, 'gen',
    '--fuzz-seed', seedHex, '--batch-size', String(options.batchSize),
    '--profile', options.profile, '--out-dir', options.outDir, '--repo-root', repoRoot,
  ];
  if (options.inputSeed !== null) genArgs.push('--input-seed', String(options.inputSeed));
  for (const allowedIssue of options.allowedIssues) genArgs.push('--allow-issue', String(allowedIssue));
  if (options.swarmFocus) genArgs.push('--swarm-focus', options.swarmFocus);
  if (cudaArch) genArgs.push('--cuda-arch', cudaArch);

  const genRes = runCommand('dotnet', genArgs, { timeoutMs: 60_000 });
  if (genRes.status !== 0) {
    process.stderr.write(genRes.stdout + '\n' + genRes.stderr + '\n');
    fail(`fuzz-diff gen failed on seed ${seedHex}`);
  }

  const kernelsSource = fs.readFileSync(path.join(options.outDir, 'FuzzBatchKernels.cu.cs'), 'utf8');
  const casesSource = fs.readFileSync(path.join(options.outDir, 'FuzzBatchCases.cs'), 'utf8');

  const cpuProj = path.join(options.workDir, 'FuzzBatch.Cpu.csproj');
  const cpuDll = path.join(options.workDir, 'bin', hostOs, 'FuzzBatch.Cpu', 'Debug', 'net10.0', 'FuzzBatch.Cpu.dll');
  const skipCpuCpp = options.profile === 'M' ? 'true' : 'false';
  const cpuAssets = path.join(options.workDir, 'obj', hostOs, 'FuzzBatch.Cpu', 'project.assets.json');
  const cpuBuildArgs = ['build', cpuProj, '-c', 'Debug', '-m:1', '-nodeReuse:false', '--no-dependencies', `-p:GSharpSkipCpuCompilation=${skipCpuCpp}`];
  if (fs.existsSync(cpuAssets)) cpuBuildArgs.push('--no-restore');

  let cpuEvents = [];
  let cudaEvents = [];
  const stageFailures = [];

  const cpuBuild = runCommand('dotnet', cpuBuildArgs, { timeoutMs: DEFAULT_BUILD_TIMEOUT_MS });
  if (cpuBuild.status !== 0) {
    const isHang = cpuBuild.timedOut;
    const status = isHang ? 'hang' : 'compile_error';
    stageFailures.push({
      stage: isHang ? 'cpu_build_hang' : 'cpu_build',
      details: { status, failureKind: status, dialect: 'Cpu', failingPaths: options.profile === 'M' ? ['Managed'] : ['Native'], batchFuzzSeed: seedHex, stdout: cpuBuild.stdout, stderr: cpuBuild.stderr },
    });
    process.stdout.write(`[fuzz-diff] Batch ${batchIndex + 1} (${seedHex}): CPU build ${isHang ? 'timed out' : 'failed'}! Recorded to findings.\n`);
  } else {
    const cpuRunArgs = ['exec', cpuDll];
    if (options.profile === 'M') cpuRunArgs.push('--managed-only');
    if (options.inputSeed !== null) cpuRunArgs.push('--input-seed', String(options.inputSeed));

    const cpuRun = runCommand('dotnet', cpuRunArgs, { timeoutMs: DEFAULT_RUN_TIMEOUT_MS });
    cpuEvents = parseBatchJsonl(cpuRun.stdout);
    if (cpuRun.timedOut) {
      stageFailures.push({
        stage: 'cpu_hang',
        details: { status: 'hang', failureKind: 'hang', dialect: 'Cpu', failingPaths: options.profile === 'M' ? ['Managed'] : ['Managed', 'Native'], batchFuzzSeed: seedHex, timeoutMs: DEFAULT_RUN_TIMEOUT_MS, stdout: cpuRun.stdout, stderr: cpuRun.stderr },
      });
    } else if (cpuRun.status !== 0 && cpuEvents.length === 0) {
      stageFailures.push({
        stage: 'cpu_crash',
        details: { status: 'crash', failureKind: 'crash', dialect: 'Cpu', failingPaths: options.profile === 'M' ? ['Managed'] : ['Native'], batchFuzzSeed: seedHex, exitCode: cpuRun.status, stdout: cpuRun.stdout, stderr: cpuRun.stderr },
      });
    }
  }

  if (options.profile === 'G') {
    const cudaProj = path.join(options.workDir, 'FuzzBatch.Cuda.csproj');
    const cudaDll = path.join(options.workDir, 'bin', hostOs, 'FuzzBatch.Cuda', 'Debug', 'net10.0', 'FuzzBatch.Cuda.dll');
    const cudaAssets = path.join(options.workDir, 'obj', hostOs, 'FuzzBatch.Cuda', 'project.assets.json');
    const cudaBuildArgs = ['build', cudaProj, '-c', 'Debug', '-m:1', '-nodeReuse:false', '--no-dependencies'];
    if (fs.existsSync(cudaAssets)) cudaBuildArgs.push('--no-restore');

    const cudaBuild = runCommand('dotnet', cudaBuildArgs, { timeoutMs: DEFAULT_BUILD_TIMEOUT_MS });
    if (cudaBuild.status !== 0) {
      const isHang = cudaBuild.timedOut;
      const status = isHang ? 'hang' : 'compile_error';
      stageFailures.push({
        stage: isHang ? 'cuda_build_hang' : 'cuda_build',
        details: { status, failureKind: status, dialect: 'Cuda', failingPaths: ['Cuda'], batchFuzzSeed: seedHex, stdout: cudaBuild.stdout, stderr: cudaBuild.stderr },
      });
      process.stdout.write(`[fuzz-diff] Batch ${batchIndex + 1} (${seedHex}): CUDA build ${isHang ? 'timed out' : 'failed'}! Recorded to findings.\n`);
    } else {
      const useLock = !options.noGpuLock && !isAlreadyInsideGpuLock();
      const inputSeedArg = options.inputSeed !== null ? ` --input-seed ${options.inputSeed}` : '';
      let cudaRun;
      if (useLock) {
        const lockScript = path.join(repoRoot, 'scripts', 'agent', 'gpu-lock.js');
        cudaRun = runCommand('node', [lockScript, '--', `dotnet exec "${cudaDll}"${inputSeedArg}`], { timeoutMs: DEFAULT_RUN_TIMEOUT_MS });
      } else {
        const runArgs = ['exec', cudaDll];
        if (options.inputSeed !== null) runArgs.push('--input-seed', String(options.inputSeed));
        cudaRun = runCommand('dotnet', runArgs, { timeoutMs: DEFAULT_RUN_TIMEOUT_MS });
      }

      cudaEvents = parseBatchJsonl(cudaRun.stdout);
      if (cudaRun.timedOut) {
        stageFailures.push({
          stage: 'cuda_hang',
          details: { status: 'hang', failureKind: 'hang', dialect: 'Cuda', failingPaths: ['Cuda'], batchFuzzSeed: seedHex, timeoutMs: DEFAULT_RUN_TIMEOUT_MS, stdout: cudaRun.stdout, stderr: cudaRun.stderr },
        });
      } else if (cudaRun.status !== 0 && cudaEvents.length === 0) {
        stageFailures.push({
          stage: 'cuda_crash',
          details: { status: 'crash', failureKind: 'crash', dialect: 'Cuda', failingPaths: ['Cuda'], batchFuzzSeed: seedHex, exitCode: cudaRun.status, stdout: cudaRun.stdout, stderr: cudaRun.stderr },
        });
      }
    }
  }

  return { kernelsSource, casesSource, stageFailures, mergedFailures: mergeKernelFailureEvents(cpuEvents, cudaEvents) };
}

function handleBatchFindings(ctx, seedHex, batchResult) {
  const { findingsDir, options, fuzzToolDll, reducedKernelSeeds, catalogEntries } = ctx;
  const { kernelsSource, casesSource, stageFailures, mergedFailures } = batchResult;

  for (const sf of stageFailures) {
    ctx.totalFailures++;
    if (sf.details.status === 'hang') ctx.hangCount++;
    else ctx.otherFindingsCount++;
    recordFinding({ findingsDir, batchSeedHex: seedHex, stage: sf.stage, details: sf.details, kernelsSource, casesSource, options, fuzzToolDll, reducedKernelSeeds, catalogEntries });
  }

  for (const item of mergedFailures) {
    ctx.totalFailures++;
    ctx.totalReductionMs += recordFinding({ findingsDir, batchSeedHex: seedHex, stage: item.stage, details: item.event, kernelsSource, casesSource, options, fuzzToolDll, reducedKernelSeeds, catalogEntries });
    if (item.event.suspectedIssue === 1078) ctx.suspected1078Count++;
    else if (item.event.status === 'hang') ctx.hangCount++;
    else ctx.otherFindingsCount++;
  }
}

function writeRunSummary(ctx) {
  const { options, activeKnownIssues, startMs, batchIndex, totalKernels, totalFailures, suspected1078Count, hangCount, otherFindingsCount, totalReductionMs } = ctx;
  const elapsedSec = ((Date.now() - startMs) / 1000).toFixed(2);
  const pureEvalTotalSec = Math.max((Date.now() - startMs - totalReductionMs) / 1000, 0.001);
  const finalKernelsPerHour = Math.round((totalKernels / Math.max((Date.now() - startMs) / 1000, 0.001)) * 3600);
  const finalPureKernelsPerHour = Math.round((totalKernels / pureEvalTotalSec) * 3600);
  const summary = {
    profile: options.profile, allowedIssues: options.allowedIssues, swarmFocus: options.swarmFocus || 'default',
    activeKnownIssues, batches: batchIndex, batchSize: options.batchSize, totalKernels,
    elapsedSeconds: Number(elapsedSec), reductionSeconds: Number((totalReductionMs / 1000).toFixed(2)),
    kernelsPerHour: finalKernelsPerHour, pureEvalKernelsPerHour: finalPureKernelsPerHour,
    findingsCount: totalFailures, suspected1078Count, hangCount, otherFindingsCount,
  };
  fs.writeFileSync(path.join(options.workDir, 'summary.json'), JSON.stringify(summary, null, 2) + '\n', 'utf8');
  process.stdout.write(
    `[fuzz-diff] Completed ${batchIndex} batches (${totalKernels} kernels) in ${elapsedSec}s (${finalKernelsPerHour} kernels/hr overall, ${finalPureKernelsPerHour} kernels/hr eval). Findings: ${totalFailures} (#1078 suspected: ${suspected1078Count}, hangs: ${hangCount}, other: ${otherFindingsCount}).\n`
  );
}

function normalizeSlashes(p) {
  return String(p || '').replace(/\\/g, '/');
}

function buildCompareDirectoryBuildProps() {
  return [
    '<Project>',
    '  <PropertyGroup>',
    "    <GSharpHostOs Condition=\"$([MSBuild]::IsOSPlatform('Windows'))\">win</GSharpHostOs>",
    "    <GSharpHostOs Condition=\"'$(GSharpHostOs)' == '' and $([MSBuild]::IsOSPlatform('Linux'))\">linux</GSharpHostOs>",
    "    <GSharpHostOs Condition=\"'$(GSharpHostOs)' == '' and $([MSBuild]::IsOSPlatform('OSX'))\">osx</GSharpHostOs>",
    "    <GSharpHostOs Condition=\"'$(GSharpHostOs)' == ''\">unix</GSharpHostOs>",
    '    <BaseOutputPath>bin\\$(GSharpHostOs)\\$(MSBuildProjectName)\\</BaseOutputPath>',
    '    <BaseIntermediateOutputPath>obj\\$(GSharpHostOs)\\$(MSBuildProjectName)\\</BaseIntermediateOutputPath>',
    '    <DefaultItemExcludes>$(DefaultItemExcludes);bin\\**;obj\\**;findings\\**</DefaultItemExcludes>',
    '    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>',
    '    <GenerateDocumentationFile>false</GenerateDocumentationFile>',
    '    <GSharpExportDeviceCode>false</GSharpExportDeviceCode>',
    '    <GSharpGenerateSnapshotTestCases>false</GSharpGenerateSnapshotTestCases>',
    '    <NoWarn>$(NoWarn);GS0001;GS0002;CS0162;CS0169;CS0219;CS1591;CS1717;CS1718;CS8019;CS8321;SA1600</NoWarn>',
    '  </PropertyGroup>',
    '</Project>',
    '',
  ].join('\n');
}

function buildCompareProjectXml(dialect, relRepo) {
  const isCuda = dialect.toLowerCase() === 'cuda';
  const defineConstant = isCuda ? 'GSHARP_CUDA' : 'GSHARP_CPU';
  return [
    '<Project Sdk="Microsoft.NET.Sdk">',
    '  <PropertyGroup>',
    '    <OutputType>Library</OutputType>',
    '    <TargetFramework>net10.0</TargetFramework>',
    '    <ImplicitUsings>enable</ImplicitUsings>',
    '    <Nullable>enable</Nullable>',
    '    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>',
    `    <GSharpTargetDialect>${dialect}</GSharpTargetDialect>`,
    '    <GSharpEmitSource>true</GSharpEmitSource>',
    '    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>',
    '    <GSharpExportDeviceCode>false</GSharpExportDeviceCode>',
    '    <GSharpGenerateSnapshotTestCases>false</GSharpGenerateSnapshotTestCases>',
    '    <GSharpSkipCpuCompilation>true</GSharpSkipCpuCompilation>',
    '    <GSharpSkipCudaCompilation>true</GSharpSkipCudaCompilation>',
    `    <GSharpCompilerTaskAssembly>$(MSBuildThisFileDirectory)${relRepo}/src/Compiler/Kassyi.GSharp.Compiler.Tasks/bin/$(GSharpHostOs)/$(Configuration)/netstandard2.0/Kassyi.GSharp.Compiler.Tasks.dll</GSharpCompilerTaskAssembly>`,
    `    <DefineConstants>$(DefineConstants);${defineConstant}</DefineConstants>`,
    '  </PropertyGroup>',
    '  <ItemGroup>',
    '    <CompilerVisibleProperty Include="GSharpTargetDialect" />',
    '    <CompilerVisibleProperty Include="GSharpEmitSource" />',
    '    <CompilerVisibleProperty Include="GSharpGenerateSnapshotTestCases" />',
    '    <Compile Include="CompareTargetKernels.cu.cs" />',
    '  </ItemGroup>',
    '  <ItemGroup>',
    `    <ProjectReference Include="${relRepo}/src/Core/Kassyi.GSharp.Core/Kassyi.GSharp.Core.csproj" />`,
    `    <ProjectReference Include="${relRepo}/src/Runtime/Kassyi.GSharp.Runtime/Kassyi.GSharp.Runtime.csproj" />`,
    `    <ProjectReference Include="${relRepo}/src/Compiler/Kassyi.GSharp.Compiler/Kassyi.GSharp.Compiler.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="true" />`,
    `    <ProjectReference Include="${relRepo}/src/Compiler/Kassyi.GSharp.Compiler.Tasks/Kassyi.GSharp.Compiler.Tasks.csproj" ReferenceOutputAssembly="false" />`,
    '  </ItemGroup>',
    '  <UsingTask TaskName="Kassyi.GSharp.Compiler.Tasks.CompileCudaToFatbinTask" AssemblyFile="$(GSharpCompilerTaskAssembly)" />',
    '  <UsingTask TaskName="Kassyi.GSharp.Compiler.Tasks.CompileCppToNativeTask" AssemblyFile="$(GSharpCompilerTaskAssembly)" />',
    `  <Import Project="${relRepo}/src/Compiler/Kassyi.GSharp.Compiler/build/Kassyi.GSharp.Compiler.targets" />`,
    '</Project>',
    '',
  ].join('\n');
}

function writeCompareProjectScaffolds(compareDir, targetRepoRoot) {
  fs.mkdirSync(compareDir, { recursive: true });
  const relRepo = normalizeSlashes(path.relative(compareDir, targetRepoRoot));
  fs.writeFileSync(path.join(compareDir, 'Directory.Build.props'), buildCompareDirectoryBuildProps(), 'utf8');
  fs.writeFileSync(path.join(compareDir, 'FuzzCompareCpu.csproj'), buildCompareProjectXml('Cpu', relRepo), 'utf8');
  fs.writeFileSync(path.join(compareDir, 'FuzzCompareCuda.csproj'), buildCompareProjectXml('Cuda', relRepo), 'utf8');
}

function runCompareGeneratorBuild(compareDir, dialect, kernelsSourceContent) {
  const projName = `FuzzCompare${dialect}`;
  const csprojPath = path.join(compareDir, `${projName}.csproj`);
  const targetKernelsPath = path.join(compareDir, 'CompareTargetKernels.cu.cs');
  fs.writeFileSync(targetKernelsPath, kernelsSourceContent, 'utf8');

  const ext = dialect.toLowerCase() === 'cuda' ? 'cu' : 'cpp';
  const objNetDir = path.join(compareDir, 'obj', hostOs, projName, 'Debug', 'net10.0');
  const binNetDir = path.join(compareDir, 'bin', hostOs, projName, 'Debug', 'net10.0');
  const aotFilePath = path.join(objNetDir, `Kassyi.GSharp.Kernels_${projName}.g.${ext}`);
  const wrapperGenPath = path.join(
    objNetDir,
    'generated',
    'Kassyi.GSharp.Compiler',
    'Kassyi.GSharp.Compiler.GpuKernelGenerator',
    'Kassyi_GSharp_Tests_Shared_Differential_FuzzBatchKernels.g.cs'
  );
  const objDllPath = path.join(objNetDir, `${projName}.dll`);
  const binDllPath = path.join(binNetDir, `${projName}.dll`);

  for (const stale of [aotFilePath, wrapperGenPath, objDllPath, binDllPath]) {
    if (fs.existsSync(stale)) {
      try { fs.unlinkSync(stale); } catch { /* ignore */ }
    }
  }

  const assetsPath = path.join(compareDir, 'obj', hostOs, projName, 'project.assets.json');
  const buildArgs = [
    'build', csprojPath, '-c', 'Debug', '-m:1', '-nodeReuse:false', '--no-dependencies',
    '-p:GSharpSkipCpuCompilation=true', '-p:GSharpSkipCudaCompilation=true',
  ];
  if (fs.existsSync(assetsPath)) buildArgs.push('--no-restore');

  const buildRes = runCommand('dotnet', buildArgs, { cwd: compareDir, timeoutMs: DEFAULT_BUILD_TIMEOUT_MS });
  if (buildRes.status !== 0 || !fs.existsSync(aotFilePath)) {
    return { ok: false, stdout: buildRes.stdout, stderr: buildRes.stderr, aotSource: '', wrapperGeneratedCs: '' };
  }

  const aotSource = fs.readFileSync(aotFilePath, 'utf8');
  const wrapperGeneratedCs = fs.existsSync(wrapperGenPath) ? fs.readFileSync(wrapperGenPath, 'utf8') : '';
  return { ok: true, stdout: buildRes.stdout, stderr: buildRes.stderr, aotSource, wrapperGeneratedCs };
}

function stripSymbolHeader(sourceText) {
  const lines = String(sourceText || '').replace(/\r\n/g, '\n').split('\n');
  if (lines.length > 0 && lines[0].startsWith('// gsharp-symbols:')) {
    return { hadHeader: true, body: lines.slice(1).join('\n'), lines: lines.slice(1) };
  }
  return { hadHeader: false, body: lines.join('\n'), lines };
}

function extractKernelNamesFromCSharp(kernelsSource) {
  const names = [];
  const re = /\[GpuKernel\]\s*public\s+static\s+void\s+([A-Za-z0-9_]+)\s*\(/g;
  let m;
  while ((m = re.exec(kernelsSource)) !== null) {
    names.push(m[1]);
  }
  return names;
}

function extractPerKernelSourcesFromWrapper(wrapperCs) {
  const map = new Map();
  if (!wrapperCs) return map;
  const re = /public const string ([A-Za-z0-9_]+)_Source = @"([\s\S]*?)";\r?\n\s*public static readonly string \1_FullSource=/g;
  let m;
  while ((m = re.exec(wrapperCs)) !== null) {
    const kernelName = m[1];
    const unescaped = m[2].replace(/""/g, '"').replace(/\r\n/g, '\n');
    map.set(kernelName, unescaped);
  }
  return map;
}

function findFirstLineDiff(baseText, headText) {
  const baseLines = String(baseText || '').replace(/\r\n/g, '\n').split('\n');
  const headLines = String(headText || '').replace(/\r\n/g, '\n').split('\n');
  const maxLen = Math.max(baseLines.length, headLines.length);
  for (let i = 0; i < maxLen; i++) {
    const b = i < baseLines.length ? baseLines[i] : '<EOF>';
    const h = i < headLines.length ? headLines[i] : '<EOF>';
    if (b !== h) {
      return { line: i + 1, baseText: b, headText: h };
    }
  }
  return null;
}

function computeAotLineOffset(aotBody, kernelChunk) {
  if (!aotBody || !kernelChunk) return 0;
  const idx = aotBody.indexOf(kernelChunk);
  if (idx < 0) return 0;
  return aotBody.slice(0, idx).split('\n').length;
}

function compareBatchDialect(baseRes, headRes, dialect, kernelNames) {
  const baseStripped = stripSymbolHeader(baseRes.aotSource);
  const headStripped = stripSymbolHeader(headRes.aotSource);
  if (baseStripped.body === headStripped.body) {
    return [];
  }

  const baseKernelMap = extractPerKernelSourcesFromWrapper(baseRes.wrapperGeneratedCs);
  const headKernelMap = extractPerKernelSourcesFromWrapper(headRes.wrapperGeneratedCs);
  const diffs = [];

  for (const kernelName of kernelNames) {
    const baseK = baseKernelMap.get(kernelName);
    const headK = headKernelMap.get(kernelName);
    if (baseK !== undefined && headK !== undefined && baseK !== headK) {
      const firstDiff = findFirstLineDiff(baseK, headK);
      if (firstDiff) {
        const baseOffset = computeAotLineOffset(baseStripped.body, baseK);
        const aotLine = baseOffset > 0 ? baseOffset + firstDiff.line : firstDiff.line + 1;
        diffs.push({
          kernel: kernelName,
          dialect,
          scope: 'kernel',
          line: firstDiff.line,
          aotLine,
          baseText: firstDiff.baseText,
          headText: firstDiff.headText,
        });
      }
    }
  }

  if (diffs.length === 0) {
    const firstDiff = findFirstLineDiff(baseStripped.body, headStripped.body);
    if (firstDiff) {
      diffs.push({
        kernel: kernelNames[0] || 'Kernel_00',
        dialect,
        scope: 'module',
        line: firstDiff.line,
        aotLine: firstDiff.line + (baseStripped.hadHeader ? 1 : 0),
        baseText: firstDiff.baseText,
        headText: firstDiff.headText,
      });
    }
  }

  return diffs;
}

function parseCompareDialects(raw) {
  const parts = String(raw || 'Cpu,Cuda').split(',').map((s) => s.trim()).filter(Boolean);
  const result = [];
  for (const p of parts) {
    const norm = p.toLowerCase() === 'cuda' ? 'Cuda' : 'Cpu';
    if (!result.includes(norm)) result.push(norm);
  }
  return result.length > 0 ? result : ['Cpu', 'Cuda'];
}

function runCompareSingleMode(options) {
  if (!options.compareSingle || !fs.existsSync(options.compareSingle)) {
    process.exit(1);
  }
  const candidateSource = fs.readFileSync(options.compareSingle, 'utf8');
  const headCompareDir = path.join(options.workDir, 'compare-head');
  const baseCompareDir = path.join(options.workDir, 'compare-base');
  const dialects = parseCompareDialects(options.compareDialect);

  for (const dialect of dialects) {
    const baseRes = runCompareGeneratorBuild(baseCompareDir, dialect, candidateSource);
    const headRes = runCompareGeneratorBuild(headCompareDir, dialect, candidateSource);
    if (!baseRes.ok || !headRes.ok) {
      process.exit(1);
    }

    if (options.compareScope === 'kernel' && options.compareKernel) {
      const baseKernelMap = extractPerKernelSourcesFromWrapper(baseRes.wrapperGeneratedCs);
      const headKernelMap = extractPerKernelSourcesFromWrapper(headRes.wrapperGeneratedCs);
      const baseK = baseKernelMap.get(options.compareKernel);
      const headK = headKernelMap.get(options.compareKernel);
      if (baseK !== undefined && headK !== undefined) {
        if (baseK !== headK) {
          process.exit(0);
        }
        continue;
      }
    }

    const baseStripped = stripSymbolHeader(baseRes.aotSource);
    const headStripped = stripSymbolHeader(headRes.aotSource);
    if (baseStripped.body !== headStripped.body) {
      process.exit(0);
    }
  }

  process.exit(1);
}

function runCompareBaseMode(options) {
  const totalBatches = options.batches !== null ? options.batches : 20;
  const fuzzToolProj = path.join(repoRoot, 'tools', 'fuzz-diff', 'Kassyi.GSharp.Tools.FuzzDiff.csproj');
  const fuzzToolDll = path.join(repoRoot, 'tools', 'fuzz-diff', 'bin', hostOs, 'Debug', 'net10.0', 'Kassyi.GSharp.Tools.FuzzDiff.dll');
  ensurePrereqsBuilt(fuzzToolProj, true);

  fs.mkdirSync(options.workDir, { recursive: true });
  fs.mkdirSync(options.outDir, { recursive: true });
  const findingsDir = path.join(options.workDir, 'findings');
  const baseWorktreeDir = path.join(options.workDir, 'base-worktree');
  const headCompareDir = path.join(options.workDir, 'compare-head');
  const baseCompareDir = path.join(options.workDir, 'compare-base');

  spawnSync('git', ['worktree', 'remove', '--force', baseWorktreeDir], { cwd: repoRoot, encoding: 'utf8' });
  if (fs.existsSync(baseWorktreeDir)) {
    fs.rmSync(baseWorktreeDir, { recursive: true, force: true });
  }

  process.stdout.write(`[fuzz-compare] Creating detached worktree for '${options.compareBase}' at ${baseWorktreeDir}...\n`);
  const wtAdd = runCommand('git', ['worktree', 'add', '--detach', baseWorktreeDir, options.compareBase]);
  if (wtAdd.status !== 0) {
    fail(`Failed to create git worktree for '${options.compareBase}': ${wtAdd.stderr || wtAdd.stdout}`);
  }

  const startMs = Date.now();
  let totalKernels = 0;
  let totalDiffKernels = 0;
  const allDiffRecords = [];
  const reducedBySignature = new Map();

  try {
    process.stdout.write(`[fuzz-compare] Building compiler prerequisites in base worktree (${options.compareBase})...\n`);
    ensureCompilerPrereqsBuiltForRoot(baseWorktreeDir, false);

    writeCompareProjectScaffolds(headCompareDir, repoRoot);
    writeCompareProjectScaffolds(baseCompareDir, baseWorktreeDir);

    for (let batchIndex = 0; batchIndex < totalBatches; batchIndex++) {
      const currentSeed = BigInt.asUintN(64, options.fuzzSeed + BigInt(batchIndex));
      const seedHex = formatHexSeed(currentSeed);
      const batchStartMs = Date.now();

      const genArgs = [
        'exec', fuzzToolDll, 'gen',
        '--fuzz-seed', seedHex, '--batch-size', String(options.batchSize),
        '--profile', options.profile, '--out-dir', options.outDir, '--repo-root', repoRoot,
      ];
      if (options.inputSeed !== null) genArgs.push('--input-seed', String(options.inputSeed));
      for (const allowedIssue of options.allowedIssues) genArgs.push('--allow-issue', String(allowedIssue));
      if (options.swarmFocus) genArgs.push('--swarm-focus', options.swarmFocus);

      const genRes = runCommand('dotnet', genArgs, { timeoutMs: 60_000 });
      if (genRes.status !== 0) {
        process.stderr.write(genRes.stdout + '\n' + genRes.stderr + '\n');
        fail(`fuzz-diff gen failed on seed ${seedHex}`);
      }

      const kernelsSource = fs.readFileSync(path.join(options.outDir, 'FuzzBatchKernels.cu.cs'), 'utf8');
      const kernelNames = extractKernelNamesFromCSharp(kernelsSource);
      totalKernels += kernelNames.length;

      const byKernel = new Map();
      for (const dialect of ['Cpu', 'Cuda']) {
        const baseRes = runCompareGeneratorBuild(baseCompareDir, dialect, kernelsSource);
        const headRes = runCompareGeneratorBuild(headCompareDir, dialect, kernelsSource);
        if (!baseRes.ok || !headRes.ok) {
          fail(`Generator CoreCompile failed on batch ${seedHex} (${dialect}):\nBase:\n${baseRes.stdout}\n${baseRes.stderr}\nHead:\n${headRes.stdout}\n${headRes.stderr}`);
        }

        const dialectDiffs = compareBatchDialect(baseRes, headRes, dialect, kernelNames);
        for (const d of dialectDiffs) {
          let entry = byKernel.get(d.kernel);
          if (!entry) {
            entry = { seedHex, kernel: d.kernel, dialects: [], scope: d.scope, diffsByDialect: {} };
            byKernel.set(d.kernel, entry);
          }
          if (!entry.dialects.includes(dialect)) entry.dialects.push(dialect);
          entry.diffsByDialect[dialect] = {
            line: d.line,
            aotLine: d.aotLine,
            baseText: d.baseText,
            headText: d.headText,
          };
        }
      }

      for (const entry of byKernel.values()) {
        totalDiffKernels++;
        process.stdout.write(
          `[fuzz-compare] DIFF seed=${entry.seedHex} kernel=${entry.kernel} dialects=${entry.dialects.join(',')}\n`
        );
        for (const dialect of entry.dialects) {
          const info = entry.diffsByDialect[dialect];
          process.stdout.write(
            `  [${dialect}] kernel line ${info.line} (AOT line ${info.aotLine}):\n    base (${options.compareBase}): ${info.baseText}\n    working-tree: ${info.headText}\n`
          );
        }

        fs.mkdirSync(findingsDir, { recursive: true });
        const findingDir = path.join(findingsDir, `${entry.seedHex}_compare_${entry.kernel}`);
        fs.mkdirSync(findingDir, { recursive: true });

        const firstDialect = entry.dialects[0];
        const firstInfo = entry.diffsByDialect[firstDialect];
        const diffSigKey = `${entry.dialects.join(',')}|${firstInfo.baseText.replace(/Kernel_\d+/g, 'K')}|${firstInfo.headText.replace(/Kernel_\d+/g, 'K')}`;

        let reducedKernelSource = null;
        let reductionMeta = {};
        if (!options.noReduce) {
          if (reducedBySignature.has(diffSigKey)) {
            const cached = reducedBySignature.get(diffSigKey);
            reducedKernelSource = cached.reducedKernelSource;
            reductionMeta = { ...cached.reductionMeta, reusedFromKernel: cached.kernel };
          } else {
            const tmpSourcePath = path.join(findingsDir, `_tmp_cmp_${entry.seedHex}_${entry.kernel}.cu.cs`);
            const tmpReducedPath = path.join(findingsDir, `_tmp_cmp_${entry.seedHex}_${entry.kernel}.reduced.cu.cs`);
            try {
              fs.writeFileSync(tmpSourcePath, kernelsSource, 'utf8');
              const testCmd = `node "${__filename}" --compare-single "{source}" --base-worktree "${baseWorktreeDir}" --work-dir "${options.workDir}" --compare-dialect "${entry.dialects.join(',')}" --compare-kernel "${entry.kernel}" --compare-scope "${entry.scope}"`;
              const reduceArgs = [
                'exec', fuzzToolDll, 'reduce',
                '--source', tmpSourcePath,
                '--fuzz-seed', entry.seedHex,
                '--kernel', entry.kernel,
                '--test-cmd', testCmd,
                '--failing-paths', entry.dialects.join(','),
                '--failure-kind', 'CodegenDiff',
                '--out', tmpReducedPath,
              ];
              const redRes = runCommand('dotnet', reduceArgs, { timeoutMs: DEFAULT_REDUCE_TIMEOUT_MS });
              if (redRes.status === 0 && fs.existsSync(tmpReducedPath)) {
                reducedKernelSource = fs.readFileSync(tmpReducedPath, 'utf8');
                const redInfo = parseBatchJsonl(redRes.stdout).find((e) => e.status === 'reduced') || {};
                reductionMeta = {
                  fingerprint: redInfo.fingerprint || null,
                  originalStatements: redInfo.originalStatements,
                  reducedStatements: redInfo.reducedStatements,
                  iterations: redInfo.iterations,
                };
                reducedBySignature.set(diffSigKey, {
                  kernel: entry.kernel,
                  reducedKernelSource,
                  reductionMeta,
                });
                process.stdout.write(
                  `  [reduced] ${entry.kernel}: ${reductionMeta.originalStatements} -> ${reductionMeta.reducedStatements} statements (${reductionMeta.iterations} iterations)\n`
                );
              }
            } finally {
              if (fs.existsSync(tmpSourcePath)) fs.unlinkSync(tmpSourcePath);
              if (fs.existsSync(tmpReducedPath)) fs.unlinkSync(tmpReducedPath);
            }
          }
        }

        if (reducedKernelSource) {
          fs.writeFileSync(path.join(findingDir, 'ReproKernel.cu.cs'), reducedKernelSource, 'utf8');
        }
        fs.writeFileSync(path.join(findingDir, 'FuzzBatchKernels.cu.cs'), kernelsSource, 'utf8');
        const findingRecord = {
          status: 'codegen_diff',
          failureKind: 'CodegenDiff',
          compareBase: options.compareBase,
          batchFuzzSeed: entry.seedHex,
          kernel: entry.kernel,
          differingDialects: entry.dialects,
          diffsByDialect: entry.diffsByDialect,
          ...reductionMeta,
        };
        fs.writeFileSync(path.join(findingDir, 'finding.json'), JSON.stringify(findingRecord, null, 2) + '\n', 'utf8');
        fs.appendFileSync(path.join(findingsDir, 'occurrences.jsonl'), JSON.stringify(findingRecord) + '\n', 'utf8');
        allDiffRecords.push(findingRecord);
      }

      const batchSec = ((Date.now() - batchStartMs) / 1000).toFixed(2);
      process.stdout.write(
        `[fuzz-compare] Batch #${batchIndex + 1}/${totalBatches} (seed=${seedHex}, kernels=${kernelNames.length}) finished in ${batchSec}s | diffs_in_batch=${byKernel.size}, total_diffs=${totalDiffKernels}\n`
      );
    }
  } finally {
    spawnSync('git', ['worktree', 'remove', '--force', baseWorktreeDir], { cwd: repoRoot, encoding: 'utf8' });
    if (fs.existsSync(baseWorktreeDir)) {
      fs.rmSync(baseWorktreeDir, { recursive: true, force: true });
    }
  }

  const elapsedSec = ((Date.now() - startMs) / 1000).toFixed(2);
  const summary = {
    mode: 'compare-base',
    compareBase: options.compareBase,
    expectChange: Boolean(options.expectChange),
    batches: totalBatches,
    batchSize: options.batchSize,
    totalKernels,
    totalDifferences: totalDiffKernels,
    elapsedSeconds: Number(elapsedSec),
    differences: allDiffRecords,
  };
  fs.writeFileSync(path.join(options.workDir, 'compare-summary.json'), JSON.stringify(summary, null, 2) + '\n', 'utf8');
  process.stdout.write(
    `[fuzz-compare] Completed ${totalBatches} batches (${totalKernels} kernels) vs '${options.compareBase}' in ${elapsedSec}s. Differences: ${totalDiffKernels}.\n`
  );

  if (totalDiffKernels > 0 && !options.expectChange) {
    process.exit(1);
  }
  process.exit(0);
}

function main() {
  const options = parseArgs(process.argv.slice(2));
  if (options.compareSingle) {
    runCompareSingleMode(options);
    return;
  }
  if (options.compareBase) {
    runCompareBaseMode(options);
    return;
  }
  const ctx = prepareWorkspace(options);

  while (true) {
    if (options.batches !== null) {
      if (ctx.batchIndex >= options.batches) break;
    } else if (ctx.batchIndex > 0 && Date.now() - ctx.startMs >= ctx.timeLimitMs) {
      break;
    }

    const currentSeed = BigInt.asUintN(64, options.fuzzSeed + BigInt(ctx.batchIndex));
    const seedHex = formatHexSeed(currentSeed);
    const batchStartMs = Date.now();

    const batchResult = runSingleBatch(ctx, ctx.batchIndex, seedHex);
    handleBatchFindings(ctx, seedHex, batchResult);

    ctx.batchIndex++;
    ctx.totalKernels += options.batchSize;

    const batchSec = ((Date.now() - batchStartMs) / 1000).toFixed(2);
    const totalSec = (Date.now() - ctx.startMs) / 1000;
    const pureEvalSec = Math.max((Date.now() - ctx.startMs - ctx.totalReductionMs) / 1000, 0.001);
    const kernelsPerHour = Math.round((ctx.totalKernels / Math.max(totalSec, 0.001)) * 3600);
    const pureKernelsPerHour = Math.round((ctx.totalKernels / pureEvalSec) * 3600);
    process.stdout.write(
      `[fuzz-diff] Batch #${ctx.batchIndex} (seed=${seedHex}, profile=${options.profile}, size=${options.batchSize}) finished in ${batchSec}s | total=${ctx.totalKernels} kernels (${kernelsPerHour} kernels/hr overall, ${pureKernelsPerHour} kernels/hr eval), findings=${ctx.totalFailures} (#1078=${ctx.suspected1078Count}, hangs=${ctx.hangCount}, other=${ctx.otherFindingsCount})\n`
    );
  }

  writeRunSummary(ctx);
}

main();
