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
    '# チャット台帳（担当と送信先）',
    '',
    '送信は必ず「名前 [ref]」で行う。呼び名は人間が「〜へ電話」と言うときの名前（非同期メッセージ）。会話記録ID は参考情報。',
    '',
    '| 呼び名 | 参照 (ref) | 表示名 | 会話記録ID | 担当 | 状態 |',
    '| :--- | :--- | :--- | :--- | :--- | :--- |',
];

const columns = ['call', 'ref', 'name', 'session', 'topic', 'state'];

// The registry is shared by all worktrees: the directory of the main checkout (the parent of the common git directory).
function registryPath() {
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
    const first = lines.findIndex((l) => l.startsWith('| 呼び名'));
    if (first < 0) return { before: lines, rows: [], after: [] };
    let end = first;
    while (end < lines.length && lines[end].startsWith('|')) end++;
    const rows = [];
    for (const line of lines.slice(first + 2, end)) {
        const cells = line.split('|').slice(1, -1).map((c) => c.trim());
        if (cells.length === columns.length) rows.push(Object.fromEntries(columns.map((k, i) => [k, cells[i]])));
    }
    return { before: lines.slice(0, first), rows, after: lines.slice(end) };
}

function render(model) {
    const table = [
        '| 呼び名 | 参照 (ref) | 表示名 | 会話記録ID | 担当 | 状態 |',
        '| :--- | :--- | :--- | :--- | :--- | :--- |',
        ...model.rows.map((r) => `| ${columns.map((k) => r[k] || '-').join(' | ')} |`),
    ];
    const before = model.before.length ? model.before : header.slice(0, 4);
    return [...before, ...table, ...model.after].join('\n').replace(/\n*$/, '\n');
}

function load(file) {
    if (!fs.existsSync(file)) return { before: header.slice(0, 4), rows: [], after: [] };
    return parse(fs.readFileSync(file, 'utf8'));
}

function save(file, model) {
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, render(model), 'utf8');
}

function pendingRow(session) {
    return { call: '（未設定）', ref: '-', name: '-', session, topic: '-', state: '登録待ち' };
}

function upsert(model, session, fields) {
    const row = model.rows.find((r) => r.session === session);
    if (row) Object.assign(row, fields);
    else model.rows.push({ ...pendingRow(session), ...fields, session });
    return model;
}

function registerContext(session) {
    return [
        `【台帳登録】このチャットの会話記録ID は ${session} です。`,
        '最初の応答で、このチャットの担当トピックを1〜2行で要約し、「担当トピック名はこんな感じでいいですか？」とユーザーに確認してください。',
        'ユーザーが承認したら、ListAgents で自分の ref（`This session is … [ref]`）を確認し、次を実行して台帳に登録してください。',
        `node scripts/agent/chat-registry.js set --session ${session} --ref <ref> --name "<表示名>" --call "<呼び名>" --topic "<担当>"`,
        '「〜へ電話」と言われたら、台帳で呼び名を引いて `名前 [ref]` 宛てに SendMessage で送ってください（台帳を読んでから送る）。',
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
    if (!session) return 0;
    const file = registryPath();
    const model = load(file);
    if (!model.rows.some((r) => r.session === session)) {
        model.rows.push(pendingRow(session));
        save(file, model);
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
    const model = upsert(load(file), opts.session, {
        ref: opts.ref, name: opts.name, call: opts.call, topic: opts.topic,
        state: typeof opts.state === 'string' ? opts.state : '稼働中・確定',
    });
    save(file, model);
    console.log(`registered: ${opts.call} -> ${opts.name} [${opts.ref}]`);
    return 0;
}

function runResolve(call) {
    const hits = load(registryPath()).rows.filter((r) => r.call.toLowerCase() === String(call || '').toLowerCase());
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
    save(file, upsert(load(file), 'aaaa-1111', { ref: '111111', name: 'henicos-x', call: 'テスト', topic: 'テスト担当' }));
    save(file, upsert(load(file), 'aaaa-1111', { ref: '222222' }));
    save(file, upsert(load(file), 'bbbb-2222', { ref: '333333', name: 'henicos-y', call: 'ジョブ', topic: 'ジョブ担当' }));
    const model = load(file);
    const ok = model.rows.length === 2 && model.rows[0].ref === '222222' && model.rows[0].call === 'テスト'
        && model.rows[1].call === 'ジョブ';
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

process.exitCode = main(process.argv.slice(2));
