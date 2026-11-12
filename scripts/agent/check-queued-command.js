#!/usr/bin/env node
'use strict';

// PreToolUse hook of Claude Code (Bash and PowerShell, .claude/settings.json) and Antigravity (run_command,
// .agents/hooks.json): denies a build or test command that does not go through the build queue
// (scripts/agent/build-queue.js), so that agents running side by side do not oversubscribe the machine. The input
// format tells the two apart. A command that may run produces no output: an Antigravity "allow" would skip the
// user's permission prompt.
//
// The command is read in the dialect of the tool that runs it: bash for the Claude Code Bash tool, PowerShell for its
// PowerShell tool and for Antigravity (` escapes in double quotes, \ does not; '' in single quotes), cmd inside cmd /c.
// It is split into segments at &&, ||, ;, |, & and line breaks outside quotes, after joining continued lines
// (\ or ` at the end of a line); comments, heredoc bodies and PowerShell here-strings are skipped, since their text is
// data, and the bodies of command substitutions $( ... ) are checked as commands. A segment is denied when its
// program, after environment assignments, launch prefixes (time, env, nohup, timeout, sudo, xargs, exec, !, the
// PowerShell call operator, Invoke-Expression) and shell keywords (if, then, do, else, a PowerShell { body }), and
// through Start-Process and shell wrappers (cmd /c, pwsh -Command, bash -c, with their other options), is
//   - dotnet build, test, msbuild, publish, pack, vstest, stryker or watch, or msbuild itself (dotnet run is left alone:
//     it starts the small tools of the repository, such as the demangler),
//   - just with a recipe that builds or runs tests (test*, coverage-diff, fuzz-*, mutate, long-checks, bench*, doctest),
//   - node with one of the long test scripts or gpu-lock.js.
// A segment that runs build-queue.js is allowed with everything quoted in it, and a git or gh segment is left alone.
//
// Usage:
//   node scripts/agent/check-queued-command.js --hook            reads the hook JSON on stdin
//   node scripts/agent/check-queued-command.js --text "<command>" prints the decision (exit 1 when denied)
//   node scripts/agent/check-queued-command.js --self-test

const usage = 'Usage: check-queued-command.js (--hook | --text <command> | --self-test)';

const JustRecipes = /^(test[\w-]*|coverage-diff|fuzz-[\w-]+|mutate|long-checks|bench[\w-]*|doctest)$/;
const NodeScripts =
  /(^|[\\/])(gpu-lock|test-stability|long-checks|test-memory|test-sanitized|mutate|coverage-diff|fuzz-differential)\.js$/;
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

/** Skips the lines of one heredoc body starting at `index`; returns the index after its closing line. */
function skipHeredocBody(command, index, word, stripTabs) {
  let i = index;
  while (i < command.length) {
    let end = command.indexOf('\n', i);
    if (end < 0) {
      end = command.length;
    }
    const line = command.slice(i, end).replace(/\r$/, '');
    i = end + 1;
    if ((stripTabs ? line.replace(/^\t+/, '') : line) === word) {
      break;
    }
  }
  return i;
}

/**
 * Splits a command into segments at &&, ||, ;, |, & and line breaks outside quotes. Heredoc bodies and PowerShell
 * here-strings are left out.
 */
