#!/usr/bin/env node
'use strict';

// The summarizer process of chat-status.js, started detached by the hook when a chat enters a wait:
//   node scripts/human/chat-status/summarizer.js <chat file> <since> <Claude Code CLI>
// It asks Claude Haiku through the CLI for the questions the chat leaves open for the person and its title, and writes
// them into the chat file only while the chat still waits in the state that began at <since> (chat-status/summary.js).
// Failures go to errors.log next to the chat file; it prints nothing.

const path = require('path');
const { ChildVariable, runSummarizer } = require('./summary.js');
const { logError } = require('./store.js');

const [file, since, executable] = process.argv.slice(2);
if (process.env[ChildVariable] || !file || !Number.isFinite(Number(since)) || !executable) {
  process.exitCode = 2; // started by a summarizer's own session, or by hand without the three arguments
} else {
  try {
    runSummarizer(path.resolve(file), Number(since), executable);
  } catch (error) {
    logError(path.dirname(path.resolve(file)), error);
  }
}
