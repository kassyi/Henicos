'use strict';

// Hook rules and rewrite support for the built-in just plugin: matches recipes configured in
// plugins[].options.{hookRecipes, hookDryRunRecipes} and inspects the repository justfile for queue options.
//
// Usage: loaded by the hook engine plugin loader when `just` is enabled in job-queue.config.json.

const { recipeQueue } = require('./justfile.js');

/** just options that take a value. */
const JustValueOptions = [
  '-f',
  '--justfile',
  '-d',
  '--working-directory',
  '--dotenv-path',
  '--dotenv-filename',
  '--shell',
  '--shell-arg',
  '--color',
  '--chooser',
  '--command-color',
  '--list-heading',
  '--list-prefix',
];

const PrintOptions = ['-n', '--dry-run', '-s', '--show', '-l', '--list', '--summary', '--evaluate'];

function isPlainObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function defaultUnquote(word) {
  return word.replace(/^["']|["']$/g, '');
}

/** Validates required regex string options for the just hook plugin and compiles them. */
function validateOptions(options) {
  if (!isPlainObject(options)) {
    throw new Error('check-queued-command: just plugin options must be an object');
  }
  const compiled = {};
  for (const key of ['hookRecipes', 'hookDryRunRecipes']) {
    const value = options[key];
    if (typeof value !== 'string' || value === '') {
      throw new Error(`check-queued-command: just plugin options need ${key} (a non-empty regular expression string)`);
    }
    try {
      compiled[key] = new RegExp(value);
    } catch (error) {
      throw new Error(
        `check-queued-command: just plugin options ${key} is not a valid regular expression: ${error.message}`,
      );
    }
  }
  return {
    recipes: compiled.hookRecipes,
    dryRunRecipes: compiled.hookDryRunRecipes,
  };
}

/** The recipe of a just invocation, after its options. */
function justRecipe(list, unquote) {
  for (let i = 1; i < list.length; i++) {
    const arg = unquote(list[i]);
    if (JustValueOptions.includes(arg)) {
      i++;
      continue;
    }
    if (arg === '--set') {
      i += 2;
      continue;
    }
    if (/^\w[\w-]*=/.test(arg)) {
      continue; // a variable assignment before the recipe: just profile=cpu fuzz-diff
    }
    if (!arg.startsWith('-')) {
      return arg;
    }
  }
  return '';
}

/** Builds the `just` recipe command rule from `options.{hookRecipes, hookDryRunRecipes}`. */
function rules(options, _engine) {
  const { recipes, dryRunRecipes } = validateOptions(options);

  const just = {
    matches: ({ name }) => name === 'just',
    reason: (reader, { list }) => {
      const unquote = (reader && reader.unquote) || defaultUnquote;
      const recipe = justRecipe(list, unquote);
      if (!recipes.test(recipe)) {
        return null;
      }
      const recipeIndex = list.findIndex((word, index) => index > 0 && unquote(word) === recipe);
      const before = list.slice(1, recipeIndex).map(unquote);
      const after = list.slice(recipeIndex + 1).map(unquote);
      // just --dry-run / --show / --list prints the recipe; dry-run recipes run nothing with --dry-run
      const printsOnly = before.some((arg) => PrintOptions.includes(arg));
      const scriptDryRun = dryRunRecipes.test(recipe) && after.includes('--dry-run');
      return printsOnly || scriptDryRun ? null : { tool: 'just', verb: recipe, text: `just ${recipe}` };
    },
  };

  return [just];
}

/** Rewrite decision for `just <recipe>` segments (disabled after a directory change). */
function rewrite(_segment, list, reason, context) {
  if (!/^just(\.exe)?$/i.test(list[0] || '')) {
    return null;
  }
  if (reason.tool !== 'just' || (context && context.changedDirectory)) {
    return null;
  }
  return recipeQueue(reason.verb, context.root, context.entry);
}

module.exports = {
  name: 'just',
  runnerTargets: ['just'],
  validateOptions,
  rules,
  rewrite,
};
