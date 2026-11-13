'use strict';

// The command rules of check-queued-command.js: which commands must go through the job queue, the denial message, and
// the hook input and output of Claude Code and Antigravity.
//
// A segment (shell-reader.js) is denied when its program, after environment and PowerShell variable assignments,
// groups, launch prefixes (time, env, nohup, timeout, nice, sudo, xargs, exec, !, the PowerShell call and dot
// operators) and shell keywords (if, then, do, else, a { body }, a bash function body), and through Start-Process,
// Invoke-Expression, wsl, shell wrappers (cmd /c, pwsh -Command, bash -c, with their other options; the wrapped string
// is unescaped as its shell does) and programs that run a command given in their arguments (compute-sanitizer,
// valgrind, a program held in a variable), is
//   - dotnet build, test, msbuild, publish, pack, vstest, stryker or watch, Stryker in any form, or msbuild itself
//     (dotnet run is left alone: it starts the small tools of the repository, such as the demangler),
//   - just with a recipe that builds or runs tests (test*, coverage-diff, fuzz-*, mutate, long-checks, bench*,
//     doctest), unless just only prints it (--dry-run, --show, --list) or mutate / long-checks get --dry-run,
//   - node with one of the long test scripts or gpu-lock.js (in any folder: scripts/human/, or scripts/agent/ in older
//     checkouts), unless they only print (--help, --self-test, --dry-run before any --), or gpu-lock.js takes a private
//     lock (--lock-path, for its own tests).
// A segment that runs job-queue.js is allowed with everything quoted in it, and a git or gh segment is left alone.
//
// Usage: require('./command-rules.js').check(command, dialect) returns the reasons (empty when allowed);
// hookAnswer(payload) returns the answer to one parsed hook input (null when allowed; a Claude Code command that
// queue-rewrite.js can queue comes back as updatedInput); runHook() answers one hook call on stdin.

const {
  currentDialect,
  withDialect,
  splitSegments,
  words,
  unquote,
  argumentText,
  programName,
  dropPrefixes,
  startProcessCommand,
  shellCommandIndex,
  substitutions,
} = require('./shell-reader.js');

// \S*: a recipe name may hold a variable, as in a loop: just test-$r
const JustRecipes = /^(test\S*|coverage-diff|fuzz-\S+|mutate|long-checks|bench\S*|doctest)$/;
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
const NodeScripts = new RegExp(`(^|[\\\\/])(${NodeScriptNames.join('|')})\\.js$`);
const DotnetVerbs = /^(build|test|msbuild|publish|pack|vstest|stryker|watch)$/;
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
    } else if (arg === '--set') {
      i += 2;
    } else if (/^\w[\w-]*=/.test(arg)) {
      continue; // a variable assignment before the recipe: just profile=cpu fuzz-diff
    } else if (!arg.startsWith('-')) {
      return arg;
    }
  }
  return '';
}

/** The first reason of a nested command, or null. */
function nestedReason(command) {
  const reasons = check(command);
  return reasons.length > 0 ? reasons[0] : null;
}

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

/** True for the Stryker.NET tool name, also with a version: dotnet-stryker, dotnet-stryker@4.0.0. */
function isStryker(word) {
  return /^dotnet-stryker(@.*)?$/i.test(word);
}

