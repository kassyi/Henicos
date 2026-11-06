#!/usr/bin/env node
// Tells whether a Stryker.NET run wrote its report, so that `just mutate` fails when Stryker stops early.
// Usage: node scripts/human/stryker-run-check.js (--begin | --end) --output <dir> [-- <stryker args>...]
//   Stryker.NET exits 0 after a fatal error such as "Failed to restore the project to a buildable state": it logs the
//   error and returns a score of 0 without reporting. Every run that finishes writes <dir>/reports/mutation-report.json
//   (the json reporter of the stryker-config.json files), also when no mutant was created. --begin, before the run,
//   records the start in <dir>/run-started; --end, after it, exits 1 unless the report is newer than that record.
//   The arguments after -- are the extra Stryker arguments of the run. When they choose another output directory
//   (-O / --output), the report is not under <dir>, so neither step checks anything.
'use strict';

const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..', '..');
const markerName = 'run-started';
const reportPath = path.join('reports', 'mutation-report.json');

const helpText = [
  'Usage: node scripts/human/stryker-run-check.js (--begin | --end) --output <dir> [-- <stryker args>...]',
  '',
  '  --begin    Before the run: record its start in <dir>/run-started.',
  '  --end      After the run: exit 1 unless <dir>/reports/mutation-report.json was written after --begin.',
  '  --output   The output directory given to Stryker with -O (relative to the repository root).',
  '  --         The extra Stryker arguments of the run. With -O or --output among them, the run reports',
  '             elsewhere and both steps skip the check.',
  '  --help     Show this help.',
].join('\n');

function fail(message) {
  process.stderr.write(message + '\n');
  process.exit(1);
}

function parseArgs(argv) {
  const options = { mode: null, output: null, strykerArgs: [] };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--') {
      options.strykerArgs = argv.slice(i + 1);
      break;
    } else if (arg === '--help' || arg === '-h') {
      process.stdout.write(helpText + '\n');
      process.exit(0);
    } else if (arg === '--begin' || arg === '--end') {
      if (options.mode !== null) {
        fail('give either --begin or --end');
      }
      options.mode = arg.slice(2);
    } else if (arg === '--output') {
      const value = argv[++i];
      if (value === undefined) {
        fail('--output needs a value');
      }
      options.output = value;
    } else {
      fail(`unknown argument: ${arg}\n\n${helpText}`);
    }
  }

  if (options.mode === null || options.output === null) {
    fail(helpText);
  }

  return options;
}

// -O <dir>, --output <dir>, and the -O:<dir> / --output=<dir> forms (PowerShell passes -O:<dir> as "-O:" and "<dir>")
function choosesOutput(strykerArgs) {
  return strykerArgs.some((arg) => /^(-O|--output)([:=].*)?$/.test(arg));
}

function main() {
  const options = parseArgs(process.argv.slice(2));
  if (choosesOutput(options.strykerArgs)) {
    if (options.mode === 'end') {
      process.stderr.write('The Stryker arguments choose the output directory (-O / --output): ' +
        'whether the run wrote its report is not checked.\n');
    }
    return;
  }

  const outputDir = path.resolve(repoRoot, options.output);
  const marker = path.join(outputDir, markerName);
  if (options.mode === 'begin') {
    fs.mkdirSync(outputDir, { recursive: true });
    fs.writeFileSync(marker, new Date().toISOString() + '\n');
    return;
  }

  if (!fs.existsSync(marker)) {
    fail(`${marker} is missing: run --begin before the Stryker run`);
  }

  const report = path.join(outputDir, reportPath);
  const started = fs.statSync(marker).mtimeMs;
  if (!fs.existsSync(report) || fs.statSync(report).mtimeMs < started) {
    fail(`Stryker.NET wrote no ${reportPath} under ${outputDir}: the run stopped before it reported ` +
      '(see the critical and error lines above, and the logs folder there)');
  }
}

main();
