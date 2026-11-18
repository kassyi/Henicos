'use strict';

// Node test-script and gpu-lock.js command rules for check-queued-command.js.
//
// Usage: require('./rules/node-scripts.js').nodeScriptRules(project) returns [directNodeScript, nodeScript] (or [] when
// no script names are configured), where each entry implements { matches(program), reason(reader, program) }.

const { unquote } = require('../shell-reader.js');

/** Builds node script command rules for the given repository config. */
function nodeScriptRules(project = {}) {
  const config = project.nodeScripts || {};
  const names = Array.isArray(config.names) ? config.names.filter((name) => name !== '') : [];
  if (names.length === 0) {
    return [];
  }
  const escaped = names.map((name) => name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
  const scriptsPattern = new RegExp(`(^|[\\\\/])(${escaped.join('|')})\\.js$`);
  const noHelpExemption = config.noHelpExemption || null;
  const exemptOptions = Array.isArray(config.exemptOptions) ? config.exemptOptions : [];

  const directNodeScript = {
    matches: ({ list }) => scriptsPattern.test(unquote(list[0])),
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
      // scripts matching noHelpExemption build even for --help.
      const ownArgs = args.slice(scriptIndex + 1);
      const passOn = ownArgs.indexOf('--');
      const helpExempt =
        (!noHelpExemption || !noHelpExemption.test(script)) &&
        (passOn < 0 ? ownArgs : ownArgs.slice(0, passOn)).some((arg) =>
          ['--help', '-h', '--self-test', '--dry-run'].includes(arg),
        );
      const optionExempt = exemptOptions.some(
        ({ script: pattern, option }) => pattern.test(script) && ownArgs.includes(option),
      );
      const helper =
        args.slice(0, Math.max(scriptIndex, 0)).some((arg) => arg === '--check' || arg === '-c') ||
        helpExempt ||
        optionExempt;
      if (scriptsPattern.test(script) && !helper) {
        const file = script.replace(/^.*[\\/]/, '');
        return { tool: 'node-script', verb: file, text: file };
      }
      return null;
    },
  };

  return [directNodeScript, nodeScript];
}

module.exports = { nodeScriptRules };
