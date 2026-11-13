'use strict';

// The shell reader of check-queued-command.js: how a command line is read in the dialect of the tool that runs it.
//
// The command is read in the dialect of the tool that runs it: bash for the Claude Code Bash tool, PowerShell for its
// PowerShell tool and for Antigravity (` escapes in double quotes, \ does not; '' in single quotes), cmd inside cmd /c.
// It is split into segments at &&, ||, ;, |, & and line breaks outside quotes, after joining continued lines
// (\ or ` at the end of a line); comments, heredoc bodies and PowerShell here-strings are skipped, since their text is
// data, and the bodies of command substitutions $( ... ) and PowerShell @( ... ) are returned to be checked as
// commands. The words of a segment are read with their quotes; launch prefixes, assignments, Start-Process and shell
// wrappers are taken apart here, and command-rules.js decides what the program found that way may do.
//
// Usage: require('./shell-reader.js'); withDialect(kind, body) runs `body` with `kind` as the current dialect.

/**
 * The shell dialect the command is written in: 'bash' (Claude Code Bash tool), 'pwsh' (Claude Code PowerShell tool,
 * Antigravity) or 'cmd'. It decides the escape character in double quotes and how quoted words end.
 */
let dialect = 'bash';

function escapeCharacter() {
  return dialect === 'bash' ? '\\' : dialect === 'pwsh' ? '`' : '';
}

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
  // msg="a b", -p:Foo="a b", $x="dotnet test"; in bash a substitution belongs to its word too:
  // LD_PRELOAD=$(g++ -print-file-name=libasan.so) (its body is checked on its own, see substitutions)
  const pattern =
    dialect === 'bash'
      ? /(?:"(?:[^"\\]|\\.)*"?|'[^']*'?|\$\((?:[^()]|\([^()]*\))*\)|\\.|[^\s"'\\])+/g
      : dialect === 'pwsh'
        ? /(?:"(?:[^"`]|`.)*"?|'(?:[^']|'')*'?|`.|[^\s"'`])+/g
        : /(?:"[^"]*"?|[^\s"])+/g;
  const list = segment.match(pattern) || [];
  // PowerShell: the call operator may touch its operand: &"dotnet" test
  return dialect === 'pwsh'
    ? list.flatMap((word) => (/^&["'$\w.\\/]/.test(word) ? ['&', word.slice(1)] : [word]))
    : list;
}

/** Removes the quotes around a word. */
function unquote(word) {
  return word.replace(/^["']|["']$/g, '');
}

/**
 * The text a quoted argument passes on, escapes undone in the current dialect: the command string of a shell wrapper,
 * as in pwsh -Command "& `"C:\x\dotnet.exe`" test" or bash -c "dotnet build \"x y\"".
 */
function argumentText(word) {
  // A word may join quoted and bare parts ("cd x --filter "A|B), and a cut command may leave a quote open: join the
  // parts as the shell does, quotes removed and escapes undone
  let out = '';
  let i = 0;
  while (i < word.length) {
    const ch = word[i];
    if (ch === '"') {
      i++;
      while (i < word.length && word[i] !== '"') {
        const escaped =
          (dialect === 'pwsh' && word[i] === '`') ||
          (dialect === 'bash' && word[i] === '\\' && '"\\$`'.includes(word[i + 1]));
        if (escaped && i + 1 < word.length) {
          out += word[i + 1];
          i += 2;
        } else {
          out += word[i++];
        }
      }
      i++;
    } else if (ch === "'" && dialect !== 'cmd') {
      i++;
      while (i < word.length) {
        if (word[i] === "'" && dialect === 'pwsh' && word[i + 1] === "'") {
          out += "'";
          i += 2;
        } else if (word[i] === "'") {
          break;
        } else {
          out += word[i++];
        }
      }
      i++;
    } else if (((dialect === 'pwsh' && ch === '`') || (dialect === 'bash' && ch === '\\')) && i + 1 < word.length) {
      out += word[i + 1];
      i += 2;
    } else {
      out += ch;
      i++;
    }
  }
  return out;
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
  '(', // a group, subshell or subexpression whose opener stands alone: $o = ( dotnet test ), time ( dotnet build )
  '{',
  '@(',
  '$(',
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
    // An opener stuck to the program: time (timeout 600 dotnet build), {dotnet test}
    if (/^[({]+[^({]/.test(head)) {
      rest = [head.replace(/^[({]+/, ''), ...rest.slice(1)];
      continue;
    }
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

/**
 * The bodies of the command substitutions of one segment (comments, heredocs and here-strings already removed):
 * $( ... ) outside single quotes, also inside double quotes, and PowerShell's @( ... ) outside quotes.
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
    } else if (
      quote !== "'" &&
      segment[i + 1] === '(' &&
      (ch === '$' || (ch === '@' && quote === '' && dialect === 'pwsh'))
    ) {
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

/** The dialect the reader currently uses. */
function currentDialect() {
  return dialect;
}

/** Runs `body` with `kind` as the dialect and restores the previous one afterwards. */
function withDialect(kind, body) {
  const saved = dialect;
  dialect = kind;
  try {
    return body();
  } finally {
    dialect = saved;
  }
}

module.exports = {
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
};
