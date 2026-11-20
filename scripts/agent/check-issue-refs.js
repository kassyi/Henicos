#!/usr/bin/env node
// Checks the issue and pull request numbers (#1234) in a text against GitHub, so that a mistyped number is caught
// before it reaches an issue, a pull request, a comment, a commit or a branch name.
// Usage:
//   node scripts/agent/check-issue-refs.js --text "<text>"      check the numbers in the text
//   node scripts/agent/check-issue-refs.js --file <path>...     check the numbers in the files
//   node scripts/agent/check-issue-refs.js --stdin              check the numbers in standard input
//   node scripts/agent/check-issue-refs.js --hook               PreToolUse hook of Claude Code and Antigravity
//   node scripts/agent/check-issue-refs.js --self-test          runs the hook on fixed payloads, GitHub lookup stubbed
//   --repo <owner/name>   repository to look the numbers up in (default: the origin remote)
// Prints one line per number: number, kind (issue / PR), state, milestone and title. A "#n" inside an inline code span or
// a fenced code block is not a reference (GitHub does not link it) and is not checked.
// Fails (exit 1) when a number does not exist, or when a closing keyword (Fixes / Closes / Resolves #n) names a pull
// request or an issue that is already closed. In --hook mode the same failures deny the command; otherwise the table
// is passed back to the agent as additional context, and the command goes on to the normal permission check.
// The hook also denies a `gh issue create` with the `bug` label that has no `found-by:` label (foundByProblem).
'use strict';

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');
const shellReader = require('./check-queued-command/shell-reader.js');

const repoRoot = path.resolve(__dirname, '..', '..');

const usage = 'Usage: check-issue-refs.js (--text <text> | --file <path>... | --stdin | --hook | --self-test) '
    + '[--repo <owner/name>]';

// "#1234" not preceded by a word character, "&" (HTML entity) or "/" (owner/repo#1234, a URL fragment).
const refPattern = /(?<![\w&\/])#(\d{1,6})\b/g;
// "Fixes #12", "closes: #12", "Resolved #12" (GitHub's closing keywords)
const closingPattern = /\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\b:?\s+#(\d{1,6})\b/gi;
// Branch names that carry the issue number (<type>/issue-<n>-<slug>)
const branchPattern = /\bissue-(\d{1,6})\b/g;
// The issue or PR a gh command acts on (gh issue comment 1234)
const targetPattern = /\bgh\s+(?:issue|pr)\s+(?:comment|edit|close|review|merge)\s+(\d{1,6})\b/g;
// Commands whose text is published: issue and PR bodies and comments, commits, new branches and worktrees.
// `gh -R owner/repo issue create`, `gh.exe` and the alias `gh issue new` publish too. One reading per word in the
// repository flag (`[^\s=]\S*`), so that a failed match does not backtrack exponentially.
const publishingCommand = new RegExp(
    String.raw`\bgh(?:\.exe)?\s+(?:(?:-R|--repo)(?:=|\s+)?[^\s=]\S*\s+)*`
    + String.raw`(?:issue|pr)\s+(?:create|new|comment|edit|close|review|merge)\b`
    + String.raw`|\bgit\s+(?:commit|switch|checkout|branch|worktree)\b`);

const foundByLabels = [
    'found-by:audit',
    'found-by:matrix',
    'found-by:differential',
    'found-by:review',
    'found-by:operation',
];

function fail(message) {
    console.error(message);
    process.exit(1);
}

function parseArgs(argv) {
    const options = { texts: [], files: [], stdin: false, hook: false, selfTest: false, repo: null };
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        const value = () => {
            const v = argv[++i];
            if (v === undefined) fail(`${arg} requires a value.`);
            return v;
        };
        switch (arg) {
            case '--text': options.texts.push(value()); break;
            case '--file': options.files.push(value()); break;
            case '--stdin': options.stdin = true; break;
            case '--hook': options.hook = true; break;
            case '--self-test': options.selfTest = true; break;
            case '--repo': options.repo = value(); break;
            case '--help':
            case '-h': console.log(usage); process.exit(0); break;
            default: fail(`Unknown option: ${arg}\n${usage}`);
        }
    }
    const hasInput = options.stdin || options.texts.length > 0 || options.files.length > 0;
    if (!options.hook && !options.selfTest && !hasInput) fail(usage);
    return options;
}

