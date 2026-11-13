#!/usr/bin/env node
'use strict';

// PreToolUse hook of Claude Code (Bash and PowerShell, .claude/settings.json) and Antigravity (run_command,
// .agents/hooks.json): denies a build or test command that does not go through the job queue
// (scripts/human/job-queue.js), so that agents running side by side do not oversubscribe the machine. The input
// format tells the two apart. A command that may run produces no output: an Antigravity "allow" would skip the
// user's permission prompt.
//
// Modules (check-queued-command/):
//   shell-reader.js   reads the command in the tool's dialect (bash, PowerShell, cmd): quotes, segments at &&, ||, ;,
//                     |, & and line breaks, $( ) / @( ) substitutions, heredocs and here-strings, assignments, launch
//                     prefixes and shell wrappers
//   command-rules.js  what is denied and allowed, the denial message, the hook input and output
//   self-test.js      --self-test
// Out of reach: a command held in a variable and run later ($c = 'dotnet test'; cmd /c $c) and the contents of a
// script file the command runs.
//
// Usage:
//   node scripts/agent/check-queued-command.js --hook                                reads the hook JSON on stdin
//   node scripts/agent/check-queued-command.js --text "<command>" [--dialect bash|pwsh|cmd]
//                                                       prints the decision (exit 1 when denied; default dialect bash)
//   node scripts/agent/check-queued-command.js --self-test
//   require('./check-queued-command.js').check(command, dialect) returns the reasons (empty when allowed)

const { check, denialText, runHook } = require('./check-queued-command/command-rules.js');

const usage = 'Usage: check-queued-command.js (--hook | --text <command> [--dialect bash|pwsh|cmd] | --self-test)';

function main() {
  const args = process.argv.slice(2);
  const dialects = ['bash', 'pwsh', 'cmd'];
  if (args[0] === '--hook') {
    runHook();
  } else if (args[0] === '--text' && (args.length === 2 || (args.length === 4 && args[2] === '--dialect'))) {
    const kind = args.length === 4 ? args[3] : 'bash';
    if (!dialects.includes(kind)) {
      process.stderr.write(`--dialect needs ${dialects.join(', ')}.\n${usage}\n`);
      process.exit(2);
    }
    const reasons = check(args[1], kind);
    process.stdout.write(reasons.length === 0 ? 'allowed\n' : `${denialText(reasons)}\n`);
    process.exit(reasons.length === 0 ? 0 : 1);
  } else if (args[0] === '--self-test') {
    require('./check-queued-command/self-test.js').selfTest();
  } else if (args[0] === '--help' || args[0] === '-h') {
    process.stdout.write(`${usage}\n`);
  } else {
    process.stderr.write(`${usage}\n`);
    process.exit(2);
  }
}

module.exports = { check };

if (require.main === module) {
  main();
}
