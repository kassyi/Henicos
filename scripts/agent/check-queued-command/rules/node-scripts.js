'use strict';

// Node test-script and gpu-lock.js command rules for check-queued-command.js.
//
// Usage: require('./rules/node-scripts.js') returns [directNodeScript, nodeScript], where each entry implements
// { matches(program), reason(reader, program) }.

const { unquote } = require('../shell-reader.js');

/** The long test scripts and gpu-lock.js, in any folder (gpu-lock.js was in scripts/agent/ in older checkouts). */
const NodeScriptNames = [
  'gpu-lock',
  'test-stability',
  'long-checks',
  'test-memory',
  'test-sanitized',
  'mutate',
  'coverage-diff',
  'fuzz-differential',
];

const NodeScripts = new RegExp(`(^|[\\\\/])(${NodeScriptNames.join('|')})\\.js$`);

const directNodeScript = {
  matches: ({ list }) => NodeScripts.test(unquote(list[0])),
  reason: (_reader, { list }) => {
    const file = unquote(list[0]).replace(/^.*[\\/]/, ''); // ./scripts/human/long-checks.js run directly
    return { tool: 'node-script', verb: file, text: file };
  },
};

const nodeScript = {
  matches: ({ name }) => name === 'node',
  reason: (_reader, { args }) => {
    const scriptIndex = args.findIndex((arg) => !arg.startsWith('-'));
    const script = scriptIndex < 0 ? '' : args[scriptIndex];
    // node --check <script>, <script> --help / --self-test / --dry-run: no build, no test run. Only the script's
    // own options count, not those after `--` that it passes on (test-stability.js -- --dry-run still builds), and
    // the fuzzer builds tools/fuzz-diff even for --help.
    const ownArgs = args.slice(scriptIndex + 1);
    const passOn = ownArgs.indexOf('--');
    const helper =
      args.slice(0, Math.max(scriptIndex, 0)).some((arg) => arg === '--check' || arg === '-c') ||
      (!/fuzz-differential\.js$/.test(script) &&
        (passOn < 0 ? ownArgs : ownArgs.slice(0, passOn)).some((arg) =>
          ['--help', '-h', '--self-test', '--dry-run'].includes(arg),
        )) ||
      // gpu-lock.js --lock-path <dir> takes a private lock, for tests of the script itself
      (/gpu-lock\.js$/.test(script) && ownArgs.includes('--lock-path'));
    if (NodeScripts.test(script) && !helper) {
      const file = script.replace(/^.*[\\/]/, '');
      return { tool: 'node-script', verb: file, text: file };
    }
    return null;
  },
};

module.exports = [directNodeScript, nodeScript];
