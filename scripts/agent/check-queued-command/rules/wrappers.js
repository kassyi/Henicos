'use strict';

// Wrapper and runner program rules for check-queued-command.js: Start-Process, Invoke-Expression, wsl, shell
// wrappers (cmd /c, pwsh -Command, bash -c), and programs/variables that run another command in their arguments.
//
// Usage: require('./rules/wrappers.js').wrapperRules({ nestedReason, segmentReason }) returns
// { shellWrappers, runners } where each entry is { matches(program, reader), reason(reader, program) }.

const { createReader, programName } = require('../shell-reader.js');

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

/** Builds the shell wrapper and runner rule handlers with injected recursion callbacks. */
function wrapperRules({ nestedReason, segmentReason }) {
  const startProcess = {
    matches: ({ name }) => name === 'start-process' || name === 'saps',
    reason: (reader, { list }) => nestedReason(reader.startProcessCommand(list), reader),
  };

  const invokeExpression = {
    matches: ({ name }) => name === 'invoke-expression' || name === 'iex',
    reason: (reader, { list }) => nestedReason(list.slice(1).map(reader.argumentText).join(' '), reader),
  };

  const wsl = {
    matches: ({ name }) => name === 'wsl',
    reason: (_reader, { list }) => {
      // wsl [options] [-e | --exec] <command>, wsl [options] -- <command line>: the command runs in Linux, on this
      // machine's CPU
      let i = 1;
      while (i < list.length && /^-/.test(list[i]) && !['-e', '--exec', '--'].includes(list[i])) {
        i += ['-d', '--distribution', '-u', '--user', '--cd', '--shell-type'].includes(list[i]) ? 2 : 1;
      }
      if (['-e', '--exec', '--'].includes(list[i])) {
        i++;
      }
      return nestedReason(list.slice(i).join(' '), createReader('bash'));
    },
  };

  const shell = {
    matches: ({ name, list }, reader) => reader.shellCommandIndex(name, list) > 0,
    reason: (reader, { name, list }) => {
      const shellIndex = reader.shellCommandIndex(name, list);
      const inner = list.slice(shellIndex).map(reader.argumentText).join(' ');
      const innerDialect = name === 'cmd' ? 'cmd' : /^(pwsh|powershell)$/.test(name) ? 'pwsh' : 'bash';
      return nestedReason(inner, createReader(innerDialect));
    },
  };

  // A program that runs the command given in its arguments (a sanitizer, tracer or profiler, or a program held in a
  // variable): compute-sanitizer --tool memcheck dotnet test, "$CS" --log-file x dotnet test
  const runner = {
    matches: ({ name, list }) => Runners.includes(name) || /^["']?\$/.test(list[0]),
    reason: (reader, { list }) => {
      for (let i = 1; i < list.length; i++) {
        if (['dotnet', 'just', 'msbuild', 'node'].includes(programName(list[i]))) {
          const inner = segmentReason(list.slice(i).join(' '), reader);
          if (inner !== null) {
            return inner;
          }
        }
      }
      return null;
    },
  };

  return {
    shellWrappers: [startProcess, invokeExpression, wsl, shell],
    runners: [runner],
  };
}

module.exports = { wrapperRules };
