#!/usr/bin/env node
// Keeps the registry of the Claude chats that work on this repository: which chat has which topic, under which call name
// and session reference. A person says "call <name>"; an agent resolves the name to the ref, finds the row with that ref
// in ListAgents and sends to the session name shown there. Session names change (/rename), so the registry keeps none.
// The registry lives in tmp/status/chat-registry.md of the main checkout, so every worktree shares one file. Older
// versions of this script wrote tmp/status/registry.md with a "Display name" column; that file is read once to start
// the new one and is never written, so a worktree that still runs an older version cannot erase the new rows.
// Usage:
//   node scripts/agent/chat-registry.js --hook                       SessionStart hook: adds a pending row, tells the chat to register
//   node scripts/agent/chat-registry.js set --session <id> --ref <ref> --call <call> --topic <topic> [--state <s>]
//   node scripts/agent/chat-registry.js resolve <call>               prints the ref; exit 1 when unknown, 2 when ambiguous
//   node scripts/agent/chat-registry.js show                         prints the registry
//   node scripts/agent/chat-registry.js --self-test                  checks the parser and the writer on a temporary file
'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const { execFileSync } = require('child_process');

const usage = 'Usage: chat-registry.js (--hook | set --session <id> --ref <ref> --call <call> '
    + '--topic <topic> [--state <s>] | resolve <call> | show | --self-test)';

const header = [
    '# Chat registry (topics and send targets)',
    '',
    'A call name is what a person says to reach a chat (an asynchronous message). To send, find the row with the ref in ListAgents and send to "<name shown there> [ref]". The session ID is for reference only.',
    '',
    '| Call name | Ref | Session ID | Topic | State |',
    '| :--- | :--- | :--- | :--- | :--- |',
];

const columns = ['call', 'ref', 'session', 'topic', 'state'];

// Header text to row key. "Display name" is a column of older registries: it is read and dropped on the next write.
const headerKeys = {
    'Call name': 'call', Ref: 'ref', 'Display name': 'name', 'Session ID': 'session', Topic: 'topic', State: 'state',
};

// The registry is shared by all worktrees: the directory of the main checkout (the parent of the common git directory).
function registryPath() {
    // CHAT_REGISTRY_FILE lets the self-test and manual checks use a scratch file instead of the real registry. Never point
    // it at a file named registry.md that an older version of the script also writes: that version drops five-column rows.
    if (process.env.CHAT_REGISTRY_FILE) return process.env.CHAT_REGISTRY_FILE;
    try {
        const common = execFileSync('git', ['rev-parse', '--path-format=absolute', '--git-common-dir'], {
            cwd: __dirname, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'],
        }).trim();
        return path.join(path.dirname(common), 'tmp', 'status', 'chat-registry.md');
    } catch (_) {
        return path.resolve(__dirname, '..', '..', 'tmp', 'status', 'chat-registry.md');
    }
}

// Splits the file into the text before the table, the table rows (cells), and the text after it.
function parse(text) {
    const lines = text.split(/\r?\n/);
    const first = lines.findIndex((l) => l.startsWith('| Call name'));
    if (first < 0) return { before: lines, rows: [], after: [] };
    let end = first;
    while (end < lines.length && lines[end].startsWith('|')) end++;
    const titles = lines[first].split('|').slice(1, -1).map((c) => c.trim());
    const keys = titles.map((t) => headerKeys[t]);
    for (const t of titles.filter((t) => !headerKeys[t])) {
        process.stderr.write(`chat-registry: unknown column "${t}" is dropped on the next write\n`);
    }
    const rows = [];
    for (const line of lines.slice(first + 2, end)) {
        // Every "|" in the file is a cell separator: literal pipes inside cells are stored as "&#124;" (see escapeCell).
        const cells = line.split('|').slice(1, -1).map((c) => unescapeCell(c.trim()));
        if (cells.length !== keys.length) {
            process.stderr.write(`chat-registry: a row with ${cells.length} cells instead of ${keys.length} is dropped on the next write: ${line}\n`);
            continue;
        }
        const row = {};
        keys.forEach((k, i) => { if (columns.includes(k)) row[k] = cells[i]; });
        rows.push(row);
    }
    return { before: lines.slice(0, first), rows, after: lines.slice(end) };
}

function render(model) {
    const table = [
        header[4],
        header[5],
        ...model.rows.map((r) => `| ${columns.map((k) => escapeCell(r[k] || '-')).join(' | ')} |`),
    ];
    const before = model.before.length ? model.before : header.slice(0, 4);
    return [...before, ...table, ...model.after].join('\n').replace(/\n*$/, '\n');
}

