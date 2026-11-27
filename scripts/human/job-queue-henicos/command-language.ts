// Small allow-listed command language for GPU turn sharing.
// Every token is drawn from a closed set of typed choices with no free-form ARG.

import type { ShellName } from "../job-queue/constants.ts";
import { CommandShell } from "../job-queue/shell.ts";

/** Maximum accepted command length in characters (well below Git Bash's ~8 KiB truncation). */
export const MaxCommandLength = 4000;

/** Environment variable names allowed as a leading prefix before `dotnet test`. */
export const AllowedPrefixEnvNames = ["DiffEngine_Disabled", "DOTNET_CLI_UI_LANGUAGE"] as const;
export type AllowedPrefixEnvName = (typeof AllowedPrefixEnvNames)[number];

/** Mutation testing phases allowed in `just mutate <phase>` and `node scripts/human/mutate.js --phase <phase> --`. */
export const MutatePhases = ["gpumath", "compiler", "compiler-cuda"] as const;
export type MutatePhase = (typeof MutatePhases)[number];

/** Closed set of values allowed in leading environment prefixes. */
export const AllowedPrefixEnvValues = ["true", "false", "1", "0", "en", "en-US"] as const;
export type AllowedPrefixEnvValue = (typeof AllowedPrefixEnvValues)[number];

/** Allowed filter keys in `--filter` terms. */
export const AllowedFilterKeys = [
  "Category",
  "FullyQualifiedName",
  "Name",
  "ClassName",
  "Priority",
  "Platform",
  "Scenario",
] as const;
export type AllowedFilterKey = (typeof AllowedFilterKeys)[number];

export interface EnvBinding {
  name: AllowedPrefixEnvName;
  value: AllowedPrefixEnvValue;
}

export type TestOption =
  | { kind: "no-build" }
  | { kind: "no-node-reuse" }
  | { kind: "disable-build-servers" }
  | { kind: "nologo" }
  | { kind: "configuration"; flag: "-c" | "--configuration"; value: "Debug" | "Release" }
  | {
      kind: "verbosity";
      flag: "-v" | "--verbosity";
      value: "q" | "quiet" | "m" | "minimal" | "n" | "normal" | "d" | "detailed";
    }
  | { kind: "blame-crash" }
  | { kind: "blame-crash-dump-type"; value: "mini" | "full" }
  | { kind: "blame-hang-timeout"; duration: string };

export interface CommandRedirect {
  stdout: string | null;
  append: boolean;
  mergeStderr: boolean;
}

export type SharedCommand =
  | {
      kind: "test";
      env: EnvBinding[];
      project: string | null;
      options: TestOption[];
      filter: string | null;
      logger: string | null;
      redirect: CommandRedirect;
      /** Exact argv tokens after `dotnet test`, in source order (for property testing). */
      argv: string[];
    }
  | {
      kind: "recipe";
      name: string;
    }
  | {
      kind: "mutate";
      phase: MutatePhase;
    };

export interface ParseFailure {
  position: number;
  token: string;
  expected: string;
}

// Printable ASCII only: rejects CR, LF, tabs, control chars, and non-ASCII (e.g. PowerShell smart quotes).
const PrintableAscii = /^[ -~]+$/;
const AllowedNameSet = new Set<string>(AllowedPrefixEnvNames);
const AllowedValueSet = new Set<string>(AllowedPrefixEnvValues);
const AllowedFilterKeySet = new Set<string>(AllowedFilterKeys);
const AllowedFilterKeyLowerSet = new Set<string>(AllowedFilterKeys.map((k) => k.toLowerCase()));
const MutatePhaseSet = new Set<string>(MutatePhases);
const RecipeName = /^[A-Za-z][A-Za-z0-9_-]*$/;
const ProjectSegment = /^[A-Za-z0-9_.]+$/;
const RedirectSegment = /^[A-Za-z0-9_.][A-Za-z0-9_.-]*$/;
const WindowsDeviceName = /^(?:NUL|CON|PRN|AUX|COM\d+|LPT\d+)$/i;
const DurationPattern = /^[0-9]+[sm]$/;
const FilterWord = /^[A-Za-z0-9_.]+$/;
const TrxLoggerPattern = /^trx;LogFileName=[A-Za-z0-9_.-]+\.trx$/;
const ConsoleLoggerPattern = /^console;verbosity=(?:quiet|minimal|normal|detailed)$/;
const Configurations = new Set(["Debug", "Release"]);
const VerbosityValues = new Set(["q", "quiet", "m", "minimal", "n", "normal", "d", "detailed"]);
const DumpTypes = new Set(["mini", "full"]);

