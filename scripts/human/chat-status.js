#!/usr/bin/env node
'use strict';

// Records the state of each agent chat (Claude Code and Antigravity) in one small JSON file per chat, so that a status
// window can show which chats wait for the person and what they asked. It runs as a hook of both agents
// (.claude/settings.json, .agents/hooks.json); the payload on stdin tells the two apart (Claude Code: hook_event_name,
// Antigravity: hookEventName).
//
// The files are <main checkout>/tmp/chat-status/<agent>-<session>.json (the main checkout is the parent of the git
// common directory, as gpu-lock.js finds its lock, read here from the .git file of a worktree without starting git);
// GSHARP_CHAT_STATUS_DIR overrides the directory.
// Each file is written to a temporary file and renamed over the old one. The format is described in
// scripts/human/chat-status/README.md.
//
// The hook never blocks or fails the agent: it exits 0 on every input, also on an error (logged to errors.log in the
// same directory), reads no more than the last MiB of a transcript, and uses no network. For Claude Code it prints
// nothing; for Antigravity it prints `{}` after Stop and PreInvocation (a JSON object is expected there) and nothing
// after PreToolUse (an Antigravity "allow" would skip the permission prompt, as in check-queued-command.js).
//
// Events:
//   Claude Code   UserPromptSubmit                   working
//                 Stop                               waiting for a reply; the question is taken from the last
//                                                    assistant text of transcript_path
//                 PreToolUse AskUserQuestion         waiting for an answer, with the questions and their options
//                 PostToolUse                        working again, when the chat waited for an answer or a permission
//                 Notification                       waiting for a permission (permission_prompt) or for input
//                 SessionEnd                         the file is removed
//   Antigravity   Stop                               waiting for a reply when fullyIdle, else working in the
//                                                    background; the question from the last PLANNER_RESPONSE of the
//                                                    model in transcript_full.jsonl next to transcriptPath
//                 PreInvocation                      working, only when the chat was not working already (it fires
//                                                    before every model call of a turn)
//                 PreToolUse ask_question            waiting for an answer, with the questions and their options
//
// Usage:
//   node scripts/human/chat-status.js --hook           reads one hook payload on stdin
//   node scripts/human/chat-status.js --list [--json]  prints the current chats, waiting first
//   node scripts/human/chat-status.js --self-test
//   require('./chat-status.js').listChats() returns the current chats for the status window

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const usage = 'Usage: chat-status.js (--hook | --list [--json] | --self-test | --help)';

const DirectoryVariable = 'GSHARP_CHAT_STATUS_DIR';
const FormatVersion = 1;
/** A chat file not updated for this long is not shown, and the hook removes it. */
const StaleMs = 12 * 60 * 60 * 1000;
const MaxQuestionLength = 2000;
const MaxOptionLength = 500;
const TranscriptTailBytes = 1024 * 1024;
const ErrorLogLimitBytes = 1024 * 1024;
const StdinTimeoutMs = 3000;

/**
 * The main checkout from the `.git` entry of this script's checkout, without starting git (a hook runs on every
 * event): a directory in the main checkout; in a worktree, a file `gitdir: <main>/.git/worktrees/<name>` whose
 * `commondir` names the main `.git`. Null when the layout is not one of these.
 */
function readMainCheckout(root) {
  const entry = path.join(root, '.git');
  const stat = fs.statSync(entry, { throwIfNoEntry: false });
  if (stat?.isDirectory()) {
    return root;
  }
  if (!stat?.isFile()) {
    return null;
  }
  const match = /^gitdir:\s*(.+?)\s*$/m.exec(fs.readFileSync(entry, 'utf8'));
  if (!match) {
    return null;
  }
  const gitDirectory = path.resolve(root, match[1]);
  const commonFile = path.join(gitDirectory, 'commondir');
  if (!fs.existsSync(commonFile)) {
    return null;
  }
  const commonDirectory = path.resolve(gitDirectory, fs.readFileSync(commonFile, 'utf8').trim());
  return path.dirname(commonDirectory);
}

