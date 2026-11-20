'use strict';

// The rewrite of check-queued-command.js: a plain denied command in a rewrite-capable agent is handed back wrapped in
// the job queue (updatedInput of the PreToolUse hook) instead of being denied:
//   buildtool test tests/UnitTests --no-build
//   -> node scripts/human/job-queue.js run --gpu --shell bash -- 'buildtool test tests/UnitTests --no-build'
//      (on one line)
//
// What is rewritten: segments joined by && only, each either an allowed command (cd x, git ...) or a command that an
// enabled plugin's `rewrite` handler accepts (environment assignments before it allowed in bash), with a trailing
// comment kept outside the quotes (# intent: ...). Everything else stays denied: other separators (; | || & line
// breaks), heredocs and here-strings, substitutions and bash backticks, input redirections, groups, assignments, launch
// prefixes, wrappers (cmd /c, pwsh -c, bash -c, wsl, Start-Process), PowerShell's --%, a command that Git Bash would
// turn into a Windows path (/x..., X=/x...), a segment that already runs the queue, and a working directory without the
// configured queue entry script.
//
// GPU use and build-then-test splitting are delegated to the enabled plugins (`rewrite` and `splitForRewrite`) in
// configuration order. An undecided command is denied as before, with a sentence saying that it may need --gpu.
//
// Usage: require('./queue-rewrite.js').createRewriter(engineConfig, rules, opts) returns
// { planRewrite, queuedText, quote }; planRewrite(command, dialect, cwd) returns { command, gpu } for a rewrite,
// { note } for a denial with a sentence to add, or null for the plain denial; queuedText(rewritten, dialect) returns
// the command a rewritten command queues.

const fs = require('node:fs');
const path = require('node:path');
const { createRules } = require('./command-rules.js');
const { createReader } = require('./shell-reader.js');
const { pluginError } = require('./plugin-loader.js');

const DirectoryChanges = ['cd', 'pushd', 'popd', 'chdir', 'set-location', 'sl', 'push-location', 'pop-location'];

function defaultMessages(entry) {
  return {
    denialText: (reasons) =>
      `${reasons.join(', ')} must run through the job queue:\n` +
      `  node ${entry} run -- "<command>"\n` +
      `  node ${entry} run --gpu -- "<command>"   (when the command uses the GPU)`,
    additionalContext: (plan) =>
      `check-queued-command.js sent this command through the job queue as: ${plan.command} . ` +
      (plan.split
        ? 'The build was queued as a job of its own, without --gpu, so that the GPU is held only for the ' +
          'tests: two `queued j<nnnn>` lines, the build first; the tests run only when the build succeeded. '
        : '') +
      `Its output starts with \`queued j<nnnn>\`; \`node ${entry} status <id>\` and ` +
      `\`node ${entry} wait <id>\` follow the job.`,
    undecidedNote: (undecided) =>
      'The hook did not queue it by itself, since it cannot tell whether the command uses the GPU ' +
      `(${undecided.join('; ')}): add --gpu when it does.`,
  };
}

/**
 * The hook wording of the first rule plugin that supplies `messages`, else the neutral defaults. A wording function
 * that throws or returns no string goes to `report` and the default wording is used for that call, so that a broken
 * sentence never turns a denial into an allowed command.
 */
function selectMessages(engineConfig, report = (error) => {
  throw error;
}) {
  const defaults = defaultMessages(engineConfig.entry);
  const plugin = engineConfig.rulePlugins.find((item) => item.module.messages);
  if (plugin === undefined) {
    return defaults;
  }
  const guarded = {};
  for (const key of Object.keys(defaults)) {
    guarded[key] = (...args) => {
      try {
        const text = plugin.module.messages[key](...args);
        if (typeof text !== 'string') {
          throw new Error('must return a string');
        }
        return text;
      } catch (error) {
        report(pluginError(plugin.name, `messages.${key}()`, error));
        return defaults[key](...args);
      }
    };
  }
  return guarded;
}

