'use strict';

// The --hook mode of chat-status.js: reads one payload on stdin, applies it, prints what the agent expects, and always
// exits 0 (also on a damaged payload or a file error, which go to errors.log).

const { resolveStatusDirectory, applyPayload, logError } = require('./store.js');

const StdinTimeoutMs = 3000;

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

module.exports = { runHook };
