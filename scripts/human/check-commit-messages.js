#!/usr/bin/env node
// Fails when a pull request carries a commit whose subject is not a Conventional Commits subject,
// `<type>[(<scope>)][!]: <description>` with one of the types of this repository, or the `Revert "..."` subject that
// `git revert` writes. Work-in-progress commits (`wip ...`) and the commits that `git rebase --autosquash` folds
// (`fixup!`, `squash!`, `amend!`) fail with it. Pull requests are merged with a merge commit, so every commit of the
// branch reaches `main` as it is. Merge commits (main merged into the branch) are not checked. With --title, the title
// of the pull request is checked by the same rule: it is the subject people read in the list of pull requests and in
// the body of the merge commit.
// Usage: node scripts/human/check-commit-messages.js [--base <ref> [--head <ref>]] [--title <text>] [--github]
'use strict';

const { execFileSync } = require('child_process');
const path = require('path');

const helpText = [
    'Usage: node scripts/human/check-commit-messages.js [--base <ref> [--head <ref>]] [--title <text>] [--github]',
    '       node scripts/human/check-commit-messages.js --self-test',
    '',
    '  --base       The base branch (for example origin/main); the commits of <base>..<head> are checked.',
    '  --head       The tip to check (default: HEAD).',
    '  --title      A pull request title to check by the same rule as a commit subject.',
    '  --github     Print a GitHub Actions ::error annotation per rejected commit or title.',
    '  --self-test  Check the subject rules and exit.',
    '  --help       Show this help.',
    '',
    'Accepted subjects: <type>[(<scope>)][!]: <description>, with a type of feat, fix, docs, style, refactor, perf,',
    'test, build, ci, chore or revert, and the Revert "..." subject of git revert. Reword any other commit',
    '(git rebase -i, or a new branch) before the pull request is merged.',
].join('\n');

const types = ['feat', 'fix', 'docs', 'style', 'refactor', 'perf', 'test', 'build', 'ci', 'chore', 'revert'];
const conventionalSubject = new RegExp(String.raw`^(?:${types.join('|')})(?:\([^()\s][^()]*\))?!?: \S`);
const revertSubject = /^Revert ".+"$/;

function isRejected(subject) {
    return !conventionalSubject.test(subject) && !revertSubject.test(subject);
}

function selfTest() {
    const cases = [
        ['wip 643 review', true],
        ['wip: escape module identifier parts', true],
        ['WIP: in-place shifts (in progress)', true],
        ['fixup! fix(compiler): keep nested break', true],
        ['squash! test: add cases', true],
        ['amend! docs: wording', true],
        ['﻿test(shared): isolate global state tests', true],
        ['Fix the build', true],
        ['feat: ', true],
        ['fix(): empty scope', true],
        ['refactor(lowering) missing colon', true],
        ['Feat: capital type', true],
        ['test: resolve WIP skips in snapshot generator tests', false],
        ['fix(generator): wipe the stale artifacts', false],
        ['chore(merge): merge main into the branch', false],
        ['feat(api)!: drop the old launcher', false],
        ['Revert "test(differential): drop an unused local"', false],
    ];
    let failed = 0;
    for (const [subject, expected] of cases) {
        if (isRejected(subject) !== expected) {
            console.error(`self-test: "${subject}" should ${expected ? '' : 'not '}be rejected`);
            failed++;
        }
    }
    console.log(failed === 0 ? `self-test: ${cases.length} cases passed` : `self-test: ${failed} failed`);
    return failed === 0 ? 0 : 1;
}

function main(argv) {
    let base = '';
    let head = 'HEAD';
    let title = null;
    let github = false;
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === '--help' || arg === '-h') {
            console.log(helpText);
            return 0;
        }
        if (arg === '--self-test') return selfTest();
        if (arg === '--github') github = true;
        else if (arg === '--base' && i + 1 < argv.length) base = argv[++i];
        else if (arg === '--head' && i + 1 < argv.length) head = argv[++i];
        else if (arg === '--title' && i + 1 < argv.length) title = argv[++i];
        else {
            console.error(`Unknown or incomplete argument: ${arg}\n\n${helpText}`);
            return 2;
        }
    }
    if (!base && title === null) {
        console.error(`--base or --title is required\n\n${helpText}`);
        return 2;
    }

    let exitCode = 0;
    if (title !== null && checkTitle(title, github) !== 0) exitCode = 1;
    if (base && checkCommits(base, head, github) !== 0) exitCode = 1;
    return exitCode;
}

function checkTitle(title, github) {
    if (!isRejected(title)) {
        console.log('The pull request title is a Conventional Commits subject.');
        return 0;
    }
    const text = `The pull request title "${title}" is not a Conventional Commits subject`;
    console.log(github ? `::error title=Pull request title::${text}` : text);
    console.log('Rename it as <type>[(<scope>)][!]: <description>, for example with gh pr edit <n> --title "...".');
    return 1;
}

function checkCommits(base, head, github) {
    const root = path.resolve(__dirname, '..', '..');
    const log = execFileSync('git', ['log', '--no-merges', '--format=%H%x09%s', `${base}..${head}`], {
        cwd: root,
        encoding: 'utf8',
    });
    const rejected = log
        .split('\n')
        .filter((line) => line.length > 0)
        .map((line) => {
            const tab = line.indexOf('\t');
            return { sha: line.slice(0, tab), subject: line.slice(tab + 1) };
        })
        .filter((commit) => isRejected(commit.subject));

    if (rejected.length === 0) {
        console.log(`Every commit of ${base}..${head} has a Conventional Commits subject.`);
        return 0;
    }
    for (const commit of rejected) {
        const text = `${commit.sha.slice(0, 9)} "${commit.subject}" is not a Conventional Commits subject`;
        console.log(github ? `::error title=Commit message::${text}` : text);
    }
    console.log(
        `${rejected.length} commit(s) would reach main as they are (pull requests are merged with a merge commit). ` +
            'Reword them, for example with git rebase -i, and push the branch again.',
    );
    return 1;
}

process.exitCode = main(process.argv.slice(2));
