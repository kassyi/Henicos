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
// Modules (chat-status/): hook.js (--hook), events.js (the events), questions.js (question extraction), store.js (the
// files and listChats), open-questions.js (the heuristic), summary.js (Claude Haiku), summarizer.js (its process),
// antigravity.js (subagents and titles of Antigravity), self-test*.js.
//
// Open questions: at each Stop the hook records the questions the chat leaves open for the person by a heuristic (kept
// until a new prompt, then a question mark in the reply), and starts chat-status/summarizer.js detached, which asks
// Claude Haiku through the Claude Code CLI (the person's subscription) for the open questions after the turn and a
// title, and writes them while the chat still waits in that state. A chat that only reported is idle. The CLI:
// GSHARP_CHAT_STATUS_CLAUDE, else CLAUDE_CODE_EXECPATH of the hook (kept in claude-executable.txt for the hooks without
// it), else the newest Claude Code extension of Antigravity, VS Code or VS Code Insiders. GSHARP_CHAT_STATUS_SUMMARY=0
// turns Haiku off; without a CLI the heuristic decides alone. Antigravity subagents are never recorded.
//
// Events:
//   Claude Code   UserPromptSubmit                   working
//                 Stop                               waiting for a reply; the text is the last paragraph with a
//                                                    question mark of the last assistant text (else its last
//                                                    paragraph), with asks: whether it has one
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

const { runHook } = require('./chat-status/hook.js');
const store = require('./chat-status/store.js');
const { extractQuestion, extractReply, MaxQuestionLength } = require('./chat-status/questions.js');
const { interpret } = require('./chat-status/events.js');
const summary = require('./chat-status/summary.js');
const { openOf } = require('./chat-status/open-questions.js');

const usage = 'Usage: chat-status.js (--hook | --list [--json] | --self-test | --help)';

function printList(json) {
  const chats = store.listChats();
  if (json) {
    process.stdout.write(`${JSON.stringify(chats, null, 2)}\n`);
    return;
  }
  const now = Date.now();
  for (const chat of chats) {
    const minutes = Math.round((now - chat.since) / 60000);
    const kind = chat.kind === 'reply' ? (summary.asksPerson(chat) ? 'reply, asks' : 'reply, reports') : chat.kind;
    const state = chat.state === 'waiting' ? `waiting (${kind})` : chat.state;
    // a reply: its newest open question (marked with ≈ when Haiku judged it); else the recorded text
    const open = chat.kind === 'reply' ? openOf(chat) : [];
    const judged = chat.askVerdict === 'haiku' && chat.summaryOf === chat.since ? '≈ ' : '';
    const more = open.length > 1 ? ` (+${open.length - 1})` : '';
    const shown = open.length > 0 ? `${judged}${open[0].text}${more}` : chat.question;
    const text = shown ? Array.from(shown.replace(/\s+/g, ' ')) : [];
    const question = text.length === 0 ? '' : `  ${text.length > 80 ? '…' : ''}${text.slice(-80).join('')}`;
    const named = chat.agentTitle || chat.title;
    const title = named ? `\t${named}` : '';
    process.stdout.write(`${chat.agent}\t${chat.worktree ?? '?'}${title}\t${state}\t${minutes}m${question}\n`);
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
  StaleMs: store.StaleMs,
  MaxQuestionLength,
  extractQuestion,
  extractReply,
  interpret,
  applyPayload: store.applyPayload,
  writeAtomically: store.writeAtomically,
  readMainCheckout: store.readMainCheckout,
  listChats: store.listChats,
  /** Whether summaries are made (the live view's help): `{ on, executable, source }` or `{ on: false, reason }`. */
  summaryStatus: (options = {}) => summary.summaryStatus({ directory: store.resolveStatusDirectory(), ...options }),
  chatFileName: store.chatFileName,
};

if (require.main === module) {
  main();
}
