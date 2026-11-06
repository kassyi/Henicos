#!/usr/bin/env node
'use strict';

/*
 * fuzz-differential.js - facade of the differential kernel fuzzer.
 * Builds tools/fuzz-diff and passes every argument to `fuzz-diff run` (which also handles --compare-base), then
 * returns its exit code. All logic (options, batches, builds, runs, reduction, findings) is in tools/fuzz-diff;
 * run with --help for the options. Usage: node scripts/human/fuzz-differential.js [options]
 */

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const repoRoot = path.resolve(__dirname, '..', '..');
const hostOs = process.platform === 'win32' ? 'win' : process.platform === 'linux' ? 'linux' : process.platform === 'darwin' ? 'osx' : 'unix';
const toolProject = path.join(repoRoot, 'tools', 'fuzz-diff', 'Kassyi.GSharp.Tools.FuzzDiff.csproj');
const toolDll = path.join(repoRoot, 'tools', 'fuzz-diff', 'bin', hostOs, 'Debug', 'net10.0', 'Kassyi.GSharp.Tools.FuzzDiff.dll');
const env = { ...process.env, DiffEngine_Disabled: 'true', CI: 'true', DOTNET_NOLOGO: '1' };

function fail(message, code = 1) {
  process.stderr.write(`fuzz-differential: ${message}\n`);
  process.exit(code);
}

function buildTool() {
  const args = ['build', toolProject, '-c', 'Debug', '-m:1', '-nodeReuse:false'];
  if (fs.existsSync(path.join(path.dirname(toolProject), 'obj', hostOs, 'project.assets.json'))) args.push('--no-restore');
  const build = spawnSync('dotnet', args, { cwd: repoRoot, env, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024, timeout: 240_000 });
  if (build.status !== 0) {
    process.stderr.write(`${build.stdout || ''}\n${build.stderr || ''}\n`);
    fail('Failed to build tools/fuzz-diff.');
  }
}

const argv = process.argv.slice(2);
const isHelp = argv.includes('--help') || argv.includes('-h');
const isCompare = argv.some((a) => a === '--compare-base' || a.startsWith('--compare-base='));
if (!isHelp) {
  process.stdout.write(isCompare
    ? '[fuzz-compare] Building tools/fuzz-diff...\n'
    : '[fuzz-diff] Building tools/fuzz-diff and compiler/runtime prerequisites...\n');
}
buildTool();

// The ids of this process and its parent let the tool recognize a GPU lock that scripts/agent/gpu-lock.js took for this run.
const result = spawnSync('dotnet', ['exec', toolDll, 'run', '--repo-root', repoRoot, ...argv], {
  cwd: repoRoot,
  env: { ...env, FUZZ_DIFF_LAUNCHER_PIDS: `${process.pid},${process.ppid}` },
  stdio: 'inherit',
});
if (result.error) fail(`Failed to start fuzz-diff run: ${result.error.message}`);
process.exit(result.status === null ? 1 : result.status);
