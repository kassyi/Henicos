'use strict';

// Discovers job-queue.config.json, resolves configured plugin folders, and loads hook-rules.js and hook-io.js modules
// in strict mode (--text, --self-test, exported helpers) or tolerant mode (--hook, failing open on errors).
//
// Usage: require('./plugin-loader.js').loadHookConfig({ configPath, tolerant, onError }) returns
// { entry, configRoot, rulePlugins, hookIo, apiByName }.

const fs = require('node:fs');
const path = require('node:path');
const { loadConfigFile } = require('../../human/job-queue/lib/config-file.js');

const DefaultEntry = 'scripts/human/job-queue.js';
const EntryScript = path.resolve(__dirname, '..', 'check-queued-command.js');
const PluginsRoot = path.resolve(__dirname, '..', '..', 'human', 'job-queue', 'plugins');

function isPlainObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function isNonEmptyString(value) {
  return typeof value === 'string' && value.trim() !== '';
}

function isStringArray(value) {
  return Array.isArray(value) && value.every(isNonEmptyString);
}

const Types = {
  string: [isNonEmptyString, 'a non-empty string'],
  function: [(value) => typeof value === 'function', 'a function'],
  regexp: [(value) => value instanceof RegExp, 'a regular expression'],
  object: [isPlainObject, 'an object'],
  strings: [isStringArray, 'an array of non-empty strings'],
  names: [(value) => isStringArray(value) && value.length > 0, 'a non-empty array of non-empty strings'],
  exemptOptions: [
    (value) =>
      Array.isArray(value) &&
      value.every((item) => isPlainObject(item) && item.script instanceof RegExp && isNonEmptyString(item.option)),
    'an array of { script: RegExp, option: string }',
  ],
};

function validateSchema(target, schema, where, label) {
  for (const [key, spec] of Object.entries(schema)) {
    const fullKey = where === '' ? key : `${where}.${key}`;
    const value = target[key];
    if (value === undefined && spec.optional) {
      continue;
    }
    if (spec.nested) {
      if (!isPlainObject(value)) {
        throw new Error(`check-queued-command: ${label} needs ${fullKey} (an object)`);
      }
      validateSchema(value, spec.nested, fullKey, label);
      continue;
    }
    const typeName = typeof spec === 'string' ? spec : spec.type;
    if (!Types[typeName][0](value)) {
      throw new Error(`check-queued-command: ${label} needs ${fullKey} (${Types[typeName][1]})`);
    }
  }
}

const HookRulesSchema = {
  rules: { type: 'function', optional: true },
  runners: { type: 'strings', optional: true },
  runnerTargets: { type: 'strings', optional: true },
  rewrite: { type: 'function', optional: true },
  splitForRewrite: { type: 'function', optional: true },
  messages: {
    optional: true,
    nested: {
      denialText: 'function',
      additionalContext: 'function',
      undecidedNote: 'function',
    },
  },
  nodeScripts: {
    optional: true,
    nested: {
      names: 'names',
      noHelpExemption: 'regexp',
      exemptOptions: 'exemptOptions',
    },
  },
  legacyQueueScripts: { type: 'strings', optional: true },
  api: { type: 'object', optional: true },
  validateOptions: { type: 'function', optional: true },
};

const HookIoSchema = {
  read: 'function',
  deny: 'function',
  rewrite: { type: 'function', optional: true },
};

/**
 * Resolves a plugin folder using the same rules as the job queue plugin host: `./` or `.\` relative to `configRoot`,
 * or a bare folder name inside `scripts/human/job-queue/plugins/`. Returns null for invalid bare names.
 */
function resolvePluginFolder(name, configRoot) {
  if (name.startsWith('./') || name.startsWith('.\\')) {
    return path.resolve(configRoot, name);
  }
  if (name.includes('/') || name.includes('\\') || name.includes('..')) {
    return null;
  }
  const resolved = path.resolve(PluginsRoot, name);
  const relative = path.relative(PluginsRoot, resolved);
  if (relative === '' || relative.startsWith('..') || path.isAbsolute(relative)) {
    return null;
  }
  return resolved;
}

