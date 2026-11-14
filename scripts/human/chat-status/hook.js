'use strict';

// The --hook mode of chat-status.js: reads one payload on stdin, applies it, prints what the agent expects, and always
// exits 0 (also on a damaged payload or a file error, which go to errors.log). A record that enters a wait starts the
// summarizer detached (chat-status/summary.js); the hook never waits for it. In the Claude Code session the summarizer
// itself starts (GSHARP_CHAT_STATUS_CHILD set), the hook does nothing at all.

const { resolveStatusDirectory, applyPayload, logError } = require('./store.js');
const { ChildVariable, startSummarizer } = require('./summary.js');

const StdinTimeoutMs = 3000;

/** What to print when the payload could not be applied: an Antigravity Stop / PreInvocation expects a JSON object. */
function fallbackOutput(input) {
  return /"hookEventName"\s*:\s*"(?:Stop|PreInvocation)"/.test(input) ? '{}' : '';
}

/** The hook: reads the payload, applies it, prints what the agent expects, and always exits 0. */
function runHook() {
  if (process.env[ChildVariable]) {
    process.exitCode = 0; // the summarizer's own CLI session: no chat to record, and no summarizer of a summarizer
    return;
  }
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
      const summarize = (file, record) => {
        try {
          startSummarizer(file, record);
        } catch (error) {
          logError(directory, error);
        }
      };
      output = payload === null ? fallbackOutput(input) : applyPayload(payload, directory, Date.now(), summarize);
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

module.exports = { runHook };
