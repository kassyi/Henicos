'use strict';

// Hook rules and rewrite helpers for the built-in dotnet plugin: command rules for dotnet, dotnet-stryker, and
// msbuild, dotnet profiler/coverage runners, runnerTargets, build/test split, and CPU-build rewrite.
//
// Usage: loaded by the hook engine plugin loader when `dotnet` is enabled in job-queue.config.json.

const DotnetVerbs = /^(build|test|msbuild|publish|pack|vstest|stryker|watch)$/;
const CpuVerbs = ['build', 'publish', 'pack', 'msbuild'];
const TestVerbs = ['test', 'vstest'];

function isPlainObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

/** True for the Stryker.NET tool name, also with a version: dotnet-stryker, dotnet-stryker@4.0.0. */
function isStryker(word) {
  return /^dotnet-stryker(@.*)?$/i.test(word);
}

/** True for an MSBuild target option naming a test target: -t:VSTest, /target:Test. */
function runsTestTarget(list) {
  return list.some((word) => /^[-/]t(arget)?:.*test/i.test(word));
}

/** Drops leading bash environment variable assignments (`FOO=1 cmd`) from a word list. */
function stripBashAssignments(list, kind) {
  let i = 0;
  while (kind === 'bash' && list.length - i > 1 && /^[A-Za-z_]\w*=/.test(list[i])) {
    i++;
  }
  return i === 0 ? list : list.slice(i);
}

function validateOptions(options) {
  if (options !== undefined && !isPlainObject(options)) {
    throw new Error('check-queued-command: dotnet plugin options must be an object');
  }
}

/** Builds dotnet, dotnet-stryker, and msbuild command rules. */
function rules(options, _engine) {
  validateOptions(options);

  const dotnet = {
    matches: ({ name }) => name === 'dotnet',
    reason: (_reader, { args }) => {
      const verbIndex = args.findIndex((arg) => !arg.startsWith('-'));
      const verb = verbIndex < 0 ? '' : args[verbIndex];
      const rest = args.slice(verbIndex + 1);
      // dotnet tool run / exec, dotnet dnx, dotnet dotnet-stryker: Stryker anywhere among the words, with any
      // version (dotnet-stryker@4.0.0) and options before it (--tool-manifest m.json)
      if (/^(tool|dnx|dotnet-stryker(@.*)?)$/i.test(verb) && args.some(isStryker)) {
        return { tool: 'stryker', verb: '', text: 'dotnet-stryker' };
      }
      if (rest.some((arg) => ['--help', '-h', '-?', '/?'].includes(arg))) {
        return null; // dotnet test --help builds nothing
      }
      if (DotnetVerbs.test(verb)) {
        return { tool: 'dotnet', verb, text: `dotnet ${verb}` };
      }
      return null;
    },
  };

  const stryker = {
    matches: ({ name, args }) => isStryker(name) || (name === 'dnx' && args.some(isStryker)),
    reason: () => ({ tool: 'stryker', verb: '', text: 'dotnet-stryker' }),
  };

  const msbuild = {
    matches: ({ name }) => name === 'msbuild',
    reason: () => ({ tool: 'msbuild', verb: '', text: 'msbuild' }),
  };

  return [dotnet, stryker, msbuild];
}

/**
 * The builds and the tests of `dotnet build <P> && dotnet test <P> --no-build` (several builds, then several tests,
 * joined by &&; environment assignments before a segment allowed), each part joined by && again: { builds, tests }, or
 * null for any other shape. A test without --no-build would build under the GPU turn again.
 */
function splitForRewrite(segments, reader) {
  const builds = [];
  const tests = [];
  for (const raw of segments) {
    const segment = raw.trim();
    const list = stripBashAssignments(reader.words(segment).map(reader.unquote), reader.dialect);
    if (!/^dotnet(\.exe)?$/i.test(list[0] || '')) {
      return null;
    }
    if (list[1] === 'build' && tests.length === 0) {
      builds.push(segment);
    } else if (list[1] === 'test' && list.includes('--no-build')) {
      tests.push(segment);
    } else {
      return null;
    }
  }
  return builds.length > 0 && tests.length > 0 ? { builds: builds.join(' && '), tests: tests.join(' && ') } : null;
}

/** Rewrite decision for dotnet/msbuild segments when no earlier plugin handled them. */
function rewrite(_segment, list, reason, _context) {
  if (!/^(dotnet|msbuild)(\.exe)?$/i.test(list[0] || '')) {
    return null;
  }
  if (reason.tool === 'msbuild' || (reason.tool === 'dotnet' && CpuVerbs.includes(reason.verb))) {
    return runsTestTarget(list)
      ? { undecided: 'it runs an MSBuild target named like a test target' }
      : { gpu: false, options: [] };
  }
  if (reason.tool === 'dotnet' && TestVerbs.includes(reason.verb)) {
    return { undecided: 'no repository plugin determines whether this test project uses the GPU' };
  }
  return null;
}

module.exports = {
  name: 'dotnet',
  runners: ['dotnet-trace', 'dotnet-counters', 'dotnet-coverage'],
  runnerTargets: ['dotnet', 'msbuild'],
  validateOptions,
  rules,
  splitForRewrite,
  rewrite,
  api: {
    cpuVerbs: [...CpuVerbs],
    testVerbs: [...TestVerbs],
    isTestInvocation: (list, reason) =>
      /^dotnet(\.exe)?$/i.test(list[0] || '') && reason.tool === 'dotnet' && TestVerbs.includes(reason.verb),
  },
};
