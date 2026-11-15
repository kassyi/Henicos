#!/usr/bin/env node
// Fails when a pull request carries a commit that was never meant to stay in the history: a work-in-progress commit
// (`wip`, `WIP: ...`) or a commit that `git rebase --autosquash` would fold into another (`fixup!`, `squash!`,
// `amend!`). Pull requests are merged with a merge commit, so every commit of the branch reaches `main` as it is.
// Merge commits (main merged into the branch) are not checked.
// Usage: node scripts/human/check-commit-messages.js --base <ref> [--head <ref>] [--github] [--self-test]
'use strict';

const { execFileSync } = require('child_process');
const path = require('path');

const helpText = [
    'Usage: node scripts/human/check-commit-messages.js --base <ref> [--head <ref>] [--github] [--self-test]',
    '',
    '  --base       The base branch (for example origin/main); the commits of <base>..<head> are checked.',
    '  --head       The tip to check (default: HEAD).',
    '  --github     Print a GitHub Actions ::error annotation per rejected commit.',
    '  --self-test  Check the subject rules and exit.',
    '  --help       Show this help.',
    '',
    'Rejected subjects: "wip" as the first word (any case, with or without a colon), and "fixup!", "squash!",',
    '"amend!". Reword such commits (git rebase -i, or a new branch) before the pull request is merged.',
].join('\n');

const rejectedSubject = /^\s*(?:wip\b|fixup!|squash!|amend!)/i;

function isRejected(subject) {
    return rejectedSubject.test(subject);
}

function selfTest() {
    const cases = [
        ['wip 643 review', true],
        ['WIP: in-place shifts (in progress)', true],
        ['wip: escape module identifier parts', true],
        ['fixup! fix(compiler): keep nested break', true],
        ['squash! test: add cases', true],
        ['amend! docs: wording', true],
        ['test: resolve WIP skips in snapshot generator tests', false],
        ['fix(generator): wipe the stale artifacts', false],
        ['chore(merge): merge main into the branch', false],
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
        else {
            console.error(`Unknown or incomplete argument: ${arg}\n\n${helpText}`);
            return 2;
        }
    }
    if (!base) {
        console.error(`--base is required\n\n${helpText}`);
        return 2;
    }

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
        console.log(`No work-in-progress or fixup commits in ${base}..${head}.`);
        return 0;
    }
    for (const commit of rejected) {
        const text = `${commit.sha.slice(0, 9)} "${commit.subject}" must be reworded before the merge`;
        console.log(github ? `::error title=Work-in-progress commit::${text}` : text);
    }
    console.log(
        `${rejected.length} commit(s) would reach main as they are (pull requests are merged with a merge commit). ` +
            'Reword them, for example with git rebase -i, and push the branch again.',
    );
    return 1;
}

process.exitCode = main(process.argv.slice(2));