function readStdin() {
    try {
        return fs.readFileSync(0, 'utf8');
    } catch (error) {
        fail(`Cannot read standard input: ${error.message}`);
    }
}

function collect(text, pattern) {
    const numbers = new Set();
    for (const match of text.matchAll(pattern)) numbers.add(Number(match[1]));
    return numbers;
}

// Fenced code blocks (``` or ~~~, closed by a fence of the same character at least as long) and inline code spans
// (a run of backticks closed by a run of the same length).
const fencedCodePattern = /^ {0,3}(`{3,}|~{3,})[^\n]*\n[\s\S]*?(?:^ {0,3}\1[`~]*[ \t]*$|(?![\s\S]))/gm;
const inlineCodePattern = /(`+)(?!`)[\s\S]*?(?<!`)\1(?!`)/g;

// The text without its code: GitHub does not link "#n" inside code, so a number there (Roslyn's flow-capture "#0",
// a C preprocessor line) is not a reference. Each code part becomes one space so that text around it stays apart.
function withoutCode(text) {
    return text.replace(fencedCodePattern, ' ').replace(inlineCodePattern, ' ');
}

function defaultRepo(cwd) {
    let url;
    try {
        url = execFileSync('git', ['remote', 'get-url', 'origin'], { cwd, encoding: 'utf8' }).trim();
    } catch (error) {
        fail(`Cannot read the origin remote (${error.message.split('\n')[0]}); pass --repo <owner/name>.`);
    }
    const match = /github\.com[:/]([^/]+)\/(.+?)(?:\.git)?$/.exec(url);
    if (!match) fail(`The origin remote is not a GitHub URL: ${url}; pass --repo <owner/name>.`);
    return `${match[1]}/${match[2]}`;
}

// One GraphQL request for every number. A number that does not exist comes back as null (with an error entry).
function lookUp(repo, numbers) {
    const [owner, name] = repo.split('/');
    const fields = numbers
        .map(n => `n${n}: issueOrPullRequest(number: ${n}) { __typename `
            + `... on Issue { title state milestone { title } } `
            + `... on PullRequest { title state milestone { title } } }`)
        .join(' ');
    const query = `query($owner: String!, $name: String!) { repository(owner: $owner, name: $name) { ${fields} } }`;
    let output;
    try {
        output = execFileSync(
            'gh',
            ['api', 'graphql', '-f', `query=${query}`, '-F', `owner=${owner}`, '-F', `name=${name}`],
            { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }
        );
    } catch (error) {
        // gh exits non-zero when a number does not exist, but still prints the partial data.
        output = error.stdout;
        if (!output) fail(`gh api graphql failed: ${String(error.stderr || error.message).trim()}`);
    }
    let parsed;
    try {
        parsed = JSON.parse(output);
    } catch (error) {
        fail(`Unexpected gh output (${error.message}): ${output.slice(0, 200)}`);
    }
    const repository = parsed.data && parsed.data.repository;
    if (!repository) fail(`Repository ${repo} not found: ${JSON.stringify(parsed.errors || parsed)}`);
    const result = new Map();
    for (const n of numbers) result.set(n, repository[`n${n}`] || null);
    return result;
}

// Returns the table lines and the problems found.
function check(text, repo, lookUpNumbers = lookUp) {
    const prose = withoutCode(text);
    const numbers = new Set([
        ...collect(prose, refPattern),
        ...collect(text, branchPattern),
        ...collect(text, targetPattern),
    ]);
    const closing = collect(prose, closingPattern);
    if (numbers.size === 0) return { lines: [], problems: [] };
    const sorted = [...numbers].sort((a, b) => a - b);
    const found = lookUpNumbers(repo, sorted);
    const lines = [];
    const problems = [];
    for (const n of sorted) {
        const item = found.get(n);
        if (!item) {
            lines.push(`#${n}\t(does not exist)`);
            problems.push(`#${n} does not exist in ${repo}.`);
            continue;
        }
        const kind = item.__typename === 'PullRequest' ? 'PR' : 'issue';
        const milestone = item.milestone ? item.milestone.title : '-';
        lines.push(`#${n}\t${kind}\t${item.state}\t${milestone}\t${item.title}`);
        if (closing.has(n) && kind === 'PR') problems.push(`A closing keyword names #${n}, which is a pull request (${item.title}).`);
        else if (closing.has(n) && item.state !== 'OPEN') problems.push(`A closing keyword names #${n}, which is already closed (${item.title}).`);
    }
    return { lines, problems };
}

