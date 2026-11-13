#!/usr/bin/env node
'use strict';

// Type-checks the TypeScript scripts (scripts/**/*.ts, scripts/tsconfig.json) with tsc, without emitting anything.
//
// Node runs those scripts as they are (type stripping) with its built-in modules only; tsc and the Node type
// declarations are the one exception to the no-npm rule (agent-scripts.md), used only here. They are installed at the
// pinned versions below into <checkout>/tmp/typecheck (no package.json in the repository), again when a version
// changes. scripts/tsconfig.json names that folder as its type root, so an editor finds the Node types there too once
// this script has run in the checkout.
//
// Usage: node scripts/human/check-types.js [--install-only]
//   --install-only  Install the pinned tools and stop (for an editor, or before a machine goes offline).
// Exit code: 0 when the types check, 1 when tsc reports errors or the tools cannot be installed.

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const TypeScriptVersion = '7.0.2';
const NodeTypesVersion = '24.19.2';

const root = path.resolve(__dirname, '..', '..');
const toolDirectory = path.join(root, 'tmp', 'typecheck');
const tsc = path.join(toolDirectory, 'node_modules', 'typescript', 'bin', 'tsc');

/** The version of an installed package, or '' when it is missing. */
function installedVersion(name) {
  try {
    return JSON.parse(fs.readFileSync(path.join(toolDirectory, 'node_modules', name, 'package.json'), 'utf8')).version || '';
  } catch (error) {
    if (error.code !== 'ENOENT') {
      process.stderr.write(`check-types: could not read the version of ${name}: ${error.message}\n`);
    }
    return '';
  }
}

function main(argv) {
  if (argv.includes('--help') || argv.includes('-h')) {
    process.stdout.write('Usage: node scripts/human/check-types.js [--install-only]\n');
    return 0;
  }
  const unknown = argv.filter((arg) => arg !== '--install-only');
  if (unknown.length > 0) {
    process.stderr.write(`Unknown argument: ${unknown[0]}\n`);
    return 2;
  }
  if (installedVersion('typescript') !== TypeScriptVersion || installedVersion('@types/node') !== NodeTypesVersion) {
    fs.mkdirSync(toolDirectory, { recursive: true });
    const packages = [`typescript@${TypeScriptVersion}`, `@types/node@${NodeTypesVersion}`];
    const args = ['install', '--no-save', '--no-package-lock', '--no-audit', '--no-fund', '--prefix', toolDirectory, ...packages];
    // Windows: npm is a batch file, which spawn runs only through a shell; the npm next to node.exe runs without one
    const npmCli = path.join(path.dirname(process.execPath), 'node_modules', 'npm', 'bin', 'npm-cli.js');
    const install = fs.existsSync(npmCli)
      ? spawnSync(process.execPath, [npmCli, ...args], { stdio: 'inherit' })
      : spawnSync('npm', args, { stdio: 'inherit' });
    if (install.status !== 0 || !fs.existsSync(tsc)) {
      process.stderr.write(`check-types: could not install ${packages.join(' ')} into ${toolDirectory}\n`);
      return 1;
    }
  }
  if (argv.includes('--install-only')) {
    return 0;
  }
  // The type root comes from scripts/tsconfig.json (../tmp/typecheck/node_modules/@types), as for an editor
  const result = spawnSync(process.execPath, [tsc, '-p', path.join(root, 'scripts', 'tsconfig.json')], { stdio: 'inherit' });
  if (result.status === 0) {
    process.stdout.write('check-types: no type errors\n');
  }
  return result.status === null ? 1 : result.status;
}

process.exitCode = main(process.argv.slice(2));
