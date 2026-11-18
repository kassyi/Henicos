'use strict';

// .NET, Stryker.NET, and MSBuild command rules for check-queued-command.js.
//
// Usage: require('./rules/dotnet.js').dotnetRules(project) returns { dotnet, stryker, msbuild }, where each entry
// implements { matches(program), reason(reader, program) }.

/** True for the Stryker.NET tool name, also with a version: dotnet-stryker, dotnet-stryker@4.0.0. */
function isStryker(word) {
  return /^dotnet-stryker(@.*)?$/i.test(word);
}

/** Builds .NET, Stryker.NET, and MSBuild command rules for the given repository config. */
function dotnetRules(project = {}) {
  const verbs = project.dotnet && project.dotnet.verbs ? project.dotnet.verbs : null;

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
      if (verbs && verbs.test(verb)) {
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

  return { dotnet, stryker, msbuild };
}

module.exports = { dotnetRules };
