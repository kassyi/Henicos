'use strict';

// Self-test of chat-status.js (`node scripts/human/chat-status.js --self-test`): the question extraction, every event
// of both agents on synthetic payloads and transcripts, the PreInvocation-after-Stop rule, fullyIdle, the atomic write,
// the 12-hour drop and the order of listChats, and the hook as a child process (exit code 0 and what it prints);
// self-test-summary.js adds the summaries (finding the CLI, the prompt, the summarizer with a fake CLI, the guards)
// and through self-test-open-questions.js the open questions,
// self-test-antigravity.js what is read from Antigravity's files (subagents, titles).
// It writes only under <repository>/tmp/chat-status-self-test-<pid>/ and removes it at the end.

const assert = require('assert');
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');
const chat = require('../chat-status.js');

const Script = path.join(__dirname, '..', 'chat-status.js');
const RepositoryRoot = path.resolve(__dirname, '..', '..', '..');

const checks = [];
function check(name, body) {
  checks.push({ name, body });
}

function freshDirectory(root, name) {
  const directory = path.join(root, name);
  fs.rmSync(directory, { recursive: true, force: true });
  fs.mkdirSync(directory, { recursive: true });
  return directory;
}

function writeJsonl(file, records, padding = '') {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, padding + records.map((record) => `${JSON.stringify(record)}\n`).join(''));
}

function readChat(directory, agent, session) {
  const file = path.join(directory, chat.chatFileName(agent, session));
  return fs.existsSync(file) ? JSON.parse(fs.readFileSync(file, 'utf8')) : null;
}

function sameWorktree(actual, expected) {
  const fold = (text) => (process.platform === 'win32' ? path.resolve(text).toLowerCase() : path.resolve(text));
  return fold(actual) === fold(expected);
}

// --- question extraction -------------------------------------------------------------------------------------------

check('extract: the question at the end, after a long summary', () => {
  const summary = 'I changed three files.\n\n- view.ts: the panel\n- watch.ts: the reader\n\nAll checks pass.';
  const text = `${summary}\n\nTwo pull requests touch the queue; **which one goes first?**`;
  assert.deepStrictEqual(chat.extractReply(text), {
    question: 'Two pull requests touch the queue; which one goes first?',
    asks: true,
  });
});

check('extract: the last asking paragraph, not the first one', () => {
  const text = 'Should I keep the old key?\n\nDone with the panel.\n\nShall I push it now?\n\nThe tests pass.';
  assert.deepStrictEqual(chat.extractReply(text), { question: 'Shall I push it now?', asks: true });
});

check('extract: a full-width question mark', () => {
  const text = '修正しました。\n\nマージしてよいですか？次は検証です。\n\n以上です。';
  const question = 'マージしてよいですか？次は検証です。';
  assert.deepStrictEqual(chat.extractReply(text), { question, asks: true });
});

check('extract: Markdown that only takes width is removed', () => {
  const text = 'Notes\n\n## Merge now?\n- see [the PR](https://example.com/pull/1) and **its checks**';
  assert.strictEqual(chat.extractQuestion(text), 'Merge now?\nsee the PR and its checks');
});

check('extract: a ? in a URL, a code span or a code block is not a question', () => {
  const url = 'See https://example.com/?a=b for details.';
  assert.deepStrictEqual(chat.extractReply(url), { question: url, asks: false });
  assert.deepStrictEqual(chat.extractReply('Ran it.\n```\nx = a ? b : c?\n```\nAll green.'), {
    question: 'All green.',
    asks: false,
  });
  assert.deepStrictEqual(chat.extractReply('Use `a ? b : c?` here.\n\nDone.'), { question: 'Done.', asks: false });
});

check('extract: no question -> the last paragraph, asks false', () => {
  const text = 'First paragraph.\n\nSecond paragraph,\nstill second.';
  assert.deepStrictEqual(chat.extractReply(text), { question: 'Second paragraph,\nstill second.', asks: false });
  assert.strictEqual(chat.extractQuestion('   \n\n'), null);
  assert.strictEqual(chat.extractQuestion(null), null);
});

check('extract: a long paragraph is cut from its start, so that the question stays', () => {
  const question = chat.extractQuestion(`${'word '.repeat(1000)}`);
  assert.strictEqual(Array.from(question).length, chat.MaxQuestionLength);
  assert.ok(question.startsWith('…'));
  const long = chat.extractQuestion(`${'x '.repeat(3000)}which one goes first?`);
  assert.strictEqual(Array.from(long).length, chat.MaxQuestionLength);
  assert.ok(long.endsWith('which one goes first?'));
});