// The `gh issue create` flags that take a value: the value is skipped, so that a title such as "-lfound-by:audit"
// is not read as a label. -l / --label is read on its own.
const valueFlags = new Set([
    '-t', '--title', '-b', '--body', '-F', '--body-file', '-m', '--milestone', '-a', '--assignee', '-p', '--project',
    '-T', '--template', '-R', '--repo',
]);

// The labels given to the `gh issue create` (or `gh issue new`) that starts a word list, or null when the list is
// another command. `gh -R owner/repo issue create` is read too.
function createLabels(list) {
    let i = 1;
    if (list.length === 0 || shellReader.programName(list[0]) !== 'gh') return null;
    // -R v, --repo v (two words), --repo=v and -Rv (one word)
    while (i < list.length && /^(-R|--repo)(=.*|.+)?$/.test(list[i])) i += /^(-R|--repo)$/.test(list[i]) ? 2 : 1;
    if (list[i] !== 'issue' || !['create', 'new'].includes(list[i + 1])) return null;
    const labels = [];
    for (i += 2; i < list.length; i++) {
        const w = list[i];
        if (w === '--label' || w === '-l') {
            if (i + 1 < list.length) labels.push(list[++i]);
        } else if (w.startsWith('--label=')) {
            labels.push(w.slice('--label='.length));
        } else if (/^-l[^-]/.test(w)) {
            labels.push(w.slice(2).replace(/^=/, ''));
        } else if (valueFlags.has(w)) {
            i++;
        }
    }
    return labels;
}

// The bodies of the backtick substitutions of a bash segment (outside single quotes, also inside double quotes).
function backtickBodies(segment) {
    const bodies = [];
    let quote = '';
    let start = -1;
    for (let i = 0; i < segment.length; i++) {
        const ch = segment[i];
        if (ch === '\\' && quote !== "'") i++;
        else if (start >= 0) {
            if (ch === '`') { bodies.push(segment.slice(start, i)); start = -1; }
        } else if (ch === "'" && quote !== '"') quote = quote === "'" ? '' : "'";
        else if (ch === '"' && quote !== "'") quote = quote === '"' ? '' : '"';
        else if (ch === '`' && quote !== "'") start = i + 1;
    }
    return bodies;
}

// The index of the command a shell wrapper runs (cmd /c, also //c from Git Bash; pwsh -Command; bash -c), or -1.
function wrapperCommandIndex(reader, name, raw) {
    if (name === 'cmd') {
        const flag = raw.findIndex((word, i) => i > 0 && /^\/\/?[ck]$/i.test(reader.unquote(word)));
        return flag < 0 ? -1 : flag + 1;
    }
    return reader.shellCommandIndex(name, raw);
}

// The reader dialect of the command string a shell wrapper (bash -c, pwsh -Command, cmd /c) runs.
function wrapperDialect(name) {
    if (name === 'cmd') return 'cmd';
    return /^(bash|sh|zsh)$/.test(name) ? 'bash' : 'pwsh';
}

