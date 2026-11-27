'use strict';

// Justfile parsing and queue option inspection for the built-in just hook plugin.
//
// Usage: require('./justfile.js').recipeQueue(recipe, root, entry, args) returns { options, gpu, gpuExclusive } or
// { undecided }.

const childProcess = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');

const JustfileName = 'justfile';
const OptionsPattern = /^((--gpu-exclusive|--gpu|--weight \d+)( |$))*$/;
const DryRunTimeoutMs = 5000;

/**
 * True when bash or PowerShell may change the word `raw` (as written, quotes included) before just sees it: `$` or a
 * backtick outside single quotes, a glob or brace character outside quotes, or `~` unquoted at the start of the word
 * or after `=` or `:`. Errs towards true (an escaped `\$` counts).
 */
function expandsInShell(raw) {
  let quote = null;
  for (let i = 0; i < raw.length; i++) {
    const c = raw[i];
    if (quote === "'") {
      quote = c === "'" ? null : quote;
      continue;
    }
    if (c === '$' || c === '`') {
      return true;
    }
    if (quote === '"') {
      quote = c === '"' ? null : quote;
      continue;
    }
    if (c === "'" || c === '"') {
      quote = c;
    } else if ('*?[{'.includes(c) || (c === '~' && (i === 0 || raw[i - 1] === '=' || raw[i - 1] === ':'))) {
      return true;
    }
  }
  return false;
}

/** Builds the regex matching `queue := "node <entry> run"` in a justfile. */
function buildQueueLinePattern(entry) {
  const escaped = entry.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return new RegExp(`^queue := "node ${escaped} run"\\s*$`, 'm');
}

/** Builds the regex matching a `node <entry> run <options> -- ` command in `just --dry-run` output. */
function buildDryRunQueuePattern(entry) {
  const escaped = entry.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return new RegExp(`(?:^|\\s)node\\s+${escaped}\\s+run\\b(.*?)\\s--\\s`);
}

/** Splits validated queue options into { options, gpu, gpuExclusive }. */
function parseQueueOptions(options) {
  const list = options === '' ? [] : options.split(' ');
  const gpuExclusive = list.includes('--gpu-exclusive');
  const gpu = gpuExclusive || list.includes('--gpu');
  return {
    options: list.filter((option) => option !== '--gpu' && option !== '--gpu-exclusive'),
    gpu,
    gpuExclusive,
  };
}

/** Runs `just --dry-run` once with a 5-second timeout to resolve conditional recipe queue options. */
function runJustDryRun(justfilePath, recipe, args, spawn = childProcess.spawnSync) {
  return spawn('just', ['--dry-run', '--justfile', justfilePath, recipe, ...args], {
    encoding: 'utf8',
    timeout: DryRunTimeoutMs,
    windowsHide: true,
  });
}

/** Resolves queue options from the first `node <entry> run ... -- ` line of `just --dry-run` output. */
function resolveDryRunQueue(recipe, justfilePath, entry, args, dryRun, rawArgs) {
  const fallback = { undecided: `just ${recipe} chooses its queue options from its arguments` };
  if (rawArgs.some((arg) => typeof arg !== 'string' || expandsInShell(arg))) {
    return fallback; // the shell expands $P or a glob after the hook: the dry run would read another argument
  }
  let result;
  try {
    result = dryRun(justfilePath, recipe, args);
  } catch (_error) {
    return fallback;
  }
  if (!result || result.error || result.signal || result.status !== 0) {
    return fallback;
  }
  const output = [result.stderr, result.stdout].filter((part) => typeof part === 'string' && part !== '').join('\n');
  const pattern = buildDryRunQueuePattern(entry);
  for (const line of output.split(/\r?\n/)) {
    const match = pattern.exec(line);
    if (match === null) {
      continue;
    }
    const options = match[1].trim().replace(/\s+/g, ' ');
    return OptionsPattern.test(options) ? parseQueueOptions(options) : fallback;
  }
  return fallback;
}

