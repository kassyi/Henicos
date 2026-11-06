#!/usr/bin/env node
// Lists the smell findings that this checkout has and the base of its branch does not (CI: smell-scan.yml writes them as
// warnings on a pull request). Both sides are measured completely with the scanner and rules of this checkout: the
// sources of the merge base are checked out into a temporary worktree, so a limit change can never look like a new
// finding, and a part of a partial type that changed is added up with the parts that did not. Findings are matched by
// fingerprint (rule, metric and qualified symbol, not path or line), so moving a type to another file is not new, and
// the report never changes the exit code.
// Usage: node scripts/human/smell-scan-new.js [--base <ref>] [--format github|text|markdown|json]
//   --base    The branch to compare with, through its merge base with HEAD (default origin/main).
//   --format  How the new findings are written (default github: one workflow warning each).
//   --help    Show this help.
'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const { execFileSync, spawnSync } = require('child_process');

const repoRoot = path.resolve(__dirname, '..', '..');
const scanner = path.join(__dirname, 'smell-scan.js');

const helpText = [
    'Usage: node scripts/human/smell-scan-new.js [--base <ref>] [--format github|text|markdown|json]',
    '',
    '  --base     Branch to compare with, through its merge base with HEAD (default origin/main).',
    '  --format   github (default), text, markdown or json.',
    '  --help     Show this help.',
].join('\n');

function fail(message) {
    process.stderr.write(`smell-scan-new: ${message}\n`);
    process.exit(1);
}

function parseArgs(argv) {
    const options = { base: 'origin/main', format: 'github' };
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === '--help' || arg === '-h') {
            process.stdout.write(helpText + '\n');
            process.exit(0);
        } else if (arg === '--base' || arg === '--format') {
            if (i + 1 >= argv.length) {
                fail(`${arg} needs a value`);
            }
            options[arg.slice(2)] = argv[++i];
        } else {
            fail(`unknown argument ${arg}\n\n${helpText}`);
        }
    }
    return options;
}

function git(...args) {
    return execFileSync('git', ['-C', repoRoot, ...args], { encoding: 'utf8' }).trim();
}

function scan(args) {
    const result = spawnSync(process.execPath, [scanner, ...args], { stdio: 'inherit', cwd: repoRoot });
    if (result.status !== 0) {
        // Thrown, not exited: main removes the temporary worktree first
        throw new Error(`the scanner failed with exit code ${result.status}`);
    }
}

function main() {
    const options = parseArgs(process.argv.slice(2));
    const mergeBase = git('merge-base', options.base, 'HEAD');
    const work = fs.mkdtempSync(path.join(os.tmpdir(), 'smell-scan-new-'));
    const baseTree = path.join(work, 'base');
    git('worktree', 'add', '--detach', '--quiet', baseTree, mergeBase);
    try {
        const config = path.join(repoRoot, 'tools', 'smell-scan', 'smells.yml');
        const baseline = path.join(work, 'base.json');
        scan(['--root', baseTree, '--config', config, '--out', baseline]);
        scan(['--root', repoRoot, '--config', config, '--baseline', baseline, '--format', options.format]);
    } catch (error) {
        process.stderr.write(`smell-scan-new: ${error.message}\n`);
        process.exitCode = 1;
    } finally {
        git('worktree', 'remove', '--force', baseTree);
        fs.rmSync(work, { recursive: true, force: true });
    }
}

main();