// The denial text when a command contains a `gh issue create` with the `bug` label (any milestone, or none) and no
// `found-by:` label, else null. A `tracking` issue (a table of sub-issues) is exempt. The command is read by the shell
// reader of check-queued-command (continued lines, heredoc bodies and redirections) in the dialect of its tool; command
// substitutions (url=$(gh issue create ...)) and the command string of a shell wrapper (bash -c "...") are read too.
function foundByProblem(command, dialect) {
    const reader = shellReader.createReader(dialect);
    for (const segment of reader.splitSegments(command)) {
        const raw = reader.dropPrefixes(reader.words(segment));
        const labels = createLabels(raw.map(word => reader.argumentText(word)));
        if (labels !== null) {
            const names = labels.flatMap(l => l.split(',')).map(l => l.trim().toLowerCase());
            const exempt = !names.includes('bug') || names.includes('tracking');
            if (!exempt && !names.some(l => foundByLabels.includes(l))) {
                return 'A bug issue needs a found-by label: '
                    + `${foundByLabels.join(', ')}. Add the label of how the bug was found; `
                    + 'in M1 or M2, also add the issue to its tracking table.';
            }
        }
        const inner = reader.substitutions(segment).map(body => ({ body, dialect }));
        if (dialect === 'bash') inner.push(...backtickBodies(segment).map(body => ({ body, dialect })));
        const name = raw.length > 0 ? reader.programName(raw[0]) : '';
        const at = wrapperCommandIndex(reader, name, raw);
        if (at >= 0 && at < raw.length) {
            // cmd and PowerShell take the rest of the words as the command (cmd /c gh issue create ..., a script
            // block { ... }); the bash family takes one word.
            const wrapper = wrapperDialect(name);
            const rest = raw.slice(at);
            const joined = wrapper === 'bash' || rest.length === 1 ? reader.argumentText(rest[0]) : rest.join(' ');
            inner.push({ body: joined, dialect: wrapper });
        }
        for (const item of inner) {
            const problem = foundByProblem(item.body, item.dialect);
            if (problem !== null) return problem;
        }
    }
    return null;
}

// Files a command reads its message or body from: --body-file <f>, -F <f>, --file <f>, -F<f> is not used by gh/git.
function referencedFiles(command, cwd) {
    const texts = [];
    const pattern = /(?:--body-file|--file|-F)\s+("([^"]+)"|'([^']+)'|(\S+))/g;
    for (const match of command.matchAll(pattern)) {
        const file = match[2] || match[3] || match[4];
        if (file === '-') continue;
        const full = path.resolve(cwd, file);
        try {
            texts.push(fs.readFileSync(full, 'utf8'));
        } catch (error) {
            // The file may be written by the same command; the command text itself is still checked.
            texts.push('');
            console.error(`check-issue-refs: cannot read ${full}: ${error.message}`);
        }
    }
    return texts;
}

// The directory the command runs in. Claude Code passes `cwd`. Antigravity passes none: its run_command carries `Cwd`
// in the tool arguments and the payload lists the workspace folders, and the hook process itself starts in `.agents/`
// (hooks.json runs `node ../scripts/...`), so its own directory would resolve `--body-file tmp/x.md` wrongly.
function commandDirectory(input, antigravity, processCwd) {
    if (input.cwd) return input.cwd;
    if (antigravity) {
        const args = input.toolCall.args || {};
        if (typeof args.Cwd === 'string' && args.Cwd !== '') return args.Cwd;
        if (Array.isArray(input.workspacePaths) && typeof input.workspacePaths[0] === 'string') return input.workspacePaths[0];
    }
    return processCwd;
}

// The hook's answer to one PreToolUse payload: the JSON to print, or null to print nothing (the command goes on to the
// normal permission check). `env` holds what the self-test replaces: the repository, the lookup and the process cwd.
function hookAnswer(input, env) {
    const antigravity = input.toolCall !== undefined;
    const command = antigravity
        ? String((input.toolCall.args && input.toolCall.args.CommandLine) || '')
        : (input.tool_input && typeof input.tool_input.command === 'string' ? input.tool_input.command : '');
    if (!publishingCommand.test(command)) return null;
    // PowerShell reads ` as its escape, bash ; Antigravity's run_command runs PowerShell on Windows.
    const dialect = antigravity || input.tool_name === 'PowerShell' ? 'pwsh' : 'bash';
    const foundBy = foundByProblem(command, dialect);
    const cwd = commandDirectory(input, antigravity, env.processCwd);
    const text = [command, ...referencedFiles(command, cwd)].join('\n');
    const repo = env.repo || defaultRepo(cwd);
    const { lines, problems } = check(text, repo, env.lookUp);
    if (foundBy !== null) problems.unshift(foundBy);
    if (lines.length === 0 && problems.length === 0) return null;
    const table = lines.length > 0 ? `\nIssue / PR numbers in this command:\n${lines.join('\n')}` : '';
    const reason = `${problems.join('\n')}${table}\nFix the problem and run the command again.`;
    if (antigravity) {
        // An Antigravity "allow" would skip the permission prompt, so an allowed command gets no output.
        return problems.length > 0 ? { decision: 'deny', reason } : null;
    }
    return problems.length > 0
        ? {
            hookSpecificOutput: {
                hookEventName: 'PreToolUse',
                permissionDecision: 'deny',
                permissionDecisionReason: reason,
            },
        }
        : {
            hookSpecificOutput: {
                hookEventName: 'PreToolUse',
                additionalContext: `Issue / PR numbers in this command:\n${lines.join('\n')}\n`
                    + 'Check that each title is the one you meant.',
            },
        };
}