/** The text quoted as one single-quoted word of the dialect. */
function quote(kind, text) {
  return kind === 'pwsh' ? `'${text.replace(/'/g, "''")}'` : `'${text.replace(/'/g, "'\\''")}'`;
}

/** Drops leading bash environment variable assignments (`FOO=1 cmd`) from a word list. */
function stripBashAssignments(list, kind) {
  let i = 0;
  while (kind === 'bash' && list.length - i > 1 && /^[A-Za-z_]\w*=/.test(list[i])) {
    i++;
  }
  return i === 0 ? list : list.slice(i);
}

/** The command that queues `command`: node <queueScript> run [options] --shell <s> -- '<command>'. */
function queuedCommand(kind, command, options, comment, queueScript) {
  const shell = kind === 'pwsh' ? 'pwsh' : 'bash';
  const head = ['node', queueScript, 'run', ...options, '--shell', shell, '--'].join(' ');
  return `${head} ${quote(kind, command)}${comment === '' ? '' : ` ${comment}`}`;
}

/** The command a queued command runs: the word after `--`, unquoted as its shell does. */
function queuedText(rewritten, kind) {
  const reader = createReader(kind);
  const list = reader.words(rewritten);
  const index = list.indexOf('--');
  return index < 0 || index + 1 >= list.length ? '' : reader.argumentText(list[index + 1]);
}

/** True when a word outside quotes holds <: an input redirection or a process substitution. */
function hasInputRedirection(segment) {
  return /</.test(segment.replace(/"(?:[^"\\`]|[\\`].)*"|'[^']*'/g, ''));
}

function isPlainObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function validateRewriteResult(result, pluginName) {
  if (!isPlainObject(result)) {
    throw new Error(`check-queued-command: plugin ${pluginName} rewrite() must return an object or null`);
  }
  if (result.undecided !== undefined) {
    if (typeof result.undecided !== 'string') {
      throw new Error(`check-queued-command: plugin ${pluginName} rewrite() undecided must be a string`);
    }
    return result;
  }
  if (typeof result.gpu !== 'boolean' || !Array.isArray(result.options)) {
    throw new Error(
      `check-queued-command: plugin ${pluginName} rewrite() must return { gpu: boolean, options: array }`,
    );
  }
  return result;
}

function validateSplitResult(result, pluginName) {
  if (!isPlainObject(result) || typeof result.builds !== 'string' || typeof result.tests !== 'string') {
    throw new Error(
      `check-queued-command: plugin ${pluginName} splitForRewrite() must return { builds: string, tests: string }`,
    );
  }
  return result;
}

