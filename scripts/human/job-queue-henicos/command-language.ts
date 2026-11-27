// Small allow-listed command language for GPU turn sharing (design §9).
// Only commands accepted by this grammar can be classified as shared or handed to name-filter checking;
// every other string returns null and stays exclusive ("not-language").

import type { ShellName } from "../job-queue/constants.ts";

/** Environment variable names allowed as a leading prefix before `dotnet test`. */
export const AllowedPrefixEnvNames = ["DiffEngine_Disabled", "DOTNET_CLI_UI_LANGUAGE"] as const;
export type AllowedPrefixEnvName = (typeof AllowedPrefixEnvNames)[number];

export interface EnvBinding {
  name: AllowedPrefixEnvName;
  value: string;
}

export type SharedCommand =
  | {
      kind: "test";
      env: EnvBinding[];
      args: string[];
      filter: string | null;
      logger: string | null;
      redirects: string[];
    }
  | {
      kind: "recipe";
      recipe: string;
      args: string[];
      filter: string | null;
      logger: string | null;
      redirects: string[];
    };

// Printable ASCII only: rejects CR, LF, tabs, control chars, and all non-ASCII (e.g. PowerShell smart quotes U+2018..201E).
const PrintableAscii = /^[ -~]+$/;
const AllowedNameSet = new Set<string>(AllowedPrefixEnvNames);
const PrefixValue = /^[A-Za-z0-9_-]+$/;
const RecipeName = /^[A-Za-z][A-Za-z0-9_-]*$/;
const FilterChars = /^[A-Za-z0-9_.~=!&|() -]+$/;
const LoggerChars = /^[A-Za-z0-9_.;=-]+$/;
const BashArgChars = /^[A-Za-z0-9-][A-Za-z0-9_./:=-]*$/;
const WinArgChars = /^[A-Za-z0-9-][A-Za-z0-9_./\\:=-]*$/;
const BashPathChars = /^[A-Za-z0-9_./:-]+$/;
const WinPathChars = /^[A-Za-z0-9_./\\:-]+$/;
const ReservedOptionArg = /^--(?:filter|logger)(?:=|:|$)/i;

function isPowerShell(shell: ShellName): boolean {
  return shell === "pwsh" || shell === "powershell";
}

function isPosixShell(shell: ShellName): boolean {
  return shell === "bash" || shell === "sh";
}

/**
 * Parses a command in the allow-listed GPU-share language for `shell`, or returns null if any character or token falls
 * outside the grammar.
 *
 * Grammar (ASCII space ' ' only; leading/trailing spaces ignored; any newline, tab, or non-ASCII returns null):
 *   command      := prefixes "dotnet" " "+ "test" (" "+ item)* redirects
 *                 | "just" " "+ RECIPE (" "+ item)* redirects
 *   prefixes     := (psPrefix)*  [pwsh|powershell] | (shPrefix)*  [bash|sh] | empty  [cmd]
 *   psPrefix     := "$env:" NAME "=" ( "'" VALUE "'" | '"' VALUE '"' ) ";" " "*
 *   shPrefix     := NAME "=" VALUE " "+
 *   NAME         := "DiffEngine_Disabled" | "DOTNET_CLI_UI_LANGUAGE"
 *   VALUE        := [A-Za-z0-9_-]+
 *   RECIPE       := [A-Za-z][A-Za-z0-9_-]*
 *   item         := "--filter" " "+ quotedFilter | "--logger" " "+ quotedLogger | ARG
 *   quotedFilter := '"' FILTER '"' | "'" FILTER "'"   (no single quote on cmd)
 *   FILTER       := [A-Za-z0-9_.~=!&|() -]+
 *   quotedLogger := '"' [A-Za-z0-9_.;=-]+ '"'
 *   ARG          := [A-Za-z0-9-][A-Za-z0-9_./:=-]* (plus '\\' on non-POSIX; no leading '-' with '.' on PowerShell)
 *   redirects    := (" "+ ("2>&1" | (">" | ">>") " "* PATH))*
 *   PATH         := [A-Za-z0-9_./:-]+ (plus '\\' on non-POSIX)
 */