function isPosixShell(shell: ShellName): boolean {
  return shell === "bash" || shell === "sh";
}

function skipSpaces(text: string, pos: number): number {
  while (pos < text.length && text.charCodeAt(pos) === 0x20) {
    pos++;
  }
  return pos;
}

function readBareToken(text: string, pos: number): { token: string; nextPos: number } {
  let end = pos;
  while (end < text.length && text.charCodeAt(end) !== 0x20) {
    end++;
  }
  return { token: text.slice(pos, end), nextPos: end };
}

function readQuotedToken(text: string, pos: number, allowSingle: boolean): { value: string; nextPos: number } | null {
  if (pos >= text.length) {
    return null;
  }
  const quote = text.charAt(pos);
  if (quote !== '"' && !(allowSingle && quote === "'")) {
    return null;
  }
  let end = pos + 1;
  while (end < text.length && text.charAt(end) !== quote) {
    end++;
  }
  if (end >= text.length) {
    return null;
  }
  const nextPos = end + 1;
  if (nextPos < text.length && text.charCodeAt(nextPos) !== 0x20) {
    return null;
  }
  return { value: text.slice(pos + 1, end), nextPos };
}

/** Validates a `PROJECT` path (`tests/SEG{/SEG}`, no `.` or `..` segments). */
export function isValidProject(token: string): boolean {
  if (!token.startsWith("tests/")) {
    return false;
  }
  const segments = token.slice("tests/".length).split("/");
  return segments.length > 0 && segments.every((seg) => ProjectSegment.test(seg) && seg !== "." && seg !== "..");
}

/** Validates a `FILTER` expression (`TERM { ("&" | "|") TERM }`, no parentheses or spaces). */
export function isValidFilter(value: string, caseInsensitiveKeys = false): boolean {
  if (value.length === 0) {
    return false;
  }
  const terms = value.split(/[&|]/);
  return terms.every((term) => {
    const m = /^([A-Za-z]+)(!=|!~|=|~)([A-Za-z0-9_.]+)$/.exec(term);
    if (m === null || !FilterWord.test(m[3] as string)) {
      return false;
    }
    const key = m[1] as string;
    return caseInsensitiveKeys ? AllowedFilterKeyLowerSet.has(key.toLowerCase()) : AllowedFilterKeySet.has(key);
  });
}

/**
 * Validates an `RPATH` redirect target: relative, `[A-Za-z]:/`, or `/[a-z]/` under bash and sh only (cmd and
 * PowerShell read `/c/x` as `\c\x` on the current drive). A relative path starts with a letter or `_`, since
 * PowerShell reads a bare `1kb`, `0x10` or `1e2` as a number. No `..`, no segment ending in `.` (Win32 drops it), no
 * Windows device names.
 */
export function isValidRedirectPath(token: string, shell: ShellName): boolean {
  let rest = token;
  if (/^[A-Za-z]:\//.test(rest)) {
    rest = rest.slice(3);
  } else if (/^\/[a-z]\//.test(rest)) {
    if (shell !== "bash" && shell !== "sh") {
      return false;
    }
    rest = rest.slice(3);
  } else if (!/^[A-Za-z_]/.test(rest)) {
    return false;
  }
  if (rest.length === 0) {
    return false;
  }
  const segments = rest.split("/");
  return segments.every((seg) => {
    if (!RedirectSegment.test(seg) || seg.endsWith(".")) {
      return false;
    }
    const base = (seg.split(".")[0] ?? "").toUpperCase();
    return !WindowsDeviceName.test(base);
  });
}