/** Creates the command rewriter bound to `engineConfig` and rule evaluator `rules`. */
function createRewriter(engineConfig, rules = createRules(engineConfig), { tolerant = false, onError = null } = {}) {
  const reportError = (error) => {
    if (!tolerant) {
      throw error;
    }
    if (typeof onError === 'function') {
      onError(error);
    }
  };

  const { checkDetailed, isQueueScript } = rules;
  const queueScript = engineConfig.entry;
  const { undecidedNote } = selectMessages(engineConfig, reportError);
  const rewritePlugins = engineConfig.rulePlugins.filter((plugin) => typeof plugin.module.rewrite === 'function');
  const splitPlugins = engineConfig.rulePlugins.filter(
    (plugin) => typeof plugin.module.splitForRewrite === 'function',
  );
  const api = (name) => engineConfig.apiByName.get(name);

  /** One denied segment: { gpu, options } or { undecided } or null (plain denial). */
  function deniedSegment(segment, list, reason, root, changedDirectory) {
    const context = { root, changedDirectory, entry: queueScript, api };
    for (const plugin of rewritePlugins) {
      try {
        const result = plugin.module.rewrite(segment, list, reason, { ...context, options: plugin.options });
        if (result !== null && result !== undefined) {
          return validateRewriteResult(result, plugin.name);
        }
      } catch (error) {
        reportError(pluginError(plugin.name, 'rewrite()', error));
      }
    }
    return null;
  }

  /** Optional build-then-test split delegated to enabled plugins. */
  function splitBuilds(segments, reader) {
    for (const plugin of splitPlugins) {
      try {
        const result = plugin.module.splitForRewrite(segments, reader);
        if (result !== null && result !== undefined) {
          return validateSplitResult(result, plugin.name);
        }
      } catch (error) {
        reportError(pluginError(plugin.name, 'splitForRewrite()', error));
      }
    }
    return null;
  }

  /**
   * The rewrite of a command the hook denies, run in dialect `kind` ('bash' or 'pwsh') in `cwd`:
   * { command, gpu } to queue it, { note } to deny it with that sentence added, or null to deny it as before.
   */
  function planRewrite(command, kind, cwd) {
    const text = command.trim();
    if (text === '' || !fs.existsSync(path.join(cwd, queueScript))) {
      return null;
    }
    const reader = createReader(kind);
    const found = [];
    reader.splitSegments(text, found);
    const comments = found.filter((item) => item.kind === 'comment');
    if (found.some((item) => item.kind !== '&&' && item.kind !== 'comment') || comments.length > 1) {
      return null;
    }
    // A trailing comment (# intent: ...) stays outside the quotes, where the command gate reads it
    const body = comments.length === 0 ? text : text.slice(0, comments[0].index).trimEnd();
    const comment = comments.length === 0 ? '' : text.slice(comments[0].index);
    if (
      (kind === 'bash' && (/`/.test(body) || /^([^\s=]*=)?\//.test(body))) ||
      (kind === 'pwsh' && /--%/.test(body))
    ) {
      return null; // a bash backtick substitution; a path Git Bash converts; PowerShell's stop-parsing token
    }
    const segments = reader.splitSegments(body);
    let gpu = false;
    let options = [];
    let changedDirectory = false;
    const undecided = [];
    for (const raw of segments) {
      const segment = raw.trim();
      if (segment === '' || /^[({@$&.!]/.test(segment) || hasInputRedirection(segment)) {
        return null;
      }
      if (reader.substitutions(segment).length > 0) {
        return null;
      }
      // FOO=1 buildtool test: the assignment travels with the command
      const list = stripBashAssignments(reader.words(segment), kind);
      const name = reader.programName(list[0] || '');
      const reasons = checkDetailed(segment, kind);
      if (reasons.length === 0) {
        if (isQueueScript(list)) {
          return null; // already queued: nesting would run it at once in this job
        }
        changedDirectory = changedDirectory || DirectoryChanges.includes(name);
        continue;
      }
      if (reasons.length > 1) {
        return null;
      }
      const result = deniedSegment(segment, list, reasons[0], cwd, changedDirectory);
      if (result === null) {
        return null;
      }
      if (result.undecided !== undefined) {
        undecided.push(`${reasons[0].text}: ${result.undecided}`);
        continue;
      }
      gpu = gpu || result.gpu;
      if (result.options.length > options.length) {
        options = result.options; // extra queue options returned by a plugin rewrite
      }
    }
    if (undecided.length > 0) {
      return { note: undecidedNote(undecided) };
    }
    const split = gpu ? splitBuilds(segments, reader) : null;
    if (split !== null) {
      // The builds take CPU slots only; the GPU turn waits for them and is held for the tests alone
      const builds = queuedCommand(kind, split.builds, options, '', queueScript);
      const tests = queuedCommand(kind, split.tests, ['--gpu', ...options], comment, queueScript);
      return { command: `${builds} && ${tests}`, gpu, split: true };
    }
    return { command: queuedCommand(kind, body, gpu ? ['--gpu', ...options] : options, comment, queueScript), gpu };
  }

  return { planRewrite, queuedText, quote };
}

module.exports = { createRewriter, defaultMessages, selectMessages, queuedText, quote };
