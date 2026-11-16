#!/usr/bin/env node
// Fails when a pull request carries a commit whose message does not follow the repository's Conventional Commits
// format, or when a pull request title does not follow the header rule:
//   Header: <type>(<scope>)[!]: <description>  (or Revert "..." from git revert)
//   Body:   - Details of changes:
//           - Technical background:
//           (and for fix commits: - Symptom:, - Cause:, - Solution:)
// The parser structure (header regex correspondence + line-based section splitting) is adapted from the MIT-licensed
// conventional-commits-parser / @commitlint packages, implemented with Node built-ins only.
// Usage:
//   node scripts/human/check-commit-messages.js [--base <ref> [--head <ref>]] [--title <text>] [--github]
//   node scripts/human/check-commit-messages.js (--message <text> | --file <path> | --hook | --self-test)
'use strict';

const fs = require('fs');
const { execFileSync } = require('child_process');
const path = require('path');

const helpText = [
    'Usage: node scripts/human/check-commit-messages.js [--base <ref> [--head <ref>]] [--title <text>] [--github]',
    '       node scripts/human/check-commit-messages.js (--message <text> | --file <path> | --hook | --self-test)',
    '',
    '  --base       The base branch (for example origin/main); the commits of <base>..<head> are checked.',
    '  --head       The tip to check (default: HEAD).',
    '  --title      A pull request title to check against the header rule (<type>(<scope>)[!]: <description>).',
    '  --message    A full commit message string to check (header and required body sections).',
    '  --file       A file containing a commit message to check.',
    '  --hook       PreToolUse hook for Claude Code and Antigravity on git commit and gh pr create/edit.',
    '  --github     Print a GitHub Actions ::error annotation per rejected commit or title.',
    '  --self-test  Run parser, validator and hook self-tests and exit.',
    '  --help       Show this help.',
    '',
    'Accepted commit format:',
    '  <type>(<scope>)[!]: <description>',
    '',
    '  - Details of changes:',
    '    - <what changed>',
    '  - Technical background:',
    '    - <why the change was needed>',
    '  (plus - Symptom:, - Cause:, - Solution: when <type> is fix)',
    '',
    'Allowed types: feat, fix, docs, style, refactor, perf, test, build, ci, chore, revert (and Revert "...").',
    'Scope is required and must be a single lowercase word ([a-z0-9-]+).',
].join('\n');