/** <main checkout>/tmp/chat-status, whichever worktree runs the script. */
function resolveStatusDirectory() {
  const configured = process.env[DirectoryVariable];
  if (configured) {
    return path.resolve(configured);
  }
  const root = path.resolve(__dirname, '..', '..');
  const main = readMainCheckout(root);
  if (main !== null) {
    return path.join(main, 'tmp', 'chat-status');
  }
  let base = root;
  const git = spawnSync('git', ['rev-parse', '--path-format=absolute', '--git-common-dir'], {
    cwd: __dirname,
    encoding: 'utf8',
    windowsHide: true,
  });
  if (!git.error && git.status === 0) {
    const commonDir = git.stdout.trim();
    if (commonDir !== '' && path.isAbsolute(commonDir)) {
      base = path.dirname(path.resolve(commonDir));
    }
  }
  return path.join(base, 'tmp', 'chat-status');
}

/** The root of the worktree that contains `cwd`, or `cwd` itself when git does not know it. */
function resolveWorktree(cwd) {
  if (!cwd) {
    return null;
  }
  const git = spawnSync('git', ['rev-parse', '--show-toplevel'], { cwd, encoding: 'utf8', windowsHide: true });
  if (!git.error && git.status === 0 && git.stdout.trim() !== '') {
    return path.resolve(git.stdout.trim());
  }
  return path.resolve(cwd);
}

// --- question extraction -------------------------------------------------------------------------------------------

/** Cuts a text to `limit` characters, marking the cut with an ellipsis. */
function clip(text, limit) {
  if (typeof text !== 'string') {
    return null;
  }
  const chars = Array.from(text);
  return chars.length <= limit ? text : `${chars.slice(0, limit - 1).join('')}…`;
}

// An ASCII '?' ends a sentence only before a space, a line end or closing punctuation (not in `a?b` or a URL query);
// a full-width '？' always does.
const QuestionMark = /？|\?(?=$|[\s)\]"'*_」』）])/g;
// What ends the sentence before the question: a line break, a full-width stop, or an ASCII stop before a space.
const SentenceEnd = /[\n。！？]|[.!?](?=\s)/g;
const ListMarker = /^(?:[-*+]|\d+[.)]|#+|>)\s+/;

/** The last sentence that ends in `?` or `？`, or else the last paragraph, cut to MaxQuestionLength. */
function extractQuestion(text) {
  if (typeof text !== 'string') {
    return null;
  }
  const plain = text.replace(/```[\s\S]*?(?:```|$)/g, '\n').replace(/\r\n?/g, '\n');
  let mark = -1;
  for (const match of plain.matchAll(QuestionMark)) {
    mark = match.index;
  }
  if (mark >= 0) {
    let start = 0;
    for (const match of plain.slice(0, mark).matchAll(SentenceEnd)) {
      start = match.index + 1;
    }
    let end = mark + 1;
    while (end < plain.length && '*_」』）)'.includes(plain[end])) {
      end++;
    }
    const sentence = plain.slice(start, end).trim().replace(ListMarker, '').trim();
    if (sentence !== '') {
      return clip(sentence, MaxQuestionLength);
    }
  }
  const paragraphs = plain
    .split(/\n[ \t]*\n/)
    .map((part) => part.trim())
    .filter((part) => part !== '');
  return paragraphs.length === 0 ? null : clip(paragraphs[paragraphs.length - 1], MaxQuestionLength);
}

/** Reads the last `limit` bytes of a file as complete lines (the first, partial line is dropped). */
function readTailLines(file, limit) {
  const handle = fs.openSync(file, 'r');
  try {
    const size = fs.fstatSync(handle).size;
    const length = Math.min(size, limit);
    const buffer = Buffer.alloc(length);
    fs.readSync(handle, buffer, 0, length, size - length);
    const lines = buffer.toString('utf8').split('\n');
    if (length < size) {
      lines.shift();
    }
    return lines;
  } finally {
    fs.closeSync(handle);
  }
}

/** The text of the last record of a JSONL transcript that `pick` returns a non-empty text for. */
function lastTextOf(file, pick) {
  const lines = readTailLines(file, TranscriptTailBytes);
  for (let i = lines.length - 1; i >= 0; i--) {
    const line = lines[i].trim();
    if (line === '') {
      continue;
    }
    let record;
    try {
      record = JSON.parse(line);
    } catch {
      continue; // a line still being written
    }
    const text = pick(record);
    if (typeof text === 'string' && text.trim() !== '') {
      return text;
    }
  }
  return null;
}

/** Claude Code: the text blocks of the last assistant message of the main chain (not a subagent's). */
function claudeAssistantText(record) {
  if (record?.type !== 'assistant' || record.isSidechain === true || !Array.isArray(record.message?.content)) {
    return null;
  }
  const texts = record.message.content
    .filter((block) => block?.type === 'text' && typeof block.text === 'string')
    .map((block) => block.text);
  return texts.length === 0 ? null : texts.join('\n\n');
}

