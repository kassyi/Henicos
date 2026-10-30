#!/usr/bin/env node
// Writes a solution filter (.slnf) of Henicos.slnx without the given projects.
// Usage: node scripts/human/ci-solution-filter.js [--exclude <project>]... [--out <file.slnf>]
//   Hybrid CI (.github/workflows/gpu-tests.yml) builds and tests Tests.Cpu and Tests.Cuda in jobs of their own; one more
//   job builds and tests this filter of everything else, so a project added to the solution is built and tested there
//   without a workflow change, as it was when CI built the whole solution. Without --out the projects are printed.
'use strict';

const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..', '..');
const solutionPath = path.join(repoRoot, 'Henicos.slnx');

const helpText = [
    'Usage: node scripts/human/ci-solution-filter.js [--exclude <project>]... [--out <file.slnf>]',
    '',
    '  --exclude   A project path as Henicos.slnx spells it (repeatable). It must be a project of the solution.',
    '  --out       Write a solution filter of the remaining projects to this file (relative to the repository root).',
    '              Without it, the remaining projects are printed one per line.',
    '  --help      Show this help.',
].join('\n');

function fail(message) {
    process.stderr.write(message + '\n');
    process.exit(1);
}

function parseArgs(argv) {
    const options = { exclude: [], out: null };
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === '--help' || arg === '-h') {
            process.stdout.write(helpText + '\n');
            process.exit(0);
        } else if (arg === '--exclude' || arg === '--out') {
            const value = argv[++i];
            if (value === undefined) {
                fail(`${arg} needs a value`);
            }
            if (arg === '--exclude') {
                options.exclude.push(value);
            } else {
                options.out = value;
            }
        } else {
            fail(`Unknown argument: ${arg}\n\n${helpText}`);
        }
    }
    return options;
}

function normalize(projectPath) {
    return projectPath.replace(/\\/g, '/');
}

function solutionProjects() {
    const solutionText = fs.readFileSync(solutionPath, 'utf8');
    const projects = [];
    for (const match of solutionText.matchAll(/<Project\s+Path="([^"]+)"/g)) {
        projects.push(normalize(match[1]));
    }
    return projects;
}

function main() {
    const options = parseArgs(process.argv.slice(2));
    const projects = solutionProjects();
    const excluded = new Set(options.exclude.map(normalize));
    for (const project of excluded) {
        if (!projects.includes(project)) {
            fail(`--exclude ${project} is not a project of Henicos.slnx; its projects:\n  ${projects.join('\n  ')}`);
        }
    }

    const remaining = projects.filter((p) => !excluded.has(p));
    if (remaining.length === 0) {
        fail('No project remains after the exclusions.');
    }

    if (options.out === null) {
        process.stdout.write(remaining.join('\n') + '\n');
        return;
    }

    const outPath = path.resolve(repoRoot, options.out);
    fs.mkdirSync(path.dirname(outPath), { recursive: true });
    const filter = {
        solution: {
            path: normalize(path.relative(path.dirname(outPath), solutionPath)),
            projects: remaining,
        },
    };
    fs.writeFileSync(outPath, JSON.stringify(filter, null, 2) + '\n');
    process.stdout.write(`Wrote ${normalize(path.relative(repoRoot, outPath))}:\n  ${remaining.join('\n  ')}\n`);
}

main();