function readPrefixes(
  text: string,
  startPos: number,
  shell: ShellName,
): { env: EnvBinding[]; nextPos: number } | { failure: ParseFailure } {
  const env: EnvBinding[] = [];
  let pos = startPos;

  if (CommandShell.isPowerShell(shell)) {
    while (text.startsWith("$env:", pos)) {
      const m = /^\$env:([A-Za-z0-9_]+)=(?:'([^']*)'|"([^"]*)"); +/.exec(text.slice(pos));
      if (m === null) {
        const tok = readBareToken(text, pos).token;
        return {
          failure: {
            position: pos,
            token: tok,
            expected: `$env:(${AllowedPrefixEnvNames.join("|")})='<value>'; `,
          },
        };
      }
      const name = m[1] as string;
      const value = (m[2] ?? m[3]) as string;
      if (!AllowedNameSet.has(name) || !AllowedValueSet.has(value)) {
        return {
          failure: {
            position: pos,
            token: m[0].trim(),
            expected: `$env:(${AllowedPrefixEnvNames.join("|")})=(${AllowedPrefixEnvValues.join("|")});`,
          },
        };
      }
      env.push({ name: name as AllowedPrefixEnvName, value: value as AllowedPrefixEnvValue });
      pos += m[0].length;
    }
  } else if (isPosixShell(shell)) {
    while (pos < text.length) {
      const m = /^([A-Za-z0-9_]+)=([^ ]+) +/.exec(text.slice(pos));
      if (m === null) {
        break;
      }
      const name = m[1] as string;
      const value = m[2] as string;
      if (!AllowedNameSet.has(name) || !AllowedValueSet.has(value)) {
        return {
          failure: {
            position: pos,
            token: `${name}=${value}`,
            expected: `(${AllowedPrefixEnvNames.join("|")})=(${AllowedPrefixEnvValues.join("|")})`,
          },
        };
      }
      env.push({ name: name as AllowedPrefixEnvName, value: value as AllowedPrefixEnvValue });
      pos += m[0].length;
    }
  }
  return { env, nextPos: pos };
}

type HeadResult =
  | { kind: "test"; nextPos: number }
  | { kind: "recipe"; name: string; nextPos: number }
  | { kind: "mutate"; phase: MutatePhase; nextPos: number }
  | { failure: ParseFailure };

function readHead(text: string, startPos: number, envCount: number): HeadResult {
  const first = readBareToken(text, startPos);
  if (first.token === "dotnet") {
    const afterSpace = skipSpaces(text, first.nextPos);
    if (afterSpace === first.nextPos) {
      return { failure: { position: first.nextPos, token: "", expected: "dotnet test" } };
    }
    const sub = readBareToken(text, afterSpace);
    if (sub.token !== "test") {
      return { failure: { position: afterSpace, token: sub.token, expected: "test" } };
    }
    return { kind: "test", nextPos: sub.nextPos };
  }
  if (first.token === "node" && envCount === 0) {
    // Inner form queued by `just mutate <phase>`: `node scripts/human/mutate.js --phase <phase> --`
    const tokens = text.slice(startPos).split(/ +/);
    if (
      tokens.length === 5 &&
      tokens[1] === "scripts/human/mutate.js" &&
      tokens[2] === "--phase" &&
      MutatePhaseSet.has(tokens[3] ?? "") &&
      tokens[4] === "--"
    ) {
      return { kind: "mutate", phase: tokens[3] as MutatePhase, nextPos: text.length };
    }
    return {
      failure: {
        position: startPos,
        token: text.slice(startPos),
        expected: "node scripts/human/mutate.js --phase <gpumath|compiler|compiler-cuda> --",
      },
    };
  }
  if (first.token === "just" && envCount === 0) {
    const afterSpace = skipSpaces(text, first.nextPos);
    if (afterSpace === first.nextPos) {
      return {
        failure: { position: first.nextPos, token: "", expected: "just <sharedRecipe> or just mutate <phase>" },
      };
    }
    const rec = readBareToken(text, afterSpace);
    if (!RecipeName.test(rec.token)) {
      return { failure: { position: afterSpace, token: rec.token, expected: "<sharedRecipe>" } };
    }
    if (rec.token === "mutate") {
      const phasePos = skipSpaces(text, rec.nextPos);
      if (phasePos === rec.nextPos) {
        return { failure: { position: rec.nextPos, token: "", expected: "gpumath|compiler|compiler-cuda" } };
      }
      const phaseTok = readBareToken(text, phasePos);
      if (!MutatePhaseSet.has(phaseTok.token) || phaseTok.nextPos !== text.length) {
        return {
          failure: {
            position: phasePos,
            token: text.slice(phasePos),
            expected: "gpumath|compiler|compiler-cuda (no extra arguments)",
          },
        };
      }
      return { kind: "mutate", phase: phaseTok.token as MutatePhase, nextPos: phaseTok.nextPos };
    }
    if (rec.nextPos !== text.length) {
      const extra = readBareToken(text, skipSpaces(text, rec.nextPos));
      return {
        failure: {
          position: skipSpaces(text, rec.nextPos),
          token: extra.token,
          expected: "end of command (just recipes take no arguments)",
        },
      };
    }
    return { kind: "recipe", name: rec.token, nextPos: rec.nextPos };
  }
  return {
    failure: {
      position: startPos,
      token: first.token,
      expected: envCount === 0 ? "dotnet test, just <sharedRecipe>, or just mutate <phase>" : "dotnet test",
    },
  };
}

