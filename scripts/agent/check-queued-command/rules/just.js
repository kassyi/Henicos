'use strict';

// just recipe command rule for check-queued-command.js.
//
// Usage: require('./rules/just.js').justRules(project) returns [just], implementing
// { matches(program), reason(reader, program) }.

const { unquote } = require('../shell-reader.js');

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

/** The recipe of a just invocation, after its options. */
function justRecipe(list) {
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

/** Builds the `just` recipe command rule for the given repository config. */
function justRules(project = {}) {
  const recipes = project.justRecipes || null;
  const dryRunRecipes = project.justDryRunRecipes || null;

  const just = {
    matches: ({ name }) => name === 'just',
    reason: (_reader, { list }) => {
      const recipe = justRecipe(list);
      if (!recipes || !recipes.test(recipe)) {
        return null;
      }
      const recipeIndex = list.findIndex((word, index) => index > 0 && unquote(word) === recipe);
      const before = list.slice(1, recipeIndex).map(unquote);
      const after = list.slice(recipeIndex + 1).map(unquote);
      // just --dry-run / --show / --list prints the recipe; dry-run recipes run nothing with --dry-run
      const printsOnly = before.some((arg) =>
        ['-n', '--dry-run', '-s', '--show', '-l', '--list', '--summary', '--evaluate'].includes(arg),
      );
      const scriptDryRun = Boolean(dryRunRecipes && dryRunRecipes.test(recipe) && after.includes('--dry-run'));
      return printsOnly || scriptDryRun ? null : { tool: 'just', verb: recipe, text: `just ${recipe}` };
    },
  };

  return [just];
}

module.exports = { justRules };
