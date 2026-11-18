#!/usr/bin/env node
'use strict';

// PreToolUse hook of Claude Code (Bash and PowerShell, .claude/settings.json) and Antigravity (run_command,
// .agents/hooks.json): denies a build or test command that does not go through the job queue
// (scripts/human/job-queue.js), so that agents running side by side do not oversubscribe the machine. The input
// format tells the two apart. A command that may run produces no output: an Antigravity "allow" would skip the
// user's permission prompt. In Claude Code, a plain build or test command (segments joined by && only, no wrapper)
// is not denied but handed back queued, `node scripts/human/job-queue.js run [--gpu] --shell <s> -- '<command>'`
// (updatedInput, with no permission decision, so the user's permission prompt still runs); --gpu comes from the
// dialect of the named test projects and from the queue options of a just recipe, and a command whose GPU use cannot
// be told is denied with a sentence saying that it may need --gpu.
//
// Modules (check-queued-command/):
//   shell-reader.js       reads the command in the tool's dialect (bash, PowerShell, cmd): quotes, segments at &&,
//                         ||, ;, |, & and line breaks, $( ) / @( ) substitutions, heredocs and here-strings,
//                         assignments, launch prefixes and shell wrappers
//   command-rules.js      what is denied and allowed (dispatches to rules/ handlers)
//   rules/                per-program rule handlers (wrappers.js, dotnet.js, just.js, node-scripts.js)
//   queue-rewrite.js      which denied commands are queued instead, with which queue options
//   hook.js               the denial message, the hook input and output
//   project-henicos.js    Henicos-specific recipes, scripts, verbs, runners, queue path, test lookup, and wording
//   project-config.js     the shape of that config, checked when the engine is created (a missing key throws)
//   self-test.js          --self-test (self-test-rewrite.js: the rewrite)
// Out of reach: a command held in a variable and run later ($c = 'dotnet test'; cmd /c $c) and the contents of a
// script file the command runs.
// A hook that fails stops every command in Antigravity, which treats any non-zero exit code as a denial, so --hook
// allows the command (no output, exit code 0) when a module fails to load or an exception escapes while judging, and
// appends a line to tmp/hook-errors.log of the main checkout. --text and --self-test still fail loudly.
//
// Usage:
//   node scripts/agent/check-queued-command.js --hook                                reads the hook JSON on stdin
//   node scripts/agent/check-queued-command.js --text "<command>" [--dialect bash|pwsh|cmd]
//                                                       prints the decision (exit 1 when denied; default dialect bash)
//   node scripts/agent/check-queued-command.js --self-test
//   require('./check-queued-command.js').check(command, dialect) returns the reasons (empty when allowed)
//   require('./check-queued-command.js').hookAnswer(payload) returns the hook JSON response (or null when allowed)

const fs = require('fs');
const path = require('path');

let loaded = null;

/** The rules and the hook adapter, loaded on first use so that --hook can catch a module that fails to load. */
function engine() {
  if (loaded === null) {
    const projectHenicos = require('./check-queued-command/project-henicos.js');
    const rules = require('./check-queued-command/command-rules.js').createRules(projectHenicos);
    const hook = require('./check-queued-command/hook.js').createHook(projectHenicos, rules);
    loaded = { check: rules.check, ...hook };
  }
  return loaded;
}

/**
 * Appends one line about a hook failure to tmp/hook-errors.log of the main checkout (of this script's checkout when
 * the lookup fails). Never throws: a hook that fails stops every command in Antigravity.
 */
function recordHookError(error) {
  try {
    const root = path.resolve(__dirname, '..', '..');
    let base = root;
    try {
      base = require('../human/job-queue/lib/main-checkout.js').mainCheckout(root, root);
    } catch (lookupError) {
      // A checkout without the lookup module: the log goes next to this script's checkout instead.
      base = root;
    }
    const file = path.join(base, 'tmp', 'hook-errors.log');
    const text = error instanceof Error ? error.stack || error.message : String(error);
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.appendFileSync(file, `${new Date().toISOString()} check-queued-command --hook: ${text.replace(/\s+/g, ' ')}\n`);
  } catch (writeError) {
    // Nowhere left to report it; the command must still run (see runHookFailingOpen).
    return;
  }
}

/**
 * --hook: a hook that exits with a non-zero code or throws stops the command in Antigravity (Claude Code runs it), so
 * a failure to load or to judge allows the command, with no output and exit code 0, and is recorded instead.
 */
function runHookFailingOpen() {
  let hook;
  try {
    hook = engine();
  } catch (error) {
    recordHookError(error);
    process.exit(0);
  }
  hook.runHook(recordHookError);
}

const usage = 'Usage: check-queued-command.js (--hook | --text <command> [--dialect bash|pwsh|cmd] | --self-test)';

function main() {
  const args = process.argv.slice(2);
  const dialects = ['bash', 'pwsh', 'cmd'];
  if (args[0] === '--hook') {
    runHookFailingOpen();
  } else if (args[0] === '--text' && (args.length === 2 || (args.length === 4 && args[2] === '--dialect'))) {
    const kind = args.length === 4 ? args[3] : 'bash';
    if (!dialects.includes(kind)) {
      process.stderr.write(`--dialect needs ${dialects.join(', ')}.\n${usage}\n`);
      process.exit(2);
    }
    const reasons = engine().check(args[1], kind);
    process.stdout.write(reasons.length === 0 ? 'allowed\n' : `${engine().denialText(reasons)}\n`);
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

module.exports = {
  check: (command, kind) => engine().check(command, kind),
  hookAnswer: (payload) => engine().hookAnswer(payload),
};

if (require.main === module) {
  main();
}
