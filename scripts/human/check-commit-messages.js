#!/usr/bin/env node
// Fails when a pull request carries a commit whose message does not follow the repository's Conventional Commits
// format, or when a pull request title does not follow the header rule:
//   Header: <type>(<scope>)[!]: <description>  (or Revert "..." from git revert)
//   Body:   - Details of changes:
//           - Technical background:
//           (and for fix commits: - Symptom:, - Cause:, - Solution:)
// The header follows the grammar of Conventional Commits 1.0.0; the body sections are those of the commit.en skill.
// A commit made before RulesCutoff keeps the earlier rule (subject only, scope optional). --hook reads the command
// with the shell reader of check-queued-command.js.
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
// The commit that brought in the required scope and body sections. A commit descends from it once its branch was made
// from, or merged, a main that has it; the commits of older branches keep the earlier rule (a Conventional Commits
// subject, scope optional, no body rule), so that the branches open when the rule came in need no rewording. Ancestry,
// not a date: an author date survives a rebase and a committer date changes on an amend.
const RulesCutoff = '220618bd605020b4d2b153e3f3c08d49b292b82d';
const revertSubject = /^Revert ".+"$/;
const wipSubject = /^\s*(?:wip\b|fixup!|squash!|amend!)/i;
// The Conventional Commits 1.0.0 header: type, scope, breaking indicator, separator, subject.
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

