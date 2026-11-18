'use strict';

// The shape of the repository configuration that check-queued-command.js passes to its engine (project-henicos.js is
// the one in use). The engine reads every key without a default, so a missing or mistyped key must stop the hook at
// load instead of silently allowing the commands it would deny.
//
// Usage: require('./project-config.js').validateProject(project) returns `project` or throws, naming the first key
// that is missing or of the wrong type.

/** The type each key must have; a nested object lists the keys of a nested object. */
const Schema = {
  justRecipes: 'regexp',
  justDryRunRecipes: 'regexp',
  nodeScripts: { names: 'names', noHelpExemption: 'regexp', exemptOptions: 'array' },
  dotnet: { verbs: 'regexp', cpuVerbs: 'strings', testVerbs: 'strings' },
  runners: 'strings',
  runnerTargets: 'strings',
  exemptPrograms: 'strings',
  queueScript: 'string',
  queueScriptPattern: 'regexp',
  justfile: { fileName: 'string', queueLinePattern: 'regexp', optionsPattern: 'regexp' },
  testProjects: { list: 'function', readErrorMessage: 'function', missingMessage: 'string', usesGpu: 'function' },
  messages: { denialText: 'function', additionalContext: 'function', undecidedNote: 'function' },
};

const isText = (value) => typeof value === 'string' && value !== '';
const isTexts = (value) => Array.isArray(value) && value.every(isText);

/** Each type: [test, what the error message says]. */
const Types = {
  regexp: [(value) => value instanceof RegExp, 'a regular expression'],
  string: [isText, 'a non-empty string'],
  function: [(value) => typeof value === 'function', 'a function'],
  array: [Array.isArray, 'an array'],
  strings: [isTexts, 'an array of non-empty strings'],
  names: [(value) => isTexts(value) && value.length > 0, 'a non-empty array of non-empty strings'],
};

function validate(value, schema, where) {
  for (const [key, type] of Object.entries(schema)) {
    const name = where === '' ? key : `${where}.${key}`;
    const item = value[key];
    if (typeof type === 'object') {
      if (item === null || typeof item !== 'object') {
        throw new Error(`check-queued-command: the project config needs ${name} (an object)`);
      }
      validate(item, type, name);
    } else if (!Types[type][0](item)) {
      throw new Error(`check-queued-command: the project config needs ${name} (${Types[type][1]})`);
    }
  }
}

function validateProject(project) {
  if (project === null || typeof project !== 'object') {
    throw new Error('check-queued-command: the project config must be an object');
  }
  validate(project, Schema, '');
  return project;
}

module.exports = { validateProject, Schema };