interface ItemsResult {
  project: string | null;
  options: TestOption[];
  filter: string | null;
  logger: string | null;
  argv: string[];
  nextPos: number;
}

function readItems(text: string, startPos: number, shell: ShellName): ItemsResult | { failure: ParseFailure } {
  let pos = startPos;
  let project: string | null = null;
  const options: TestOption[] = [];
  const seenOptions = new Set<TestOption["kind"]>();
  let filter: string | null = null;
  let logger: string | null = null;
  const argv: string[] = [];

  const addOption = (opt: TestOption, tokens: string[], optPos: number, rawTok: string): ParseFailure | null => {
    if (seenOptions.has(opt.kind)) {
      return { position: optPos, token: rawTok, expected: `at most one ${opt.kind} option` };
    }
    seenOptions.add(opt.kind);
    options.push(opt);
    argv.push(...tokens);
    return null;
  };

  while (pos < text.length) {
    const next = skipSpaces(text, pos);
    if (next === pos) {
      return { failure: { position: pos, token: text.slice(pos), expected: "space" } };
    }
    pos = next;
    if (pos >= text.length) {
      break;
    }

    // Stop at redirect start
    const peek = readBareToken(text, pos);
    if (peek.token === ">" || peek.token === ">>" || peek.token === "2>&1") {
      return { project, options, filter, logger, argv, nextPos: pos };
    }

    const tok = peek.token;
    if (tok === "--filter") {
      if (filter !== null) {
        return { failure: { position: pos, token: tok, expected: "at most one --filter" } };
      }
      const valPos = skipSpaces(text, peek.nextPos);
      if (valPos === peek.nextPos) {
        return { failure: { position: peek.nextPos, token: "", expected: 'quoted filter ("KEY=WORD")' } };
      }
      const quoted = readQuotedToken(text, valPos, shell !== "cmd");
      if (quoted === null || !isValidFilter(quoted.value)) {
        const bad = readBareToken(text, valPos).token;
        return {
          failure: {
            position: valPos,
            token: bad,
            expected: 'quoted FILTER (e.g. "Category!=Stress&Category!=Benchmark&Category!=GpuTrap")',
          },
        };
      }
      filter = quoted.value;
      argv.push("--filter", filter);
      pos = quoted.nextPos;
      continue;
    }

    if (tok === "--logger") {
      if (logger !== null) {
        return { failure: { position: pos, token: tok, expected: "at most one --logger" } };
      }
      const valPos = skipSpaces(text, peek.nextPos);
      if (valPos === peek.nextPos) {
        return { failure: { position: peek.nextPos, token: "", expected: "quoted logger" } };
      }
      const quoted = readQuotedToken(text, valPos, false);
      if (quoted === null || (!TrxLoggerPattern.test(quoted.value) && !ConsoleLoggerPattern.test(quoted.value))) {
        const bad = readBareToken(text, valPos).token;
        return {
          failure: {
            position: valPos,
            token: bad,
            expected: '"trx;LogFileName=<name>.trx" or "console;verbosity=normal|detailed|minimal|quiet"',
          },
        };
      }
      logger = quoted.value;
      argv.push("--logger", logger);
      pos = quoted.nextPos;
      continue;
    }

    if (tok === "--no-build") {
      const err = addOption({ kind: "no-build" }, [tok], pos, tok);
      if (err !== null) return { failure: err };
      pos = peek.nextPos;
      continue;
    }
    if (tok === "-nodeReuse:false") {
      const err = addOption({ kind: "no-node-reuse" }, [tok], pos, tok);
      if (err !== null) return { failure: err };
      pos = peek.nextPos;
      continue;
    }
    if (tok === "--disable-build-servers") {
      const err = addOption({ kind: "disable-build-servers" }, [tok], pos, tok);
      if (err !== null) return { failure: err };
      pos = peek.nextPos;
      continue;
    }
    if (tok === "--nologo") {
      const err = addOption({ kind: "nologo" }, [tok], pos, tok);
      if (err !== null) return { failure: err };
      pos = peek.nextPos;
      continue;
    }
    if (tok === "--blame-crash") {
      const err = addOption({ kind: "blame-crash" }, [tok], pos, tok);
      if (err !== null) return { failure: err };
      pos = peek.nextPos;
      continue;
    }

    if (tok === "-c" || tok === "--configuration") {
      const valPos = skipSpaces(text, peek.nextPos);
      const val = valPos === peek.nextPos ? { token: "", nextPos: valPos } : readBareToken(text, valPos);
      if (!Configurations.has(val.token)) {
        return { failure: { position: valPos, token: val.token, expected: "Debug or Release" } };
      }
      const err = addOption(
        { kind: "configuration", flag: tok, value: val.token as "Debug" | "Release" },
        [tok, val.token],
        pos,
        tok,
      );
      if (err !== null) return { failure: err };
      pos = val.nextPos;
      continue;
    }

    if (tok === "-v" || tok === "--verbosity") {
      const valPos = skipSpaces(text, peek.nextPos);
      const val = valPos === peek.nextPos ? { token: "", nextPos: valPos } : readBareToken(text, valPos);
      if (!VerbosityValues.has(val.token)) {
        return { failure: { position: valPos, token: val.token, expected: "q|quiet|m|minimal|n|normal|d|detailed" } };
      }
      const err = addOption(
        {
          kind: "verbosity",
          flag: tok,
          value: val.token as "q" | "quiet" | "m" | "minimal" | "n" | "normal" | "d" | "detailed",
        },
        [tok, val.token],
        pos,
        tok,
      );
      if (err !== null) return { failure: err };
      pos = val.nextPos;
      continue;
    }

    if (tok === "--blame-crash-dump-type") {
      const valPos = skipSpaces(text, peek.nextPos);
      const val = valPos === peek.nextPos ? { token: "", nextPos: valPos } : readBareToken(text, valPos);
      if (!DumpTypes.has(val.token)) {
        return { failure: { position: valPos, token: val.token, expected: "mini or full" } };
      }
      const err = addOption(
        { kind: "blame-crash-dump-type", value: val.token as "mini" | "full" },
        [tok, val.token],
        pos,
        tok,
      );
      if (err !== null) return { failure: err };
      pos = val.nextPos;
      continue;
    }

    if (tok === "--blame-hang-timeout") {
      const valPos = skipSpaces(text, peek.nextPos);
      const val = valPos === peek.nextPos ? { token: "", nextPos: valPos } : readBareToken(text, valPos);
      if (!DurationPattern.test(val.token)) {
        return { failure: { position: valPos, token: val.token, expected: "<digits>s or <digits>m" } };
      }
      const err = addOption({ kind: "blame-hang-timeout", duration: val.token }, [tok, val.token], pos, tok);
      if (err !== null) return { failure: err };
      pos = val.nextPos;
      continue;
    }

    if (isValidProject(tok)) {
      if (project !== null) {
        return { failure: { position: pos, token: tok, expected: "at most one project path" } };
      }
      project = tok;
      argv.push(tok);
      pos = peek.nextPos;
      continue;
    }

    return {
      failure: {
        position: pos,
        token: tok,
        expected: "tests/<project>, allowed option, --filter, --logger, or redirect",
      },
    };
  }

  return { project, options, filter, logger, argv, nextPos: pos };
}