/** Validates the parsed `job-queue.config.json` fields needed by the hook engine. */
function validateConfigSchema(raw) {
  if (!isPlainObject(raw)) {
    throw new Error('check-queued-command: config root must be a JSON object');
  }
  if (raw.version !== 1) {
    throw new Error('check-queued-command: config version must be 1');
  }
  if (!isNonEmptyString(raw.entry)) {
    throw new Error('check-queued-command: config entry must be a non-empty string');
  }
  if (!Array.isArray(raw.plugins)) {
    throw new Error('check-queued-command: config plugins must be an array');
  }
  raw.plugins.forEach((item, index) => {
    if (!isPlainObject(item)) {
      throw new Error(`check-queued-command: config plugins[${index}] must be an object`);
    }
    if (!isNonEmptyString(item.name)) {
      throw new Error(`check-queued-command: config plugins[${index}].name must be a non-empty string`);
    }
    if (item.options !== undefined && !isPlainObject(item.options)) {
      throw new Error(`check-queued-command: config plugins[${index}].options must be an object`);
    }
  });
}

function validateHookRules(mod, expectedName) {
  const label = `plugin ${expectedName} hook-rules.js`;
  if (!isPlainObject(mod)) {
    throw new Error(`check-queued-command: ${label} must export an object`);
  }
  if (mod.name !== expectedName) {
    throw new Error(`check-queued-command: ${label} name must be "${expectedName}"`);
  }
  validateSchema(mod, HookRulesSchema, '', label);
}

function validateHookIo(mod, expectedName) {
  const label = `plugin ${expectedName} hook-io.js`;
  if (!isPlainObject(mod)) {
    throw new Error(`check-queued-command: ${label} must export an object`);
  }
  if (mod.name !== expectedName) {
    throw new Error(`check-queued-command: ${label} name must be "${expectedName}"`);
  }
  validateSchema(mod, HookIoSchema, '', label);
}

function emptyConfig() {
  return {
    entry: DefaultEntry,
    configRoot: path.dirname(EntryScript),
    rulePlugins: [],
    hookIo: [],
    apiByName: new Map(),
  };
}

/**
 * Loads `job-queue.config.json` and the plugin modules of the hook (`hook-rules.js` and `hook-io.js`).
 * In tolerant mode (`--hook`), errors are passed to `onError` and the faulty config or plugin is skipped.
 */
function loadHookConfig({ configPath, tolerant = false, onError = null } = {}) {
  const report = (error) => {
    if (!tolerant) {
      throw error;
    }
    if (typeof onError === 'function') {
      onError(error);
    }
  };

  const loaded = loadConfigFile(path.dirname(EntryScript), configPath);
  if (loaded.status !== 'ok') {
    const message =
      loaded.status === 'missing'
        ? 'check-queued-command: job-queue.config.json not found'
        : `check-queued-command: invalid config (${loaded.path}): ${loaded.error}`;
    report(new Error(message));
    return emptyConfig();
  }

  try {
    validateConfigSchema(loaded.raw);
  } catch (error) {
    report(error);
    return emptyConfig();
  }

  const configRoot = path.dirname(loaded.path);
  const rulePlugins = [];
  const hookIo = [];
  const apiByName = new Map();

  for (const entry of loaded.raw.plugins) {
    const folder = resolvePluginFolder(entry.name, configRoot);
    if (folder === null) {
      report(new Error(`check-queued-command: plugin ${entry.name} has an invalid bare name`));
      continue;
    }
    if (!fs.existsSync(folder)) {
      report(new Error(`check-queued-command: plugin ${entry.name} folder not found (${folder})`));
      continue;
    }
    const options = entry.options === undefined ? {} : entry.options;

    const rulesFile = path.join(folder, 'hook-rules.js');
    if (fs.existsSync(rulesFile)) {
      try {
        const realRules = fs.realpathSync.native(rulesFile);
        const mod = require(realRules);
        validateHookRules(mod, entry.name);
        if (typeof mod.validateOptions === 'function') {
          mod.validateOptions(options);
        }
        rulePlugins.push({ name: entry.name, options, module: mod });
        if (mod.api !== undefined) {
          apiByName.set(entry.name, mod.api);
        }
      } catch (error) {
        report(error);
      }
    }

    const ioFile = path.join(folder, 'hook-io.js');
    if (fs.existsSync(ioFile)) {
      try {
        const realIo = fs.realpathSync.native(ioFile);
        const mod = require(realIo);
        validateHookIo(mod, entry.name);
        hookIo.push({ name: entry.name, options, module: mod });
      } catch (error) {
        report(error);
      }
    }
  }

  return {
    entry: loaded.raw.entry,
    configRoot,
    rulePlugins,
    hookIo,
    apiByName,
  };
}

module.exports = {
  DefaultEntry,
  PluginsRoot,
  resolvePluginFolder,
  validateConfigSchema,
  validateHookRules,
  validateHookIo,
  loadHookConfig,
};