/** Antigravity: the content of a planner response of the model. */
function antigravityPlannerText(record) {
  return record?.type === 'PLANNER_RESPONSE' && record.source === 'MODEL' ? record.content : null;
}

/** Antigravity writes transcript_full.jsonl next to transcriptPath (transcript.jsonl); either may be missing. */
function antigravityTranscripts(transcriptPath) {
  const directory = path.dirname(transcriptPath);
  return [path.join(directory, 'transcript_full.jsonl'), transcriptPath];
}

function firstExisting(files) {
  return files.find((file) => fs.existsSync(file)) ?? null;
}

/** The questions of AskUserQuestion (Claude Code) or ask_question (Antigravity), with their options. */
function questionsOf(input) {
  const list = Array.isArray(input?.questions) ? input.questions : input?.question ? [input] : [];
  return list
    .filter((item) => typeof item?.question === 'string')
    .map((item) => ({
      question: clip(item.question, MaxQuestionLength),
      multiSelect: item.multiSelect === true || item.is_multi_select === true || item.isMultiSelect === true,
      choices: (Array.isArray(item.options) ? item.options : []).map((option) =>
        typeof option === 'string'
          ? { label: clip(option, MaxOptionLength) }
          : {
              label: clip(String(option?.label ?? ''), MaxOptionLength),
              ...(typeof option?.description === 'string'
                ? { description: clip(option.description, MaxOptionLength) }
                : {}),
            }
      ),
    }));
}

// --- events ---------------------------------------------------------------------------------------------------------

/**
 * What one hook call means, with no file access but the transcript read: `{ agent, session, cwd, update, remove,
 * output }`, where `update(previous)` returns the fields to record or null to leave the file alone. Returns null for
 * a payload that is neither agent's.
 */
function interpret(payload, now) {
  if (typeof payload?.hook_event_name === 'string') {
    return interpretClaude(payload, now);
  }
  if (typeof payload?.hookEventName === 'string') {
    return interpretAntigravity(payload, now);
  }
  return null;
}

function waiting(kind, now, question, options) {
  return { state: 'waiting', kind, since: now, question: question ?? null, options: options ?? null };
}

function working(now) {
  return { state: 'working', kind: null, since: now, question: null, options: null };
}

function joinQuestions(questions) {
  return questions.length === 0 ? null : clip(questions.map((q) => q.question).join('\n'), MaxQuestionLength);
}

function interpretClaude(payload, now) {
  const event = payload.hook_event_name;
  const base = { agent: 'claude', session: payload.session_id, cwd: payload.cwd, output: '', remove: false };
  switch (event) {
    case 'UserPromptSubmit':
      return { ...base, update: () => working(now) };
    case 'Stop':
      return {
        ...base,
        update: () => {
          let text = typeof payload.last_assistant_message === 'string' ? payload.last_assistant_message : null;
          if (!text && payload.transcript_path && fs.existsSync(payload.transcript_path)) {
            text = lastTextOf(payload.transcript_path, claudeAssistantText);
          }
          return waiting('reply', now, extractQuestion(text));
        },
      };
    case 'PreToolUse':
      if (payload.tool_name !== 'AskUserQuestion') {
        return { ...base, update: () => null };
      }
      return {
        ...base,
        update: () => {
          const questions = questionsOf(payload.tool_input);
          return waiting('question', now, joinQuestions(questions), questions);
        },
      };
    case 'PostToolUse':
      return {
        ...base,
        update: (previous) =>
          previous?.state === 'waiting' && (previous.kind === 'question' || previous.kind === 'permission')
            ? working(now)
            : null,
      };
    case 'Notification':
      return {
        ...base,
        update: (previous) => {
          const message = clip(typeof payload.message === 'string' ? payload.message : '', MaxQuestionLength);
          if (payload.notification_type === 'permission_prompt') {
            return waiting('permission', now, message);
          }
          // idle_prompt and the like: a chat that already waits keeps its question and since
          if (previous?.state === 'waiting') {
            return null;
          }
          return waiting('input', now, message);
        },
      };
    case 'SessionEnd':
      return { ...base, remove: true, update: () => null };
    default:
      return { ...base, update: () => null };
  }
}