const types = ['feat', 'fix', 'docs', 'style', 'refactor', 'perf', 'test', 'build', 'ci', 'chore', 'revert'];
const revertSubject = /^Revert ".+"$/;
const wipSubject = /^\s*(?:wip\b|fixup!|squash!|amend!)/i;
// Adapted from conventional-commits-parser (MIT): split header into type, scope, breaking indicator, separator, subject.
const headerPattern = /^(?<type>[A-Za-z]+)(?<scopePart>\([^()]*\))?(?<breaking>!)?(?<sep>:\s*)(?<subject>.*)$/;
const scopePattern = /^[a-z0-9-]+$/;
const sectionHeaderPattern = /^[ \t]*-[ \t]+(Details of changes|Technical background|Symptom|Cause|Solution):[ \t]*(.*)$/;
const footerLinePattern = /^(?:Fixes|Closes|Resolves|Part of|Refs?|Co-authored-by:|Signed-off-by:)[ \t]*(?:#\d+|\S)/i;
const templatePlaceholder = /^\[.*\]$/;

function requiredSectionsFor(type) {
    return type === 'fix'
        ? ['Details of changes', 'Technical background', 'Symptom', 'Cause', 'Solution']
        : ['Details of changes', 'Technical background'];
}

function parseCommitMessage(raw) {
    const text = String(raw || '');
    const hasBom = text.startsWith('\uFEFF');
    const normalized = text.replace(/\r\n/g, '\n').replace(/\n+$/, '');
    const lines = normalized.split('\n');
    const header = lines[0] || '';
    const secondLine = lines.length > 1 ? lines[1] : null;
    const bodyLines = lines.length > 2 ? lines.slice(2) : [];
    const isRevert = revertSubject.test(header);
    const match = headerPattern.exec(header);

    let type = null;
    let scope = null;
    let breaking = false;
    let sep = null;
    let subject = '';
    if (match && match.groups) {
        type = match.groups.type;
        scope = match.groups.scopePart !== undefined ? match.groups.scopePart.slice(1, -1) : null;
        breaking = Boolean(match.groups.breaking);
        sep = match.groups.sep;
        subject = match.groups.subject;
    }

    const sections = new Map();
    let currentSection = null;
    let fence = null;
    for (const line of bodyLines) {
        const fenceMatch = /^ {0,3}(`{3,}|~{3,})/.exec(line);
        if (fenceMatch) {
            if (fence === null) {
                fence = fenceMatch[1];
            } else if (line.trimStart().startsWith(fence[0].repeat(fence.length))) {
                fence = null;
            }
            if (currentSection) sections.get(currentSection).push(line.trim());
            continue;
        }
        if (fence !== null) {
            if (currentSection && line.trim() !== '') sections.get(currentSection).push(line.trim());
            continue;
        }

        const secMatch = sectionHeaderPattern.exec(line);
        if (secMatch) {
            currentSection = secMatch[1];
            if (!sections.has(currentSection)) sections.set(currentSection, []);
            const inline = secMatch[2].trim();
            if (inline && !templatePlaceholder.test(inline)) sections.get(currentSection).push(inline);
            continue;
        }
        const trimmed = line.trim();
        if (trimmed === '') continue;
        if (footerLinePattern.test(trimmed)) {
            currentSection = null;
            continue;
        }
        if (currentSection) {
            const content = trimmed.replace(/^[-*]\s*/, '').trim();
            if (content && !templatePlaceholder.test(content)) sections.get(currentSection).push(content);
        }
    }

    return {
        hasBom,
        header,
        secondLine,
        bodyLines,
        isRevert,
        matchedHeader: Boolean(match),
        type,
        scope,
        breaking,
        sep,
        subject,
        sections,
    };
}

function validateCommitMessage(raw, options = {}) {
    const requireBody = options.requireBody !== undefined ? options.requireBody : true;
    const parsed = parseCommitMessage(raw);
    const errors = [];

    if (parsed.hasBom) {
        errors.push('must not start with a UTF-8 byte order mark (BOM)');
        return errors;
    }
    if (!requireBody && parsed.secondLine !== null) {
        errors.push('pull request title must be a single line');
        return errors;
    }
    if (parsed.isRevert) {
        return errors;
    }
    if (wipSubject.test(parsed.header)) {
        errors.push('work-in-progress or autosquash subject (wip, fixup!, squash!, amend!) is not allowed');
        return errors;
    }
    if (!parsed.matchedHeader) {
        errors.push('subject must match <type>(<scope>)[!]: <description>');
        return errors;
    }
    if (!types.includes(parsed.type)) {
        errors.push(`type "${parsed.type}" must be one of: ${types.join(', ')}`);
    }
    if (parsed.scope === null) {
        errors.push(`scope is required (expected ${parsed.type}(<scope>): <description>)`);
    } else if (parsed.scope === '') {
        errors.push(`scope must not be empty (expected ${parsed.type}(<scope>): <description>)`);
    } else if (!scopePattern.test(parsed.scope)) {
        errors.push(`scope "${parsed.scope}" must be a single lowercase word ([a-z0-9-]+)`);
    }
    if (parsed.sep !== ': ' || parsed.subject.length === 0 || /^\s/.test(parsed.subject)) {
        errors.push('description after ": " must start with a non-whitespace character');
    } else if (parsed.subject.trimEnd().endsWith('.')) {
        errors.push('subject description must not end with a period');
    }

    if (!requireBody) {
        return errors;
    }

    const reqSections = requiredSectionsFor(parsed.type);
    const reqLabels = reqSections.map((s) => `- ${s}:`).join(', ');
    if (parsed.secondLine === null) {
        errors.push(`commit body is missing (required sections: ${reqLabels})`);
        return errors;
    }
    if (parsed.secondLine.trim() !== '') {
        errors.push('line 2 must be blank (separate the subject and body with an empty line)');
    }
    const missing = reqSections.filter((s) => !parsed.sections.has(s));
    if (missing.length > 0) {
        errors.push(`missing required body section(s): ${missing.map((s) => `- ${s}:`).join(', ')}`);
    }
    const empty = reqSections.filter((s) => parsed.sections.has(s) && parsed.sections.get(s).length === 0);
    if (empty.length > 0) {
        errors.push(`empty body section(s): ${empty.map((s) => `- ${s}:`).join(', ')}`);
    }
    return errors;
}

// Extracts a literal commit message or PR title from a shell command for --hook mode.
// Returns { kind: 'commit' | 'title', text: string, allowHeaderOnly?: boolean } or null when not applicable / dynamic.
function extractHookTarget(command, cwd) {
    const prMatch = /\bgh\s+pr\s+(?:create|edit)\b/.exec(command);
    if (prMatch) {
        const prSlice = command.slice(prMatch.index);
        const titleMatch = /(?:--title|-t)(?:=|[ \t]+)(?:"([^"$`\\]*)"|'([^']*)')/.exec(prSlice);
        if (!titleMatch) return null;
        const title = titleMatch[1] !== undefined ? titleMatch[1] : titleMatch[2];
        return { kind: 'title', text: title };
    }

    const commitMatch = /\bgit\s+(?:-C\s+(?:"[^"\r\n]+"|'[^'\r\n]+'|[^\s"']+)\s+)*commit\b(?![-\w])/.exec(command);
    if (!commitMatch) {
        return null;
    }
    const commitSlice = command.slice(commitMatch.index);
    if (/\s(?:--help|-h|--no-edit)\b/.test(commitSlice)) {
        return null;
    }

    const fileMatch = /(?:--file|-F)(?:=|[ \t]+)(?:"([^"\r\n]+)"|'([^'\r\n]+)'|([^\s"']+))/.exec(commitSlice);
    if (fileMatch) {
        const rawPath = fileMatch[1] || fileMatch[2] || fileMatch[3];
        if (!rawPath || rawPath === '-' || rawPath.includes('$')) return null;
        const fullPath = path.resolve(cwd, rawPath);
        if (!fs.existsSync(fullPath)) return null;
        try {
            const text = fs.readFileSync(fullPath, 'utf8');
            return { kind: 'commit', text, allowHeaderOnly: isMergeCommitMessage(text) };
        } catch {
            return null;
        }
    }

    const heredocMatch = /<<-?[ \t]*['"]?([A-Za-z0-9_]+)['"]?\r?\n([\s\S]*?)\r?\n\1\b/.exec(commitSlice);
    if (heredocMatch) {
        const body = heredocMatch[2];
        if (body.includes('$(')) return null;
        return { kind: 'commit', text: body, allowHeaderOnly: isMergeCommitMessage(body) };
    }

    const msgPattern = /(?:^|\s)(?:-m|--message)(?:=|[ \t]+)(?:"((?:[^"\\]|\\[\s\S])*)"|'([^']*)'|([^\s"']+))/g;
    const parts = [];
    for (const match of commitSlice.matchAll(msgPattern)) {
        if (match[3] !== undefined) {
            if (match[3].includes('$')) return null;
            parts.push(match[3]);
        } else if (match[2] !== undefined) {
            parts.push(match[2]);
        } else if (match[1] !== undefined) {
            const dq = match[1];
            if (/\$[A-Za-z_({]/.test(dq)) return null;
            const unescaped = dq
                .replace(/\\n|`n/g, '\n')
                .replace(/\\r|`r/g, '\r')
                .replace(/\\t|`t/g, '\t')
                .replace(/\\"|`"/g, '"')
                .replace(/\\\\/g, '\\');
            parts.push(unescaped);
        }
    }
    if (parts.length === 0) return null;
    const text = parts.join('\n\n');
    return { kind: 'commit', text, allowHeaderOnly: isMergeCommitMessage(text) };
}

function isMergeCommitMessage(text) {
    const firstLine = String(text || '').split(/\r?\n/)[0] || '';
    return /^chore\(merge\): merge\b/i.test(firstLine);
}

function commandDirectory(input, antigravity, processCwd) {
    if (input.cwd) return input.cwd;
    if (antigravity && input.toolCall && input.toolCall.args) {
        const args = input.toolCall.args;
        if (typeof args.Cwd === 'string' && args.Cwd !== '') return args.Cwd;
        if (Array.isArray(input.workspacePaths) && typeof input.workspacePaths[0] === 'string') {
            return input.workspacePaths[0];
        }
    }
    return processCwd;
}

function hookAnswer(input, processCwd = process.cwd()) {
    const antigravity = input.toolCall !== undefined;
    const command = antigravity
        ? String((input.toolCall.args && input.toolCall.args.CommandLine) || '')
        : input.tool_input && typeof input.tool_input.command === 'string'
          ? input.tool_input.command
          : '';
    if (!command) return null;
    const cwd = commandDirectory(input, antigravity, processCwd);
    const target = extractHookTarget(command, cwd);
    if (!target) return null;

    const requireBody = target.kind === 'commit' && !target.allowHeaderOnly;
    const errors = validateCommitMessage(target.text, { requireBody });
    if (errors.length === 0) return null;

    const label = target.kind === 'title' ? 'Pull request title' : 'Commit message';
    const reason = [
        `${label} does not follow the repository's Conventional Commits format:`,
        ...errors.map((e) => `  - ${e}`),
        '',
        'Expected commit format:',
        '  <type>(<scope>): <description>',
        '',
        '  - Details of changes:',
        '    - <specific change details>',
        '  - Technical background:',
        '    - <why this change was necessary>',
        '  (and for fix commits: - Symptom:, - Cause:, - Solution:)',
    ].join('\n');

    if (antigravity) {
        return { decision: 'deny', reason };
    }
    return {
        hookSpecificOutput: {
            hookEventName: 'PreToolUse',
            permissionDecision: 'deny',
            permissionDecisionReason: reason,
        },
    };
}

function runHook() {
    let input;
    try {
        input = JSON.parse(fs.readFileSync(0, 'utf8'));
    } catch (error) {
        console.error(`check-commit-messages --hook: invalid hook input: ${error.message}`);
        return 1;
    }
    const answer = hookAnswer(input, process.cwd());
    if (answer !== null) process.stdout.write(JSON.stringify(answer));
    return 0;
}

function selfTest() {
    const validNonFixBody = [
        '- Details of changes:',
        '  - Add parser and body section validator.',
        '- Technical background:',
        '  - Single-line commits bypassed the Conventional Commits template.',
    ].join('\n');

    const validFixBody = [
        '- Details of changes:',
        '  - Require (<scope>) and structured body sections in check-commit-messages.js.',
        '- Technical background:',
        '  - The regex treated (<scope>) as optional and only inspected %s.',
        '- Symptom:',
        '  - Commits without (<scope>) or body sections passed CI.',
        '- Cause:',
        '  - The scope group carried a trailing ? and git log read only %s.',
        '- Solution:',
        '  - Parse the full message (%B) and validate scope and required sections.',
    ].join('\n');

    const titleCases = [
        ['wip 643 review', true],
        ['wip: escape module identifier parts', true],
        ['WIP: in-place shifts (in progress)', true],
        ['fixup! fix(compiler): keep nested break', true],
        ['squash! test(compiler): add cases', true],
        ['amend! docs(spec): wording', true],
        ['\uFEFFtest(shared): isolate global state tests', true],
        ['Fix the build', true],
        ['feat(compiler): ', true],
        ['fix(): empty scope', true],
        ['test: missing scope is rejected', true],
        ['fix(two words): space in scope', true],
        ['fix(Compiler): uppercase in scope', true],
        ['refactor(lowering) missing colon', true],
        ['Feat(compiler): capital type', true],
        ['fix(compiler): trailing period.', true],
        ['fix(compiler): multi-line\ntitle', true],
        ['test(snapshots): resolve WIP skips in snapshot generator tests', false],
        ['fix(generator): wipe the stale artifacts', false],
        ['chore(merge): merge main into the branch', false],
        ['feat(gpu-lock)!: drop the old launcher', false],
        ['Revert "test(differential): drop an unused local"', false],
    ];

    const commitCases = [
        [`feat(queue): key projects by worktree\n\n${validNonFixBody}`, false],
        [`fix(compiler): handle in parameter aliasing\n\n${validFixBody}`, false],
        [`fix(compiler): handle in parameter aliasing\n\n${validFixBody}\n\nFixes #1842`, false],
        ['Revert "test(differential): drop an unused local"\n\nThis reverts commit 1234567.', false],
        // Unindented bullets under section headers (used in historical commits)
        [
            [
                'style(compiler): wrap long line',
                '',
                '- Details of changes:',
                '- Wrap a comment line that was 121 columns long.',
                '- Technical background:',
                '- The repository keeps lines within 120 columns.',
            ].join('\n'),
            false,
        ],
        // Fenced code block inside section containing Fixes #123 does not end the section early
        [
            [
                'feat(scripts): ignore fenced blocks',
                '',
                '- Details of changes:',
                '  ```text',
                '  Fixes #123',
                '  ```',
                '  - Keep parsing the section after the fence.',
                '- Technical background:',
                '  - Code blocks can contain footer-like text.',
            ].join('\n'),
            false,
        ],
        // Fenced code block faking a section header does not satisfy required section
        [
            [
                'feat(scripts): reject fake section in fence',
                '',
                '- Details of changes:',
                '  - Only Details is real.',
                '  ```markdown',
                '- Technical background:',
                '  - Fake section inside fence.',
                '  ```',
            ].join('\n'),
            true,
        ],
        // Missing scope in commit
        [`test: keep the line of the in overload case\n\n${validNonFixBody}`, true],
        // 1-line commit without body (like 9b0e91c4f / cb17bd3d0)
        ['test(snapshots): add the target shared memory check', true],
        // Non-merge commit with chore(merge) still requires body in commit check
        ['chore(merge): rebuild the overlap test files', true],
        // Missing blank line between header and body
        [`feat(queue): key projects by worktree\n${validNonFixBody}`, true],
        // fix commit missing Symptom / Cause / Solution
        [`fix(analyzer): check kernel parameters\n\n${validNonFixBody}`, true],
        // Non-fix commit missing Technical background
        ['test(ir): count the stack allocations\n\n- Details of changes:\n  - Update expected count.', true],
        // Empty section content (goes straight from Technical background to Symptom)
        [
            [
                'fix(scripts): skip numbers inside code',
                '',
                '- Details of changes:',
                '  - Read numbers outside code blocks.',
                '- Technical background:',
                '- Symptom:',
                '  - Rejected flow-capture numbers.',
                '- Cause:',
                '  - Pattern ran over whole text.',
                '- Solution:',
                '  - Strip fenced code blocks first.',
            ].join('\n'),
            true,
        ],
        // Unfilled template placeholder
        [
            [
                'feat(queue): key projects',
                '',
                '- Details of changes:',
                '  - [Specific change details]',
                '- Technical background:',
                '  - [Why this change was necessary]',
            ].join('\n'),
            true,
        ],
    ];

    let failed = 0;
    for (const [title, expectedRejected] of titleCases) {
        const errors = validateCommitMessage(title, { requireBody: false });
        const rejected = errors.length > 0;
        if (rejected !== expectedRejected) {
            console.error(
                `self-test (title): "${title}" should ${expectedRejected ? '' : 'not '}be rejected (errors: ${errors.join('; ')})`,
            );
            failed++;
        }
    }

    for (const [msg, expectedRejected] of commitCases) {
        const errors = validateCommitMessage(msg, { requireBody: true });
        const rejected = errors.length > 0;
        if (rejected !== expectedRejected) {
            const firstLine = msg.split('\n')[0];
            console.error(
                `self-test (commit): "${firstLine}" should ${expectedRejected ? '' : 'not '}be rejected (errors: ${errors.join('; ')})`,
            );
            failed++;
        }
    }

    // Hook tests (Claude Code and Antigravity payloads)
    const hookCases = [
        [
            { tool_input: { command: 'git commit -m "test: keep the line of the in overload case"' } },
            true,
        ],
        [
            { toolCall: { args: { CommandLine: 'git commit -m "test(snapshots): one-liner without body"' } } },
            true,
        ],
        [
            {
                tool_input: {
                    command: `git tag -a v1 -m "tag msg" && git commit -m "feat(queue): key projects" -m "${validNonFixBody.replace(/\n/g, '\\n')}"`,
                },
            },
            false,
        ],
        [
            {
                tool_input: {
                    command: 'git commit -m "chore(merge): merge origin/main into fix/issue-1"',
                },
            },
            false,
        ],
        [
            { tool_input: { command: 'gh pr create --title "[Cleanup]: bad title" --body "x"' } },
            true,
        ],
        [
            { tool_input: { command: 'gh pr create --title "feat(queue): valid title" --body "x"' } },
            false,
        ],
        [
            { tool_input: { command: `git ${'-C "!" '.repeat(100)}status` } },
            false,
        ],
    ];

    for (const [payload, expectedDenied] of hookCases) {
        const answer = hookAnswer(payload);
        const denied = answer !== null;
        if (denied !== expectedDenied) {
            console.error(
                `self-test (hook): ${JSON.stringify(payload)} should ${expectedDenied ? '' : 'not '}be denied`,
            );
            failed++;
        }
    }

    const total = titleCases.length + commitCases.length + hookCases.length;
    console.log(failed === 0 ? `self-test: ${total} cases passed` : `self-test: ${failed} failed`);
    return failed === 0 ? 0 : 1;
}

function main(argv) {
    let base = '';
    let head = 'HEAD';
    let title = null;
    let message = null;
    let file = null;
    let hook = false;
    let github = false;
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === '--help' || arg === '-h') {
            console.log(helpText);
            return 0;
        }
        if (arg === '--self-test') return selfTest();
        if (arg === '--hook') hook = true;
        else if (arg === '--github') github = true;
        else if (arg === '--base' && i + 1 < argv.length) base = argv[++i];
        else if (arg === '--head' && i + 1 < argv.length) head = argv[++i];
        else if (arg === '--title' && i + 1 < argv.length) title = argv[++i];
        else if (arg === '--message' && i + 1 < argv.length) message = argv[++i];
        else if (arg === '--file' && i + 1 < argv.length) file = argv[++i];
        else {
            console.error(`Unknown or incomplete argument: ${arg}\n\n${helpText}`);
            return 2;
        }
    }
    if (hook) return runHook();
    if (!base && title === null && message === null && file === null) {
        console.error(`--base, --title, --message, --file or --hook is required\n\n${helpText}`);
        return 2;
    }

    let exitCode = 0;
    if (title !== null && checkTitle(title, github) !== 0) exitCode = 1;
    if (message !== null && checkSingleMessage(message, 'Commit message', github) !== 0) exitCode = 1;
    if (file !== null) {
        let content;
        try {
            content = fs.readFileSync(file, 'utf8');
        } catch (error) {
            console.error(`Cannot read ${file}: ${error.message}`);
            return 2;
        }
        if (checkSingleMessage(content, file, github) !== 0) exitCode = 1;
    }
    if (base && checkCommits(base, head, github) !== 0) exitCode = 1;
    return exitCode;
}

