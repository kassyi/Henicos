'use strict';

// The chat files of chat-status.js: where they are (the main checkout lookup), applying one hook payload to them (the
// atomic write, the removal of stale files), the error log, and listChats() for the status window.

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');
const { interpret } = require('./events.js');
const { conversationFacts } = require('./antigravity.js');

const DirectoryVariable = 'GSHARP_CHAT_STATUS_DIR';
const FormatVersion = 1;
/** A chat file not updated for this long is not shown, and the hook removes it. */
const StaleMs = 12 * 60 * 60 * 1000;
const ErrorLogLimitBytes = 1024 * 1024;
/** The fields of a chat that a new state keeps unless it sets them (the rest are written anew by every state). */
const ChatFields = [
  'firstPrompt',
  'recentPrompts',
  'title',
  'agentTitle',
  'openQuestions',
  'lastPrompt',
  'promptedAt',
  'stoppedAt',
];

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
  const root = path.resolve(__dirname, '..', '..', '..');
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

/** Removes the chat files, stray temporary files and summarizer locks not changed for StaleMs. */
function pruneStale(directory, now) {
  for (const name of fs.readdirSync(directory)) {
    if (!name.endsWith('.json') && !name.endsWith('.tmp') && !name.endsWith('.lock')) {
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
 * error; the caller logs it. `onRecord(file, record)`, when given, is called with each record written (the hook starts
 * the summarizer from it).
 */
function applyPayload(payload, directory, now, onRecord = null) {
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
    // what belongs to the chat outlives its states; Haiku's verdict on one wait is dropped with it
    ...Object.fromEntries(ChatFields.map((name) => [name, fields[name] ?? previous?.[name] ?? null])),
    event: payload.hook_event_name ?? payload.hookEventName,
    updatedAt: now,
  };
  writeAtomically(file, `${JSON.stringify(record, null, 2)}\n`);
  pruneStale(directory, now);
  onRecord?.(file, record);
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

/**
 * The chats recorded in `directory` (default: the main checkout's tmp/chat-status), without those not updated for
 * 12 hours, sorted: waiting chats first, the longest wait first, then the others, the longest in their state first.
 * Unreadable files are skipped (a file is replaced by a rename, so a reader sees the old or the new one), and so are
 * the chats of Antigravity subagents (`options.antigravityRoot`: its folder, default ~/.gemini/antigravity).
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
    if (record.agent === 'antigravity' && typeof record.session === 'string') {
      // a subagent's file from before the hook could tell is never shown; a title given since is read now
      const facts = conversationFacts(record.session, null, options.antigravityRoot);
      if (facts.subagent === true) {
        continue;
      }
      record.agentTitle = record.agentTitle ?? facts.title;
    }
    chats.push(record);
  }
  const rank = (chat) => (chat.state === 'waiting' ? 0 : 1);
  return chats.sort((a, b) => rank(a) - rank(b) || a.since - b.since || a.updatedAt - b.updatedAt);
}

module.exports = {
  StaleMs,
  readMainCheckout,
  resolveStatusDirectory,
  chatFileName,
  readJson,
  writeAtomically,
  applyPayload,
  logError,
  listChats,
};