// The older registry.md beside the file is read, never written. The first load starts from its rows (keeping none of its
// other text). Later, a chat that an older version registered there (a worktree of an older branch) is taken over when
// registry.md was written after this file: only rows whose session is not here yet, so a row removed here comes back
// only if an older version writes registry.md again.
function load(file) {
    const legacy = path.join(path.dirname(file), 'registry.md');
    const hasLegacy = legacy !== file && fs.existsSync(legacy);
    if (!fs.existsSync(file)) {
        const rows = hasLegacy ? parse(fs.readFileSync(legacy, 'utf8')).rows : [];
        return { before: header.slice(0, 4), rows, after: [] };
    }
    const model = parse(fs.readFileSync(file, 'utf8'));
    if (hasLegacy && fs.statSync(legacy).mtimeMs > fs.statSync(file).mtimeMs) {
        const known = new Set(model.rows.map((r) => r.session));
        for (const r of parse(fs.readFileSync(legacy, 'utf8')).rows) {
            if (r.session && r.session !== '-' && !known.has(r.session)) model.rows.push(r);
        }
    }
    return model;
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
    return { call: '(not set)', ref: '-', session, topic: '-', state: 'pending registration' };
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
        `node scripts/agent/chat-registry.js set --session ${session} --ref <ref> --call "<call name>" --topic "<topic>"`,
        'When asked to call another chat, resolve the call name to a ref (chat-registry.js resolve "<call name>"), find the row with that ref in ListAgents, and send with SendMessage to "<name shown there> [ref]". No row means the chat has ended.',
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
    // --name of older instructions is accepted and ignored: the session name is read from ListAgents when sending.
    for (const k of ['session', 'ref', 'call', 'topic']) {
        if (typeof opts[k] !== 'string' || !opts[k]) { console.error(`missing --${k}\n${usage}`); return 1; }
    }
    const file = registryPath();
    withLock(file, () => {
        const model = upsert(load(file), opts.session, {
            ref: opts.ref, call: opts.call, topic: opts.topic,
            state: typeof opts.state === 'string' ? opts.state : 'active, confirmed',
        });
        save(file, model);
    });
    console.log(`registered: ${opts.call} -> [${opts.ref}]`);
    return 0;
}

function runResolve(call) {
    if (typeof call !== 'string' || !call) { console.error(usage); return 1; }
    // Rows still waiting for their call name or ref are not callable.
    const hits = load(registryPath()).rows.filter((r) => r.ref !== '-' && r.call !== '(not set)'
        && r.call.toLowerCase() === String(call || '').toLowerCase());
    if (hits.length === 0) { console.error(`no chat is registered as "${call}"`); return 1; }
    if (hits.length > 1) {
        console.error(`"${call}" is ambiguous:`);
        for (const h of hits) console.error(`  [${h.ref}] ${h.topic}`);
        return 2;
    }
    console.log(hits[0].ref);
    return 0;
}

function selfTest() {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'chat-registry-'));
    const file = path.join(dir, 'chat-registry.md');
    save(file, upsert(load(file), 'aaaa-1111', { ref: '111111', call: 'alpha', topic: 'topic A' }));
    save(file, upsert(load(file), 'aaaa-1111', { ref: '222222' }));
    save(file, upsert(load(file), 'bbbb-2222', { ref: '333333', call: 'beta', topic: 'topic B' }));
    const model = load(file);
    let ok = model.rows.length === 2 && model.rows[0].ref === '222222' && model.rows[0].call === 'alpha'
        && model.rows[1].call === 'beta';
    // A registry written with the older "Display name" column is read, and the column is dropped on the next write.
    fs.writeFileSync(file, ['# old', '', '| Call name | Ref | Display name | Session ID | Topic | State |',
        '| :--- | :--- | :--- | :--- | :--- | :--- |', '| gamma | 444444 | henicos-z | cccc-3333 | topic C | active |', ''].join('\n'));
    save(file, load(file));
    const migrated = fs.readFileSync(file, 'utf8');
    const row = load(file).rows[0];
    ok = ok && !migrated.includes('Display name') && !migrated.includes('henicos-z') && row.call === 'gamma'
        && row.ref === '444444' && row.session === 'cccc-3333' && row.topic === 'topic C' && row.state === 'active';
    // Without the new file, the rows of the older registry.md beside it are the start, and registry.md stays unchanged.
    const legacy = path.join(dir, 'registry.md');
    fs.renameSync(file, legacy);
    const legacyText = fs.readFileSync(legacy, 'utf8');
    save(file, upsert(load(file), 'dddd-4444', { ref: '555555', call: 'delta', topic: 'topic D' }));
    const started = load(file).rows;
    ok = ok && started.length === 2 && started[0].call === 'gamma' && started[1].call === 'delta'
        && fs.readFileSync(legacy, 'utf8') === legacyText && !fs.readFileSync(file, 'utf8').includes('# old');
    // A chat an older version registers in registry.md later is taken over; a row removed from the new file stays
    // removed while registry.md is older than the new file.
    fs.writeFileSync(legacy, ['| Call name | Ref | Display name | Session ID | Topic | State |',
        '| :--- | :--- | :--- | :--- | :--- | :--- |', '| gamma | 444444 | henicos-z | cccc-3333 | topic C | active |',
        '| epsilon | 666666 | henicos-e | eeee-5555 | topic E | active |', ''].join('\n'));
    const later = new Date(Date.now() + 60000);
    fs.utimesSync(legacy, later, later);
    const merged = load(file).rows;
    ok = ok && merged.length === 3 && merged[2].call === 'epsilon' && merged[2].session === 'eeee-5555';
    save(file, { before: header.slice(0, 4), rows: [started[1]], after: [] });
    const earlier = new Date(Date.now() - 60000);
    fs.utimesSync(legacy, earlier, earlier);
    const kept = load(file).rows;
    ok = ok && kept.length === 1 && kept[0].call === 'delta';
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
        // Through load, so that rows still only in the older registry.md are shown as resolve sees them.
        const model = load(registryPath());
        if (model.rows.length === 0) { console.error('registry is empty'); return 1; }
        process.stdout.write(render(model));
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
