'use strict';

// The command rules of check-queued-command.js: which commands must go through the job queue.
//
// A segment (shell-reader.js) is denied when its program, after environment and PowerShell variable assignments,
// groups, launch prefixes (time, env, nohup, timeout, nice, sudo, xargs, exec, !, the PowerShell call and dot
// operators) and shell keywords (if, then, do, else, a { body }, a bash function body), and through Start-Process,
// Invoke-Expression, wsl, shell wrappers (cmd /c, pwsh -Command, bash -c, with their other options; the wrapped string
// is unescaped as its shell does) and programs that run a command given in their arguments (runners from the engine
// or plugins, or a program held in a variable), matches:
//   - a rule returned by an enabled plugin's `hook-rules.js` (evaluated in configuration order), or
//   - node with a script configured via a plugin's `nodeScripts`, unless it only prints (--help, --self-test, --dry-run
//     before any --) or matches an exempt option.
// A segment that runs the configured queue script is allowed with everything quoted in it, and git / gh are left
// alone.
//
// Usage: require('./command-rules.js').createRules(engineConfig, opts) returns
// { check, checkDetailed, isQueueScript }; check(command, dialect) returns the reason texts (empty when allowed), and
// checkDetailed(...) returns structured { tool, verb, text } reasons.

const path = require('node:path');
const { createReader, programName, unquote } = require('./shell-reader.js');
const { DefaultRunners, DefaultRunnerTargets, wrapperRules } = require('./rules/wrappers.js');
const { nodeScriptRules } = require('./rules/node-scripts.js');
const { pluginError } = require('./plugin-loader.js');

const DefaultExemptPrograms = ['git', 'gh'];

