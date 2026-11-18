#!/usr/bin/env node
// Keeps the registry of the Claude chats that work on this repository: which chat has which topic, under which call name
// and session reference. A person says "call <name>" and an agent resolves the name to "<display name> [ref]" for SendMessage.
// The registry lives in tmp/status/registry.md of the main checkout, so every worktree shares one file.
// Usage:
//   node scripts/agent/chat-registry.js --hook                       SessionStart hook: adds a pending row, tells the chat to register
//   node scripts/agent/chat-registry.js set --session <id> --ref <ref> --name <display> --call <call> --topic <topic> [--state <s>]
//   node scripts/agent/chat-registry.js resolve <call>               prints "<display> [<ref>]"; exit 1 when unknown, 2 when ambiguous
//   node scripts/agent/chat-registry.js show                         prints the registry
//   node scripts/agent/chat-registry.js --self-test                  checks the parser and the writer on a temporary file
'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const { execFileSync } = require('child_process');

const usage = 'Usage: chat-registry.js (--hook | set --session <id> --ref <ref> --name <display> --call <call> '
    + '--topic <topic> [--state <s>] | resolve <call> | show | --self-test)';

const header = [
    '# Chat registry (topics and send targets)',
    '',
    'Always send to "<display name> [ref]". A call name is what a person says to reach a chat (an asynchronous message). The session ID is for reference only.',
    '',
    '| Call name | Ref | Display name | Session ID | Topic | State |',
    '| :--- | :--- | :--- | :--- | :--- | :--- |',
];

const columns = ['call', 'ref', 'name', 'session', 'topic', 'state'];

// The registry is shared by all worktrees: the directory of the main checkout (the parent of the common git directory).
function registryPath() {
    // CHAT_REGISTRY_FILE lets the self-test and manual checks use a scratch file instead of the real registry.
    if (process.env.CHAT_REGISTRY_FILE) return process.env.CHAT_REGISTRY_FILE;
    try {
        const common = execFileSync('git', ['rev-parse', '--path-format=absolute', '--git-common-dir'], {
            cwd: __dirname, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'],
        }).trim();
        return path.join(path.dirname(common), 'tmp', 'status', 'registry.md');
    } catch (_) {
        return path.resolve(__dirname, '..', '..', 'tmp', 'status', 'registry.md');
    }
}

// Splits the file into the text before the table, the table rows (cells), and the text after it.
function parse(text) {
    const lines = text.split(/\r?\n/);
    const first = lines.findIndex((l) => l.startsWith('| Call name'));
    if (first < 0) return { before: lines, rows: [], after: [] };
    let end = first;
    while (end < lines.length && lines[end].startsWith('|')) end++;
    const rows = [];
    for (const line of lines.slice(first + 2, end)) {
        // Every "|" in the file is a cell separator: literal pipes inside cells are stored as "&#124;" (see escapeCell).
        const cells = line.split('|').slice(1, -1).map((c) => unescapeCell(c.trim()));
        if (cells.length === columns.length) rows.push(Object.fromEntries(columns.map((k, i) => [k, cells[i]])));
    }
    return { before: lines.slice(0, first), rows, after: lines.slice(end) };
}

function render(model) {
    const table = [
        '| Call name | Ref | Display name | Session ID | Topic | State |',
        '| :--- | :--- | :--- | :--- | :--- | :--- |',
        ...model.rows.map((r) => `| ${columns.map((k) => escapeCell(r[k] || '-')).join(' | ')} |`),
    ];
    const before = model.before.length ? model.before : header.slice(0, 4);
    return [...before, ...table, ...model.after].join('\n').replace(/\n*$/, '\n');
}

function load(file) {
    if (!fs.existsSync(file)) return { before: header.slice(0, 4), rows: [], after: [] };
    return parse(fs.readFileSync(file, 'utf8'));
}

// A cell holds one line of text. "&" is encoded first and "|" becomes "&#124;", so a literal pipe never splits a row
// and the encoding can be reversed exactly (no backslash escapes, which are easy to get wrong).
function escapeCell(value) {
    return String(value).replace(/[\r\n]+/g, ' ').replace(/&/g, '&amp;').replace(/\|/g, '&#124;');
}

