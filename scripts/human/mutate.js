#!/usr/bin/env node
'use strict';

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const repoRoot = path.resolve(__dirname, '..', '..');
const defaultHeapLimit = '8589934592';
const markerName = 'run-started';
const reportRelativePath = path.join('reports', 'mutation-report.json');

const phaseDirectories = {
  'gpumath': 'tests/Stdlib/Kassyi.GSharp.Stdlib.GpuMath.Tests',
  'compiler': 'tests/Compiler/Kassyi.GSharp.Tests.Cpu',
  'compiler-cuda': 'tests/Compiler/Kassyi.GSharp.Tests.Cuda',
};

const helpText = [
  'Usage: node scripts/human/mutate.js [--phase <name>] [--since <ref>] [--dry-run] [-- <stryker args>...]',
  '',
  'Runs Stryker.NET mutation testing for the given phase with a capped test host heap, records run-started,',
  'and verifies that mutation-report.json was produced.',
  '',
  'Options:',
  '  --phase <name>  Phase to test: gpumath, compiler, or compiler-cuda (default: gpumath)',
  '  --since <ref>   Git ref to mutate only files changed after it (e.g. main)',
  '  --dry-run       Print the commands and environment without executing them',
  '  --help, -h      Show this help',
  '',
  'Arguments after \'--\' are forwarded to dotnet stryker.',
].join('\n');

function fail(message) {
  process.stderr.write(message + '\n');
  process.exit(1);
}

function choosesOutput(strykerArgs) {
  return strykerArgs.some((arg) => /^(-O|--output)([:=].*)?$/.test(arg));
}

function parseArgs(argv) {
  const options = {
    phase: 'gpumath',
    since: '',
    dryRun: false,
    strykerArgs: [],
  };

  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--') {
      const rest = argv.slice(i + 1).filter((a) => a !== '');
      for (const r of rest) {
        if (r === '--dry-run') {
          options.dryRun = true;
        } else if (r === '--help' || r === '-h') {
          process.stdout.write(helpText + '\n');
          process.exit(0);
        } else {
          options.strykerArgs.push(r);
        }
      }
      break;
    } else if (arg === '--help' || arg === '-h') {
      process.stdout.write(helpText + '\n');
      process.exit(0);
    } else if (arg === '--dry-run') {
      options.dryRun = true;
    } else if (arg === '--phase') {
      const val = argv[++i];
      if (val === undefined) {
        fail('--phase requires a value');
      }
      options.phase = val;
    } else if (arg === '--since') {
      const val = argv[++i];
      if (val === undefined) {
        fail('--since requires a value');
      }
      options.since = val;
    } else if (arg.startsWith('--phase=')) {
      options.phase = arg.slice('--phase='.length);
    } else if (arg.startsWith('--since=')) {
      options.since = arg.slice('--since='.length);
    } else {
      fail(`unknown argument: ${arg}\n\n${helpText}`);
    }
  }

  if (!Object.prototype.hasOwnProperty.call(phaseDirectories, options.phase)) {
    fail('phase must be gpumath, compiler or compiler-cuda');
  }

  return options;
}

function main() {
  const options = parseArgs(process.argv.slice(2));
  const relativeDir = phaseDirectories[options.phase];
  const targetDir = path.resolve(repoRoot, relativeDir);
  const defaultOutputDir = path.resolve(repoRoot, 'tmp', 'stryker', options.phase);
  const hasCustomOutput = choosesOutput(options.strykerArgs);
  const outputDir = defaultOutputDir;

  const heapLimit = process.env.GSharpTestHeapHardLimit || defaultHeapLimit;
  const env = { ...process.env, GSharpTestHeapHardLimit: heapLimit };

  const strykerArgs = [
    'stryker',
    '-f',
    'stryker-config.json',
    '-L',
    '--skip-version-check',
  ];

  if (!hasCustomOutput) {
    strykerArgs.push('-O', defaultOutputDir);
  }

  if (options.since !== '') {
    strykerArgs.push(`--since:${options.since}`);
  }

  strykerArgs.push(...options.strykerArgs);

  if (options.dryRun) {
    process.stdout.write('Dry run configuration:\n');
    process.stdout.write(`  Phase: ${options.phase}\n`);
    process.stdout.write(`  Directory: ${targetDir}\n`);
    process.stdout.write(`  GSharpTestHeapHardLimit: ${heapLimit}\n`);
    process.stdout.write(`  Restore command: dotnet tool restore (in ${repoRoot})\n`);
    process.stdout.write(`  Stryker command: dotnet ${strykerArgs.join(' ')}\n`);
    if (hasCustomOutput) {
      process.stdout.write('  Report check: skipped (custom output directory)\n');
    } else {
      process.stdout.write(`  Report check: ${path.join(outputDir, reportRelativePath)}\n`);
    }
    process.exit(0);
  }

  if (hasCustomOutput) {
    process.stderr.write(
      'The Stryker arguments choose the output directory (-O / --output): ' +
      'whether the run wrote its report is not checked.\n');
  } else {
    fs.mkdirSync(outputDir, { recursive: true });
    const marker = path.join(outputDir, markerName);
    fs.writeFileSync(marker, new Date().toISOString() + '\n');
  }

  const restoreResult = spawnSync('dotnet', ['tool', 'restore'], {
    cwd: repoRoot,
    stdio: 'inherit',
    env,
  });

  if (restoreResult.status !== 0) {
    process.exit(restoreResult.status || 1);
  }

  const strykerResult = spawnSync('dotnet', strykerArgs, {
    cwd: targetDir,
    stdio: 'inherit',
    env,
  });

  const exitCode = strykerResult.status !== null ? strykerResult.status : 1;
  if (exitCode !== 0) {
    process.exit(exitCode);
  }

  if (!hasCustomOutput) {
    const marker = path.join(outputDir, markerName);
    if (!fs.existsSync(marker)) {
      fail(`${marker} is missing: run marker was not found`);
    }

    const report = path.join(outputDir, reportRelativePath);
    const started = fs.statSync(marker).mtimeMs;
    if (!fs.existsSync(report) || fs.statSync(report).mtimeMs < started) {
      fail(
        `Stryker.NET wrote no ${reportRelativePath} under ${outputDir}: the run stopped before it reported ` +
        '(see the critical and error lines above, and the logs folder there)');
    }
  }

  process.exit(0);
}

main();
