#!/usr/bin/env node
'use strict';

/*
 * fuzz-differential.js
 *
 * Purpose:
 *   Batch differential kernel fuzzer orchestrator for G#.
 *   Generates batches of scalar/device kernels via tools/fuzz-diff into tmp/differential-fuzz/,
 *   compiles and runs them in batch mode across Profile M (Managed only), Profile C (Managed + Native CPU),
 *   or Profile G (Managed + Native CPU + CUDA), logs throughput, and writes any findings to
 *   <work-dir>/findings/.
 *
 * Usage:
 *   node scripts/human/fuzz-differential.js [options]
 *
 * Options:
 *   --profile <managed|cpu|all|M|C|G>  Execution profile (default: cpu).
 *   --minutes <n>                      Time budget in minutes (default: 5 when --batches is not set).
 *   --batches <n>                      Exact number of batches to run (overrides --minutes).
 *   --batch-size <n>                   Number of kernels per batch (1..256, default: 64).
 *   --fuzz-seed <seed>                 Starting 64-bit fuzz seed in decimal or 0x hex (default: 1).
 *   --input-seed <int>                 Optional explicit runtime data input seed (default: DifferentialSeeds.Fixed 0..8).
 *   --work-dir <dir>                   Temporary project directory (default: tmp/differential-fuzz).
 *   --out-dir <dir>                    Directory for generated .cu.cs / .cs files (default: <work-dir>/Generated).
 *   --no-reduce                        Skip automatic 2-stage reduction and deduplication when a finding is detected.
 *   --no-gpu-lock                      Do not wrap CUDA execution in scripts/agent/gpu-lock.js (when caller holds lock).
 *   --help, -h                         Show this help text.
 */

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const repoRoot = path.resolve(__dirname, '..', '..');
const hostOs = process.platform === 'win32' ? 'win' : process.platform === 'linux' ? 'linux' : process.platform === 'darwin' ? 'osx' : 'unix';

const helpText = [
  'Usage: node scripts/human/fuzz-differential.js [options]',
  '',
  '  --profile <managed|cpu|all|M|C|G>  Execution profile: M/managed, C/cpu (default), or G/all (includes CUDA).',
  '  --minutes <n>                      Run duration in minutes (default: 5 when --batches is not given).',
  '  --batches <n>                      Run a fixed number of batches instead of a time limit.',
  '  --batch-size <n>                   Kernels generated and compiled per batch (default: 64).',
  '  --fuzz-seed <seed>                 Initial 64-bit fuzz seed in decimal or 0x hex (default: 1).',
  '  --input-seed <int>                 Optional explicit runtime data input seed (default: 0..8).',
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
  if (value === 'm' || value === 'managed') {
    return 'M';
  }
  if (value === 'c' || value === 'cpu' || value === 'native') {
    return 'C';
  }
  if (value === 'g' || value === 'all' || value === 'cuda' || value === 'gpu') {
    return 'G';
  }
  fail(`Invalid --profile '${raw}'. Expected managed|cpu|all|M|C|G.`, 2);
}

function parseSeedBigInt(raw) {
  const text = String(raw ?? '').trim();
  if (!text) {
    fail('--fuzz-seed requires a 64-bit integer.', 2);
  }
  try {
    return BigInt.asUintN(64, BigInt(text));
  } catch {
    fail(`Invalid --fuzz-seed '${raw}'.`, 2);
  }
}

function formatHexSeed(seedBigInt) {
  const hex = BigInt.asUintN(64, seedBigInt).toString(16).toUpperCase().padStart(16, '0');
  return `0x${hex}`;
}