export function parseSharedCommand(command: string, shell: ShellName): SharedCommand | null {
  if (!PrintableAscii.test(command)) {
    return null;
  }
  const text = command.replace(/^ +| +$/g, "");
  if (text.length === 0) {
    return null;
  }

  let pos = 0;
  const len = text.length;

  const skipSpaces = (): number => {
    const start = pos;
    while (pos < len && text.charCodeAt(pos) === 0x20) {
      pos++;
    }
    return pos - start;
  };

  const readBareToken = (): string => {
    const start = pos;
    while (pos < len && text.charCodeAt(pos) !== 0x20) {
      pos++;
    }
    return text.slice(start, pos);
  };

  const readQuoted = (allowSingle: boolean): string | null => {
    if (pos >= len) {
      return null;
    }
    const quote = text.charAt(pos);
    if (quote !== '"' && !(allowSingle && quote === "'")) {
      return null;
    }
    pos++;
    const start = pos;
    while (pos < len && text.charAt(pos) !== quote) {
      pos++;
    }
    if (pos >= len) {
      return null;
    }
    const value = text.slice(start, pos);
    pos++;
    // Closing quote must end the token (end of string or ASCII space)
    if (pos < len && text.charCodeAt(pos) !== 0x20) {
      return null;
    }
    return value;
  };

  const env: EnvBinding[] = [];

  // 1. Optional leading environment prefixes (only before `dotnet test`)
  if (isPowerShell(shell)) {
    while (text.startsWith("$env:", pos)) {
      const m = /^\$env:([A-Za-z0-9_]+)=(?:'([^']*)'|"([^"]*)"); */.exec(text.slice(pos));
      if (m === null) {
        return null;
      }
      const name = m[1] as string;
      const value = (m[2] ?? m[3]) as string;
      if (!AllowedNameSet.has(name) || !PrefixValue.test(value)) {
        return null;
      }
      env.push({ name: name as AllowedPrefixEnvName, value });
      pos += m[0].length;
    }
  } else if (isPosixShell(shell)) {
    while (pos < len) {
      const m = /^([A-Za-z0-9_]+)=([^ ]+) +/.exec(text.slice(pos));
      if (m === null) {
        break;
      }
      const name = m[1] as string;
      const value = m[2] as string;
      if (!AllowedNameSet.has(name) || !PrefixValue.test(value)) {
        return null;
      }
      env.push({ name: name as AllowedPrefixEnvName, value });
      pos += m[0].length;
    }
  }

  // 2. Command head: `dotnet test` or `just <recipe>` (no env prefixes allowed before `just`)
  const head = readBareToken();
  let kind: "test" | "recipe";
  let recipe = "";

  if (head === "dotnet") {
    if (skipSpaces() === 0 || readBareToken() !== "test") {
      return null;
    }
    kind = "test";
  } else if (head === "just" && env.length === 0) {
    if (skipSpaces() === 0) {
      return null;
    }
    recipe = readBareToken();
    if (!RecipeName.test(recipe)) {
      return null;
    }
    kind = "recipe";
  } else {
    return null;
  }

  // 3. Items and trailing redirects
  const posix = isPosixShell(shell);
  const pwsh = isPowerShell(shell);
  const argPattern = posix ? BashArgChars : WinArgChars;
  const pathPattern = posix ? BashPathChars : WinPathChars;
  const args: string[] = [];
  let filter: string | null = null;
  let logger: string | null = null;
  const redirects: string[] = [];
  let hasStdoutRedirect = false;
  let hasStderrMerge = false;
  let inRedirects = false;

  while (pos < len) {
    if (skipSpaces() === 0) {
      return null;
    }
    if (pos >= len) {
      break;
    }

    // Redirects can only appear at the end (at most one stdout redirect and one `2>&1`: PowerShell rejects duplicates)
    if (text.startsWith("2>&1", pos) && (pos + 4 === len || text.charCodeAt(pos + 4) === 0x20)) {
      if (hasStderrMerge) {
        return null;
      }
      hasStderrMerge = true;
      inRedirects = true;
      pos += 4;
      continue;
    }
    if (text.charAt(pos) === ">") {
      if (hasStdoutRedirect) {
        return null;
      }
      hasStdoutRedirect = true;
      inRedirects = true;
      pos += text.startsWith(">>", pos) ? 2 : 1;
      skipSpaces();
      const target = readBareToken();
      if (!pathPattern.test(target)) {
        return null;
      }
      redirects.push(target);
      continue;
    }
    if (inRedirects) {
      return null;
    }

    // Items: --filter, --logger, or ARG
    if (text.startsWith("--filter", pos) && (pos + 8 === len || text.charCodeAt(pos + 8) === 0x20)) {
      if (filter !== null) {
        return null;
      }
      pos += 8;
      if (skipSpaces() === 0) {
        return null;
      }
      const value = readQuoted(shell !== "cmd");
      if (value === null || !FilterChars.test(value)) {
        return null;
      }
      filter = value;
      args.push("--filter", value);
      continue;
    }

    if (text.startsWith("--logger", pos) && (pos + 8 === len || text.charCodeAt(pos + 8) === 0x20)) {
      if (logger !== null) {
        return null;
      }
      pos += 8;
      if (skipSpaces() === 0) {
        return null;
      }
      const value = readQuoted(false);
      if (value === null || !LoggerChars.test(value)) {
        return null;
      }
      logger = value;
      args.push("--logger", value);
      continue;
    }

    const token = readBareToken();
    if (!argPattern.test(token) || ReservedOptionArg.test(token)) {
      return null;
    }
    // PowerShell splits bare arguments starting with '-' that contain '.' (e.g. `-a.b` -> `-a`, `.b`)
    if (pwsh && token.startsWith("-") && token.includes(".")) {
      return null;
    }
    // Git Bash (MSYS2) rewrites arguments containing `//`, `=/`, `:/`, or `:` together with `/`
    if (posix && token.includes("/") && (token.includes("//") || token.includes("=") || token.includes(":"))) {
      return null;
    }
    args.push(token);
  }

  if (kind === "test") {
    return { kind: "test", env, args, filter, logger, redirects };
  }
  return { kind: "recipe", recipe, args, filter, logger, redirects };
}