function readRedirect(
  text: string,
  startPos: number,
  shell: ShellName,
): { redirect: CommandRedirect; nextPos: number } | { failure: ParseFailure } {
  if (startPos >= text.length) {
    return { redirect: { stdout: null, append: false, mergeStderr: false }, nextPos: startPos };
  }
  const first = readBareToken(text, startPos);
  if (first.token === "2>&1") {
    if (first.nextPos !== text.length) {
      const extraPos = skipSpaces(text, first.nextPos);
      return {
        failure: { position: extraPos, token: readBareToken(text, extraPos).token, expected: "end of command" },
      };
    }
    return { redirect: { stdout: null, append: false, mergeStderr: true }, nextPos: first.nextPos };
  }
  if (first.token !== ">" && first.token !== ">>") {
    return { failure: { position: startPos, token: first.token, expected: "> <path>, >> <path>, or 2>&1" } };
  }
  const append = first.token === ">>";
  const pathPos = skipSpaces(text, first.nextPos);
  if (pathPos === first.nextPos) {
    return { failure: { position: first.nextPos, token: "", expected: "space and redirect path" } };
  }
  const pathTok = readBareToken(text, pathPos);
  if (!isValidRedirectPath(pathTok.token, shell)) {
    return {
      failure: {
        position: pathPos,
        token: pathTok.token,
        expected: "safe redirect path (starts with a letter or _, no '..', backslashes, trailing dots or device names)",
      },
    };
  }
  let pos = pathTok.nextPos;
  let mergeStderr = false;
  if (pos < text.length) {
    const nextPos = skipSpaces(text, pos);
    const tail = readBareToken(text, nextPos);
    if (tail.token !== "2>&1" || tail.nextPos !== text.length) {
      return { failure: { position: nextPos, token: tail.token, expected: "optional 2>&1 at end of command" } };
    }
    mergeStderr = true;
    pos = tail.nextPos;
  }
  return { redirect: { stdout: pathTok.token, append, mergeStderr }, nextPos: pos };
}

