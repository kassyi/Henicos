#!/usr/bin/env node
// Runs the smell scanner (tools/smell-scan), which measures size, nesting and error-handling smells of C# files with
// Roslyn and prints findings as review triggers. The scanner is a .NET tool because it needs Roslyn; this script only
// finds it and passes the arguments through, so the rules and the output formats live in one place.
// Usage: node scripts/human/smell-scan.js [options] [paths...]
//   Every argument goes to the tool as it is, except `--no-build`, which this script takes itself: it runs the tool
//   as it was last built, so that scans started side by side do not compete for the build (run one scan without it
//   first). `--help` prints the tool's own usage (`just smell-scan --help`).
//   Relative paths are read from the current directory, and the repository is the one that holds it, so the script can
//   scan another checkout (a worktree) while using the scanner built from this one.
//   The exit code is the tool's: 0 scan done (findings do not change it), 1 internal error, 2 wrong command line.
'use strict';

const path = require('path');
const { spawnSync } = require('child_process');

const repoRoot = path.resolve(__dirname, '..', '..');
const project = path.join(repoRoot, 'tools', 'smell-scan');

const args = process.argv.slice(2);
const noBuild = args.includes('--no-build');
const toolArgs = args.filter((arg) => arg !== '--no-build');
const runArgs = ['run', '--project', project, '-c', 'Release', ...(noBuild ? ['--no-build'] : []), '--', ...toolArgs];

const result = spawnSync('dotnet', runArgs, { stdio: 'inherit' });

if (result.error) {
    process.stderr.write(`smell-scan: could not start dotnet (${result.error.message}); install the .NET SDK.\n`);
    process.exit(1);
}
process.exit(result.status === null ? 1 : result.status);
