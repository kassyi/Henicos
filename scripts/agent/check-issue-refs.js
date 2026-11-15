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
'use strict';

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

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
const publishingCommand = /\bgh\s+(?:issue|pr)\s+(?:create|comment|edit|close|review|merge)\b|\bgit\s+(?:commit|switch|checkout|branch|worktree)\b/;

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
    const cwd = commandDirectory(input, antigravity, env.processCwd);
    const text = [command, ...referencedFiles(command, cwd)].join('\n');
    const repo = env.repo || defaultRepo(cwd);
    const { lines, problems } = check(text, repo, env.lookUp);
    if (lines.length === 0) return null;
    const table = `Issue / PR numbers in this command:\n${lines.join('\n')}`;
    const reason = `${problems.join('\n')}\n${table}\nFix the number and run the command again.`;
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
                additionalContext: `${table}\nCheck that each title is the one you meant.`,
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