function parseArgs(argv) {
  const options = {
    profile: 'C',
    minutes: 5,
    batches: null,
    batchSize: 64,
    fuzzSeed: 1n,
    inputSeed: null,
    workDir: path.join(repoRoot, 'tmp', 'differential-fuzz'),
    outDir: null,
    noReduce: false,
    noGpuLock: false,
  };

  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--help' || arg === '-h') {
      process.stdout.write(`${helpText}\n`);
      process.exit(0);
    } else if (arg === '--profile') {
      options.profile = normalizeProfile(argv[++i]);
    } else if (arg.startsWith('--profile=')) {
      options.profile = normalizeProfile(arg.slice('--profile='.length));
    } else if (arg === '--minutes') {
      const v = Number(argv[++i]);
      if (!Number.isFinite(v) || v <= 0) {
        fail('--minutes requires a positive number.', 2);
      }
      options.minutes = v;
    } else if (arg.startsWith('--minutes=')) {
      const v = Number(arg.slice('--minutes='.length));
      if (!Number.isFinite(v) || v <= 0) {
        fail('--minutes requires a positive number.', 2);
      }
      options.minutes = v;
    } else if (arg === '--batches') {
      const v = Number(argv[++i]);
      if (!Number.isInteger(v) || v <= 0) {
        fail('--batches requires a positive integer.', 2);
      }
      options.batches = v;
    } else if (arg.startsWith('--batches=')) {
      const v = Number(arg.slice('--batches='.length));
      if (!Number.isInteger(v) || v <= 0) {
        fail('--batches requires a positive integer.', 2);
      }
      options.batches = v;
    } else if (arg === '--batch-size') {
      const v = Number(argv[++i]);
      if (!Number.isInteger(v) || v < 1 || v > 256) {
        fail('--batch-size requires an integer in [1, 256].', 2);
      }
      options.batchSize = v;
    } else if (arg.startsWith('--batch-size=')) {
      const v = Number(arg.slice('--batch-size='.length));
      if (!Number.isInteger(v) || v < 1 || v > 256) {
        fail('--batch-size requires an integer in [1, 256].', 2);
      }
      options.batchSize = v;
    } else if (arg === '--fuzz-seed') {
      options.fuzzSeed = parseSeedBigInt(argv[++i]);
    } else if (arg.startsWith('--fuzz-seed=')) {
      options.fuzzSeed = parseSeedBigInt(arg.slice('--fuzz-seed='.length));
    } else if (arg === '--input-seed') {
      const v = Number(argv[++i]);
      if (!Number.isInteger(v)) {
        fail('--input-seed requires an integer.', 2);
      }
      options.inputSeed = v;
    } else if (arg.startsWith('--input-seed=')) {
      const v = Number(arg.slice('--input-seed='.length));
      if (!Number.isInteger(v)) {
        fail('--input-seed requires an integer.', 2);
      }
      options.inputSeed = v;
    } else if (arg === '--work-dir') {
      const rawDir = argv[++i];
      if (!rawDir) {
        fail('--work-dir requires a directory path.', 2);
      }
      options.workDir = path.resolve(repoRoot, rawDir);
    } else if (arg.startsWith('--work-dir=')) {
      options.workDir = path.resolve(repoRoot, arg.slice('--work-dir='.length));
    } else if (arg === '--out-dir') {
      const rawDir = argv[++i];
      if (!rawDir) {
        fail('--out-dir requires a directory path.', 2);
      }
      options.outDir = path.resolve(repoRoot, rawDir);
    } else if (arg.startsWith('--out-dir=')) {
      options.outDir = path.resolve(repoRoot, arg.slice('--out-dir='.length));
    } else if (arg === '--no-reduce') {
      options.noReduce = true;
    } else if (arg === '--no-gpu-lock') {
      options.noGpuLock = true;
    } else {
      fail(`Unknown option '${arg}'.\n\n${helpText}`, 2);
    }
  }

  if (!options.outDir) {
    options.outDir = path.join(options.workDir, 'Generated');
  }

  return options;
}

function runCommand(command, args, extraEnv = {}) {
  const env = {
    ...process.env,
    DiffEngine_Disabled: 'true',
    CI: 'true',
    DOTNET_NOLOGO: '1',
    ...extraEnv,
  };
  const result = spawnSync(command, args, {
    cwd: repoRoot,
    env,
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
  });
  return {
    status: result.status ?? 1,
    stdout: result.stdout || '',
    stderr: result.stderr || '',
  };
}