function runHook(options) {
    let input;
    try {
        input = JSON.parse(readStdin());
    } catch (error) {
        fail(`check-issue-refs --hook: invalid hook input: ${error.message}`);
    }
    const answer = hookAnswer(input, { repo: options.repo, lookUp, processCwd: process.cwd() });
    if (answer !== null) process.stdout.write(JSON.stringify(answer));
    process.exit(0);
}

// Runs hookAnswer on fixed payloads with the GraphQL lookup replaced (#1 exists and is open, every other number does
// not), from a process cwd that is not the command's directory, as the Antigravity hook starts in `.agents/`.
function selfTest() {
    const assert = require('assert');
    const os = require('os');
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'check-issue-refs-'));
    const hookDir = path.join(root, '.agents');
    fs.mkdirSync(path.join(root, 'tmp'), { recursive: true });
    fs.mkdirSync(hookDir);
    const missing = '#' + '999999';
    fs.writeFileSync(path.join(root, 'tmp', 'body.md'), `Body. Fixes ${missing}\n`);
    fs.writeFileSync(path.join(root, 'tmp', 'good.md'), 'Body. Refers to #1\n');
    const fakeLookUp = (repo, numbers) => new Map(numbers.map(n => [
        n,
        n === 1 ? { __typename: 'Issue', title: 'An issue', state: 'OPEN', milestone: null } : null,
    ]));
    const env = { repo: 'owner/name', lookUp: fakeLookUp, processCwd: hookDir };
    const create = ['gh', 'pr', 'create'].join(' ');
    const antigravity = (commandLine, extra = {}) => ({
        toolCall: { name: 'run_command', args: { CommandLine: commandLine, ...extra } },
    });
    const cases = [];
    const test = (name, body) => cases.push({ name, body });
    try {
        test('Antigravity: a missing number inline is denied', () => {
            const answer = hookAnswer(antigravity(`${create} --title x --body "see ${missing}"`), env);
            assert.strictEqual(answer && answer.decision, 'deny');
        });
        test('Antigravity: --body-file resolves against args.Cwd', () => {
            const answer = hookAnswer(antigravity(`${create} --title x --body-file tmp/body.md`, { Cwd: root }), env);
            assert.strictEqual(answer && answer.decision, 'deny');
        });
        test('Antigravity: --body-file resolves against workspacePaths without args.Cwd', () => {
            const payload = { workspacePaths: [root], ...antigravity(`${create} --title x --body-file tmp/body.md`) };
            const answer = hookAnswer(payload, env);
            assert.strictEqual(answer && answer.decision, 'deny');
        });
        test('Antigravity: an existing number prints nothing (no allow, the prompt still runs)', () => {
            assert.strictEqual(hookAnswer(antigravity(`${create} --body-file tmp/good.md`, { Cwd: root }), env), null);
        });
        test('Antigravity: a command that publishes nothing prints nothing', () => {
            assert.strictEqual(hookAnswer(antigravity(`git status ${missing}`), env), null);
        });
        test('Claude Code: --body-file resolves against cwd and is denied', () => {
            const payload = { cwd: root, tool_input: { command: `${create} --title x --body-file tmp/body.md` } };
            const answer = hookAnswer(payload, env);
            assert.strictEqual(answer.hookSpecificOutput.permissionDecision, 'deny');
        });
        test('Claude Code: an existing number is passed back as context', () => {
            const payload = { cwd: root, tool_input: { command: `${create} --body-file tmp/good.md` } };
            const answer = hookAnswer(payload, env);
            assert.match(answer.hookSpecificOutput.additionalContext, /#1\tissue\tOPEN/);
        });
        const hook = (command, tool = 'Bash') =>
            hookAnswer({ cwd: root, tool_name: tool, tool_input: { command } }, env);
        const denied = (command, tool = 'Bash') => {
            const answer = hook(command, tool);
            const reason = answer === null ? '' : answer.hookSpecificOutput.permissionDecisionReason;
            return answer !== null && answer.hookSpecificOutput.permissionDecision === 'deny'
                && reason.includes('found-by:audit, found-by:matrix, found-by:differential, found-by:review, '
                    + 'found-by:operation')
                && reason.includes('in M1 or M2, also add the issue to its tracking table');
        };
        const issueCreate = ['gh', 'issue', 'create'].join(' ');
        const m1 = '"M1 Wrong Results"';
        const m2 = '"M2 Build Failures & Missing Diagnostics"';
        test('found-by: M1 bug with a found-by label is allowed', () => {
            assert.strictEqual(
                hook(`${issueCreate} --title x --milestone ${m1} --label bug --label found-by:audit`), null);
        });
        test('found-by: M2 bug without a found-by label is denied', () => {
            assert.ok(denied(`${issueCreate} --title x --milestone ${m2} --label bug`));
        });
        test('found-by: M4 bug without a found-by label is denied, with no milestone too', () => {
            assert.ok(denied(`${issueCreate} --title x --milestone "M4 Bug-Finding Infrastructure" --label bug`));
            assert.ok(denied(`${issueCreate} --title x --label bug`));
        });
        test('found-by: a feature without a found-by label is allowed, also in M1', () => {
            assert.strictEqual(hook(`${issueCreate} --title x --milestone Future --label feature`), null);
            assert.strictEqual(hook(`${issueCreate} --title x -m M1 --label documentation`), null);
            assert.strictEqual(hook(`${issueCreate} --title x`), null);
        });
        test('found-by: a tracking issue is exempt', () => {
            assert.strictEqual(hook(`${issueCreate} --title x --label bug,tracking`), null);
            assert.strictEqual(
                hook(`${issueCreate} --title 'x y' --label 'bug, tracking' --milestone 'M1 Wrong Results'`), null);
            assert.ok(denied(`${issueCreate} --title x --label bug,documentation`));
            assert.ok(denied(`${issueCreate} --title 'x y' --label 'bug, test' --milestone 'M1 Wrong Results'`));
        });
        test('found-by: comma-separated labels count', () => {
            assert.strictEqual(hook(`${issueCreate} --title x --label bug,found-by:matrix`), null);
            assert.strictEqual(hook(`${issueCreate} --title x --label found-by:operation,bug`), null);
        });
        test('found-by: -l short form (stuck and separate) and = form', () => {
            assert.ok(denied(`${issueCreate} -t x -m M1 -l bug`));
            assert.ok(denied(`${issueCreate} -t x -lbug`));
            assert.strictEqual(hook(`${issueCreate} -t x -lbug,found-by:audit`), null);
            assert.strictEqual(hook(`${issueCreate} -t x -m M2 -l bug,found-by:review`), null);
            assert.ok(denied(`${issueCreate} -t x --label=bug`));
        });
        test('found-by: labels are compared in lower case', () => {
            assert.ok(denied(`${issueCreate} --title x --label Bug`));
            assert.strictEqual(hook(`${issueCreate} --title x --label Bug --label Found-By:Audit`), null);
        });
        test('found-by: only the five found-by labels count', () => {
            assert.ok(denied(`${issueCreate} --title x --label bug,found-by:`));
            assert.ok(denied(`${issueCreate} --title x --label bug --label found-by:fuzz`));
        });
        test('found-by: --body-file without labels is denied for a bug', () => {
            assert.ok(denied(`${issueCreate} --title x --body-file tmp/good.md --label bug`));
        });
        test('found-by: a later command or line is not mixed in', () => {
            assert.strictEqual(hook(`${issueCreate} --title x && echo -l bug`), null);
            assert.ok(denied(`${issueCreate} --title x --label bug\necho -l found-by:audit`));
            assert.ok(denied(`${issueCreate} --title x --label bug && echo --label found-by:audit`));
        });
        test('found-by: a bash line continuation is one command', () => {
            assert.ok(denied(`${issueCreate} --title x \\\n  --label bug`));
            assert.strictEqual(hook(`${issueCreate} --title x --label bug \\\n  --label found-by:audit`), null);
        });
        test('found-by: a PowerShell backtick continuation is one command', () => {
            assert.ok(denied(`${issueCreate} --title x \`\n  --label bug`, 'PowerShell'));
            assert.strictEqual(
                hook(`${issueCreate} --title x --label bug \`\n  --label found-by:audit`, 'PowerShell'), null);
        });
        test('found-by: a heredoc body that quotes a command is data', () => {
            const body = `${issueCreate} --title y --label bug`;
            const feature = `${issueCreate} --title x --label feature --body-file - <<'EOF'`;
            assert.strictEqual(hook(`${feature}\n${body}\nEOF`), null);
            const bug = `${issueCreate} --title x --label bug --body-file - <<'EOF'`;
            assert.ok(denied(`${bug}\n--label found-by:audit\nEOF`));
        });
        test('found-by: a PowerShell array of labels', () => {
            assert.strictEqual(hook(`${issueCreate} --title x --label "bug","found-by:audit"`, 'PowerShell'), null);
        });
        test('found-by: -R / --repo before the subcommand, and the alias new', () => {
            assert.ok(denied('gh -R owner/name issue create --title x --label bug'));
            assert.ok(denied('gh --repo owner/name issue create --title x --label bug'));
            assert.ok(denied('gh issue new --title x --label bug'));
            assert.ok(denied('gh --repo=owner/name issue create --title x --label bug'));
            assert.ok(denied('gh -Rowner/name issue create --title x --label bug'));
            assert.strictEqual(hook('gh --repo=owner/name issue create --title x --label bug,found-by:audit'), null);
            assert.strictEqual(hook('gh issue new --title x --label bug,found-by:review'), null);
        });
        test('found-by: a redirection between the flags does not cut the command', () => {
            assert.ok(denied(`${issueCreate} --title x 2>&1 --label bug`));
            assert.strictEqual(hook(`${issueCreate} --title x --label bug 2>&1 --label found-by:audit`), null);
        });
        test('found-by: the value of a flag that takes one is not a label', () => {
            assert.ok(denied(`${issueCreate} --title "-lfound-by:audit" --label bug`));
            assert.ok(denied(`${issueCreate} --body "--label found-by:audit" --label bug`));
            assert.strictEqual(hook(`${issueCreate} --title "-lbug" --label feature`), null);
        });
        test('found-by: the dialect of the tool decides how a quote is read', () => {
            // PowerShell: the backslash is no escape, the title ends at the second quote and --label bug counts.
            // bash: \" is an escaped quote, so the rest is part of the title and the issue has no label.
            const command = `${issueCreate} --title "a \\" --label bug`;
            assert.ok(denied(command, 'PowerShell'));
            assert.strictEqual(hook(command, 'Bash'), null);
            assert.strictEqual(hookAnswer(antigravity(command), env).decision, 'deny');
        });
        test('found-by: a command substitution, a shell wrapper and gh.exe are read', () => {
            assert.ok(denied(`url=$(${issueCreate} --title x --label bug)`));
            assert.strictEqual(hook(`url=$(${issueCreate} --title x --label bug,found-by:audit)`), null);
            assert.ok(denied(`echo "$(${issueCreate} --title x --label bug)"`));
            assert.ok(denied(`bash -c "${issueCreate} --title x --label bug"`));
            assert.strictEqual(hook(`bash -c "${issueCreate} --title x --label bug,found-by:review"`), null);
            assert.ok(denied(`pwsh -Command "${issueCreate} --title x --label bug"`, 'PowerShell'));
            assert.ok(denied('gh.exe issue create --title x --label bug', 'PowerShell'));
            assert.ok(denied('gh.exe -R o/n issue new --title x --label bug', 'PowerShell'));
        });
        test('found-by: a bash backtick substitution is read', () => {
            assert.ok(denied(`url=\`${issueCreate} --title x --label bug\``));
            assert.ok(denied(`echo "\`${issueCreate} --title x --label bug\`"`));
            assert.strictEqual(hook(`url=\`${issueCreate} --title x --label bug,found-by:audit\``), null);
            assert.strictEqual(hook(`echo '\`${issueCreate} --title x --label bug\`'`), null);
            // an apostrophe inside double quotes does not open a single-quoted span
            assert.ok(denied(`echo "it's \`${issueCreate} --title x --label bug\`"`));
        });
        test('found-by: an escaped backtick is text, not a substitution', () => {
            assert.strictEqual(hook(`echo "a \\\` ${issueCreate} --title x --label bug \\\`"`), null);
        });
        test('found-by: after bash -c the words behind the command string are arguments', () => {
            assert.strictEqual(hook(`bash -c "echo hi" ${issueCreate} --title x --label bug`), null);
            assert.strictEqual(hook(`bash -c gh issue create --title x --label bug`), null);
        });
        test('found-by: an unquoted cmd /c, cmd //c and pwsh -Command are read', () => {
            assert.ok(denied(`cmd /c ${issueCreate} --title x --label bug`));
            assert.ok(denied(`cmd //c ${issueCreate} --title "x y" --label bug`));
            assert.strictEqual(hook(`cmd /c ${issueCreate} --title x --label bug,found-by:audit`), null);
            assert.ok(denied(`pwsh -Command ${issueCreate} --title x --label bug`, 'PowerShell'));
            assert.ok(denied(`powershell -NoProfile -c ${issueCreate} --title x --label bug`, 'PowerShell'));
            assert.strictEqual(hook(`pwsh -Command ${issueCreate} --title x --label bug,found-by:review`), null);
        });
        test('found-by: a PowerShell script block is read', () => {
            assert.ok(denied(`pwsh -Command { ${issueCreate} --title x --label bug }`, 'PowerShell'));
            assert.strictEqual(
                hook(`pwsh -Command { ${issueCreate} --title x --label bug,found-by:audit }`, 'PowerShell'), null);
        });
        test('found-by: the denial also reports a number problem', () => {
            const answer = hook(`${issueCreate} --title "see ${missing}" --label bug`);
            const reason = answer.hookSpecificOutput.permissionDecisionReason;
            assert.ok(reason.includes('needs a found-by label'));
            assert.ok(reason.includes(`${missing} does not exist`));
        });
        test('found-by: the repository flag does not backtrack exponentially', () => {
            const started = Date.now();
            assert.strictEqual(publishingCommand.test(`gh ${'-R=x '.repeat(1000)}foo`), false);
            assert.strictEqual(publishingCommand.test(`gh ${'-R=x '.repeat(24)}foo`), false);
            assert.ok(Date.now() - started < 1000, 'the match took too long');
            for (const form of ['-R o/n', '--repo o/n', '--repo=o/n', '-Ro/n', '-R=o/n']) {
                assert.ok(publishingCommand.test(`gh ${form} issue create`), form);
            }
        });
        test('found-by: Antigravity denies a bug without a label', () => {
            const answer = hookAnswer(antigravity(`${issueCreate} --title x -l bug`), env);
            assert.strictEqual(answer && answer.decision, 'deny');
        });
        let failed = 0;
        for (const { name, body } of cases) {
            try {
                body();
                console.log(`ok   ${name}`);
            } catch (error) {
                failed++;
                console.log(`FAIL ${name}: ${error.message}`);
            }
        }
        console.log(`${cases.length - failed} passed, ${failed} failed`);
        process.exitCode = failed === 0 ? 0 : 1;
    } finally {
        fs.rmSync(root, { recursive: true, force: true });
    }
}

function main() {
    const options = parseArgs(process.argv.slice(2));
    if (options.selfTest) return selfTest();
    if (options.hook) return runHook(options);
    const texts = [...options.texts];
    for (const file of options.files) {
        try {
            texts.push(fs.readFileSync(path.resolve(file), 'utf8'));
        } catch (error) {
            fail(`Cannot read ${file}: ${error.message}`);
        }
    }
    if (options.stdin) texts.push(readStdin());
    const repo = options.repo || defaultRepo(repoRoot);
    const { lines, problems } = check(texts.join('\n'), repo);
    if (lines.length === 0) {
        console.log('No issue or pull request numbers found.');
        return;
    }
    console.log(lines.join('\n'));
    if (problems.length > 0) fail(problems.join('\n'));
}

main();
