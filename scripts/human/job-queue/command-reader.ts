// The shared reader of a command line for the job queue. The plain form (rawWords, segmentsOf, variablePrefix,
// segmentParts) is what the build plan of run --gpu accepts; readCommand reads further, for the estimate keys: it
// also splits at ||, ; and |, drops a pipe into a filter and a trailing comment, and says when a command cannot be
// read (a substitution, a heredoc or input redirection, a group, a background job, an unbalanced quote).

import type { ShellName } from "./constants.ts";

/** The words of one segment as written (quotes kept), or null when it holds syntax a plain command does not. */
export function rawWords(segment: string, shell: ShellName): string[] | null {
  const powerShell = shell === "pwsh" || shell === "powershell";
  // Outside quotes: separators, redirections, groups, substitutions, escapes and comments end the plain form
  const special = shell === "cmd" ? /[&|<>()^\n\r]/ : powerShell ? /[&|<>(){};$@`#\n\r]/ : /[&|<>(){};$`\\#*?[\n\r]/;
  const list: string[] = [];
  let word = "";
  let quote = "";
  for (let i = 0; i < segment.length; i++) {
    const ch = segment.charAt(i);
    if (quote !== "") {
      word += ch;
      if (ch === "\n" || ch === "\r" || (quote === '"' && (ch === "`" || ch === "\\") && shell !== "cmd")) {
        return null; // an escape inside double quotes: keep to the plain form
      }
      if (ch === quote) {
        quote = "";
      }
    } else if (ch === '"' || (ch === "'" && shell !== "cmd")) {
      quote = ch;
      word += ch;
    } else if (ch === " " || ch === "\t") {
      if (word !== "") {
        list.push(word);
        word = "";
      }
    } else if (special.test(ch)) {
      return null;
    } else {
      word += ch;
    }
  }
  if (quote !== "") {
    return null;
  }
  if (word !== "") {
    list.push(word);
  }
  return list;
}

/** The segments of the command at && outside quotes (rawWords then refuses any other syntax in them). */
export function segmentsOf(command: string, shell: ShellName): string[] {
  const segments: string[] = [];
  let quote = "";
  let start = 0;
  for (let i = 0; i < command.length; i++) {
    const ch = command.charAt(i);
    if (quote !== "") {
      quote = ch === quote ? "" : quote;
    } else if (ch === '"' || (ch === "'" && shell !== "cmd")) {
      quote = ch;
    } else if (ch === "&" && command[i + 1] === "&") {
      segments.push(command.slice(start, i));
      start = i + 2;
      i++;
    }
  }
  segments.push(command.slice(start));
  return segments.map((segment) => segment.trim());
}

/** A word without its quotes, for reading an option name or a file extension. */
export function bare(word: string): string {
  return word.replace(/["']/g, "");
}

// Assignments and redirection targets: a quoted string without escapes or substitutions, or a plain word
const Unsafe = "$`\\\\"; // in a character class: $, backtick, backslash
const PlainValue = `(?:"[^"${Unsafe}]*"|'[^']*'|[^\\s"'${Unsafe};&|<>()*?]*)`;
export const Assignment = `[A-Za-z_]\\w*=${PlainValue}`;
const Target = `(?:"[^"${Unsafe}]*"|'[^']*'|[^\\s"'${Unsafe};&|<>()*?]+)`;

/** One output redirection at the end of a segment, after a space. */
export function redirectionAtEnd(shell: ShellName): RegExp {
  if (shell === "pwsh" || shell === "powershell") {
    return new RegExp(String.raw`\s(?:[1-6*]?>>?\s*${Target}|[2-6*]>&1)$`);
  }
  if (shell === "cmd") {
    return new RegExp(String.raw`\s(?:[12]?>>?\s*(?:"[^"]*"|[^\s"&|<>()^%]+)|2>&1)$`);
  }
  return new RegExp(String.raw`\s(?:[12&]?>>?\s*${Target}|[12]>&[12])$`);
}

/** The prefix that sets variables for the whole command (export A=b; / $env:A='b';), or "". */
export function variablePrefix(command: string, shell: ShellName): string {
  const pattern =
    shell === "pwsh" || shell === "powershell"
      ? String.raw`^(?:\$env:[A-Za-z_]\w*\s*=\s*(?:'[^']*'|"[^"$\`]*")\s*;\s*)+`
      : shell === "cmd"
        ? "^(?!)"
        : String.raw`^(?:export(?:\s+${Assignment})+\s*;\s*)+`;
  return new RegExp(pattern).exec(command)?.[0] ?? "";
}

/** A segment split into its leading assignments, its command and its trailing output redirections. */
export interface SegmentParts {
  env: string;
  core: string;
  redirections: string[];
}

export function segmentParts(segment: string, shell: ShellName): SegmentParts {
  let core = segment;
  const redirections: string[] = [];
  const atEnd = redirectionAtEnd(shell);
  for (let match = atEnd.exec(core); match !== null; match = atEnd.exec(core)) {
    redirections.unshift(match[0].trim());
    core = core.slice(0, match.index).trimEnd();
  }
  const env =
    shell === "bash" || shell === "sh" ? (new RegExp(String.raw`^(?:${Assignment}\s+)+`).exec(core)?.[0] ?? "") : "";
  return { env: env.trim(), core: core.slice(env.length), redirections };
}

/** One command of the line: the words without quotes, with the leading assignments and the redirections taken out. */
export interface ReadStep {
  words: string[];
}

const Separators = new Set([";", "\n", "\r"]);

/**
 * Reads a command line, in the bash and the PowerShell forms alike: the steps at `&&`, `||`, `;`, a line break and `|`
 * outside quotes, each as its words without quotes. A pipe into a filter (the stage after `|`) is not work and is left
 * out; so are a trailing comment, leading `A=b` assignments and output redirections (`> f`, `2>&1`, `&> f`, `*> f`).
 * Null when the line cannot be read as that: an unbalanced quote, a substitution, a backtick, a heredoc or input
 * redirection, a group, a background `&`.
 */
export function readCommand(command: string): ReadStep[] | null {
  const steps: ReadStep[] = [];
  let words: string[] = [];
  let word = "";
  let inWord = false;
  let quote = "";
  let filter = false; // the step being read is the stage after a pipe
  const endWord = (): void => {
    if (inWord) {
      words.push(word);
    }
    word = "";
    inWord = false;
  };
  const endStep = (nextIsFilter: boolean): void => {
    endWord();
    const step = stripStep(words);
    if (!filter && step.words.length > 0) {
      steps.push(step);
    }
    words = [];
    filter = nextIsFilter;
  };
  for (let i = 0; i < command.length; i++) {
    const ch = command.charAt(i);
    const next = command.charAt(i + 1);
    if (quote !== "") {
      if (ch === quote) {
        quote = "";
      } else if (ch === "`" && quote === '"') {
        return null;
      } else if (ch === "$" && next === "(" && quote === '"') {
        return null;
      } else {
        word += ch;
      }
    } else if (ch === '"' || ch === "'") {
      quote = ch;
      inWord = true;
    } else if (ch === " " || ch === "\t") {
      endWord();
    } else if (ch === "#" && !inWord) {
      while (i + 1 < command.length && command.charAt(i + 1) !== "\n") {
        i++;
      }
    } else if (ch === "&" && next === "&") {
      endStep(false);
      i++;
    } else if (ch === "|") {
      const pair = next === "|";
      endStep(!pair);
      i += pair ? 1 : 0;
    } else if (Separators.has(ch)) {
      endStep(false);
    } else if (ch === "&") {
      // part of a redirection (2>&1, &>) or a background job
      if (!(next === ">" || command.charAt(i - 1) === ">")) {
        return null;
      }
      word += ch;
      inWord = true;
    } else if (ch === "<" || ch === "`" || ch === "(" || ch === ")" || (ch === "$" && next === "(")) {
      return null;
    } else {
      word += ch;
      inWord = true;
    }
  }
  if (quote !== "") {
    return null;
  }
  endStep(false);
  return steps;
}

const AssignmentWord = /^[A-Za-z_]\w*=/;
const RedirectionWord = /^(?:\d|&|\*)?>>?(.*)$/;

/** The words of a step without the leading `A=b` assignments and without the output redirections. */
function stripStep(all: string[]): ReadStep {
  let start = 0;
  while (start < all.length && AssignmentWord.test(all[start] ?? "")) {
    start++;
  }
  const words: string[] = [];
  for (let i = start; i < all.length; i++) {
    const word = all[i] ?? "";
    const redirection = RedirectionWord.exec(word);
    if (redirection === null) {
      words.push(word);
    } else if ((redirection[1] ?? "") === "") {
      i++; // the target is the next word
    }
  }
  return { words };
}