function unescapeCell(value) {
    return value.replace(/&(amp|#124);/g, (_, entity) => (entity === 'amp' ? '&' : '|'));
}

// Writes through a temporary file and a rename, so a reader never sees a half-written registry.
function save(file, model) {
    fs.mkdirSync(path.dirname(file), { recursive: true });
    const tmp = `${file}.${process.pid}.tmp`;
    fs.writeFileSync(tmp, render(model), 'utf8');
    fs.renameSync(tmp, file);
}

// Runs fn while holding a lock file, so two chats starting together do not overwrite each other's rows.
// A lock left behind by a crash blocks for five seconds and then fails; delete <registry>.lock to clear it.
function withLock(file, fn) {
    const lock = `${file}.lock`;
    fs.mkdirSync(path.dirname(file), { recursive: true });
    const deadline = Date.now() + 5000;
    let fd;
    for (;;) {
        try {
            fd = fs.openSync(lock, 'wx');
            break;
        } catch (err) {
            if (err.code !== 'EEXIST' || Date.now() > deadline) throw err;
            Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 50);
        }
    }
    try {
        return fn();
    } finally {
        fs.closeSync(fd);
        fs.rmSync(lock, { force: true });
    }
}

function pendingRow(session) {
    return { call: '(not set)', ref: '-', name: '-', session, topic: '-', state: 'pending registration' };
}

function upsert(model, session, fields) {
    const row = model.rows.find((r) => r.session === session);
    if (row) Object.assign(row, fields);
    else model.rows.push({ ...pendingRow(session), ...fields, session });
    return model;
}

function registerContext(session) {
    return [
        `[Chat registry] This chat's session ID is ${session}.`,
        'In your first reply, summarize this chat\'s topic in one or two lines and ask the user to confirm the topic name.',
        'After the user agrees, get your own ref from ListAgents (the first line says "This session is ... [ref]"), then run:',
        `node scripts/agent/chat-registry.js set --session ${session} --ref <ref> --name "<display name>" --call "<call name>" --topic "<topic>"`,
        'When asked to call another chat, resolve the call name in the registry and send with SendMessage to "<display name> [ref]" (read the registry before sending).',
    ].join('\n');
}

function readArgs(argv) {
    const opts = { _: [] };
    for (let i = 0; i < argv.length; i++) {
        if (argv[i].startsWith('--')) {
            const key = argv[i].slice(2);
            const next = argv[i + 1];
            if (next === undefined || next.startsWith('--')) opts[key] = true;
            else { opts[key] = next; i++; }
        } else opts._.push(argv[i]);
    }
    return opts;
}

function readStdin() {
    try { return fs.readFileSync(0, 'utf8'); } catch (_) { return ''; }
}

function runHook() {
    let input = {};
    try { input = JSON.parse(readStdin() || '{}'); } catch (_) { return 0; }
    const session = input.session_id;
    if (typeof session !== 'string' || !session) return 0;
    try {
        const file = registryPath();
        withLock(file, () => {
            const model = load(file);
            if (!model.rows.some((r) => r.session === session)) {
                model.rows.push(pendingRow(session));
                save(file, model);
            }
        });
    } catch (err) {
        // A hook must not break a session start: report on stderr and still give the chat its instructions.
        process.stderr.write(`chat-registry: could not update the registry: ${err.message}\n`);
    }
    process.stdout.write(JSON.stringify({
        hookSpecificOutput: { hookEventName: 'SessionStart', additionalContext: registerContext(session) },
    }));
    return 0;
}

function runSet(opts) {
    for (const k of ['session', 'ref', 'name', 'call', 'topic']) {
        if (typeof opts[k] !== 'string' || !opts[k]) { console.error(`missing --${k}\n${usage}`); return 1; }
    }
    const file = registryPath();
    withLock(file, () => {
        const model = upsert(load(file), opts.session, {
            ref: opts.ref, name: opts.name, call: opts.call, topic: opts.topic,
            state: typeof opts.state === 'string' ? opts.state : 'active, confirmed',
        });
        save(file, model);
    });
    console.log(`registered: ${opts.call} -> ${opts.name} [${opts.ref}]`);
    return 0;
}

function runResolve(call) {
    // Rows still waiting for their call name or ref are not callable.
    const hits = load(registryPath()).rows.filter((r) => r.ref !== '-' && r.call !== '(not set)'
        && r.call.toLowerCase() === String(call || '').toLowerCase());
    if (hits.length === 0) { console.error(`no chat is registered as "${call}"`); return 1; }
    if (hits.length > 1) {
        console.error(`"${call}" is ambiguous:`);
        for (const h of hits) console.error(`  ${h.name} [${h.ref}]`);
        return 2;
    }
    console.log(`${hits[0].name} [${hits[0].ref}]`);
    return 0;
}

function selfTest() {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'chat-registry-'));
    const file = path.join(dir, 'registry.md');
    save(file, upsert(load(file), 'aaaa-1111', { ref: '111111', name: 'henicos-x', call: 'alpha', topic: 'topic A' }));
    save(file, upsert(load(file), 'aaaa-1111', { ref: '222222' }));
    save(file, upsert(load(file), 'bbbb-2222', { ref: '333333', name: 'henicos-y', call: 'beta', topic: 'topic B' }));
    const model = load(file);
    const ok = model.rows.length === 2 && model.rows[0].ref === '222222' && model.rows[0].call === 'alpha'
        && model.rows[1].call === 'beta';
    fs.rmSync(dir, { recursive: true, force: true });
    console.log(ok ? 'self-test passed' : 'self-test FAILED');
    return ok ? 0 : 1;
}

function main(argv) {
    const opts = readArgs(argv);
    if (opts.hook) return runHook();
    if (opts['self-test']) return selfTest();
    const cmd = opts._[0];
    if (cmd === 'set') return runSet(opts);
    if (cmd === 'resolve') return runResolve(opts._[1]);
    if (cmd === 'show') {
        const file = registryPath();
        if (!fs.existsSync(file)) { console.error('registry is empty'); return 1; }
        process.stdout.write(fs.readFileSync(file, 'utf8'));
        return 0;
    }
    console.error(usage);
    return 1;
}

try {
    process.exitCode = main(process.argv.slice(2));
} catch (err) {
    console.error(`chat-registry: ${err.message}`);
    // The SessionStart hook must exit 0 even on an unexpected error.
    process.exitCode = process.argv.includes('--hook') ? 0 : 1;
}