// --- Claude Code ----------------------------------------------------------------------------------------------------

function claude(event, extra = {}) {
  return { session_id: 'c1', cwd: RepositoryRoot, hook_event_name: event, ...extra };
}

function assistantRecord(text, sidechain = false) {
  return {
    type: 'assistant',
    isSidechain: sidechain,
    message: { role: 'assistant', content: [{ type: 'thinking', thinking: 'x' }, { type: 'text', text }] },
  };
}

check('claude: UserPromptSubmit -> working, with the worktree of cwd; nothing printed', (root) => {
  const directory = freshDirectory(root, 'claude-prompt');
  assert.strictEqual(chat.applyPayload(claude('UserPromptSubmit', { prompt: 'p' }), directory, 1000), '');
  const record = readChat(directory, 'claude', 'c1');
  assert.strictEqual(record.state, 'working');
  assert.strictEqual(record.since, 1000);
  assert.strictEqual(record.agent, 'claude');
  assert.ok(sameWorktree(record.worktree, RepositoryRoot), record.worktree);
});

check('claude: Stop -> waiting for a reply, question from the main chain of the transcript', (root) => {
  const directory = freshDirectory(root, 'claude-stop');
  const transcript = path.join(root, 'claude-stop.jsonl');
  writeJsonl(transcript, [
    { type: 'user', message: { role: 'user', content: 'go' } },
    assistantRecord('Changed it. Shall I open the pull request? CI is green.'),
    { type: 'assistant', isSidechain: false, message: { content: [{ type: 'tool_use', name: 'Bash', input: {} }] } },
    assistantRecord('A subagent asks something else?', true),
    { type: 'last-prompt' },
    '{"type":"assistant", "partial',
  ]);
  fs.appendFileSync(transcript, '{"type":"assistant","message":'); // a line still being written
  assert.strictEqual(chat.applyPayload(claude('Stop', { transcript_path: transcript }), directory, 2000), '');
  const record = readChat(directory, 'claude', 'c1');
  assert.strictEqual(record.state, 'waiting');
  assert.strictEqual(record.kind, 'reply');
  assert.strictEqual(record.question, 'Changed it. Shall I open the pull request? CI is green.');
  assert.strictEqual(record.asks, true);
});

check('claude: Stop reads only the last MiB of the transcript', (root) => {
  const directory = freshDirectory(root, 'claude-tail');
  const near = path.join(root, 'claude-near.jsonl');
  writeJsonl(near, [assistantRecord('Ready?')], `${JSON.stringify({ type: 'filler', x: 'f'.repeat(3 << 20) })}\n`);
  chat.applyPayload(claude('Stop', { transcript_path: near }), directory, 1);
  assert.strictEqual(readChat(directory, 'claude', 'c1').question, 'Ready?');
  const far = path.join(root, 'claude-far.jsonl');
  writeJsonl(far, [assistantRecord('Too far?'), { type: 'filler', x: 'f'.repeat(2 << 20) }]);
  chat.applyPayload(claude('Stop', { transcript_path: far }), directory, 2);
  assert.strictEqual(readChat(directory, 'claude', 'c1').question, null);
});

check('claude: PreToolUse AskUserQuestion -> the questions and options exactly; other tools change nothing', (root) => {
  const directory = freshDirectory(root, 'claude-ask');
  chat.applyPayload(claude('UserPromptSubmit'), directory, 10);
  chat.applyPayload(claude('PreToolUse', { tool_name: 'Bash', tool_input: { command: 'ls' } }), directory, 20);
  assert.strictEqual(readChat(directory, 'claude', 'c1').updatedAt, 10);
  const input = {
    questions: [
      {
        question: 'Which layout?',
        header: 'Layout',
        multiSelect: false,
        options: [
          { label: 'Wide', description: 'One line per chat' },
          { label: 'Narrow', description: 'Two lines' },
        ],
      },
      { question: 'Also the jobs?', header: 'Jobs', multiSelect: true, options: [{ label: 'Yes', description: '' }] },
    ],
  };
  chat.applyPayload(claude('PreToolUse', { tool_name: 'AskUserQuestion', tool_input: input }), directory, 30);
  const record = readChat(directory, 'claude', 'c1');
  assert.strictEqual(record.state, 'waiting');
  assert.strictEqual(record.kind, 'question');
  assert.strictEqual(record.since, 30);
  assert.strictEqual(record.question, 'Which layout?\nAlso the jobs?');
  assert.deepStrictEqual(record.options, [
    {
      question: 'Which layout?',
      multiSelect: false,
      choices: [
        { label: 'Wide', description: 'One line per chat' },
        { label: 'Narrow', description: 'Two lines' },
      ],
    },
    { question: 'Also the jobs?', multiSelect: true, choices: [{ label: 'Yes', description: '' }] },
  ]);
});

