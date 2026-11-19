'use strict';

// Justfile parsing and queue option inspection for the built-in just hook plugin.
//
// Usage: require('./justfile.js').recipeQueue(recipe, root, entry) returns { options, gpu } or { undecided }.

const fs = require('node:fs');
const path = require('node:path');

const JustfileName = 'justfile';
const OptionsPattern = /^((--gpu|--weight \d+)( |$))*$/;

/** Builds the regex matching `queue := "node <entry> run"` in a justfile. */
function buildQueueLinePattern(entry) {
  const escaped = entry.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return new RegExp(`^queue := "node ${escaped} run"\\s*$`, 'm');
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

/** The queue options of a just recipe: { options, gpu } or { undecided: why }. */
function recipeQueue(recipe, root, entry) {
  let justfile;
  try {
    justfile = fs.readFileSync(path.join(root, JustfileName), 'utf8');
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
    return { undecided: `just ${recipe} chooses its queue options from its arguments` };
  }
  const list = options === '' ? [] : options.split(' ');
  return { options: list.filter((option) => option !== '--gpu'), gpu: list.includes('--gpu') };
}

module.exports = {
  JustfileName,
  OptionsPattern,
  buildQueueLinePattern,
  recipeBodies,
  recipeQueue,
};
