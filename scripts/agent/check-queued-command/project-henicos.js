'use strict';

// Henicos-specific repository data, test-project GPU lookup, and hook wording for check-queued-command.js.
//
// Usage: passed by scripts/agent/check-queued-command.js (and self-test*.js) into command-rules.js, queue-rewrite.js,
// and hook.js; engine modules never require this file directly.

const path = require('path');
const { testProjectFiles, dialectOf } = require('../../human/job-queue-henicos/target-dialect.js');

// \S*: a recipe name may hold a variable, as in a loop: just test-$r
const JustRecipes = /^(test\S*|coverage-diff|fuzz-\S+|mutate|long-checks|bench\S*|doctest)$/;
const JustDryRunRecipes = /^(mutate|long-checks)$/;

/** The long test scripts and gpu-lock.js, in any folder (gpu-lock.js was in scripts/agent/ in older checkouts). */
const NodeScriptNames = [
  'gpu-lock',
  'test-stability',
  'long-checks',
  'test-memory',
  'test-sanitized',
  'mutate',
  'coverage-diff',
  'fuzz-differential',
];

const DotnetVerbs = /^(build|test|msbuild|publish|pack|vstest|stryker|watch)$/;

/** Programs that run another command given among their arguments. */
const Runners = [
  'compute-sanitizer',
  'cuda-memcheck',
  'valgrind',
  'strace',
  'ltrace',
  'nsys',
  'ncu',
  'drmemory',
  'perf',
  'gdb',
  'lldb',
  'dotnet-trace',
  'dotnet-counters',
  'dotnet-coverage',
  'procdump',
];

const QueueScript = 'scripts/human/job-queue.js';

function denialText(reasons) {
  return (
    `${reasons.join(', ')} must run through the job queue, ` +
    'so that parallel agents do not oversubscribe the machine:\n' +
    '  node scripts/human/job-queue.js run -- "<command>"\n' +
    '  node scripts/human/job-queue.js run --gpu -- "<command>"   (uses the GPU; replaces gpu-lock.js)\n' +
    'The queue waits for a free slot and then runs the command here. ' +
    'It prints `queued #<id>` at once: keep the id and ' +
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
        'tests: two `queued #<id>` lines, the build first; the tests run only when the build succeeded. '
      : '') +
    'Its output starts with `queued #<id>`; `node scripts/human/job-queue.js status <id>` and ' +
    '`node scripts/human/job-queue.js wait <id>` follow the job.'
  );
}

function undecidedNote(undecided) {
  return (
    'The hook did not queue it by itself, since it cannot tell whether the command uses the GPU ' +
    `(${undecided.join('; ')}): add --gpu when it does.`
  );
}

module.exports = {
  justRecipes: JustRecipes,
  justDryRunRecipes: JustDryRunRecipes,
  nodeScripts: {
    names: NodeScriptNames,
    noHelpExemption: /fuzz-differential\.js$/,
    exemptOptions: [{ script: /gpu-lock\.js$/, option: '--lock-path' }],
  },
  dotnet: {
    verbs: DotnetVerbs,
    cpuVerbs: ['build', 'publish', 'pack', 'msbuild'],
    testVerbs: ['test', 'vstest'],
  },
  runners: Runners,
  runnerTargets: ['dotnet', 'just', 'msbuild', 'node'],
  exemptPrograms: ['git', 'gh'],
  queueScript: QueueScript,
  queueScriptPattern: /(^|[\\/])(job|build)-queue\.js$/,
  justfile: {
    fileName: 'justfile',
    queueLinePattern: /^queue := "node scripts\/human\/job-queue\.js run"\s*$/m,
    optionsPattern: /^((--gpu|--weight \d+)( |$))*$/,
  },
  testProjects: {
    list: (root) => testProjectFiles(path.join(root, 'tests')),
    readErrorMessage: (root, error) => `could not read ${path.join(root, 'tests')}: ${error.message}`,
    missingMessage: 'it names no test project of tests/',
    usesGpu: ({ name, project }) => {
      const dialect = dialectOf(project);
      if (dialect === null) {
        return { undecided: `${name}.csproj sets no GSharpTargetDialect of Cpu or Cuda` };
      }
      return { gpu: dialect === 'cuda' };
    },
  },
  messages: {
    denialText,
    additionalContext,
    undecidedNote,
  },
};
