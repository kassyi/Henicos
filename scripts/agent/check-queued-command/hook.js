'use strict';

// The hook adapter of check-queued-command.js: formats the denial message and turns one Claude Code or Antigravity
// hook payload into its JSON response (or null when the command may run as it is).
//
// Usage: require('./hook.js').createHook(project, rules) returns { denialText, hookAnswer, runHook };
// hookAnswer(payload) returns the answer to one parsed hook input (null when allowed; a Claude Code command that
// queue-rewrite.js can queue comes back as updatedInput); denialText(reasons) formats the denial message;
// runHook(onError) answers on stdin and hands an exception while judging to onError.

const { createRules } = require('./command-rules.js');
const { createRewriter } = require('./queue-rewrite.js');

/** The parsed hook input; throws an error that says the input was not JSON. */
function parseInput(text) {
  try {
    return JSON.parse(text);
  } catch (error) {
    throw new Error(`invalid hook input: ${error.message}`);
  }
}

/**
 * Creates the hook adapter bound to repository config `project` and rule evaluator `rules`; createRewriter checks the
 * shape of `project` (project-config.js) and throws first.
 */
function createHook(project, rules = createRules(project)) {
  const { check } = rules;
  const { planRewrite } = createRewriter(project, rules);
  const { denialText, additionalContext } = project.messages;

  /**
   * The answer to one parsed hook input, or null when the command may run as it is.
   * Antigravity ({"toolCall": {"name": "run_command", "args": {"CommandLine": ...}}}, run in PowerShell): a denial.
   * Claude Code ({"tool_name": "Bash" | "PowerShell", "tool_input": {"command": ...}, "cwd": ...}): the command queued
   * through updatedInput when queue-rewrite.js can rewrite it, else a denial. A rewrite sets no permissionDecision, so
   * the normal permission flow still runs on the rewritten command.
   */
  function hookAnswer(payload) {
    const antigravity = payload.toolCall !== undefined;
    const toolInput = antigravity ? {} : payload.tool_input || {};
    const command = antigravity
      ? String((payload.toolCall.args && payload.toolCall.args.CommandLine) || '')
      : String(toolInput.command || '');
    const kind = antigravity || payload.tool_name === 'PowerShell' ? 'pwsh' : 'bash';
    const reasons = check(command, kind);
    if (reasons.length === 0) {
      return null;
    }
    if (antigravity) {
      return { decision: 'deny', reason: denialText(reasons) };
    }
    const plan = planRewrite(command, kind, String(payload.cwd || process.cwd()));
    if (plan !== null && plan.command !== undefined) {
      return {
        hookSpecificOutput: {
          hookEventName: 'PreToolUse',
          updatedInput: { ...toolInput, command: plan.command },
          additionalContext: additionalContext(plan),
        },
      };
    }
    const note = plan === null ? '' : `\n${plan.note}`;
    return {
      hookSpecificOutput: {
        hookEventName: 'PreToolUse',
        permissionDecision: 'deny',
        permissionDecisionReason: `${denialText(reasons)}${note}`,
      },
    };
  }

  /**
   * Answers the hook input on stdin. Input that is not JSON, an exception while judging and a stream error go to
   * `onError` and the command is allowed (no output, exit code 0): Antigravity stops the command of a hook that exits
   * with a non-zero code.
   */
  function runHook(onError) {
    const allow = (error) => {
      onError(error);
      process.exit(0);
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