function splitSegments(command) {
  const segments = [];
  const heredocs = [];
  let current = '';
  let quote = '';
  const push = () => {
    segments.push(current);
    current = '';
  };
  let i = 0;
  while (i < command.length) {
    const ch = command[i];
    const next = command[i + 1];
    if (quote !== '') {
      current += ch;
      // Inside double quotes bash escapes with \ and PowerShell with `; cmd has no escape ("C:\dir\" ends there)
      if (quote === '"' && ch === escapeCharacter() && next !== undefined) {
        current += next;
        i += 2;
        continue;
      }
      if (ch === quote) {
        quote = '';
      }
      i++;
      continue;
    }
    if (ch === '@' && (next === "'" || next === '"') && /^[ \t]*\r?\n/.test(command.slice(i + 2))) {
      // PowerShell here-string: up to the line that starts with the closing '@ or "@
      const closing = `\n${next}@`;
      const end = command.indexOf(closing, i + 2);
      current += '@here-string';
      i = end < 0 ? command.length : end + closing.length;
      continue;
    }
    if (ch === "'" || ch === '"') {
      quote = ch;
      current += ch;
      i++;
      continue;
    }
    // Line continuation (bash \, PowerShell `): the next line belongs to the same command
    if ((ch === '\\' || ch === '`') && (next === '\n' || (next === '\r' && command[i + 2] === '\n'))) {
      current += ' ';
      i += next === '\r' ? 3 : 2;
      continue;
    }
    // A comment (# at the start of a word) runs to the end of the line
    if (ch === '#' && (i === 0 || /\s/.test(command[i - 1]))) {
      while (i < command.length && command[i] !== '\n' && command[i] !== '\r') {
        i++;
      }
      continue;
    }
    if (ch === '<' && next === '<' && command[i + 2] !== '<') {
      const match = /^<<(-?)[ \t]*(?:'([^']*)'|"([^"]*)"|\\?([A-Za-z_][\w-]*))/.exec(command.slice(i));
      if (match !== null) {
        heredocs.push({ word: match[2] || match[3] || match[4], stripTabs: match[1] === '-' });
        current += '<<heredoc';
        i += match[0].length;
        continue;
      }
    }
    if (ch === '\n' || ch === '\r') {
      push();
      i += ch === '\r' && next === '\n' ? 2 : 1;
      while (heredocs.length > 0) {
        const heredoc = heredocs.shift();
        i = skipHeredocBody(command, i, heredoc.word, heredoc.stripTabs);
      }
      continue;
    }
    if ((ch === '&' && next === '&') || (ch === '|' && next === '|')) {
      push();
      i += 2;
      continue;
    }
    // A single & separates (bash background, PowerShell call operator) except in redirections such as 2>&1 and &>
    // PowerShell: & at the start of a command is the call operator (& "C:\x\dotnet.exe" test), not a separator
    // ... also after = ( { or , : $o = & "x.exe" test, { & "x.exe" test }, (& "x.exe" test)
    const callOperator = ch === '&' && dialect === 'pwsh' && /(^|[=({,])\s*$/.test(current);
    if (ch === ';' || ch === '|' || (ch === '&' && !callOperator && command[i - 1] !== '>' && next !== '>')) {
      push();
      i++;
      continue;
    }
    current += ch;
    i++;
  }
  push();
  return segments;
}

/** The words of a segment, quotes kept. */
function words(segment) {
  // A word is a run of quoted parts and other non-blank characters, so that a quote may start inside it:
  // msg="a b", -p:Foo="a b", $x="dotnet test"
  const pattern =
    dialect === 'bash'
      ? /(?:"(?:[^"\\]|\\.)*"?|'[^']*'?|\\.|[^\s"'\\])+/g
      : dialect === 'pwsh'
        ? /(?:"(?:[^"`]|`.)*"?|'(?:[^']|'')*'?|`.|[^\s"'`])+/g
        : /(?:"[^"]*"?|[^\s"])+/g;
  return segment.match(pattern) || [];
}

/**
 * The shell dialect the command is written in: 'bash' (Claude Code Bash tool), 'pwsh' (Claude Code PowerShell tool,
 * Antigravity) or 'cmd'. It decides the escape character in double quotes and how quoted words end.
 */
let dialect = 'bash';

function escapeCharacter() {
  return dialect === 'bash' ? '\\' : dialect === 'pwsh' ? '`' : '';
}