check('claude: PostToolUse ends a question or permission wait, not a reply wait', (root) => {
  const directory = freshDirectory(root, 'claude-post');
  const ask = { tool_name: 'AskUserQuestion', tool_input: { questions: [{ question: 'Q?', options: [] }] } };
  chat.applyPayload(claude('PreToolUse', ask), directory, 10);
  chat.applyPayload(claude('PostToolUse', { tool_name: 'AskUserQuestion' }), directory, 20);
  assert.strictEqual(readChat(directory, 'claude', 'c1').state, 'working');
  const permission = { message: 'Needs approval', notification_type: 'permission_prompt' };
  chat.applyPayload(claude('Notification', permission), directory, 30);
  chat.applyPayload(claude('PostToolUse', { tool_name: 'Bash' }), directory, 40);
  assert.strictEqual(readChat(directory, 'claude', 'c1').state, 'working');
  chat.applyPayload(claude('Stop', { last_assistant_message: 'Done?' }), directory, 50);
  chat.applyPayload(claude('PostToolUse', { tool_name: 'Bash' }), directory, 60);
  const record = readChat(directory, 'claude', 'c1');
  assert.strictEqual(record.state, 'waiting');
  assert.strictEqual(record.since, 50);
});

check('claude: Notification -> permission with its message; idle keeps a waiting question', (root) => {
  const directory = freshDirectory(root, 'claude-notify');
  const permission = { message: 'Claude needs your permission to use Bash', notification_type: 'permission_prompt' };
  chat.applyPayload(claude('Notification', permission), directory, 10);
  let record = readChat(directory, 'claude', 'c1');
  assert.deepStrictEqual([record.state, record.kind, record.question], ['waiting', 'permission', permission.message]);
  chat.applyPayload(claude('Stop', { last_assistant_message: 'Merge it?' }), directory, 20);
  const idle = { message: 'waiting for input', notification_type: 'idle_prompt' };
  chat.applyPayload(claude('Notification', idle), directory, 99);
  record = readChat(directory, 'claude', 'c1');
  assert.deepStrictEqual([record.kind, record.question, record.since], ['reply', 'Merge it?', 20]);
  chat.applyPayload(claude('UserPromptSubmit'), directory, 100);
  chat.applyPayload(claude('Notification', idle), directory, 110);
  record = readChat(directory, 'claude', 'c1');
  assert.deepStrictEqual([record.state, record.kind, record.question], ['waiting', 'input', 'waiting for input']);
});

check('claude: SessionEnd removes the file', (root) => {
  const directory = freshDirectory(root, 'claude-end');
  chat.applyPayload(claude('UserPromptSubmit'), directory, 10);
  assert.ok(readChat(directory, 'claude', 'c1'));
  assert.strictEqual(chat.applyPayload(claude('SessionEnd', { reason: 'exit' }), directory, 20), '');
  assert.strictEqual(readChat(directory, 'claude', 'c1'), null);
});

// --- Antigravity ----------------------------------------------------------------------------------------------------

function antigravity(event, extra = {}) {
  return { conversationId: 'g1', workspacePaths: [RepositoryRoot], hookEventName: event, ...extra };
}

function planner(content, extra = {}) {
  const text = content === null ? {} : { content };
  return { type: 'PLANNER_RESPONSE', source: 'MODEL', status: 'DONE', ...text, ...extra };
}