function interpretAntigravity(payload, now) {
  const event = payload.hookEventName;
  const workspace = Array.isArray(payload.workspacePaths) ? payload.workspacePaths[0] : undefined;
  const base = { agent: 'antigravity', session: payload.conversationId, cwd: payload.cwd || workspace, remove: false };
  switch (event) {
    case 'Stop':
      return {
        ...base,
        output: '{}',
        update: () => {
          if (payload.fullyIdle !== true) {
            return { state: 'background', kind: null, since: now, question: null, options: null };
          }
          let text = null;
          if (typeof payload.transcriptPath === 'string' && payload.transcriptPath !== '') {
            const file = firstExisting(antigravityTranscripts(payload.transcriptPath));
            text = file ? lastTextOf(file, antigravityPlannerText) : null;
          }
          return waiting('reply', now, extractQuestion(text));
        },
      };
    case 'PreInvocation':
      // fires before every model call: only the first one after a Stop or a question changes the state
      return { ...base, output: '{}', update: (previous) => (previous?.state === 'working' ? null : working(now)) };
    case 'PreToolUse':
      if (payload.toolCall?.name !== 'ask_question') {
        return { ...base, output: '', update: () => null };
      }
      return {
        ...base,
        output: '',
        update: () => {
          const questions = questionsOf(payload.toolCall.args);
          return waiting('question', now, joinQuestions(questions), questions);
        },
      };
    default:
      return { ...base, output: '{}', update: () => null };
  }
}

// --- files ----------------------------------------------------------------------------------------------------------

function chatFileName(agent, session) {
  return `${agent}-${String(session).replace(/[^A-Za-z0-9._-]/g, '_')}.json`;
}

function readJson(file) {
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch (error) {
    if (error.code === 'ENOENT') {
      return null;
    }
    throw error;
  }
}

/** Writes through a temporary file and a rename, so that a reader never sees half a file. */
function writeAtomically(file, text) {
  const temporary = `${file}.${process.pid}.${Date.now()}.tmp`;
  fs.writeFileSync(temporary, text);
  for (let attempt = 0; ; attempt++) {
    try {
      fs.renameSync(temporary, file);
      return;
    } catch (error) {
      // Windows refuses the rename while a reader has the target open; that lasts a moment
      if (attempt >= 20 || (error.code !== 'EPERM' && error.code !== 'EACCES' && error.code !== 'EBUSY')) {
        fs.rmSync(temporary, { force: true });
        throw error;
      }
      Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 10);
    }
  }
}

/** Removes the chat files and stray temporary files not changed for StaleMs. */
function pruneStale(directory, now) {
  for (const name of fs.readdirSync(directory)) {
    if (!name.endsWith('.json') && !name.endsWith('.tmp')) {
      continue;
    }
    const file = path.join(directory, name);
    try {
      if (now - fs.statSync(file).mtimeMs > StaleMs) {
        fs.rmSync(file, { force: true });
      }
    } catch (error) {
      if (error.code !== 'ENOENT') {
        throw error;
      }
    }
  }
}

/**
 * Applies one hook payload to the chat files in `directory`. Returns the text to print on stdout. Throws on a file
 * error; the caller logs it.
 */
function applyPayload(payload, directory, now) {
  const action = interpret(payload, now);
  if (action === null) {
    return '';
  }
  if (typeof action.session !== 'string' || action.session === '') {
    return action.output;
  }
  fs.mkdirSync(directory, { recursive: true });
  const file = path.join(directory, chatFileName(action.agent, action.session));
  if (action.remove) {
    fs.rmSync(file, { force: true });
    return action.output;
  }
  const previous = readJson(file);
  const fields = action.update(previous);
  if (fields === null) {
    return action.output;
  }
  const cwd = typeof action.cwd === 'string' && action.cwd !== '' ? action.cwd : (previous?.cwd ?? null);
  const worktree = previous?.cwd === cwd && previous?.worktree ? previous.worktree : resolveWorktree(cwd);
  const record = {
    version: FormatVersion,
    agent: action.agent,
    session: action.session,
    cwd,
    worktree,
    ...fields,
    event: payload.hook_event_name ?? payload.hookEventName,
    updatedAt: now,
  };
  writeAtomically(file, `${JSON.stringify(record, null, 2)}\n`);
  pruneStale(directory, now);
  return action.output;
}

function logError(directory, error) {
  try {
    fs.mkdirSync(directory, { recursive: true });
    const log = path.join(directory, 'errors.log');
    try {
      if (fs.statSync(log).size > ErrorLogLimitBytes) {
        fs.rmSync(log, { force: true });
      }
    } catch {
      // no log yet
    }
    const text = error instanceof Error ? (error.stack ?? error.message) : String(error);
    fs.appendFileSync(log, `${new Date().toISOString()} ${text}\n`);
  } catch {
    // the hook must not fail the agent, and there is nowhere left to report this
  }
}

