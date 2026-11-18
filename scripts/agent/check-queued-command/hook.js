'use strict';

// The hook adapter of check-queued-command.js: formats the denial message and turns one Claude Code or Antigravity
// hook payload into its JSON response (or null when the command may run as it is).
//
// Usage: require('./hook.js').createHook(project, rules) returns { denialText, hookAnswer, runHook };
// hookAnswer(payload) returns the answer to one parsed hook input (null when allowed; a Claude Code command that
// queue-rewrite.js can queue comes back as updatedInput); denialText(reasons) formats the denial message; runHook()
// answers on stdin.

const { createRules } = require('./command-rules.js');
const { createRewriter } = require('./queue-rewrite.js');

/** Creates the hook adapter bound to repository config `project` and rule evaluator `rules`. */
function createHook(project = {}, rules = createRules(project)) {
  const { check } = rules;
  const { planRewrite } = createRewriter(project, rules);
  const messages = project.messages || {};
  const denialText =
    typeof messages.denialText === 'function'
      ? messages.denialText
      : (reasons) => `${reasons.join(', ')} must run through the job queue.`;
  const additionalContext =
    typeof messages.additionalContext === 'function'
      ? messages.additionalContext
      : (plan) => `Queued as: ${plan.command}`;

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

  function runHook() {
    let input = '';
    process.stdin.setEncoding('utf8');
    process.stdin.on('data', (chunk) => (input += chunk));
    process.stdin.on('end', () => {
      let payload;
      try {
        payload = JSON.parse(input);
      } catch (error) {
        process.stderr.write(`check-queued-command --hook: invalid hook input: ${error.message}\n`);
        process.exit(1);
      }
      const answer = hookAnswer(payload);
      if (answer !== null) {
        process.stdout.write(JSON.stringify(answer));
      }
      process.exit(0);
    });
  }

  return { denialText, hookAnswer, runHook };
}

module.exports = { createHook };