function detectHostCudaArch() {
  const res = spawnSync('nvidia-smi', ['--query-gpu=compute_cap', '--format=csv,noheader'], {
    cwd: repoRoot,
    encoding: 'utf8',
  });
  if (res.status !== 0 || !res.stdout) {
    return null;
  }
  const firstLine = res.stdout.split(/\r?\n/).map((s) => s.trim()).find((s) => /^\d+\.\d+$/.test(s));
  if (!firstLine) {
    return null;
  }
  const digits = firstLine.replace('.', '');
  return `compute_${digits},sm_${digits}`;
}

function isAlreadyInsideGpuLock() {
  try {
    const gitCommon = spawnSync('git', ['rev-parse', '--git-common-dir'], { cwd: repoRoot, encoding: 'utf8' });
    const mainRoot = gitCommon.status === 0 && gitCommon.stdout.trim()
      ? path.resolve(repoRoot, gitCommon.stdout.trim(), '..')
      : repoRoot;
    const ownerJsonPath = path.join(mainRoot, 'tmp', 'gpu-test-lock', 'owner.json');
    if (!fs.existsSync(ownerJsonPath)) {
      return false;
    }
    const owner = JSON.parse(fs.readFileSync(ownerJsonPath, 'utf8'));
    return owner && (owner.pid === process.pid || owner.pid === process.ppid);
  } catch {
    return false;
  }
}

function hasAssetsJson(csprojPath) {
  const projDir = path.dirname(csprojPath);
  return fs.existsSync(path.join(projDir, 'obj', hostOs, 'project.assets.json'));
}

function ensurePrereqsBuilt(fuzzToolProj) {
  process.stdout.write('[fuzz-diff] Building tools/fuzz-diff and compiler/runtime prerequisites...\n');
  const toolArgs = ['build', fuzzToolProj, '-c', 'Debug', '-m:1', '-nodeReuse:false'];
  if (hasAssetsJson(fuzzToolProj)) {
    toolArgs.push('--no-restore');
  }
  const toolBuild = runCommand('dotnet', toolArgs);
  if (toolBuild.status !== 0) {
    process.stderr.write(toolBuild.stdout + '\n' + toolBuild.stderr + '\n');
    fail('Failed to build tools/fuzz-diff.');
  }

  const prereqProjects = [
    {
      proj: path.join(repoRoot, 'src', 'Core', 'Kassyi.GSharp.Core', 'Kassyi.GSharp.Core.csproj'),
      dll: path.join(repoRoot, 'src', 'Core', 'Kassyi.GSharp.Core', 'bin', hostOs, 'Debug', 'net10.0', 'Kassyi.GSharp.Core.dll'),
    },
    {
      proj: path.join(repoRoot, 'src', 'Runtime', 'Kassyi.GSharp.Runtime', 'Kassyi.GSharp.Runtime.csproj'),
      dll: path.join(repoRoot, 'src', 'Runtime', 'Kassyi.GSharp.Runtime', 'bin', hostOs, 'Debug', 'net10.0', 'Kassyi.GSharp.Runtime.dll'),
    },
    {
      proj: path.join(repoRoot, 'src', 'Compiler', 'Kassyi.GSharp.Compiler', 'Kassyi.GSharp.Compiler.csproj'),
      dll: path.join(repoRoot, 'src', 'Compiler', 'Kassyi.GSharp.Compiler', 'bin', hostOs, 'Debug', 'netstandard2.0', 'Kassyi.GSharp.Compiler.dll'),
    },
    {
      proj: path.join(repoRoot, 'src', 'Compiler', 'Kassyi.GSharp.Compiler.Tasks', 'Kassyi.GSharp.Compiler.Tasks.csproj'),
      dll: path.join(repoRoot, 'src', 'Compiler', 'Kassyi.GSharp.Compiler.Tasks', 'bin', hostOs, 'Debug', 'netstandard2.0', 'Kassyi.GSharp.Compiler.Tasks.dll'),
    },
  ];

  for (const item of prereqProjects) {
    if (fs.existsSync(item.dll)) {
      continue;
    }
    const buildArgs = ['build', item.proj, '-c', 'Debug', '-m:1', '-nodeReuse:false'];
    if (hasAssetsJson(item.proj)) {
      buildArgs.push('--no-restore');
    }
    const res = runCommand('dotnet', buildArgs);
    if (res.status !== 0) {
      process.stderr.write(res.stdout + '\n' + res.stderr + '\n');
      fail(`Failed to build prerequisite project: ${item.proj}`);
    }
  }
}

