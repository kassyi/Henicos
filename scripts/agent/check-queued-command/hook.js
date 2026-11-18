'use strict';

// The hook adapter of check-queued-command.js: formats the denial message and dispatches parsed hook payloads through
// enabled agent `hook-io.js` plugins (returning null when the command may run as it is).
//
// Usage: require('./hook.js').createHook(engineConfig, rules, opts) returns { denialText, hookAnswer, runHook };
// hookAnswer(payload) returns the answer to one parsed hook input (null when allowed; a command that queue-rewrite.js
// can queue comes back via the agent plugin's rewrite formatter); denialText(reasons) formats the denial message;
// runHook(onError) answers on stdin and hands unreadable input, judgement exceptions, and stream errors to onError.

const { createRules } = require('./command-rules.js');
const { createRewriter, selectMessages } = require('./queue-rewrite.js');

const ValidDialects = ['bash', 'pwsh', 'cmd'];

function isPlainObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

/** The parsed hook input; throws an error that says the input was not JSON. */
function parseInput(text) {
  try {
    return JSON.parse(text);
  } catch (error) {
    throw new Error(`invalid hook input: ${error.message}`);
  }
}

/** Creates the hook adapter bound to `engineConfig` and rule evaluator `rules`. */
function createHook(engineConfig, rules = createRules(engineConfig), { tolerant = false, onError = null } = {}) {
  const reportError = (error) => {
    if (!tolerant) {
      throw error;
    }
    if (typeof onError === 'function') {
      onError(error);
    }
  };
  const { check } = rules;
  const { planRewrite } = createRewriter(engineConfig, rules, { tolerant, onError });
  const { denialText, additionalContext } = selectMessages(engineConfig);

  /**
   * The answer to one parsed hook input, or null when the command may run as it is (or when no enabled `hook-io.js`
   * plugin recognizes the payload).
   */
  function hookAnswer(payload) {
    for (const io of engineConfig.hookIo) {
      let readResult;
      try {
        readResult = io.module.read(payload, io.options);
        if (readResult === null || readResult === undefined) {
          continue;
        }
        if (
          !isPlainObject(readResult) ||
          typeof readResult.command !== 'string' ||
          !ValidDialects.includes(readResult.kind)
        ) {
          throw new Error(`check-queued-command: plugin ${io.name} hook-io.js read() returned an invalid shape`);
        }
      } catch (error) {
        reportError(error);
        continue;
      }

      const reasons = check(readResult.command, readResult.kind);
      if (reasons.length === 0) {
        return null;
      }

      if (readResult.canRewrite && typeof io.module.rewrite === 'function') {
        const cwd = String(readResult.cwd || process.cwd());
        const plan = planRewrite(readResult.command, readResult.kind, cwd);
        if (plan !== null && plan.command !== undefined) {
          try {
            const rewritten = io.module.rewrite(readResult, plan.command, additionalContext(plan));
            if (rewritten !== null && rewritten !== undefined) {
              return rewritten;
            }
          } catch (error) {
            reportError(error);
            return null;
          }
        }
        const note = plan === null ? '' : `\n${plan.note}`;
        try {
          return io.module.deny(readResult, `${denialText(reasons)}${note}`);
        } catch (error) {
          reportError(error);
          return null;
        }
      }

      try {
        return io.module.deny(readResult, denialText(reasons));
      } catch (error) {
        reportError(error);
        return null;
      }
    }
    return null;
  }

  /**
   * Answers the hook input on stdin. Input that is not JSON, an exception while judging and a stream error go to
   * `onError` and the command is allowed (no output, exit code 0), so an agent that treats a non-zero exit code as a
   * denial never blocks on a hook bug or unreadable input.
   */
  function runHook(onError) {
    const allow = (error) => {
      try {
        onError(error);
      } finally {
        process.exit(0);
      }
    };
    let input = '';
    process.stdin.setEncoding('utf8');
    process.stdin.on('error', allow);
    process.stdout.on('error', allow);
    process.stdin.on('data', (chunk) => (input += chunk));
    process.stdin.on('end', () => {
      let output = '';
      try {
        const answer = hookAnswer(parseInput(input));
        output = answer === null ? '' : JSON.stringify(answer);
      } catch (error) {
        allow(error);
      }
      if (output !== '') {
        process.stdout.write(output, () => process.exit(0));
      } else {
        process.exit(0);
      }
    });
  }

  return { denialText, hookAnswer, runHook };
}

module.exports = { createHook };
