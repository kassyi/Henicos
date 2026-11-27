'use strict';

// Henicos repository hook rules, node script data, legacy queue script names, test-project GPU lookup, and wording
// for scripts/agent/check-queued-command.js.
//
// Usage: loaded by scripts/agent/check-queued-command/plugin-loader.js when `./scripts/human/job-queue-henicos` is
// enabled in job-queue.config.json.

const path = require('node:path');
const { testProjectFiles, dialectOf } = require('./target-dialect.js');

const DefaultTestsDir = 'tests';

function isPlainObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function validateOptions(options) {
  if (options === undefined) {
    return;
  }
  if (!isPlainObject(options)) {
    throw new Error('check-queued-command: ./scripts/human/job-queue-henicos plugin options must be an object');
  }
  if (options.testsDir !== undefined && (typeof options.testsDir !== 'string' || options.testsDir === '')) {
    throw new Error('check-queued-command: ./scripts/human/job-queue-henicos plugin options testsDir must be a string');
  }
}

function defaultLookup(testsDir) {
  return {
    list: (root) => testProjectFiles(path.join(root, testsDir)),
    readErrorMessage: (root, error) => `could not read ${path.join(root, testsDir)}: ${error.message}`,
    missingMessage: `it names no test project of ${testsDir}/`,
    usesGpu: ({ name, project }) => {
      const dialect = dialectOf(project);
      if (dialect === null) {
        return { undecided: `${name}.csproj sets no GSharpTargetDialect of Cpu or Cuda` };
      }
      return { gpu: dialect === 'cuda' };
    },
  };
}

/** The test projects of the repository at `root`: { names } or { error } when they cannot be read. */
function testProjects(root, lookup = defaultLookup(DefaultTestsDir)) {
  try {
    return { names: lookup.list(root), error: '' };
  } catch (error) {
    return { names: [], error: lookup.readErrorMessage(root, error) };
  }
}

/** The test projects named in the text: the name not followed by more of a name (.csproj and .dll do not count). */
function namedProjects(text, projects) {
  return projects.filter(({ name }) => {
    const escaped = name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    return new RegExp(`(^|[^\\w.])${escaped}(?=$|[^\\w.]|\\.(csproj|dll)\\b)`, 'i').test(text);
  });
}

/** GPU use of a dotnet test / vstest segment: { gpu } or { undecided: why }. */
function testGpu(segment, root, lookup = defaultLookup(DefaultTestsDir)) {
  const { names, error } = testProjects(root, lookup);
  if (error !== '') {
    return { undecided: error };
  }
  const named = namedProjects(segment, names);
  if (named.length === 0) {
    return { undecided: lookup.missingMessage };
  }
  let gpu = false;
  for (const entry of named) {
    const result = lookup.usesGpu(entry);
    if (!result || result.undecided !== undefined || typeof result.gpu !== 'boolean') {
      return { undecided: (result && result.undecided) || 'GPU use is unknown' };
    }
    gpu = gpu || result.gpu;
  }
  return { gpu };
}

function denialText(reasons) {
  return (
    `${reasons.join(', ')} must run through the job queue, ` +
    'so that parallel agents do not oversubscribe the machine:\n' +
    '  node scripts/human/job-queue.js run -- "<command>"\n' +
    '  node scripts/human/job-queue.js run --gpu -- "<command>"             (uses the GPU; replaces gpu-lock.js)\n' +
    '  node scripts/human/job-queue.js run --gpu-exclusive -- "<command>"   (uses the GPU outside the share language)\n' +
    'The queue waits for a free slot and then runs the command here. ' +
    'It prints `queued j<nnnn>` at once: keep the id and ' +
    'read the expected start and end with `node scripts/human/job-queue.js status <id> --json` (`status` lists every ' +
    'job); `node scripts/human/job-queue.js wait <id>` run in the background calls you when it ends ' +
    '(exit 0 succeeded). Steps that belong together go into one job as one quoted string, ' +
    '`run -- "dotnet build X && dotnet test X --no-build"`: it waits once, and nothing runs between the steps; ' +
    'with --gpu, queue the build as a job of its own without --gpu, so that the GPU is held only for the tests. ' +
    'Long waits: run it in the background. Text that only names such a command (a message, a search ' +
    'pattern) passes when it is quoted. ' +
    'The queued command runs in the shell you call `run` from (Git Bash, PowerShell; ' +
    '`--shell bash|pwsh|cmd|sh` to choose), so write it as you would here.'
  );
}

function additionalContext(plan) {
  return (
    `check-queued-command.js sent this command through the job queue as: ${plan.command} . ` +
    (plan.split
      ? 'The build was queued as a job of its own, without --gpu, so that the GPU is held only for the ' +
        'tests: two `queued j<nnnn>` lines, the build first; the tests run only when the build succeeded. '
      : '') +
    'Its output starts with `queued j<nnnn>`; `node scripts/human/job-queue.js status <id>` and ' +
    '`node scripts/human/job-queue.js wait <id>` follow the job.'
  );
}

function undecidedNote(undecided) {
  return (
    'The hook did not queue it by itself, since it cannot tell whether the command uses the GPU ' +
    `(${undecided.join('; ')}): add --gpu when it does, or --gpu-exclusive when it uses the GPU outside the share language.`
  );
}

function rules(options, _engine) {
  validateOptions(options);
  return [];
}

function rewrite(segment, list, reason, context) {
  const dotnetApi = context && typeof context.api === 'function' ? context.api('dotnet') : null;
  if (!dotnetApi || typeof dotnetApi.isTestInvocation !== 'function' || !dotnetApi.isTestInvocation(list, reason)) {
    return null;
  }
  const testsDir = (context && context.options && context.options.testsDir) || DefaultTestsDir;
  const result = testGpu(segment, context.root, defaultLookup(testsDir));
  return result.undecided !== undefined ? result : { gpu: result.gpu, options: [] };
}

module.exports = {
  name: './scripts/human/job-queue-henicos',
  validateOptions,
  rules,
  nodeScripts: {
    names: [
      'gpu-lock',
      'test-stability',
      'test-memory',
      'test-sanitized',
      'mutate',
      'coverage-diff',
      'fuzz-differential',
    ],
    noHelpExemption: /fuzz-differential\.js$/,
    exemptOptions: [{ script: /gpu-lock\.js$/, option: '--lock-path' }],
  },
  legacyQueueScripts: ['build-queue.js'],
  rewrite,
  messages: {
    denialText,
    additionalContext,
    undecidedNote,
  },
  api: {
    testProjects,
    namedProjects,
    testGpu,
  },
};
