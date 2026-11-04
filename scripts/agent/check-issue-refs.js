#!/usr/bin/env node
// Checks the issue and pull request numbers (#1234) in a text against GitHub, so that a mistyped number is caught
// before it reaches an issue, a pull request, a comment, a commit or a branch name.
// Usage:
//   node scripts/agent/check-issue-refs.js --text "<text>"      check the numbers in the text
//   node scripts/agent/check-issue-refs.js --file <path>...     check the numbers in the files
//   node scripts/agent/check-issue-refs.js --stdin              check the numbers in standard input
//   node scripts/agent/check-issue-refs.js --hook               Claude Code PreToolUse hook (reads the hook JSON on stdin)
//   --repo <owner/name>   repository to look the numbers up in (default: the origin remote)
// Prints one line per number: number, kind (issue / PR), state, milestone and title.
// Fails (exit 1) when a number does not exist, or when a closing keyword (Fixes / Closes / Resolves #n) names a pull
// request or an issue that is already closed. In --hook mode the same failures deny the command; otherwise the table
// is passed back to the agent as additional context, and the command goes on to the normal permission check.
'use strict';

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const repoRoot = path.resolve(__dirname, '..', '..');

const usage = 'Usage: check-issue-refs.js (--text <text> | --file <path>... | --stdin | --hook) [--repo <owner/name>]';

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
    const options = { texts: [], files: [], stdin: false, hook: false, repo: null };
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
            case '--repo': options.repo = value(); break;
            case '--help':
            case '-h': console.log(usage); process.exit(0); break;
            default: fail(`Unknown option: ${arg}\n${usage}`);
        }
    }
    if (!options.hook && !options.stdin && options.texts.length === 0 && options.files.length === 0) fail(usage);
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
function check(text, repo) {
    const numbers = new Set([
        ...collect(text, refPattern),
        ...collect(text, branchPattern),
        ...collect(text, targetPattern),
    ]);
    const closing = collect(text, closingPattern);
    if (numbers.size === 0) return { lines: [], problems: [] };
    const sorted = [...numbers].sort((a, b) => a - b);
    const found = lookUp(repo, sorted);
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

function runHook(options) {
    let input;
    try {
        input = JSON.parse(readStdin());
    } catch (error) {
        fail(`check-issue-refs --hook: invalid hook input: ${error.message}`);
    }
    const command = input.tool_input && typeof input.tool_input.command === 'string' ? input.tool_input.command : '';
    if (!publishingCommand.test(command)) process.exit(0);
    const cwd = input.cwd || process.cwd();
    const text = [command, ...referencedFiles(command, cwd)].join('\n');
    const repo = options.repo || defaultRepo(cwd);
    const { lines, problems } = check(text, repo);
    if (lines.length === 0) process.exit(0);
    const table = `Issue / PR numbers in this command:\n${lines.join('\n')}`;
    const output = problems.length > 0
        ? {
            hookSpecificOutput: {
                hookEventName: 'PreToolUse',
                permissionDecision: 'deny',
                permissionDecisionReason: `${problems.join('\n')}\n${table}\nFix the number and run the command again.`,
            },
        }
        : {
            hookSpecificOutput: {
                hookEventName: 'PreToolUse',
                additionalContext: `${table}\nCheck that each title is the one you meant.`,
            },
        };
    process.stdout.write(JSON.stringify(output));
    process.exit(0);
}

function main() {
    const options = parseArgs(process.argv.slice(2));
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