function checkTitle(title, github) {
    const errors = validateCommitMessage(title, { requireBody: false });
    if (errors.length === 0) {
        console.log('The pull request title is a valid Conventional Commits subject.');
        return 0;
    }
    const text = `The pull request title "${title}" is invalid: ${errors.join('; ')}`;
    console.log(github ? `::error title=Pull request title::${text}` : text);
    console.log('Rename it as <type>(<scope>)[!]: <description>, for example with gh pr edit <n> --title "...".');
    return 1;
}

function checkSingleMessage(raw, label, github) {
    const errors = validateCommitMessage(raw, { requireBody: true });
    if (errors.length === 0) {
        console.log(`${label} follows the Conventional Commits format.`);
        return 0;
    }
    const subject = String(raw || '').split(/\r?\n/)[0] || '';
    const text = `${label} "${subject}" is invalid: ${errors.join('; ')}`;
    console.log(github ? `::error title=Commit message::${text}` : text);
    return 1;
}

function checkCommits(base, head, github) {
    const root = path.resolve(__dirname, '..', '..');
    const log = execFileSync('git', ['log', '--no-merges', '--format=%H%x09%B%x00', `${base}..${head}`], {
        cwd: root,
        encoding: 'utf8',
    });
    const commits = log
        .split('\0')
        .map((entry) => entry.replace(/^\r?\n/, ''))
        .filter((entry) => entry.length > 0)
        .map((entry) => {
            const tab = entry.indexOf('\t');
            const sha = entry.slice(0, tab);
            const raw = entry.slice(tab + 1);
            const subject = raw.split(/\r?\n/)[0] || '';
            const errors = validateCommitMessage(raw, { requireBody: true });
            return { sha, subject, errors };
        })
        .filter((commit) => commit.errors.length > 0);

    if (commits.length === 0) {
        console.log(`Every commit of ${base}..${head} follows the Conventional Commits format.`);
        return 0;
    }
    for (const commit of commits) {
        const text = `${commit.sha.slice(0, 9)} "${commit.subject}" is invalid: ${commit.errors.join('; ')}`;
        console.log(github ? `::error title=Commit message::${text}` : text);
    }
    console.log(
        `${commits.length} commit(s) would reach main without the required Conventional Commits format ` +
            '(<type>(<scope>): <description> + "- Details of changes:" and "- Technical background:", ' +
            'plus "- Symptom:", "- Cause:", "- Solution:" for fix commits). ' +
            'Reword them, for example with git rebase -i, and push the branch again.',
    );
    return 1;
}

process.exitCode = main(process.argv.slice(2));
