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
  '  --metamorphic                      Generate semantics-preserving transformed kernel variants per kernel.',
  '  --compare-base <ref>               Compare the generated native and C# code with git <ref> (fuzz-diff compare, no GPU).',
  '  --expect-change                    Exit 0 when --compare-base finds differences.',
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
    metamorphic: false,
    compareBase: null,
    expectChange: false,
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
    } else if (arg === '--metamorphic') {
      options.metamorphic = true;
    } else if (arg === '--compare-base' || arg.startsWith('--compare-base=')) {
      const { value, nextIndex } = takeValue(i, '--compare-base');
      i = nextIndex;
      if (!value) fail('--compare-base requires a git ref.', 2);
      options.compareBase = value;
    } else if (arg === '--expect-change') {
      options.expectChange = true;
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

function runCommand(command, args, { extraEnv = {}, timeoutMs = DEFAULT_BUILD_TIMEOUT_MS } = {}) {
  const result = spawnSync(command, args, {
    cwd: repoRoot,
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

function buildFuzzTool(fuzzToolProj) {
  const toolArgs = ['build', fuzzToolProj, '-c', 'Debug', '-m:1', '-nodeReuse:false'];
  if (hasAssetsJson(fuzzToolProj)) toolArgs.push('--no-restore');
  const toolBuild = runCommand('dotnet', toolArgs);
  if (toolBuild.status !== 0) {
    process.stderr.write(toolBuild.stdout + '\n' + toolBuild.stderr + '\n');
    fail('Failed to build tools/fuzz-diff.');
  }
}

function ensurePrereqsBuilt(fuzzToolProj) {
  process.stdout.write('[fuzz-diff] Building tools/fuzz-diff and compiler/runtime prerequisites...\n');
  buildFuzzTool(fuzzToolProj);

  const prereqProjects = [
    { proj: path.join(repoRoot, 'src', 'Core', 'Kassyi.GSharp.Core', 'Kassyi.GSharp.Core.csproj'), dll: path.join(repoRoot, 'src', 'Core', 'Kassyi.GSharp.Core', 'bin', hostOs, 'Debug', 'net10.0', 'Kassyi.GSharp.Core.dll') },
    { proj: path.join(repoRoot, 'src', 'Runtime', 'Kassyi.GSharp.Runtime', 'Kassyi.GSharp.Runtime.csproj'), dll: path.join(repoRoot, 'src', 'Runtime', 'Kassyi.GSharp.Runtime', 'bin', hostOs, 'Debug', 'net10.0', 'Kassyi.GSharp.Runtime.dll') },
    { proj: path.join(repoRoot, 'src', 'Compiler', 'Kassyi.GSharp.Compiler', 'Kassyi.GSharp.Compiler.csproj'), dll: path.join(repoRoot, 'src', 'Compiler', 'Kassyi.GSharp.Compiler', 'bin', hostOs, 'Debug', 'netstandard2.0', 'Kassyi.GSharp.Compiler.dll') },
    { proj: path.join(repoRoot, 'src', 'Compiler', 'Kassyi.GSharp.Compiler.Tasks', 'Kassyi.GSharp.Compiler.Tasks.csproj'), dll: path.join(repoRoot, 'src', 'Compiler', 'Kassyi.GSharp.Compiler.Tasks', 'bin', hostOs, 'Debug', 'netstandard2.0', 'Kassyi.GSharp.Compiler.Tasks.dll') },
  ];

  for (const item of prereqProjects) {
    if (fs.existsSync(item.dll)) continue;
    const buildArgs = ['build', item.proj, '-c', 'Debug', '-m:1', '-nodeReuse:false'];
    if (hasAssetsJson(item.proj)) buildArgs.push('--no-restore');
    const res = runCommand('dotnet', buildArgs);
    if (res.status !== 0) {
      process.stderr.write(res.stdout + '\n' + res.stderr + '\n');
      fail(`Failed to build prerequisite project: ${item.proj}`);
    }
  }
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
  if (options.metamorphic) genArgs.push('--metamorphic');
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
    metamorphic: Boolean(options.metamorphic),
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

// Compares the generated code of --compare-base <ref> and the working tree with `fuzz-diff compare`, which builds
// both compilers, compares per kernel, reduces and writes findings; its exit code is passed on.
function runCompareBaseMode(options) {
  const fuzzToolProj = path.join(repoRoot, 'tools', 'fuzz-diff', 'Kassyi.GSharp.Tools.FuzzDiff.csproj');
  const fuzzToolDll = path.join(repoRoot, 'tools', 'fuzz-diff', 'bin', hostOs, 'Debug', 'net10.0', 'Kassyi.GSharp.Tools.FuzzDiff.dll');
  process.stdout.write('[fuzz-compare] Building tools/fuzz-diff...\n');
  buildFuzzTool(fuzzToolProj);

  const args = [
    'exec', fuzzToolDll, 'compare',
    '--base', options.compareBase,
    '--batches', String(options.batches !== null ? options.batches : 20),
    '--batch-size', String(options.batchSize),
    '--fuzz-seed', formatHexSeed(options.fuzzSeed),
    '--profile', options.profile,
    '--work-dir', options.workDir,
    '--repo-root', repoRoot,
  ];
  if (options.inputSeed !== null) args.push('--input-seed', String(options.inputSeed));
  for (const allowedIssue of options.allowedIssues) args.push('--allow-issue', String(allowedIssue));
  if (options.swarmFocus) args.push('--swarm-focus', options.swarmFocus);
  if (options.metamorphic) args.push('--metamorphic');
  if (options.expectChange) args.push('--expect-change');
  if (options.noReduce) args.push('--no-reduce');

  const result = spawnSync('dotnet', args, {
    cwd: repoRoot,
    env: { ...process.env, DiffEngine_Disabled: 'true', CI: 'true', DOTNET_NOLOGO: '1' },
    stdio: 'inherit',
  });
  if (result.error) fail(`Failed to start fuzz-diff compare: ${result.error.message}`);
  process.exit(result.status === null ? 1 : result.status);
}

function main() {
  const options = parseArgs(process.argv.slice(2));
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
    ctx.totalKernels += options.metamorphic ? options.batchSize * 3 : options.batchSize;

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