/** Removes the quotes around a word. */
function unquote(word) {
  return word.replace(/^["']|["']$/g, '');
}

/** A program name without directory, extension and case: "C:\x\dotnet.exe" -> dotnet. */
function programName(word) {
  return unquote(word)
    .replace(/^.*[\\/]/, '')
    .replace(/\.(exe|cmd|bat)$/i, '')
    .toLowerCase();
}

/** Words that run the rest of the segment: launch prefixes and the keywords that open a shell body. */
const LaunchPrefixes = [
  '&',
  '.', // PowerShell dot-source, bash source: . "C:\x\dotnet.exe" test
  '!',
  'time',
  'exec',
  'nohup',
  'command',
  'call',
  'then',
  'do',
  'else',
  'elif',
  'if',
  'while',
  'until',
  '{',
];

/** Drops launch prefixes (VAR=x, time, env, nohup, timeout, ...) from the front of a word list. */
function dropPrefixes(list) {
  let rest = list;
  for (;;) {
    if (rest.length === 0) {
      return rest;
    }
    const head = rest[0];
    const name = programName(head);
    // PowerShell assignment: $out = dotnet test, $x=dotnet build, $env:X = ...
    const assignment = /^\$[\w:.]+\s*[+]?=(.*)$/.exec(head);
    if (assignment !== null) {
      rest = assignment[1] === '' ? rest.slice(1) : [assignment[1], ...rest.slice(1)];
      continue;
    }
    if (/^\$[\w:.]+$/.test(head) && /^[+]?=$/.test(rest[1] || '')) {
      rest = rest.slice(2);
      continue;
    }
    if (/^[A-Za-z_][A-Za-z0-9_]*=/.test(head) || LaunchPrefixes.includes(name)) {
      rest = rest.slice(1);
    } else if (['env', 'xargs', 'sudo', 'nice', 'stdbuf', 'ionice'].includes(name)) {
      rest = rest.slice(1);
      while (rest.length > 0 && rest[0].startsWith('-')) {
        rest = rest.slice(['-u', '-C', '-S', '-n', '-c', '--chdir', '--unset', '--user'].includes(rest[0]) ? 2 : 1);
      }
    } else if (name === 'timeout') {
      rest = rest.slice(1);
      while (rest.length > 0 && rest[0].startsWith('-')) {
        rest = rest.slice(['-k', '-s', '--signal', '--kill-after'].includes(rest[0]) ? 2 : 1);
      }
      rest = rest.slice(1); // the duration
    } else {
      return rest;
    }
  }
}

/** Start-Process [-FilePath] <program> [[-ArgumentList] <args>] ...: the command it starts. */
function startProcessCommand(list) {
  let program = '';
  let argumentList = '';
  for (let i = 1; i < list.length; i++) {
    const option = list[i].toLowerCase();
    if (option === '-filepath') {
      program = list[++i] || '';
    } else if (option === '-argumentlist' || option === '-args') {
      argumentList = unquote(list[++i] || '');
    } else if (option.startsWith('-')) {
      i++;
    } else if (program === '') {
      program = list[i];
    } else if (argumentList === '') {
      argumentList = unquote(list[i]);
    }
  }
  return `${unquote(program)} ${argumentList}`;
}

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

/**
 * The index of the argument after a shell's command option (cmd /c or /k; pwsh / powershell -c or -Command; bash / sh
 * -c, also clustered as -lc), or -1 when the word list does not run a shell command.
 */
function shellCommandIndex(name, list) {
  for (let i = 1; i < list.length; i++) {
    const arg = unquote(list[i]);
    if (name === 'cmd' && /^\/[ck]$/i.test(arg)) {
      return i + 1;
    }
    if (/^(pwsh|powershell)$/.test(name) && /^-(c|command)$/i.test(arg)) {
      return i + 1;
    }
    if (/^(bash|sh|zsh)$/.test(name) && /^-[a-z]*c[a-z]*$/.test(arg)) {
      return i + 1;
    }
  }
  return -1;
}

/** The first reason of a nested command, or null. */
function nestedReason(command) {
  const reasons = check(command);
  return reasons.length > 0 ? reasons[0] : null;
}

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
    calledInGroup || raw.slice(0, Math.max(raw.length - list.length, 0)).some((word) => word === '&' || word === '.');
  if (dialect === 'pwsh' && /^["']/.test(list[0]) && !called) {
    return null;
  }
  // A PowerShell body after the condition: if (...) { dotnet test }, foreach (...) { ... }
  const brace = list.findIndex((word, index) => index > 0 && (word === '{' || word.endsWith('{')));
  if (brace > 0 && !/^["']/.test(list[brace])) {
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
    return nestedReason(list.slice(1).map(unquote).join(' '));
  }
  const shellIndex = shellCommandIndex(name, list);
  if (shellIndex > 0) {
    const inner = list.slice(shellIndex).map(unquote).join(' ');
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
  if (name === 'msbuild') {
    return 'msbuild';
  }
  if (name === 'just') {
    const recipe = justRecipe(list);
    return JustRecipes.test(recipe) ? `just ${recipe}` : null;
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
        ));
    if (NodeScripts.test(script) && !helper) {
      return script.replace(/^.*[\\/]/, '');
    }
  }
  return null;
}

/** True when the segment runs build-queue.js (which queues the command quoted in it) or is a git / gh command. */
function isExempt(segment) {
  const list = dropPrefixes(words(segment.trim()));
  if (list.length === 0) {
    return true;
  }
  const name = programName(list[0]);
  if (name === 'git' || name === 'gh') {
    return true;
  }
  return name === 'node' && /(^|[\\/])build-queue\.js$/.test(unquote(list[1] || ''));
}

/**
 * The bodies of the command substitutions of one segment (comments, heredocs and here-strings already removed): $( ... )
 * outside single quotes, also inside double quotes, and PowerShell's @( ... ) outside quotes.
 */
function substitutions(segment) {
  const bodies = [];
  let quote = '';
  for (let i = 0; i < segment.length; i++) {
    const ch = segment[i];
    if (quote === '"' && ch === escapeCharacter()) {
      i++;
    } else if (ch === "'" && quote !== '"') {
      quote = quote === "'" ? '' : "'";
    } else if (ch === '"' && quote !== "'") {
      quote = quote === '"' ? '' : '"';
    } else if (quote !== "'" && segment[i + 1] === '(' && (ch === '$' || (ch === '@' && quote === '' && dialect === 'pwsh'))) {
      const command = segment;
      let depth = 1;
      let j = i + 2;
      while (j < command.length && depth > 0) {
        if (command[j] === '(') {
          depth++;
        } else if (command[j] === ')') {
          depth--;
        }
        j++;
      }
      bodies.push(command.slice(i + 2, depth === 0 ? j - 1 : j));
      i = j - 1;
    }
  }
  return bodies;
}

/**
 * The reasons the command needs the queue (empty when it may run as it is). `kind` is the shell dialect ('bash',
 * 'pwsh', 'cmd'); nested commands keep the dialect of their caller unless a shell wrapper names another.
 */
function check(command, kind = dialect) {
  const saved = dialect;
  dialect = kind;
  try {
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
  } finally {
    dialect = saved;
  }
}

function denialText(reasons) {
  return (
    `${reasons.join(', ')} must run through the build queue, so that parallel agents do not oversubscribe the machine:\n` +
    '  node scripts/agent/build-queue.js run -- "<command>"\n' +
    '  node scripts/agent/build-queue.js run --gpu -- "<command>"   (uses the GPU; replaces gpu-lock.js)\n' +
    'The queue waits for a free slot and then runs the command here. `node scripts/agent/build-queue.js status` lists ' +
    'the jobs. Long waits: run it in the background. Text that only names such a command (a message, a search ' +
    'pattern) passes when it is quoted. The queued command runs in the shell you call `run` from (Git Bash, PowerShell; ' +
    '`--shell bash|pwsh|cmd|sh` to choose), so write it as you would here.'
  );
}

function runHook() {
  let input = '';
  process.stdin.setEncoding('utf8');
  process.stdin.on('data', (chunk) => (input += chunk));
  process.stdin.on('end', () => {
    let command = '';
    let antigravity = false;
    let kind = 'bash';
    try {
      const payload = JSON.parse(input);
      // Antigravity: {"toolCall": {"name": "run_command", "args": {"CommandLine": ...}}}
      // Claude Code: {"tool_input": {"command": ...}}
      antigravity = payload.toolCall !== undefined;
      command = antigravity
        ? String((payload.toolCall.args && payload.toolCall.args.CommandLine) || '')
        : String((payload.tool_input && payload.tool_input.command) || '');
      // Antigravity runs run_command in PowerShell; Claude Code names its tool
      kind = antigravity || payload.tool_name === 'PowerShell' ? 'pwsh' : 'bash';
    } catch (error) {
      process.stderr.write(`check-queued-command --hook: invalid hook input: ${error.message}\n`);
      process.exit(1);
    }
    const reasons = check(command, kind);
    if (reasons.length > 0 && antigravity) {
      process.stdout.write(JSON.stringify({ decision: 'deny', reason: denialText(reasons) }));
    } else if (reasons.length > 0) {
      process.stdout.write(
        JSON.stringify({
          hookSpecificOutput: {
            hookEventName: 'PreToolUse',
            permissionDecision: 'deny',
            permissionDecisionReason: denialText(reasons),
          },
        }),
      );
    }
    process.exit(0);
  });
}

function selfTest() {
  const assert = require('assert');
  const denied = [
    'dotnet test tests/X.csproj',
    'cd tmp/w && dotnet build Henicos.slnx -nodeReuse:false',
    'DiffEngine_Disabled=true dotnet test',
    'just test --filter Foo > tmp/log.txt 2>&1',
    'just test-category Runtime',
    'just -f justfile test',
    'node scripts/agent/gpu-lock.js -- "dotnet test X"',
    'node scripts/human/test-stability.js --count 3',
    '& dotnet test',
    '"C:\\Program Files\\dotnet\\dotnet.exe" build',
    'echo start; dotnet msbuild -t:Restore',
    'git status && dotnet test',
    'echo build-queue.js; dotnet test',
    'timeout 600 dotnet test',
    'nohup dotnet build &',
    '(dotnet build)',
    'cmd /c "dotnet test x"',
    'pwsh -c "dotnet build"',
    'bash -c "cd x && just test"',
    'Start-Process dotnet -ArgumentList "test x" -Wait',
    'dotnet watch test',
    'dotnet -p:Foo="a b" test',
    'just args="a b" test',
    'FOO="a b" dotnet test',
    '(just test)',
    '(msbuild)',
    'dotnet tool run dotnet-stryker',
    'dotnet tool exec dotnet-stryker',
    'dotnet dotnet-stryker',
    'dotnet dnx dotnet-stryker',
    'dnx dotnet-stryker',
    'dnx dotnet-stryker@4.0.0',
    'dotnet dnx --yes dotnet-stryker@4.0.0',
    'dotnet tool exec dotnet-stryker@4.0.0',
    'dotnet tool run --tool-manifest m.json dotnet-stryker',
    'dnx --source https://x dotnet-stryker',
    'dotnet tool run dotnet-stryker --help',
    'dotnet-stryker --since main',
    'node scripts/human/test-stability.js -- --dry-run',
    'node scripts/human/fuzz-differential.js --help',
    'nice -n 10 dotnet build',
    'stdbuf -oL dotnet test',
    'env -C tmp dotnet test',
    'do { dotnet test } while ($false)',
    'dotnet publish -c Release',
    'dotnet vstest x.dll',
    'msbuild Henicos.slnx',
    "cat > tmp/a.md <<'EOF'\nnot a command\nEOF\ndotnet test",
    'Set-Location x; dotnet test',
    'dotnet \\\n  test --filter X',
    'dotnet `\r\n  build x',
    'powershell -NoProfile -Command "dotnet test"',
    'pwsh -NoProfile -c "dotnet build"',
    'bash -lc "dotnet test"',
    'cmd.exe /d /c "dotnet test"',
    'if true; then dotnet test; fi',
    'for p in a b; do dotnet build $p; done',
    'if ($x) { dotnet test }',
    'foreach ($p in $list) { dotnet build $p }',
    'dotnet stryker',
    'echo $(dotnet test)',
    'x=$(dotnet build)',
    'node --max-old-space-size=4096 scripts/human/test-stability.js',
    './scripts/human/long-checks.js',
    'Start-Process dotnet "test x"',
    'dotnet -d build',
    'sudo dotnet test',
    'ls | xargs -n1 dotnet build',
    'Invoke-Expression "dotnet test"',
    '! dotnet build',
  ];
  const allowed = [
    'node scripts/agent/build-queue.js run -- "dotnet test X"',
    'node scripts/agent/build-queue.js run --gpu -- "just test-category Runtime"',
    'cd tmp/w && node scripts/agent/build-queue.js run -- "dotnet build && dotnet test"',
    'git commit -m "run dotnet test before merging"',
    'gh issue create --body "just test fails"',
    'dotnet build-server shutdown',
    'dotnet --info',
    'msg="fix: dotnet build failure"; git commit -m "$msg"',
    'dotnet run --project src/Compiler/Kassyi.GSharp.Compiler.Generators -- demangle log.txt',
    'node scripts/agent/gpu-lock.js --help',
    'node scripts/agent/gpu-lock.js --self-test',
    'node --check scripts/agent/gpu-lock.js',
    'dotnet test --help',
    'dotnet tool list',
    'dotnet tool restore',
    'dotnet tool install -g dotnet-counters',
    'dnx dotnet-counters',
    'node scripts/human/long-checks.js --dry-run',
    'just env-info',
    'just --list',
    'grep -n "dotnet test" justfile',
    'grep -E "foo|dotnet test" justfile',
    'rg "x; dotnet build" docs',
    'echo "a && dotnet test"',
    "cat > tmp/notes.md <<'EOF'\ndotnet build Henicos.slnx\njust test\nEOF",
    'cat > tmp/notes.md <<-EOF\n\tdotnet build x\n\tEOF',
    "$body = @'\ndotnet test\n'@\ngh issue comment 1 --body $body",
    '$msg = "fix; dotnet test now"; git commit -m $msg',
    'node -e "const a = 1;\nconsole.log(a)"',
    'node scripts/agent/check-issue-refs.js --text "x"',
    'git log --oneline | head -5',
    'ls 2>&1 | grep x',
    'rm -rf obj # intent: clean first; dotnet build checks it',
    'ls # && dotnet test',
    'cmd /c dir',
    "git commit -m \"$(cat <<'EOF'\nfix: run dotnet test (and just test) first\nEOF\n)\"",
    'echo "$(date) done"',
  ];
  for (const command of denied) {
    assert.notDeepStrictEqual(check(command), [], `should deny: ${command}`);
  }
  for (const command of allowed) {
    assert.deepStrictEqual(check(command), [], `should allow: ${command}`);
  }
  // PowerShell (Claude Code PowerShell tool, Antigravity): ` escapes in double quotes, \ does not
  const deniedPowerShell = [
    'Set-Location "tmp\\"; dotnet test',
    'Remove-Item -Recurse -Force "obj\\"; dotnet build',
    'Write-Output "it\'s starting"; Write-Output "$(dotnet test)"',
    '$out = dotnet test',
    '$x=dotnet build',
    '$res = just test',
    '@(dotnet test)',
    '$r = @(dotnet test)',
    '$o = (dotnet test)',
    '$env:CI = "true"; dotnet test',
    '& "C:\\Program Files\\dotnet\\dotnet.exe" test',
    '$o = (just test)',
    '$o = & "C:\\Program Files\\dotnet\\dotnet.exe" test',
    'if ($x) { & "C:\\x\\dotnet.exe" test }',
    '(& "C:\\x\\dotnet.exe" test)',
    '$o = (& "C:\\x\\dotnet.exe" build)',
    'Get-ChildItem | ForEach-Object { & "C:\\x\\dotnet.exe" test $_ }',
    '. "C:\\x\\dotnet.exe" test',
    'git commit -m "$(dotnet test)"',
  ];
  const allowedPowerShell = [
    'Test-Path "src\\Compiler\\"; Write-Output "fix; dotnet test"',
    "gh pr comment 1 --body @'\nIt's fine. Run $(dotnet test) later.\n'@",
    'git status # see $(dotnet test)',
    'if ($true) { Write-Output "a `" { dotnet test `"" }',
    "Write-Output 'it''s $(dotnet test)'",
    '$x = 1; Get-ChildItem',
    '$msg="dotnet test"',
    '$tool = "msbuild"',
  ];
  for (const command of deniedPowerShell) {
    assert.notDeepStrictEqual(check(command, 'pwsh'), [], `should deny in PowerShell: ${command}`);
  }
  for (const command of allowedPowerShell) {
    assert.deepStrictEqual(check(command, 'pwsh'), [], `should allow in PowerShell: ${command}`);
  }
  assert.notDeepStrictEqual(check('echo "x\\"; dotnet test', 'cmd'), [], 'cmd: "x\\" ends the quote');
  assert.notDeepStrictEqual(check('just profile=cpu fuzz-diff'), [], 'just variable before the recipe');
  process.stdout.write('check-queued-command self-test passed\n');
}

const args = process.argv.slice(2);
if (args[0] === '--hook') {
  runHook();
} else if (args[0] === '--text' && (args.length === 2 || (args.length === 4 && args[2] === '--dialect'))) {
  const reasons = check(args[1], args.length === 4 ? args[3] : 'bash');
  process.stdout.write(reasons.length === 0 ? 'allowed\n' : `${denialText(reasons)}\n`);
  process.exit(reasons.length === 0 ? 0 : 1);
} else if (args[0] === '--self-test') {
  selfTest();
} else if (args[0] === '--help' || args[0] === '-h') {
  process.stdout.write(`${usage}\n`);
} else {
  process.stderr.write(`${usage}\n`);
  process.exit(2);
}
