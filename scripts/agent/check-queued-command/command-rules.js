'use strict';

// The command rules of check-queued-command.js: which commands must go through the job queue.
//
// A segment (shell-reader.js) is denied when its program, after environment and PowerShell variable assignments,
// groups, launch prefixes (time, env, nohup, timeout, nice, sudo, xargs, exec, !, the PowerShell call and dot
// operators) and shell keywords (if, then, do, else, a { body }, a bash function body), and through Start-Process,
// Invoke-Expression, wsl, shell wrappers (cmd /c, pwsh -Command, bash -c, with their other options; the wrapped string
// is unescaped as its shell does) and programs that run a command given in their arguments (compute-sanitizer,
// valgrind, a program held in a variable), is
//   - dotnet build, test, msbuild, publish, pack, vstest, stryker or watch, Stryker in any form, or msbuild itself
//     (dotnet run is left alone: it starts the small tools of the repository, such as the demangler),
//   - just with a recipe that builds or runs tests (test*, coverage-diff, fuzz-*, mutate, long-checks, bench*,
//     doctest), unless just only prints it (--dry-run, --show, --list) or mutate / long-checks get --dry-run,
//   - node with one of the long test scripts or gpu-lock.js (in any folder: scripts/human/, or scripts/agent/ in older
//     checkouts), unless they only print (--help, --self-test, --dry-run before any --), or gpu-lock.js takes a private
//     lock (--lock-path, for its own tests).
// A segment that runs job-queue.js is allowed with everything quoted in it, and a git or gh segment is left alone.
//
// Usage: require('./command-rules.js').createRules(project) returns { check, checkDetailed, isQueueScript,
// resolveProgram }; check(command, dialect) returns the reason texts (empty when allowed), and checkDetailed(...)
// returns structured { tool, verb, text } reasons.

const { createReader, programName, unquote } = require('./shell-reader.js');
const { wrapperRules } = require('./rules/wrappers.js');
const { dotnetRules } = require('./rules/dotnet.js');
const { justRules } = require('./rules/just.js');
const { nodeScriptRules } = require('./rules/node-scripts.js');

/** Parses a segment into its target program ({ name, list, args, inner }), an inner brace reason, or null. */
function resolveProgram(reader, segment, nestedReason) {
  const raw = reader.words(segment.trim().replace(/^[({]+\s*/, ''));
  // The closer of a group or subshell may stick to a word: (just test), (msbuild)
  const list = reader.dropPrefixes(raw).map((word) => (/^["']/.test(word) ? word : word.replace(/[)}]+$/, '')));
  if (list.length === 0 || list[0] === '') {
    return null;
  }
  let calledInGroup = false;
  // A group or subexpression after an assignment: $out = (dotnet test), $r = @(dotnet test), $o = (& "x.exe" build)
  if (/^[@$]?\(+./.test(list[0])) {
    list[0] = list[0].replace(/^[@$]?\(+/, '');
    if ((list[0] === '&' || list[0] === '.') && list.length > 1) {
      list.shift();
      calledInGroup = true;
    }
  }
  // PowerShell: a quoted string at the start is a value ($tool = "msbuild"), a command only after & or .
  const called =
    calledInGroup || raw.slice(0, Math.max(raw.length - list.length, 0)).some((word) => /^[({]*[&.]$/.test(word));
  if (reader.dialect === 'pwsh' && /^["']/.test(list[0]) && !called) {
    return null;
  }
  // A PowerShell body after the condition: if (...) { dotnet test }, foreach (...) { ... }
  // ... and the body of a bash function: cnt(){ dotnet test; }, cnt() { dotnet test; }
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
  // The closer of a group or subshell may stick to the last word: (dotnet build)
  const args = list.slice(1).map((word) => unquote(word).replace(/[)}]+$/, ''));
  return { name, list, args, inner: null };
}

/** Creates the command rule evaluator bound to the given repository config `project`. */
function createRules(project = {}) {
  const exemptPrograms = Array.isArray(project.exemptPrograms) ? project.exemptPrograms : [];
  const queueScriptPattern = project.queueScriptPattern || null;

  /** The first structured reason of a nested command, or null. */
  function nestedReason(command, reader) {
    if (command === '') {
      return null;
    }
    const reasons = checkDetailedWithReader(command, reader);
    return reasons.length > 0 ? reasons[0] : null;
  }

  const { dotnet, stryker, msbuild } = dotnetRules(project);
  const { shellWrappers, runners } = wrapperRules({ nestedReason, segmentReason }, project);
  const ProgramRules = [
    ...shellWrappers,
    dotnet,
    stryker,
    ...runners,
    msbuild,
    ...justRules(project),
    ...nodeScriptRules(project),
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
    const rule = ProgramRules.find((entry) => entry.matches(program, reader));
    return rule === undefined ? null : rule.reason(reader, program);
  }

  /** True when a command word list runs the project's queue script via node. */
  function isQueueScript(list) {
    return (
      queueScriptPattern !== null &&
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
      // A substitution runs even inside an exempt command: git commit -m "$(dotnet test)"
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
    resolveProgram: (reader, segment) => resolveProgram(reader, segment, nestedReason),
  };
}

module.exports = { createRules, resolveProgram };
