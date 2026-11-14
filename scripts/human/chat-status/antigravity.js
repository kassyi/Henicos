'use strict';

// What chat-status.js reads from Antigravity's own files: whether a conversation is a subagent's (the first record of
// its transcript is a system message, not the person's input; else an annotation file without a title), and the title
// Antigravity gives a conversation (`title:"..."` in annotations/<conversation>.pbtxt, protobuf text format, written a
// few seconds after the first turn starts). The files live under ~/.gemini/antigravity, or under the folder that holds
// the brain/ directory of a payload's transcriptPath (brain/<conversation>/.system_generated/logs/transcript.jsonl).

const fs = require('fs');
const os = require('os');
const path = require('path');

const DefaultRoot = path.join(os.homedir(), '.gemini', 'antigravity');
const HeadBytes = 64 * 1024;

/** The Antigravity folder of a transcript path (four levels above it), else the default one. */
function rootOf(transcriptPath) {
  return typeof transcriptPath === 'string' && transcriptPath !== ''
    ? path.resolve(path.dirname(transcriptPath), '..', '..', '..', '..')
    : DefaultRoot;
}

function transcriptsOf(root, conversation) {
  const logs = path.join(root, 'brain', conversation, '.system_generated', 'logs');
  return [path.join(logs, 'transcript_full.jsonl'), path.join(logs, 'transcript.jsonl')];
}

function readHead(file, limit) {
  let handle;
  try {
    handle = fs.openSync(file, 'r');
  } catch {
    return null; // not written yet, or not this layout
  }
  try {
    const buffer = Buffer.alloc(Math.min(fs.fstatSync(handle).size, limit));
    fs.readSync(handle, buffer, 0, buffer.length, 0);
    return buffer.toString('utf8');
  } finally {
    fs.closeSync(handle);
  }
}

/** Who opened a transcript: `user` (the person's input), `system` (a system message: a subagent), or null. */
function openedBy(file) {
  const head = readHead(file, HeadBytes);
  if (head === null || !head.includes('\n')) {
    return null; // no file, or its first line is still being written
  }
  let record;
  try {
    record = JSON.parse(head.slice(0, head.indexOf('\n')));
  } catch {
    return null;
  }
  if (record?.type === 'USER_INPUT') {
    return 'user';
  }
  return record?.type === 'SYSTEM_MESSAGE' || record?.source === 'SYSTEM' ? 'system' : null;
}

/** Undoes the escapes of a protobuf text format string (octal bytes, \n, \", \\ and the like) as UTF-8. */
function unescapeText(text) {
  const bytes = [];
  for (let i = 0; i < text.length; i++) {
    const char = text[i];
    if (char !== '\\') {
      bytes.push(...Buffer.from(char, 'utf8'));
      continue;
    }
    const next = text[++i] ?? '';
    const octal = /^[0-7]{1,3}/.exec(text.slice(i));
    if (octal) {
      bytes.push(parseInt(octal[0], 8) & 0xff);
      i += octal[0].length - 1;
    } else {
      bytes.push(...Buffer.from({ n: '\n', t: '\t', r: '\r' }[next] ?? next, 'utf8'));
    }
  }
  return Buffer.from(bytes).toString('utf8');
}

/** The annotation of a conversation: null when there is no file, else `{ title }` (null when it has none). */
function readAnnotation(root, conversation) {
  const text = readHead(path.join(root, 'annotations', `${conversation}.pbtxt`), HeadBytes);
  if (text === null) {
    return null;
  }
  const match = /(?:^|\s)title:"((?:[^"\\]|\\.)*)"/.exec(text);
  const title = match ? unescapeText(match[1]).replace(/\s+/g, ' ').trim() : '';
  return { title: title === '' ? null : title };
}

/**
 * What Antigravity's files say about a conversation: `subagent` (true, false, or null when they cannot tell yet) and
 * its `title` (or null). `root` is the Antigravity folder (default: the one of the transcript, else
 * ~/.gemini/antigravity).
 */
function conversationFacts(conversation, transcriptPath, root = rootOf(transcriptPath)) {
  const files = [...(transcriptPath ? [path.join(path.dirname(transcriptPath), 'transcript_full.jsonl')] : [])];
  files.push(...transcriptsOf(root, conversation));
  const opened = files.map(openedBy).find((who) => who !== null) ?? null;
  const annotation = readAnnotation(root, conversation);
  const subagent =
    opened === 'system' ? true : opened === 'user' ? false : annotation && !annotation.title ? true : null;
  return { subagent, title: annotation?.title ?? null };
}

module.exports = { DefaultRoot, rootOf, openedBy, unescapeText, readAnnotation, conversationFacts };