/** What to print when the payload could not be applied: an Antigravity Stop / PreInvocation expects a JSON object. */
function fallbackOutput(input) {
  return /"hookEventName"\s*:\s*"(?:Stop|PreInvocation)"/.test(input) ? '{}' : '';
}

/** The hook: reads the payload, applies it, prints what the agent expects, and always exits 0. */
function runHook() {
  let directory = null;
  let input = '';
  let finished = false;
  const finish = () => {
    if (finished) {
      return;
    }
    finished = true;
    let output = '';
    try {
      directory = resolveStatusDirectory();
      let payload = null;
      try {
        payload = JSON.parse(input);
      } catch (error) {
        logError(directory, new Error(`unreadable hook payload (${input.length} chars): ${error.message}`));
      }
      output = payload === null ? fallbackOutput(input) : applyPayload(payload, directory, Date.now());
    } catch (error) {
      if (directory !== null) {
        logError(directory, error);
      }
      output = fallbackOutput(input);
    }
    if (output !== '') {
      process.stdout.write(`${output}\n`);
    }
    process.exitCode = 0;
    process.stdin.destroy();
  };
  setTimeout(finish, StdinTimeoutMs).unref();
  process.stdin.setEncoding('utf8');
  process.stdin.on('data', (chunk) => (input += chunk));
  process.stdin.on('end', finish);
  process.stdin.on('error', finish);
}

// --- reader for the status window ------------------------------------------------------------------------------------

/**
 * The chats recorded in `directory` (default: the main checkout's tmp/chat-status), without those not updated for
 * 12 hours, sorted: waiting chats first, the longest wait first, then the others, the longest in their state first.
 * Unreadable files are skipped (a file is replaced by a rename, so a reader sees the old or the new one).
 */
function listChats(options = {}) {
  const directory = options.directory ?? resolveStatusDirectory();
  const now = options.now ?? Date.now();
  let names;
  try {
    names = fs.readdirSync(directory);
  } catch (error) {
    if (error.code === 'ENOENT') {
      return [];
    }
    throw error;
  }
  const chats = [];
  for (const name of names) {
    if (!name.endsWith('.json')) {
      continue;
    }
    let record;
    try {
      record = JSON.parse(fs.readFileSync(path.join(directory, name), 'utf8'));
    } catch {
      continue; // removed or replaced while listing
    }
    if (typeof record?.updatedAt !== 'number' || now - record.updatedAt > StaleMs) {
      continue;
    }
    chats.push(record);
  }
  const rank = (chat) => (chat.state === 'waiting' ? 0 : 1);
  return chats.sort((a, b) => rank(a) - rank(b) || a.since - b.since || a.updatedAt - b.updatedAt);
}

function printList(json) {
  const chats = listChats();
  if (json) {
    process.stdout.write(`${JSON.stringify(chats, null, 2)}\n`);
    return;
  }
  const now = Date.now();
  for (const chat of chats) {
    const minutes = Math.round((now - chat.since) / 60000);
    const state = chat.state === 'waiting' ? `waiting (${chat.kind})` : chat.state;
    const question = chat.question ? `  ${chat.question.replace(/\s+/g, ' ').slice(0, 80)}` : '';
    process.stdout.write(`${chat.agent}\t${chat.worktree ?? '?'}\t${state}\t${minutes}m${question}\n`);
  }
}

function main() {
  const args = process.argv.slice(2);
  if (args[0] === '--hook' && args.length === 1) {
    runHook();
  } else if (args[0] === '--list' && (args.length === 1 || (args.length === 2 && args[1] === '--json'))) {
    printList(args.length === 2);
  } else if (args[0] === '--self-test' && args.length === 1) {
    require('./chat-status/self-test.js').selfTest();
  } else if ((args[0] === '--help' || args[0] === '-h') && args.length === 1) {
    process.stdout.write(`${usage}\n`);
  } else {
    process.stderr.write(`${usage}\n`);
    process.exit(2);
  }
}

module.exports = {
  StaleMs,
  MaxQuestionLength,
  extractQuestion,
  interpret,
  applyPayload,
  writeAtomically,
  readMainCheckout,
  listChats,
  chatFileName,
};

if (require.main === module) {
  main();
}