/** The bodies of the recipe in the justfile (one per platform variant), continued lines joined; empty if absent. */
function recipeBodies(justfile, recipe) {
  const lines = justfile.split(/\r?\n/);
  const bodies = [];
  // Every regular-expression character of the name escaped, backslash included (only - and . were before)
  const header = new RegExp(`^@?${recipe.replace(/[\\^$.*+?()[\]{}|-]/g, '\\$&')}(\\s[^\\n]*)?:\\s*$`);
  for (let i = 0; i < lines.length; i++) {
    if (!header.test(lines[i]) || /:=/.test(lines[i])) {
      continue;
    }
    const body = [];
    for (let j = i + 1; j < lines.length && (lines[j] === '' || /^\s/.test(lines[j])); j++) {
      body.push(lines[j]);
    }
    bodies.push(body.join('\n').replace(/\\\n\s*/g, ' '));
  }
  return bodies;
}

/** The parameter names of the recipe (`test *args="":` gives args); empty if the recipe is absent. */
function recipeParameters(justfile, recipe) {
  const header = new RegExp(`^@?${recipe.replace(/[\\^$.*+?()[\]{}|-]/g, '\\$&')}(\\s[^\\n]*)?:\\s*$`, 'm');
  const match = header.exec(justfile.replace(/\r\n/g, '\n'));
  const names = [];
  for (const word of (match?.[1] ?? '').trim().split(/\s+/)) {
    const name = /^[*+$]*(\w+)/.exec(word);
    if (name !== null) {
      names.push(name[1]);
    }
  }
  return names;
}

/**
 * The top-level variables whose value is a string literal, as just evaluates them: `[export] name := "value"` (with
 * the escapes \" \\ \n \t) or `'value'` (raw). A variable assigned an expression is left out, so its caller can tell
 * that it cannot read it.
 */
function justVariables(justfile) {
  const variables = new Map();
  const pattern = /^(?:export\s+)?(\w+)\s*:=\s*(?:"((?:[^"\\\n]|\\.)*)"|'([^'\n]*)')\s*$/gm;
  for (const match of justfile.replace(/\r\n/g, '\n').matchAll(pattern)) {
    const escapes = { n: '\n', t: '\t', '"': '"', '\\': '\\' };
    const value = match[2] !== undefined ? match[2].replace(/\\(.)/g, (all, c) => escapes[c] ?? all) : match[3];
    variables.set(match[1], value);
  }
  return variables;
}

/** The queue options of a just recipe: { options, gpu, gpuExclusive } or { undecided: why }. */
function recipeQueue(recipe, root, entry, args = [], dryRun = runJustDryRun, rawArgs = args) {
  const justfilePath = path.join(root, JustfileName);
  let justfile;
  try {
    justfile = fs.readFileSync(justfilePath, 'utf8');
  } catch (error) {
    return { undecided: `could not read the justfile: ${error.message}` };
  }
  if (typeof entry !== 'string' || entry === '' || !buildQueueLinePattern(entry).test(justfile)) {
    return { undecided: 'the justfile defines no queue variable' };
  }
  const found = new Set();
  for (const body of recipeBodies(justfile, recipe)) {
    const pattern = /\{\{queue\}\}(.*?)\s--\s/g;
    let match;
    while ((match = pattern.exec(body)) !== null) {
      found.add(match[1].trim().replace(/\s+/g, ' '));
    }
  }
  if (found.size === 0) {
    return { undecided: `just ${recipe} does not queue itself` };
  }
  const [options] = [...found];
  if (found.size > 1 || !OptionsPattern.test(options)) {
    const list = Array.isArray(args) ? args : [];
    return resolveDryRunQueue(recipe, justfilePath, entry, list, dryRun, Array.isArray(rawArgs) ? rawArgs : list);
  }
  return parseQueueOptions(options);
}

module.exports = {
  DryRunTimeoutMs,
  JustfileName,
  OptionsPattern,
  expandsInShell,
  runJustDryRun,
  buildDryRunQueuePattern,
  buildQueueLinePattern,
  justVariables,
  recipeBodies,
  recipeParameters,
  recipeQueue,
};