/** Parses `command` and returns either the structured `SharedCommand` or the first `ParseFailure`. */
export function parseSharedCommandDetailed(
  command: string,
  shell: ShellName,
): { ok: true; value: SharedCommand } | { ok: false; failure: ParseFailure } {
  if (command.length > MaxCommandLength) {
    return {
      ok: false,
      failure: {
        position: MaxCommandLength,
        token: `(${command.length} chars)`,
        expected: `at most ${MaxCommandLength} characters`,
      },
    };
  }
  if (!PrintableAscii.test(command)) {
    return {
      ok: false,
      failure: { position: 0, token: "", expected: "printable ASCII characters and spaces only (no tabs/newlines)" },
    };
  }
  const text = command.replace(/^ +| +$/g, "");
  if (text.length === 0) {
    return { ok: false, failure: { position: 0, token: "", expected: "dotnet test or just <sharedRecipe>" } };
  }

  const pref = readPrefixes(text, 0, shell);
  if ("failure" in pref) {
    return { ok: false, failure: pref.failure };
  }
  const head = readHead(text, pref.nextPos, pref.env.length);
  if ("failure" in head) {
    return { ok: false, failure: head.failure };
  }
  if (head.kind === "recipe") {
    return { ok: true, value: { kind: "recipe", name: head.name } };
  }
  if (head.kind === "mutate") {
    return { ok: true, value: { kind: "mutate", phase: head.phase } };
  }

  const items = readItems(text, head.nextPos, shell);
  if ("failure" in items) {
    return { ok: false, failure: items.failure };
  }
  const redir = readRedirect(text, items.nextPos, shell);
  if ("failure" in redir) {
    return { ok: false, failure: redir.failure };
  }
  return {
    ok: true,
    value: {
      kind: "test",
      env: pref.env,
      project: items.project,
      options: items.options,
      filter: items.filter,
      logger: items.logger,
      redirect: redir.redirect,
      argv: items.argv,
    },
  };
}