function recordFinding(findingsDir, batchSeedHex, stage, details, kernelsSource, casesSource, options, fuzzToolDll, reducedKernelSeeds) {
  fs.mkdirSync(findingsDir, { recursive: true });
  let findingDir = path.join(findingsDir, `${batchSeedHex}_${stage}`);
  let reductionMs = 0;

  // Avoid reducing the same (batchSeedHex, kernel) twice when Profile G runs both CPU and CUDA on the same batch.
  const kernelDedupKey = details && details.kernel ? `${batchSeedHex}:${details.kernel}` : null;
  const shouldReduce = options && !options.noReduce && fuzzToolDll && details && details.kernel &&
    (!reducedKernelSeeds || !reducedKernelSeeds.has(kernelDedupKey));

  // When a specific kernel failed and reduction is enabled, run 2-stage reduction, compute normalized AST fingerprint,
  // and emit the deduplicated repro into <findingsDir>/<fingerprint>/Repro_<seed>.cu.cs.
  if (shouldReduce) {
    if (reducedKernelSeeds && kernelDedupKey) {
      reducedKernelSeeds.add(kernelDedupKey);
    }
    const redStartMs = Date.now();
    const tmpSourcePath = path.join(findingsDir, `_tmp_${batchSeedHex}_${details.kernel}.cu.cs`);
    const tmpReducedPath = path.join(findingsDir, `_tmp_${batchSeedHex}_${details.kernel}.reduced.cu.cs`);
    try {
      fs.writeFileSync(tmpSourcePath, kernelsSource, 'utf8');
      const failingPathsStr = Array.isArray(details.failingPaths) && details.failingPaths.length > 0
        ? details.failingPaths.join(',')
        : (details.dialect || 'Native,Cuda');
      const failureKindStr = details.failureKind || details.status || 'CellMismatch';
      const inputSeedVal = details.inputSeed !== undefined ? String(details.inputSeed) : '0';
      const reduceProfile = stage.startsWith('cuda_') ? 'G' : options.profile;

      const reduceWorkDir = path.join(options.workDir, '_reduce');
      const reduceArgs = [
        'exec',
        fuzzToolDll,
        'reduce',
        '--source',
        tmpSourcePath,
        '--fuzz-seed',
        batchSeedHex,
        '--kernel',
        String(details.kernel),
        '--input-seed',
        inputSeedVal,
        '--profile',
        reduceProfile,
        '--work-dir',
        reduceWorkDir,
        '--repo-root',
        repoRoot,
        '--failing-paths',
        failingPathsStr,
        '--failure-kind',
        failureKindStr,
        '--out',
        tmpReducedPath,
      ];
      if (options.noGpuLock || isAlreadyInsideGpuLock()) {
        reduceArgs.push('--no-gpu-lock');
      }

      const redRes = runCommand('dotnet', reduceArgs);
      if (redRes.status === 0 && fs.existsSync(tmpReducedPath)) {
        const redEvents = parseBatchJsonl(redRes.stdout);
        const redInfo = redEvents.find((e) => e.status === 'reduced') || {};
        details.fingerprint = redInfo.fingerprint || null;
        details.originalStatements = redInfo.originalStatements;
        details.reducedStatements = redInfo.reducedStatements;

        const promoteArgs = [
          'exec',
          fuzzToolDll,
          'promote',
          '--source',
          tmpReducedPath,
          '--fuzz-seed',
          batchSeedHex,
          '--input-seed',
          inputSeedVal,
          '--failing-paths',
          failingPathsStr,
          '--failure-kind',
          failureKindStr,
          '--findings-dir',
          findingsDir,
        ];
        const promRes = runCommand('dotnet', promoteArgs);
        const promEvents = parseBatchJsonl(promRes.stdout);
        const promInfo = promEvents.find((e) => e.status === 'promoted');
        if (promInfo && promInfo.reproKernelPath) {
          findingDir = path.dirname(promInfo.reproKernelPath);
          details.fingerprint = promInfo.fingerprint || details.fingerprint;
          details.reproKernelPath = promInfo.reproKernelPath;
        }
      }
    } finally {
      if (fs.existsSync(tmpSourcePath)) {
        fs.unlinkSync(tmpSourcePath);
      }
      if (fs.existsSync(tmpReducedPath)) {
        fs.unlinkSync(tmpReducedPath);
      }
      reductionMs = Date.now() - redStartMs;
    }
  }

  fs.mkdirSync(findingDir, { recursive: true });
  fs.writeFileSync(path.join(findingDir, 'FuzzBatchKernels.cu.cs'), kernelsSource, 'utf8');
  fs.writeFileSync(path.join(findingDir, 'FuzzBatchCases.cs'), casesSource, 'utf8');
  fs.writeFileSync(path.join(findingDir, 'finding.json'), JSON.stringify(details, null, 2) + '\n', 'utf8');
  const occurrencesPath = path.join(findingsDir, 'occurrences.jsonl');
  fs.appendFileSync(occurrencesPath, JSON.stringify(details) + '\n', 'utf8');
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

function main() {
  const options = parseArgs(process.argv.slice(2));
  const fuzzToolProj = path.join(repoRoot, 'tools', 'fuzz-diff', 'Kassyi.GSharp.Tools.FuzzDiff.csproj');
  const fuzzToolDll = path.join(repoRoot, 'tools', 'fuzz-diff', 'bin', hostOs, 'Debug', 'net10.0', 'Kassyi.GSharp.Tools.FuzzDiff.dll');

  ensurePrereqsBuilt(fuzzToolProj);

  const cudaArch = options.profile === 'G' ? detectHostCudaArch() : null;
  if (options.profile === 'G' && cudaArch) {
    process.stdout.write(`[fuzz-diff] Detected host CUDA architecture: ${cudaArch}\n`);
  }

  const findingsDir = path.join(options.workDir, 'findings');
  fs.mkdirSync(options.workDir, { recursive: true });
  fs.mkdirSync(options.outDir, { recursive: true });

  const startMs = Date.now();
  const timeLimitMs = options.minutes * 60 * 1000;
  let batchIndex = 0;
  let totalKernels = 0;
  let totalFailures = 0;
  let totalReductionMs = 0;
  const reducedKernelSeeds = new Set();
  let firstBatch = true;

  while (true) {
    if (options.batches !== null) {
      if (batchIndex >= options.batches) {
        break;
      }
    } else if (batchIndex > 0 && Date.now() - startMs >= timeLimitMs) {
      break;
    }

    const currentSeed = BigInt.asUintN(64, options.fuzzSeed + BigInt(batchIndex));
    const seedHex = formatHexSeed(currentSeed);
    const batchStartMs = Date.now();

    // 1. Generate batch sources and project files
    const genArgs = [
      'exec',
      fuzzToolDll,
      'gen',
      '--fuzz-seed',
      seedHex,
      '--batch-size',
      String(options.batchSize),
      '--out-dir',
      options.outDir,
      '--repo-root',
      repoRoot,
    ];
    if (options.inputSeed !== null) {
      genArgs.push('--input-seed', String(options.inputSeed));
    }
    if (cudaArch) {
      genArgs.push('--cuda-arch', cudaArch);
    }

    const genRes = runCommand('dotnet', genArgs);
    if (genRes.status !== 0) {
      process.stderr.write(genRes.stdout + '\n' + genRes.stderr + '\n');
      fail(`fuzz-diff gen failed on seed ${seedHex}`);
    }

    const kernelsSource = fs.readFileSync(path.join(options.outDir, 'FuzzBatchKernels.cu.cs'), 'utf8');
    const casesSource = fs.readFileSync(path.join(options.outDir, 'FuzzBatchCases.cs'), 'utf8');

    const cpuProj = path.join(options.workDir, 'FuzzBatch.Cpu.csproj');
    const cpuDll = path.join(options.workDir, 'bin', hostOs, 'FuzzBatch.Cpu', 'Debug', 'net10.0', 'FuzzBatch.Cpu.dll');

    // 2. Build & run CPU project (Profile M skips C++ compile; Profile C and G run full CPU C++ compile)
    const skipCpuCpp = options.profile === 'M' ? 'true' : 'false';
    const cpuAssets = path.join(options.workDir, 'obj', hostOs, 'FuzzBatch.Cpu', 'project.assets.json');
    const cpuBuildArgs = [
      'build',
      cpuProj,
      '-c',
      'Debug',
      '-m:1',
      '-nodeReuse:false',
      '--no-dependencies',
      `-p:GSharpSkipCpuCompilation=${skipCpuCpp}`,
    ];
    if (fs.existsSync(cpuAssets)) {
      cpuBuildArgs.push('--no-restore');
    }

    const cpuBuild = runCommand('dotnet', cpuBuildArgs);
    if (cpuBuild.status !== 0) {
      totalFailures++;
      recordFinding(
        findingsDir,
        seedHex,
        'cpu_build',
        {
          status: 'compile_error',
          dialect: 'Cpu',
          batchFuzzSeed: seedHex,
          stdout: cpuBuild.stdout,
          stderr: cpuBuild.stderr,
        },
        kernelsSource,
        casesSource
      );
      process.stdout.write(`[fuzz-diff] Batch ${batchIndex + 1} (${seedHex}): CPU build failed! Recorded to findings.\n`);
    } else {
      const cpuRunArgs = ['exec', cpuDll];
      if (options.profile === 'M') {
        cpuRunArgs.push('--managed-only');
      }
      if (options.inputSeed !== null) {
        cpuRunArgs.push('--input-seed', String(options.inputSeed));
      }

      const cpuRun = runCommand('dotnet', cpuRunArgs);
      const events = parseBatchJsonl(cpuRun.stdout);
      for (const ev of events) {
        if (ev.status === 'failed' || ev.status === 'harness_error') {
          totalFailures++;
          totalReductionMs += recordFinding(findingsDir, seedHex, `cpu_${ev.kernel || 'batch'}`, ev, kernelsSource, casesSource, options, fuzzToolDll, reducedKernelSeeds);
        }
      }
      if (cpuRun.status !== 0 && events.length === 0) {
        totalFailures++;
        recordFinding(
          findingsDir,
          seedHex,
          'cpu_crash',
          {
            status: 'crash',
            dialect: 'Cpu',
            batchFuzzSeed: seedHex,
            exitCode: cpuRun.status,
            stdout: cpuRun.stdout,
            stderr: cpuRun.stderr,
          },
          kernelsSource,
          casesSource
        );
      }
    }

    // 3. Profile G: Build & run CUDA project
    if (options.profile === 'G') {
      const cudaProj = path.join(options.workDir, 'FuzzBatch.Cuda.csproj');
      const cudaDll = path.join(options.workDir, 'bin', hostOs, 'FuzzBatch.Cuda', 'Debug', 'net10.0', 'FuzzBatch.Cuda.dll');
      const cudaAssets = path.join(options.workDir, 'obj', hostOs, 'FuzzBatch.Cuda', 'project.assets.json');
      const cudaBuildArgs = [
        'build',
        cudaProj,
        '-c',
        'Debug',
        '-m:1',
        '-nodeReuse:false',
        '--no-dependencies',
      ];
      if (fs.existsSync(cudaAssets)) {
        cudaBuildArgs.push('--no-restore');
      }

      const cudaBuild = runCommand('dotnet', cudaBuildArgs);
      if (cudaBuild.status !== 0) {
        totalFailures++;
        recordFinding(
          findingsDir,
          seedHex,
          'cuda_build',
          {
            status: 'compile_error',
            dialect: 'Cuda',
            batchFuzzSeed: seedHex,
            stdout: cudaBuild.stdout,
            stderr: cudaBuild.stderr,
          },
          kernelsSource,
          casesSource
        );
        process.stdout.write(`[fuzz-diff] Batch ${batchIndex + 1} (${seedHex}): CUDA build failed! Recorded to findings.\n`);
      } else {
        const useLock = !options.noGpuLock && !isAlreadyInsideGpuLock();
        const inputSeedArg = options.inputSeed !== null ? ` --input-seed ${options.inputSeed}` : '';
        let cudaRun;
        if (useLock) {
          const lockScript = path.join(repoRoot, 'scripts', 'agent', 'gpu-lock.js');
          cudaRun = runCommand('node', [lockScript, '--', `dotnet exec "${cudaDll}"${inputSeedArg}`]);
        } else {
          const runArgs = ['exec', cudaDll];
          if (options.inputSeed !== null) {
            runArgs.push('--input-seed', String(options.inputSeed));
          }
          cudaRun = runCommand('dotnet', runArgs);
        }

        const events = parseBatchJsonl(cudaRun.stdout);
        for (const ev of events) {
          if (ev.status === 'failed' || ev.status === 'harness_error') {
            totalFailures++;
            totalReductionMs += recordFinding(findingsDir, seedHex, `cuda_${ev.kernel || 'batch'}`, ev, kernelsSource, casesSource, options, fuzzToolDll, reducedKernelSeeds);
          }
        }
        if (cudaRun.status !== 0 && events.length === 0) {
          totalFailures++;
          recordFinding(
            findingsDir,
            seedHex,
            'cuda_crash',
            {
              status: 'crash',
              dialect: 'Cuda',
              batchFuzzSeed: seedHex,
              exitCode: cudaRun.status,
              stdout: cudaRun.stdout,
              stderr: cudaRun.stderr,
            },
            kernelsSource,
            casesSource
          );
        }
      }
    }

    firstBatch = false;
    batchIndex++;
    totalKernels += options.batchSize;

    const batchSec = ((Date.now() - batchStartMs) / 1000).toFixed(2);
    const totalSec = (Date.now() - startMs) / 1000;
    const pureEvalSec = Math.max((Date.now() - startMs - totalReductionMs) / 1000, 0.001);
    const kernelsPerHour = Math.round((totalKernels / Math.max(totalSec, 0.001)) * 3600);
    const pureKernelsPerHour = Math.round((totalKernels / pureEvalSec) * 3600);
    process.stdout.write(
      `[fuzz-diff] Batch #${batchIndex} (seed=${seedHex}, profile=${options.profile}, size=${options.batchSize}) finished in ${batchSec}s | total=${totalKernels} kernels (${kernelsPerHour} kernels/hr overall, ${pureKernelsPerHour} kernels/hr eval), findings=${totalFailures}\n`
    );
  }

  const elapsedSec = ((Date.now() - startMs) / 1000).toFixed(2);
  const pureEvalTotalSec = Math.max((Date.now() - startMs - totalReductionMs) / 1000, 0.001);
  const finalKernelsPerHour = Math.round((totalKernels / Math.max((Date.now() - startMs) / 1000, 0.001)) * 3600);
  const finalPureKernelsPerHour = Math.round((totalKernels / pureEvalTotalSec) * 3600);
  const summary = {
    profile: options.profile,
    batches: batchIndex,
    batchSize: options.batchSize,
    totalKernels,
    elapsedSeconds: Number(elapsedSec),
    reductionSeconds: Number((totalReductionMs / 1000).toFixed(2)),
    kernelsPerHour: finalKernelsPerHour,
    pureEvalKernelsPerHour: finalPureKernelsPerHour,
    findingsCount: totalFailures,
  };
  fs.writeFileSync(path.join(options.workDir, 'summary.json'), JSON.stringify(summary, null, 2) + '\n', 'utf8');
  process.stdout.write(`[fuzz-diff] Completed ${batchIndex} batches (${totalKernels} kernels) in ${elapsedSec}s (${finalKernelsPerHour} kernels/hr overall, ${finalPureKernelsPerHour} kernels/hr eval). Findings: ${totalFailures}.\n`);
}

main();
