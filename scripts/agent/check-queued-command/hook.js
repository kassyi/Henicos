'use strict';

// The hook adapter of check-queued-command.js: formats the denial message and turns one Claude Code or Antigravity
// hook payload into its JSON response (or null when the command may run as it is).
//
// Usage: require('./hook.js').hookAnswer(payload) returns the answer to one parsed hook input (null when allowed; a
// Claude Code command that queue-rewrite.js can queue comes back as updatedInput); denialText(reasons) formats the
// denial message; runHook() answers one hook call on stdin.

const { check } = require('./command-rules.js');
const { planRewrite } = require('./queue-rewrite.js');

function denialText(reasons) {
  return (
    `${reasons.join(', ')} must run through the job queue, ` +
    'so that parallel agents do not oversubscribe the machine:\n' +
    '  node scripts/human/job-queue.js run -- "<command>"\n' +
    '  node scripts/human/job-queue.js run --gpu -- "<command>"   (uses the GPU; replaces gpu-lock.js)\n' +
    'The queue waits for a free slot and then runs the command here. ' +
    'It prints `queued #<id>` at once: keep the id and ' +
    'read the expected start and end with `node scripts/human/job-queue.js status <id> --json` (`status` lists every ' +
    'job); `node scripts/human/job-queue.js wait <id>` run in the background calls you when it ends ' +
    '(exit 0 succeeded). Steps that belong together go into one job as one quoted string, ' +
    '`run -- "dotnet build X && dotnet test X --no-build"`: it waits once, and nothing runs between the steps; ' +
    'with --gpu, queue the build as a job of its own without --gpu, so that the GPU is held only for the tests. ' +
    'Long waits: run it in the background. Text that only names such a command (a message, a search ' +
    'pattern) passes when it is quoted. ' +
    'The queued command runs in the shell you call `run` from (Git Bash, PowerShell; ' +
    '`--shell bash|pwsh|cmd|sh` to choose), so write it as you would here.'
  );
}

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
        additionalContext:
          `check-queued-command.js sent this command through the job queue as: ${plan.command} . ` +
          (plan.split
            ? 'The build was queued as a job of its own, without --gpu, so that the GPU is held only for the ' +
              'tests: two `queued #<id>` lines, the build first; the tests run only when the build succeeded. '
            : '') +
          'Its output starts with `queued #<id>`; `node scripts/human/job-queue.js status <id>` and ' +
          '`node scripts/human/job-queue.js wait <id>` follow the job.',
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

module.exports = { denialText, hookAnswer, runHook };