/** Parses a command in the allow-listed GPU-share language for `shell`, or returns null if outside the grammar. */
export function parseSharedCommand(command: string, shell: ShellName): SharedCommand | null {
  const result = parseSharedCommandDetailed(command, shell);
  return result.ok ? result.value : null;
}

const DefaultExample =
  'dotnet test tests/<project> --no-build --filter "Category!=Stress&Category!=Benchmark&Category!=GpuTrap"';

/**
 * Formats the 3-line refusal message for a `--gpu` command outside the allow-listed language:
 * line 1: where parsing stopped and what was expected;
 * line 2: an accepted example (built from kept parts of `dotnet test` when possible, naming dropped tokens);
 * line 3: hint to re-queue with `--gpu-exclusive` if exclusive GPU access is intended.
 */
export function formatOutsideLanguageRefusal(command: string, shell: ShellName): string {
  const parsed = parseSharedCommandDetailed(command, shell);
  const where = parsed.ok
    ? "command is not accepted for shared --gpu"
    : `outside --gpu command language at pos ${parsed.failure.position}` +
      `${parsed.failure.token !== "" ? ` (${parsed.failure.token})` : ""}: expected ${parsed.failure.expected}.`;

  let example = DefaultExample;
  let droppedNote = "";
  const trimmed = command.trim();
  const bodyMatch = /\bdotnet +test\b(.*)$/.exec(trimmed);
  if (bodyMatch !== null) {
    const rest = (bodyMatch[1] ?? "").trim();
    const tokens = rest.match(/"[^"]*"|'[^']*'|\S+/g) ?? [];
    const kept: string[] = [];
    const dropped: string[] = [];
    let hasProject = false;
    let hasFilter = false;
    for (let i = 0; i < tokens.length; i++) {
      const t = tokens[i] as string;
      if (!hasProject && isValidProject(t)) {
        kept.push(t);
        hasProject = true;
      } else if (
        t === "--no-build" ||
        t === "-nodeReuse:false" ||
        t === "--disable-build-servers" ||
        t === "--nologo"
      ) {
        kept.push(t);
      } else if ((t === "-c" || t === "--configuration") && Configurations.has(tokens[i + 1] ?? "")) {
        kept.push(t, tokens[++i] as string);
      } else if (t === "--filter" && !hasFilter && tokens[i + 1] !== undefined) {
        const raw = tokens[++i] as string;
        const inner =
          (raw.startsWith('"') && raw.endsWith('"')) || (raw.startsWith("'") && raw.endsWith("'"))
            ? raw.slice(1, -1)
            : raw;
        if (isValidFilter(inner)) {
          kept.push("--filter", `"${inner}"`);
          hasFilter = true;
        } else {
          dropped.push(`--filter ${raw}`);
        }
      } else {
        dropped.push(t);
      }
    }
    if (kept.length > 0) {
      example = `dotnet test ${kept.join(" ")}`;
      if (dropped.length > 0) {
        droppedNote = ` (dropped: ${dropped.slice(0, 4).join(", ")}${dropped.length > 4 ? ", ..." : ""})`;
      }
    }
  }

  return [
    `[gpu-share] ${where}`,
    `Accepted form: ${example}${droppedNote}`,
    "Re-queue with --gpu-exclusive to run this command with exclusive GPU access.",
  ].join("\n");
}