check('antigravity: Stop with fullyIdle -> waiting, question from transcript_full.jsonl; prints {}', (root) => {
  const directory = freshDirectory(root, 'agy-stop');
  const logs = path.join(root, 'agy-stop-logs');
  writeJsonl(path.join(logs, 'transcript_full.jsonl'), [
    { type: 'USER_INPUT', source: 'USER_EXPLICIT', content: 'Is this a question?' },
    planner('Built it. Do you want the window on the left? It fits.'),
    planner(null, { tool_calls: [{ name: 'run_command', args: {} }] }),
    { type: 'GENERIC', source: 'MODEL', content: 'Not a planner response?' },
  ]);
  writeJsonl(path.join(logs, 'transcript.jsonl'), [planner('From the short transcript?')]);
  const payload = antigravity('Stop', { fullyIdle: true, transcriptPath: path.join(logs, 'transcript.jsonl') });
  assert.strictEqual(chat.applyPayload(payload, directory, 100), '{}');
  const record = readChat(directory, 'antigravity', 'g1');
  assert.deepStrictEqual([record.state, record.kind, record.since], ['waiting', 'reply', 100]);
  const question = 'Built it. Do you want the window on the left? It fits.';
  assert.deepStrictEqual([record.question, record.asks], [question, true]);
  assert.ok(sameWorktree(record.worktree, RepositoryRoot), record.worktree);
});

check('antigravity: Stop falls back to transcript.jsonl', (root) => {
  const directory = freshDirectory(root, 'agy-fallback');
  const logs = path.join(root, 'agy-fallback-logs');
  writeJsonl(path.join(logs, 'transcript.jsonl'), [planner('From the short transcript?')]);
  const payload = antigravity('Stop', { fullyIdle: true, transcriptPath: path.join(logs, 'transcript.jsonl') });
  chat.applyPayload(payload, directory, 1);
  assert.strictEqual(readChat(directory, 'antigravity', 'g1').question, 'From the short transcript?');
});

check('antigravity: Stop without fullyIdle -> working in the background', (root) => {
  const directory = freshDirectory(root, 'agy-background');
  assert.strictEqual(chat.applyPayload(antigravity('Stop', { fullyIdle: false }), directory, 5), '{}');
  const record = readChat(directory, 'antigravity', 'g1');
  assert.deepStrictEqual([record.state, record.kind, record.question], ['background', null, null]);
});

check('antigravity: only the first PreInvocation after a Stop changes the state', (root) => {
  const directory = freshDirectory(root, 'agy-invocation');
  chat.applyPayload(antigravity('Stop', { fullyIdle: true }), directory, 10);
  assert.strictEqual(chat.applyPayload(antigravity('PreInvocation', { invocationNum: 1 }), directory, 20), '{}');
  let record = readChat(directory, 'antigravity', 'g1');
  assert.deepStrictEqual([record.state, record.since, record.updatedAt], ['working', 20, 20]);
  chat.applyPayload(antigravity('PreInvocation', { invocationNum: 2 }), directory, 30);
  record = readChat(directory, 'antigravity', 'g1');
  assert.deepStrictEqual([record.state, record.since, record.updatedAt], ['working', 20, 20]);
  chat.applyPayload(antigravity('Stop', { fullyIdle: false }), directory, 40);
  chat.applyPayload(antigravity('PreInvocation', { invocationNum: 3 }), directory, 50);
  record = readChat(directory, 'antigravity', 'g1');
  assert.deepStrictEqual([record.state, record.since], ['working', 50]);
});

check('antigravity: PreToolUse ask_question -> the questions and options; prints nothing', (root) => {
  const directory = freshDirectory(root, 'agy-ask');
  const nested = { questions: [{ question: 'Pick one?', options: ['A', 'B'], is_multi_select: true }] };
  const call = (args) => antigravity('PreToolUse', { toolCall: { id: 't1', name: 'ask_question', args } });
  assert.strictEqual(chat.applyPayload(call(nested), directory, 10), '');
  let record = readChat(directory, 'antigravity', 'g1');
  assert.deepStrictEqual([record.state, record.kind, record.question], ['waiting', 'question', 'Pick one?']);
  assert.deepStrictEqual(record.options, [
    { question: 'Pick one?', multiSelect: true, choices: [{ label: 'A' }, { label: 'B' }] },
  ]);
  chat.applyPayload(call({ question: 'Flat?', options: ['Yes'], is_multi_select: false }), directory, 20);
  record = readChat(directory, 'antigravity', 'g1');
  assert.deepStrictEqual(record.options, [{ question: 'Flat?', multiSelect: false, choices: [{ label: 'Yes' }] }]);
  const other = antigravity('PreToolUse', { toolCall: { name: 'run_command', args: { CommandLine: 'ls' } } });
  assert.strictEqual(chat.applyPayload(other, directory, 30), '');
  assert.strictEqual(readChat(directory, 'antigravity', 'g1').updatedAt, 20);
  chat.applyPayload(antigravity('PreInvocation'), directory, 40);
  assert.strictEqual(readChat(directory, 'antigravity', 'g1').state, 'working');
});