/** The reason a segment needs the queue, or null. */
function segmentReason(segment) {
  const raw = words(segment.trim().replace(/^[({]+\s*/, ''));
  // The closer of a group or subshell may stick to a word: (just test), (msbuild)
  const list = dropPrefixes(raw).map((word) => (/^["']/.test(word) ? word : word.replace(/[)}]+$/, '')));
  if (list.length === 0 || list[0] === '') {
    return null;
  }
  let calledInGroup = false;
  // A group or subexpression after an assignment: $out = (dotnet test), $r = @(dotnet test), $o = (& "x.exe" build)
  if (/^[@$]?\(+./.test(list[0])) {
    list[0] = list[0].replace(/^[@$]?\(+/, '');
    if ((list[0] === '&' || list[0] === '.') && list.length > 1) {
      list.shift();
      calledInGroup = true;
    }
  }
  // PowerShell: a quoted string at the start is a value ($tool = "msbuild"), a command only after & or .
  const called =
    calledInGroup || raw.slice(0, Math.max(raw.length - list.length, 0)).some((word) => /^[({]*[&.]$/.test(word));
  if (currentDialect() === 'pwsh' && /^["']/.test(list[0]) && !called) {
    return null;
  }
  // A PowerShell body after the condition: if (...) { dotnet test }, foreach (...) { ... }
  // ... and the body of a bash function: cnt(){ dotnet test; }, cnt() { dotnet test; }
  const brace = list.findIndex(
    (word, index) => (index > 0 && (word === '{' || word.endsWith('{'))) || (index === 0 && /\(\)\{$/.test(word)),
  );
  if (brace >= 0 && !/^["']/.test(list[brace])) {
    const inner = nestedReason(list.slice(brace + 1).join(' '));
    if (inner !== null) {
      return inner;
    }
  }
  const name = programName(list[0]);
  if (name === 'start-process' || name === 'saps') {
    return nestedReason(startProcessCommand(list));
  }
  if (name === 'invoke-expression' || name === 'iex') {
    return nestedReason(list.slice(1).map(argumentText).join(' '));
  }
  if (name === 'wsl') {
    // wsl [options] [-e | --exec] <command>, wsl [options] -- <command line>: the command runs in Linux, on this
    // machine's CPU
    let i = 1;
    while (i < list.length && /^-/.test(list[i]) && !['-e', '--exec', '--'].includes(list[i])) {
      i += ['-d', '--distribution', '-u', '--user', '--cd', '--shell-type'].includes(list[i]) ? 2 : 1;
    }
    if (['-e', '--exec', '--'].includes(list[i])) {
      i++;
    }
    const inner = list.slice(i).join(' ');
    const reasons = inner === '' ? [] : check(inner, 'bash');
    return reasons.length > 0 ? reasons[0] : null;
  }
  const shellIndex = shellCommandIndex(name, list);
  if (shellIndex > 0) {
    const inner = list.slice(shellIndex).map(argumentText).join(' ');
    const reasons = check(inner, name === 'cmd' ? 'cmd' : /^(pwsh|powershell)$/.test(name) ? 'pwsh' : 'bash');
    return reasons.length > 0 ? reasons[0] : null;
  }
  // The closer of a group or subshell may stick to the last word: (dotnet build)
  const args = list.slice(1).map((word) => unquote(word).replace(/[)}]+$/, ''));
  if (name === 'dotnet') {
    const verbIndex = args.findIndex((arg) => !arg.startsWith('-'));
    const verb = verbIndex < 0 ? '' : args[verbIndex];
    const rest = args.slice(verbIndex + 1);
    // dotnet tool run / exec, dotnet dnx, dotnet dotnet-stryker: Stryker anywhere among the words, with any version
    // (dotnet-stryker@4.0.0) and options before it (--tool-manifest m.json)
    if (/^(tool|dnx|dotnet-stryker(@.*)?)$/i.test(verb) && args.some(isStryker)) {
      return 'dotnet-stryker';
    }
    if (rest.some((arg) => ['--help', '-h', '-?', '/?'].includes(arg))) {
      return null; // dotnet test --help builds nothing
    }
    if (DotnetVerbs.test(verb)) {
      return `dotnet ${verb}`;
    }
    return null;
  }
  if (isStryker(name) || (name === 'dnx' && args.some(isStryker))) {
    return 'dotnet-stryker';
  }
  // A program that runs the command given in its arguments (a sanitizer, tracer or profiler, or a program held in a
  // variable): compute-sanitizer --tool memcheck dotnet test, "$CS" --log-file x dotnet test
  if (Runners.includes(name) || /^["']?\$/.test(list[0])) {
    for (let i = 1; i < list.length; i++) {
      if (['dotnet', 'just', 'msbuild', 'node'].includes(programName(list[i]))) {
        const inner = segmentReason(list.slice(i).join(' '));
        if (inner !== null) {
          return inner;
        }
      }
    }
    return null;
  }
  if (name === 'msbuild') {
    return 'msbuild';
  }
  if (name === 'just') {
    const recipe = justRecipe(list);
    if (!JustRecipes.test(recipe)) {
      return null;
    }
    const recipeIndex = list.findIndex((word, index) => index > 0 && unquote(word) === recipe);
    const before = list.slice(1, recipeIndex).map(unquote);
    const after = list.slice(recipeIndex + 1).map(unquote);
    // just --dry-run / --show / --list prints the recipe; mutate and long-checks run nothing with --dry-run
    const printsOnly = before.some((arg) =>
      ['-n', '--dry-run', '-s', '--show', '-l', '--list', '--summary', '--evaluate'].includes(arg),
    );
    const scriptDryRun = /^(mutate|long-checks)$/.test(recipe) && after.includes('--dry-run');
    return printsOnly || scriptDryRun ? null : `just ${recipe}`;
  }
  if (NodeScripts.test(unquote(list[0]))) {
    return unquote(list[0]).replace(/^.*[\\/]/, ''); // ./scripts/human/long-checks.js run directly
  }
  if (name === 'node') {
    const scriptIndex = args.findIndex((arg) => !arg.startsWith('-'));
    const script = scriptIndex < 0 ? '' : args[scriptIndex];
    // node --check <script>, <script> --help / --self-test / --dry-run: no build, no test run. Only the script's own
    // options count, not those after `--` that it passes on (test-stability.js -- --dry-run still builds), and
    // the fuzzer builds tools/fuzz-diff even for --help.
    const ownArgs = args.slice(scriptIndex + 1);
    const passOn = ownArgs.indexOf('--');
    const helper =
      args.slice(0, Math.max(scriptIndex, 0)).some((arg) => arg === '--check' || arg === '-c') ||
      (!/fuzz-differential\.js$/.test(script) &&
        (passOn < 0 ? ownArgs : ownArgs.slice(0, passOn)).some((arg) =>
          ['--help', '-h', '--self-test', '--dry-run'].includes(arg),
        )) ||
      // gpu-lock.js --lock-path <dir> takes a private lock, for tests of the script itself
      (/gpu-lock\.js$/.test(script) && ownArgs.includes('--lock-path'));
    if (NodeScripts.test(script) && !helper) {
      return script.replace(/^.*[\\/]/, '');
    }
  }
  return null;
}

/**
 * True when the segment runs job-queue.js (which queues the command quoted in it; build-queue.js is its name in older
 * checkouts) or is a git / gh command.
 */
function isExempt(segment) {
  const list = dropPrefixes(words(segment.trim()));
  if (list.length === 0) {
    return true;
  }
  const name = programName(list[0]);
  if (name === 'git' || name === 'gh') {
    return true;
  }
  return name === 'node' && /(^|[\\/])(job|build)-queue\.js$/.test(unquote(list[1] || ''));
}

/**
 * The reasons the command needs the queue (empty when it may run as it is). `kind` is the shell dialect ('bash',
 * 'pwsh', 'cmd'); nested commands keep the dialect of their caller unless a shell wrapper names another.
 */
function check(command, kind = currentDialect()) {
  return withDialect(kind, () => {
    const reasons = [];
    const add = (reason) => {
      if (reason !== null && !reasons.includes(reason)) {
        reasons.push(reason);
      }
    };
    for (const segment of splitSegments(command)) {
      // A substitution runs even inside an exempt command: git commit -m "$(dotnet test)"
      substitutions(segment).forEach((body) => check(body).forEach(add));
      if (!isExempt(segment)) {
        add(segmentReason(segment));
      }
    }
    return reasons;
  });
}

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

/**
 * The answer to one parsed hook input, or null when the command may run as it is.
 * Antigravity ({"toolCall": {"name": "run_command", "args": {"CommandLine": ...}}}, run in PowerShell): a denial.
 * Claude Code ({"tool_name": "Bash" | "PowerShell", "tool_input": {"command": ...}, "cwd": ...}): the command queued
 * through updatedInput when queue-rewrite.js can rewrite it, else a denial. A rewrite sets no permissionDecision, so
 * the normal permission flow still runs on the rewritten command.
 */
function hookAnswer(payload) {
  const antigravity = payload.toolCall !== undefined;
  const toolInput = antigravity ? {} : payload.tool_input || {};
  const command = antigravity
    ? String((payload.toolCall.args && payload.toolCall.args.CommandLine) || '')
    : String(toolInput.command || '');
  const kind = antigravity || payload.tool_name === 'PowerShell' ? 'pwsh' : 'bash';
  const reasons = check(command, kind);
  if (reasons.length === 0) {
    return null;
  }
  if (antigravity) {
    return { decision: 'deny', reason: denialText(reasons) };
  }
  // Required here: queue-rewrite.js requires this module for check()
  const { planRewrite } = require('./queue-rewrite.js');
  const plan = planRewrite(command, kind, String(payload.cwd || process.cwd()));
  if (plan !== null && plan.command !== undefined) {
    return {
      hookSpecificOutput: {
        hookEventName: 'PreToolUse',
        updatedInput: { ...toolInput, command: plan.command },
        additionalContext:
          `check-queued-command.js sent this command through the job queue as: ${plan.command} . ` +
          (plan.split
            ? 'The build was queued as a job of its own, without --gpu, so that the GPU is held only for the ' +
              'tests: two `queued #<id>` lines, the build first; the tests run only when the build succeeded. '
            : '') +
          'Its output starts with `queued #<id>`; `node scripts/human/job-queue.js status <id>` and ' +
          '`node scripts/human/job-queue.js wait <id>` follow the job.',
      },
    };
  }
  const note = plan === null ? '' : `\n${plan.note}`;
  return {
    hookSpecificOutput: {
      hookEventName: 'PreToolUse',
      permissionDecision: 'deny',
      permissionDecisionReason: `${denialText(reasons)}${note}`,
    },
  };
}

function runHook() {
  let input = '';
  process.stdin.setEncoding('utf8');
  process.stdin.on('data', (chunk) => (input += chunk));
  process.stdin.on('end', () => {
    let payload;
    try {
      payload = JSON.parse(input);
    } catch (error) {
      process.stderr.write(`check-queued-command --hook: invalid hook input: ${error.message}\n`);
      process.exit(1);
    }
    const answer = hookAnswer(payload);
    if (answer !== null) {
      process.stdout.write(JSON.stringify(answer));
    }
    process.exit(0);
  });
}

module.exports = { check, denialText, hookAnswer, runHook };
