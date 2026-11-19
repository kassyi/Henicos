#!/usr/bin/env node
'use strict';

// PreToolUse hook that denies a build or test command unless it goes through the configured job queue
// (job-queue.config.json), so that agents running side by side do not oversubscribe the machine. Agent hook-io.js
// plugins interpret payloads and format answers. A command that may run produces no output so the caller's normal
// permission prompt still runs. For rewrite-capable agents, a plain denied command (segments joined by && only, no
// wrapper) can be handed back queued via the agent plugin's rewrite formatter; --gpu and build/test splitting come
// from enabled plugins' hook-rules.js modules, and a command whose GPU use cannot be told is denied with a sentence
// saying that it may need --gpu.
//
// Modules (check-queued-command/):
//   shell-reader.js       reads the command in the tool's dialect (bash, PowerShell, cmd): quotes, segments at &&,
//                         ||, ;, |, & and line breaks, $( ) / @( ) substitutions, heredocs and here-strings,
//                         assignments, launch prefixes and shell wrappers
//   command-rules.js      what is denied and allowed (dispatches to wrappers, plugin rules, runners, node-scripts)
//   rules/                built-in rule helpers (wrappers.js, node-scripts.js)
//   queue-rewrite.js      which denied commands are queued instead, delegating GPU/options/split to plugins
//   hook.js               denial wording and hook-io.js dispatch
//   plugin-loader.js      discovers job-queue.config.json and loads plugin hook-rules.js / hook-io.js modules
//   self-test.js          --self-test (self-test-rewrite.js: the rewrite; self-test-failures.js: broken plugins)
// Out of reach: a command held in a variable and run later ($c = 'buildtool test'; cmd /c $c) and the contents of a
// script file the command runs.
// A hook that fails can stop every command in agents that treat a non-zero exit code as a denial, so --hook allows
// the command (no output, exit code 0) when config/plugin loading fails, the input is not JSON, an exception escapes
// while judging or a stream fails, and appends a line to tmp/hook-errors.log of the main checkout. --text and
// --self-test fail loudly in strict mode.
//
// Usage:
//   node scripts/agent/check-queued-command.js --hook                                reads the hook JSON on stdin
//   node scripts/agent/check-queued-command.js --text "<command>" [--dialect bash|pwsh|cmd]
//                                                       prints the decision (exit 1 when denied; default dialect bash)
//   node scripts/agent/check-queued-command.js --self-test
//   require('./check-queued-command.js').check(command, dialect) returns the reasons (empty when allowed)
//   require('./check-queued-command.js').hookAnswer(payload) returns the hook JSON response (or null when allowed)

const fs = require('node:fs');
const path = require('node:path');

let strictCached = null;
let strictCachedKey = null;

/** Creates an isolated hook engine instance without touching the cached strict singleton. */
function createEngine({ configPath, tolerant = false, onError = null } = {}) {
  const { loadHookConfig } = require('./check-queued-command/plugin-loader.js');
  const { createRules } = require('./check-queued-command/command-rules.js');
  const { createHook } = require('./check-queued-command/hook.js');
  const engineConfig = loadHookConfig({ configPath, tolerant, onError });
  const rules = createRules(engineConfig, { tolerant, onError });
  const hook = createHook(engineConfig, rules, { tolerant, onError });
  return {
    check: rules.check,
    checkDetailed: rules.checkDetailed,
    isQueueScript: rules.isQueueScript,
    ...hook,
  };
}

/** Cached strict engine keyed by JOB_QUEUE_CONFIG so repeated exports calls stay fast while respecting overrides. */
function engine() {
  const key = process.env.JOB_QUEUE_CONFIG || '';
  if (strictCached === null || strictCachedKey !== key) {
    strictCached = createEngine({ tolerant: false });
    strictCachedKey = key;
  }
  return strictCached;
}

/**
 * Appends one line about a hook failure to tmp/hook-errors.log of the main checkout (of this script's checkout when
 * the lookup fails). Never throws: a hook failure must not stop subsequent commands.
 */
function recordHookError(error) {
  try {
    const root = path.resolve(__dirname, '..', '..');
    let base = root;
    try {
      base = require('../human/job-queue/lib/main-checkout.js').mainCheckout(root, root);
    } catch (_lookupError) {
      // A checkout without the lookup module: the log goes next to this script's checkout instead.
      base = root;
    }
    const file = path.join(base, 'tmp', 'hook-errors.log');
    const text = error instanceof Error ? error.stack || error.message : String(error);
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.appendFileSync(file, `${new Date().toISOString()} check-queued-command --hook: ${text.replace(/\s+/g, ' ')}\n`);
  } catch (_writeError) {
    // Nowhere left to report it; the command must still run (see runHookFailingOpen).
    return;
  }
}

/**
 * --hook: a failure to load config/plugins or to judge allows the command, with no output and exit code 0, and is
 * recorded instead.
 */
function runHookFailingOpen() {
  // A broken rule fails once per segment and per pass (judgement, rewrite): record each message once per command
  const recorded = new Set();
  const onError = (error) => {
    const key = error instanceof Error ? error.message : String(error);
    if (!recorded.has(key)) {
      recorded.add(key);
      recordHookError(error);
    }
  };
  let hook;
  try {
    hook = createEngine({ tolerant: true, onError });
  } catch (error) {
    onError(error);
    process.exit(0);
  }
  hook.runHook(onError);
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
    require('./check-queued-command/self-test.js').selfTest(createEngine);
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
  createEngine,
};

if (require.main === module) {
  main();
}