check('payloads of neither agent and payloads without a session write nothing', (root) => {
  const directory = freshDirectory(root, 'unknown');
  assert.strictEqual(chat.applyPayload({ event: 'Stop' }, directory, 1), '');
  assert.strictEqual(chat.applyPayload(claude('Stop', { session_id: '' }), directory, 1), '');
  assert.strictEqual(chat.applyPayload(antigravity('Stop', { conversationId: undefined }), directory, 1), '{}');
  assert.deepStrictEqual(fs.readdirSync(directory), []);
});

// --- files ----------------------------------------------------------------------------------------------------------

check('main checkout: read from .git without git, the same as git says for this checkout', (root) => {
  const main = freshDirectory(root, 'layout-main');
  fs.mkdirSync(path.join(main, '.git', 'worktrees', 'w1'), { recursive: true });
  fs.writeFileSync(path.join(main, '.git', 'worktrees', 'w1', 'commondir'), '../..\n');
  const worktree = freshDirectory(root, 'layout-worktree');
  fs.writeFileSync(path.join(worktree, '.git'), `gitdir: ${path.join(main, '.git', 'worktrees', 'w1')}\n`);
  assert.strictEqual(chat.readMainCheckout(main), main);
  assert.strictEqual(chat.readMainCheckout(worktree), main);
  assert.strictEqual(chat.readMainCheckout(freshDirectory(root, 'layout-none')), null);
  const git = spawnSync('git', ['rev-parse', '--path-format=absolute', '--git-common-dir'], {
    cwd: RepositoryRoot,
    encoding: 'utf8',
  });
  if (git.status === 0) {
    assert.ok(sameWorktree(chat.readMainCheckout(RepositoryRoot), path.dirname(git.stdout.trim())));
  }
});

check('write: the target keeps its old content until the rename, and no temporary file is left', (root) => {
  const directory = freshDirectory(root, 'atomic');
  const file = path.join(directory, 'a.json');
  fs.writeFileSync(file, 'old');
  const rename = fs.renameSync;
  let seen = null;
  fs.renameSync = (from, to) => {
    seen = { from, to, before: fs.readFileSync(to, 'utf8'), staged: fs.readFileSync(from, 'utf8') };
    return rename(from, to);
  };
  try {
    chat.writeAtomically(file, 'new');
  } finally {
    fs.renameSync = rename;
  }
  assert.ok(seen, 'the write went through a rename');
  assert.deepStrictEqual([seen.to, seen.before, seen.staged], [file, 'old', 'new']);
  assert.strictEqual(fs.readFileSync(file, 'utf8'), 'new');
  assert.deepStrictEqual(fs.readdirSync(directory), ['a.json']);
});

check('write: chat files not changed for 12 hours are removed, others kept', (root) => {
  const directory = freshDirectory(root, 'prune');
  const now = Date.now();
  const old = path.join(directory, 'claude-old.json');
  const recent = path.join(directory, 'claude-recent.json');
  for (const file of [old, recent]) {
    fs.writeFileSync(file, '{}');
  }
  const past = (now - chat.StaleMs - 60000) / 1000;
  fs.utimesSync(old, past, past);
  chat.applyPayload(claude('UserPromptSubmit'), directory, now);
  assert.deepStrictEqual(fs.readdirSync(directory).sort(), ['claude-c1.json', 'claude-recent.json']);
});