/** Builds the regex matching `node <queue-script>` from `entry` and any plugin `legacyQueueScripts`. */
function buildQueueScriptPattern(entry, rulePlugins) {
  const names = new Set();
  const addName = (value) => {
    const base = path.basename(String(value).replace(/\\/g, '/'));
    if (base !== '') {
      names.add(base);
    }
  };
  addName(entry);
  for (const plugin of rulePlugins) {
    for (const legacy of plugin.module.legacyQueueScripts || []) {
      addName(legacy);
    }
  }
  const escaped = [...names].map((name) => name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
  return new RegExp(`(^|[\\\\/])(${escaped.join('|')})$`);
}

/** Parses a segment into its target program ({ name, list, args, inner }), an inner brace reason, or null. */
function resolveProgram(reader, segment, nestedReason) {
  const raw = reader.words(segment.trim().replace(/^[({]+\s*/, ''));
  // The closer of a group or subshell may stick to a word: (buildtool test), (buildtool)
  const list = reader.dropPrefixes(raw).map((word) => (/^["']/.test(word) ? word : word.replace(/[)}]+$/, '')));
  if (list.length === 0 || list[0] === '') {
    return null;
  }
  let calledInGroup = false;
  // A group or subexpression after an assignment: $out = (buildtool test), $r = @(buildtool), $o = (& "x.exe" build)
  if (/^[@$]?\(+./.test(list[0])) {
    list[0] = list[0].replace(/^[@$]?\(+/, '');
    if ((list[0] === '&' || list[0] === '.') && list.length > 1) {
      list.shift();
      calledInGroup = true;
    }
  }
  // PowerShell: a quoted string at the start is a value ($tool = "buildtool"), a command only after & or .
  const called =
    calledInGroup || raw.slice(0, Math.max(raw.length - list.length, 0)).some((word) => /^[({]*[&.]$/.test(word));
  if (reader.dialect === 'pwsh' && /^["']/.test(list[0]) && !called) {
    return null;
  }
  // A PowerShell body after the condition: if (...) { buildtool test }, foreach (...) { ... }
  // ... and the body of a bash function: cnt(){ buildtool test; }, cnt() { buildtool test; }
  const brace = list.findIndex(
    (word, index) => (index > 0 && (word === '{' || word.endsWith('{'))) || (index === 0 && /\(\)\{$/.test(word)),
  );
  if (brace >= 0 && !/^["']/.test(list[brace])) {
    const inner = nestedReason(list.slice(brace + 1).join(' '), reader);
    if (inner !== null) {
      return { name: '', list: [], args: [], inner };
    }
  }
  const name = programName(list[0]);
  // The closer of a group or subshell may stick to the last word: (buildtool build)
  const args = list.slice(1).map((word) => unquote(word).replace(/[)}]+$/, ''));
  return { name, list, args, inner: null };
}

/** Creates the command rule evaluator bound to the loaded `engineConfig`. */
function createRules(engineConfig, { tolerant = false, onError = null } = {}) {
  const reportError = (error) => {
    if (!tolerant) {
      throw error;
    }
    if (typeof onError === 'function') {
      onError(error);
    }
  };

  const exemptPrograms = DefaultExemptPrograms;
  const queueScriptPattern = buildQueueScriptPattern(engineConfig.entry, engineConfig.rulePlugins);

  /** The first structured reason of a nested command, or null. */
  function nestedReason(command, reader) {
    if (command === '') {
      return null;
    }
    const reasons = checkDetailedWithReader(command, reader);
    return reasons.length > 0 ? reasons[0] : null;
  }

  const runners = new Set(DefaultRunners);
  const runnerTargets = new Set(DefaultRunnerTargets);
  for (const plugin of engineConfig.rulePlugins) {
    for (const r of plugin.module.runners || []) {
      runners.add(r);
    }
    for (const target of plugin.module.runnerTargets || []) {
      runnerTargets.add(target);
    }
  }

  const { shellWrappers, runners: runnerRules } = wrapperRules(
    { nestedReason, segmentReason },
    { runners: [...runners], runnerTargets: [...runnerTargets] },
  );

  const engineApi = {
    nestedReason,
    segmentReason,
    entry: engineConfig.entry,
    api: (name) => engineConfig.apiByName.get(name),
  };

  const pluginRules = [];
  for (const plugin of engineConfig.rulePlugins) {
    if (typeof plugin.module.rules !== 'function') {
      continue;
    }
    try {
      const list = plugin.module.rules(plugin.options, engineApi);
      if (
        !Array.isArray(list) ||
        list.some((item) => !item || typeof item.matches !== 'function' || typeof item.reason !== 'function')
      ) {
        throw new Error(`check-queued-command: plugin ${plugin.name} rules() must return { matches, reason } objects`);
      }
      pluginRules.push(...list.map((rule) => ({ rule, owner: plugin.name })));
    } catch (error) {
      reportError(pluginError(plugin.name, 'rules()', error));
    }
  }

  const nodeRules = [];
  for (const plugin of engineConfig.rulePlugins) {
    if (plugin.module.nodeScripts !== undefined) {
      nodeRules.push(...nodeScriptRules(plugin.module.nodeScripts));
    }
  }

  // Each entry: { rule, owner }, owner being the plugin that supplied the rule (null for the engine's own rules)
  const engineRule = (rule) => ({ rule, owner: null });
  const ProgramRules = [
    ...shellWrappers.map(engineRule),
    ...pluginRules,
    ...runnerRules.map(engineRule),
    ...nodeRules.map(engineRule),
  ];

  /** The reason a segment needs the queue, or null. */
  function segmentReason(segment, reader) {
    const program = resolveProgram(reader, segment, nestedReason);
    if (program === null) {
      return null;
    }
    if (program.inner !== null) {
      return program.inner;
    }
    for (const { rule, owner } of ProgramRules) {
      const failed = (part, error) => reportError(owner === null ? error : pluginError(owner, part, error));
      let matched;
      try {
        matched = rule.matches(program, reader);
      } catch (error) {
        failed('matches()', error);
        continue;
      }
      if (!matched) {
        continue;
      }
      try {
        const reason = rule.reason(reader, program);
        if (reason !== null && (typeof reason !== 'object' || typeof reason.text !== 'string')) {
          throw new Error('a rule reason must be null or an object with a text string');
        }
        return reason;
      } catch (error) {
        failed('reason()', error);
        continue;
      }
    }
    return null;
  }

  /** True when a command word list runs the project's queue script via node. */
  function isQueueScript(list) {
    return (
      list.length > 1 &&
      programName(list[0] || '') === 'node' &&
      queueScriptPattern.test(unquote(list[1] || ''))
    );
  }

  /** True when the segment runs the queue script or an exempt program (such as git / gh). */
  function isExempt(segment, reader) {
    const list = reader.dropPrefixes(reader.words(segment.trim()));
    if (list.length === 0) {
      return true;
    }
    if (exemptPrograms.includes(programName(list[0]))) {
      return true;
    }
    return isQueueScript(list);
  }

  /** The structured reasons for one command using an explicit shell reader. */
  function checkDetailedWithReader(command, reader) {
    const reasons = [];
    const seen = new Set();
    const add = (reason) => {
      if (reason !== null && !seen.has(reason.text)) {
        seen.add(reason.text);
        reasons.push(reason);
      }
    };
    for (const segment of reader.splitSegments(command)) {
      // A substitution runs even inside an exempt command: git commit -m "$(buildtool test)"
      reader.substitutions(segment).forEach((body) => checkDetailedWithReader(body, reader).forEach(add));
      if (!isExempt(segment, reader)) {
        add(segmentReason(segment, reader));
      }
    }
    return reasons;
  }

  /**
   * The structured reasons (`{ tool, verb, text }`) the command needs the queue (empty when it may run as it is).
   * `kind` is the shell dialect ('bash', 'pwsh', 'cmd'); nested commands keep the dialect of their caller unless a
   * shell wrapper names another.
   */
  function checkDetailed(command, kind = 'bash') {
    return checkDetailedWithReader(command, createReader(kind));
  }

  /** The reason texts the command needs the queue (empty when it may run as it is). */
  function check(command, kind = 'bash') {
    return checkDetailed(command, kind).map((reason) => reason.text);
  }

  return {
    check,
    checkDetailed,
    isQueueScript,
  };
}

module.exports = { createRules };