// options.title: a pull request title (one line, no body). options.requireBody: check the body sections (default
// true). options.legacy: the rule of a commit made before RulesCutoff: a Conventional Commits subject whose scope may
// be left out, with no body rule.
function validateCommitMessage(raw, options = {}) {
    const legacy = options.legacy === true;
    const requireBody = !legacy && (options.requireBody !== undefined ? options.requireBody : true);
    const parsed = parseCommitMessage(raw);
    const errors = [];

    if (parsed.hasBom) {
        errors.push('must not start with a UTF-8 byte order mark (BOM)');
        return errors;
    }
    if (options.title === true && parsed.secondLine !== null) {
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
        if (!legacy) {
            errors.push(`scope is required (expected ${parsed.type}(<scope>): <description>)`);
        }
    } else if (parsed.scope === '') {
        errors.push(`scope must not be empty (expected ${parsed.type}(<scope>): <description>)`);
    } else if (!scopePattern.test(parsed.scope)) {
        errors.push(`scope "${parsed.scope}" must be a single lowercase word ([a-z0-9-]+)`);
    }
    if (parsed.sep !== ': ' || parsed.subject.length === 0 || /^\s/.test(parsed.subject)) {
        errors.push('description after ": " must start with a non-whitespace character');
    } else if (!legacy && parsed.subject.trimEnd().endsWith('.')) {
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

// The shell reader of check-queued-command.js: splits a command at &&, ||, ;, | and line breaks, reads the quotes of
// the tool's dialect, skips heredoc bodies and sees through wrappers (bash -c, pwsh -Command) and launch prefixes. Only
// --hook needs it; the CI modes run without it.
let shellReader = null;
try {
    shellReader = require('../agent/check-queued-command/shell-reader.js');
} catch (error) {
    shellReader = null;
    process.stderr.write(`check-commit-messages: the shell reader could not be loaded: ${error.message}\n`);
}

// The text a word passes on, or null when the shell computes it ($VAR, $(...), a here-string): such a message cannot be
// checked before it exists, and CI checks the commit. The one computed form read here is the body of
// "$(cat <<'EOF' ... EOF)", the usual way to pass a multi-line message.
function literalArgument(raw, kind) {
    const r = shellReader;
    if (raw === '@here-string') {
        return null;
    }
    // PowerShell turns `n, `r and `t into control characters in double quotes; the reader keeps only the letter
    const word = kind === 'pwsh' ? raw.replace(/`n/g, '\n').replace(/`r/g, '\r').replace(/`t/g, '\t') : raw;
    const text = r.argumentText(word);
    const heredoc = /^\$\(cat <<-?[ \t]*['"]?([A-Za-z_]\w*)['"]?\r?\n([\s\S]*?)\r?\n[ \t]*\1[ \t]*\r?\n?\)$/.exec(text);
    if (heredoc !== null) {
        return heredoc[2].includes('$(') ? null : heredoc[2];
    }
    return /\$/.test(word.replace(/'[^']*'/g, '')) ? null : text;
}

// The message of one `git [global options] commit ...` word list, or null when it is not a commit, the message comes
// from an editor, another commit or stdin, or the shell computes it.
function gitCommitTarget(list, cwd, kind) {
    const r = shellReader;
    let i = 1;
    for (; i < list.length; i++) {
        const option = r.argumentText(list[i]);
        if (['-C', '-c', '--git-dir', '--work-tree', '--namespace', '--exec-path'].includes(option)) {
            i++;
        } else if (!option.startsWith('-')) {
            break;
        }
    }
    if (i >= list.length || r.argumentText(list[i]) !== 'commit') {
        return null;
    }

    const messages = [];
    let file = null;
    for (let j = i + 1; j < list.length; j++) {
        const raw = list[j];
        const word = r.argumentText(raw);
        const next = () => (j + 1 < list.length ? list[++j] : null);
        if (word === '--') {
            break;
        }
        if (/^--(no-edit|fixup|squash|reuse-message|reedit-message|allow-empty-message)\b/.test(word)) {
            return null;
        }
        if (word === '-m' || word === '--message') {
            messages.push(next());
        } else if (word.startsWith('--message=')) {
            messages.push(raw.slice(raw.indexOf('=') + 1));
        } else if (word === '-F' || word === '--file') {
            file = next();
        } else if (word.startsWith('--file=')) {
            file = raw.slice(raw.indexOf('=') + 1);
        } else if (/^-[A-Za-z]/.test(word)) {
            // A cluster of short options: -qam "msg", -am"msg", -sF file; -c / -C reuse another commit's message
            for (let k = 1; k < word.length; k++) {
                const letter = word[k];
                if (letter === 'c' || letter === 'C') {
                    return null;
                }
                if (letter === 'm' || letter === 'F') {
                    const attached = raw.slice(raw.indexOf(letter, 1) + 1);
                    const value = attached !== '' ? attached : next();
                    if (letter === 'm') {
                        messages.push(value);
                    } else {
                        file = value;
                    }
                    break;
                }
            }
        }
    }

    let text = null;
    if (messages.length > 0) {
        const parts = messages.map((raw) => (raw === null ? null : literalArgument(raw, kind)));
        if (parts.includes(null)) {
            return null;
        }
        text = parts.join('\n\n');
    } else if (file !== null) {
        const name = literalArgument(file, kind);
        if (name === null || name === '-') {
            return null;
        }
        const fullPath = path.resolve(cwd, name);
        try {
            text = fs.readFileSync(fullPath, 'utf8');
        } catch {
            return null;
        }
    }
    return text === null ? null : { kind: 'commit', text, allowHeaderOnly: isMergeCommitMessage(text) };
}

// The title of one `gh pr create|edit ... --title <t>` word list, or null.
function ghPrTitle(list, kind) {
    const r = shellReader;
    if (r.argumentText(list[1] || '') !== 'pr' || !['create', 'edit'].includes(r.argumentText(list[2] || ''))) {
        return null;
    }
    for (let j = 3; j < list.length; j++) {
        const word = r.argumentText(list[j]);
        const raw =
            word === '--title' || word === '-t'
                ? list[j + 1] || null
                : word.startsWith('--title=')
                  ? list[j].slice(list[j].indexOf('=') + 1)
                  : undefined;
        if (raw !== undefined) {
            const text = raw === null ? null : literalArgument(raw, kind);
            return text === null ? null : { kind: 'title', text };
        }
    }
    return null;
}

// The commit messages and pull request titles a command would create, read in the tool's dialect ('bash' or 'pwsh'):
// every git commit and gh pr create / edit of every segment, also inside $( ), bash -c and pwsh -Command.
function hookTargets(command, kind, cwd) {
    const r = shellReader;
    if (r === null) {
        return [];
    }
    return r.withDialect(kind, () => {
        const targets = [];
        const pending = [command];
        while (pending.length > 0) {
            for (const segment of r.splitSegments(pending.shift())) {
                pending.push(...r.substitutions(segment));
                const list = r.dropPrefixes(r.words(segment));
                if (list.length === 0) {
                    continue;
                }
                const name = r.programName(list[0]);
                const shellIndex = r.shellCommandIndex(name, list);
                if (shellIndex > 0 && shellIndex < list.length) {
                    pending.push(r.argumentText(list[shellIndex]));
                    continue;
                }
                const target =
                    name === 'git' ? gitCommitTarget(list, cwd, kind) : name === 'gh' ? ghPrTitle(list, kind) : null;
                if (target !== null) {
                    targets.push(target);
                }
            }
        }
        return targets;
    });
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
    const kind = antigravity || input.tool_name === 'PowerShell' ? 'pwsh' : 'bash';
    const findings = hookTargets(command, kind, cwd)
        .map((target) => ({
            target,
            errors: validateCommitMessage(target.text, {
                title: target.kind === 'title',
                requireBody: target.kind === 'commit' && !target.allowHeaderOnly,
            }),
        }))
        .filter((finding) => finding.errors.length > 0);
    if (findings.length === 0) return null;

    const reason = [
        ...findings.flatMap(({ target, errors }) => [
            `${target.kind === 'title' ? 'Pull request title' : 'Commit message'} "${target.text.split(/\r?\n/)[0]}" ` +
                "does not follow the repository's Conventional Commits format:",
            ...errors.map((e) => `  - ${e}`),
        ]),
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
        const errors = validateCommitMessage(title, { title: true, requireBody: false });
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
                    command: `git tag -a v1 -m "tag msg" && git commit -m "feat(queue): key projects" -m "${validNonFixBody}"`,
                },
            },
            false,
        ],
        // bash keeps \n in double quotes as the two characters, so this message has no body sections
        [
            {
                tool_input: {
                    command: `git commit -m "feat(queue): key projects" -m "${validNonFixBody.replace(/\n/g, '\\n')}"`,
                },
            },
            true,
        ],
        // Short options clustered with -m, and git options before the subcommand
        [{ tool_input: { command: 'git commit -qam "wip"' } }, true],
        [{ tool_input: { command: 'git commit -am"wip"' } }, true],
        [{ tool_input: { command: 'git -c user.name=x commit -m "wip"' } }, true],
        [{ tool_input: { command: 'cd x && git -C "a b" commit --message="wip"' } }, true],
        [{ tool_input: { command: 'bash -c "git commit -m wip"' } }, true],
        // Text that only names a command is data
        [{ tool_input: { command: 'echo "git commit -m wip"' } }, false],
        [{ tool_input: { command: 'git log --grep "commit -m wip"' } }, false],
        // A message the shell computes is left to CI; the usual heredoc form is read
        [{ tool_input: { command: 'git commit -m "$MSG"' } }, false],
        [{ tool_input: { command: 'git commit -F -' } }, false],
        [{ tool_input: { command: 'git commit --amend --no-edit' } }, false],
        [{ tool_input: { command: "git commit -m \"$(cat <<'EOF'\nwip\nEOF\n)\"" } }, true],
        [
            {
                tool_input: {
                    command: `git commit -m "$(cat <<'EOF'\nfeat(queue): key projects\n\n${validNonFixBody}\nEOF\n)"`,
                },
            },
            false,
        ],
        // PowerShell: `n is a line break in double quotes
        [
            {
                tool_name: 'PowerShell',
                tool_input: {
                    command: `git commit -m "feat(queue): key projects\`n\`n${validNonFixBody.replace(/\n/g, '`n')}"`,
                },
            },
            false,
        ],
        [{ toolCall: { args: { CommandLine: "git commit -m 'wip'" } } }, true],
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

    // RulesCutoff: a commit before it keeps the earlier rule (subject only, scope optional); one after it needs the
    // scope and the body sections. The ancestry is given here, as git would answer it.
    const entries = [
        { sha: 'old-plain', raw: 'test: keep the line of the in overload case' },
        { sha: 'old-wip', raw: 'wip: half done' },
        { sha: 'old-prose', raw: 'fix(compiler): dispose the resource\n\nThe jump skipped the exit label.' },
        { sha: 'new-plain', raw: 'test: keep the line of the in overload case' },
        { sha: 'new-full', raw: `test(ir): keep the line\n\n${validNonFixBody}` },
    ];
    const invalid = findInvalidCommits(entries, (sha) => sha.startsWith('new-')).map((commit) => commit.sha);
    const cutoffCases = [
        ['old-plain', false],
        ['old-wip', true],
        ['old-prose', false],
        ['new-plain', true],
        ['new-full', false],
    ];
    for (const [sha, expectedInvalid] of cutoffCases) {
        if (invalid.includes(sha) !== expectedInvalid) {
            console.error(`self-test (cutoff): ${sha} should ${expectedInvalid ? '' : 'not '}be rejected`);
            failed++;
        }
    }

    const total = titleCases.length + commitCases.length + hookCases.length + cutoffCases.length;
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
    const errors = validateCommitMessage(title, { title: true, requireBody: false });
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

// Whether `sha` descends from RulesCutoff (or is it), asked of git. A cutoff git does not know is an error, not a pass:
// a shallow checkout (fetch-depth other than 0) would otherwise let every commit through on the legacy rule.
function descendsFromCutoff(sha, root) {
    try {
        execFileSync('git', ['merge-base', '--is-ancestor', RulesCutoff, sha], { cwd: root, stdio: 'ignore' });
        return true;
    } catch (error) {
        if (error.status === 1) {
            return false;
        }
        throw new Error(`git merge-base --is-ancestor ${RulesCutoff} ${sha} failed; is the history fetched?`);
    }
}

// The commits with their errors: the full rule for a commit that descends from RulesCutoff, the legacy rule for one made
// before it (a commit of a branch that has not merged main since the rule came in).
function findInvalidCommits(entries, descends) {
    return entries
        .map(({ sha, raw }) => ({
            sha,
            subject: raw.split(/\r?\n/)[0] || '',
            errors: validateCommitMessage(raw, { requireBody: true, legacy: !descends(sha) }),
        }))
        .filter((commit) => commit.errors.length > 0);
}

function checkCommits(base, head, github) {
    const root = path.resolve(__dirname, '..', '..');
    const log = execFileSync('git', ['log', '--no-merges', '--format=%H%x09%B%x00', `${base}..${head}`], {
        cwd: root,
        encoding: 'utf8',
    });
    const entries = log
        .split('\0')
        .map((entry) => entry.replace(/^\r?\n/, ''))
        .filter((entry) => entry.length > 0)
        .map((entry) => ({ sha: entry.slice(0, entry.indexOf('\t')), raw: entry.slice(entry.indexOf('\t') + 1) }));
    const commits = findInvalidCommits(entries, (sha) => descendsFromCutoff(sha, root));

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