check('listChats: drops entries older than 12 hours, waiting first, the longest wait first', (root) => {
  const directory = freshDirectory(root, 'list');
  const now = 100 * 60 * 60 * 1000;
  const put = (name, fields) => fs.writeFileSync(path.join(directory, `${name}.json`), JSON.stringify(fields));
  put('a', { session: 'a', state: 'working', since: now - 5000, updatedAt: now - 50 });
  put('b', { session: 'b', state: 'waiting', since: now - 100, updatedAt: now - 100 });
  put('c', { session: 'c', state: 'waiting', since: now - 900, updatedAt: now - 10 });
  put('d', { session: 'd', state: 'waiting', since: now - chat.StaleMs - 5, updatedAt: now - chat.StaleMs - 1 });
  put('e', { session: 'e', state: 'background', since: now - 7000, updatedAt: now - 70 });
  fs.writeFileSync(path.join(directory, 'f.json'), '{"half');
  fs.writeFileSync(path.join(directory, 'errors.log'), 'x');
  const sessions = chat.listChats({ directory, now }).map((entry) => entry.session);
  assert.deepStrictEqual(sessions, ['c', 'b', 'e', 'a']);
  assert.deepStrictEqual(chat.listChats({ directory: path.join(directory, 'missing'), now }), []);
});

// --- the hook as a process ------------------------------------------------------------------------------------------

function runHook(directory, input, env = {}) {
  return spawnSync(process.execPath, [Script, '--hook'], {
    input,
    encoding: 'utf8',
    env: { ...process.env, GSHARP_CHAT_STATUS_DIR: directory, ...env },
    timeout: 20000,
    windowsHide: true,
  });
}

check('hook process: Claude Code gets no output and exit 0, also on a damaged payload', (root) => {
  const directory = freshDirectory(root, 'process-claude');
  for (const event of ['UserPromptSubmit', 'Stop', 'Notification', 'PreToolUse', 'PostToolUse', 'SessionEnd']) {
    const result = runHook(directory, JSON.stringify(claude(event, { last_assistant_message: 'Q?' })));
    assert.deepStrictEqual([result.status, result.stdout, result.stderr], [0, '', ''], event);
  }
  const damaged = runHook(directory, '{"hook_event_name":"Stop",');
  assert.deepStrictEqual([damaged.status, damaged.stdout], [0, '']);
  assert.match(fs.readFileSync(path.join(directory, 'errors.log'), 'utf8'), /unreadable hook payload/);
});

check('hook process: Antigravity gets {} after Stop and PreInvocation, nothing after PreToolUse', (root) => {
  const directory = freshDirectory(root, 'process-agy');
  const stop = runHook(directory, JSON.stringify(antigravity('Stop', { fullyIdle: false })));
  assert.deepStrictEqual([stop.status, stop.stdout.trim()], [0, '{}']);
  const invocation = runHook(directory, JSON.stringify(antigravity('PreInvocation')));
  assert.deepStrictEqual([invocation.status, invocation.stdout.trim()], [0, '{}']);
  const tool = runHook(directory, JSON.stringify(antigravity('PreToolUse', { toolCall: { name: 'ask_question' } })));
  assert.deepStrictEqual([tool.status, tool.stdout], [0, '']);
  const damaged = runHook(directory, '{"hookEventName":"Stop", broken');
  assert.deepStrictEqual([damaged.status, damaged.stdout.trim()], [0, '{}']);
});

check('hook process: a directory that cannot be created still gives exit 0 and no output', (root) => {
  const blocker = path.join(root, 'not-a-directory');
  fs.writeFileSync(blocker, 'x');
  const result = runHook(path.join(blocker, 'chat-status'), JSON.stringify(claude('UserPromptSubmit')));
  assert.deepStrictEqual([result.status, result.stdout], [0, '']);
});

const summaryHelpers = { freshDirectory, readChat, writeJsonl, claude, antigravity, runHook };
checks.push(...require('./self-test-summary.js').summaryChecks(summaryHelpers));
checks.push(...require('./self-test-antigravity.js').antigravityChecks(summaryHelpers));

function selfTest() {
  // No check may reach the real Claude Code CLI: summaries are off unless a check names its fake CLI
  process.env.GSHARP_CHAT_STATUS_SUMMARY = '0';
  const root = path.join(RepositoryRoot, 'tmp', `chat-status-self-test-${process.pid}`);
  fs.rmSync(root, { recursive: true, force: true });
  fs.mkdirSync(root, { recursive: true });
  let failed = 0;
  try {
    for (const { name, body } of checks) {
      try {
        body(root);
        process.stdout.write(`ok    ${name}\n`);
      } catch (error) {
        failed++;
        process.stdout.write(`FAIL  ${name}\n      ${String(error.stack ?? error).split('\n').join('\n      ')}\n`);
      }
    }
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
  process.stdout.write(`${checks.length - failed} of ${checks.length} checks passed\n`);
  process.exitCode = failed === 0 ? 0 : 1;
}

module.exports = { selfTest };
