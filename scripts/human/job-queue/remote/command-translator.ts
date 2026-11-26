// Layer 3 Command Translator:
// Translates a Windows (PowerShell / Git Bash / sh / cmd) CPU command into a POSIX bash plan for a remote Linux
// worker via a `ShellDialect` Strategy Map and a five-step fail-closed pipeline.

import * as fs from "node:fs";
import * as path from "node:path";
import { KeepBuildServersWarmVariable, type ShellName } from "../constants.ts";
import { rawWords, readCommandLine, segmentParts } from "../command-reader.ts";
import type { PathPlace } from "../estimate-key.ts";
import type { CollectPathSpec } from "./artifact-collector.ts";
import {
  canonicalCommandEnvName,
  commandEnvLocalReason,
  getEnvValue,
  hasEnvKey,
  isSafeEnvKey,
  resolveCallerEnv,
} from "./env-policy.ts";
import { EmptyRemoteRules, type ResolvedRemoteRules } from "./rules.ts";
import { bashQuote } from "./spec.ts";

export interface RemoteCommandPlan {
  /** Single local git worktree root to sync to the remote workspace. */
  worktree: string;
  /** Working directory relative to `worktree` (POSIX slashes, "" for worktree root). */
  remoteCwdRel: string;
  /** POSIX bash command line to execute inside `<workspace>/<remoteCwdRel>`. */
  bashCommand: string;
  /** Environment variables parsed from leading assignments, plugin defaults, and allowlisted caller env. */
  env: Record<string, string>;
  /** Relative POSIX paths (e.g. plugin collectDirOptions or redirection targets) to pull back after execution. */
  collectPaths: CollectPathSpec[];
  /** Relative POSIX paths passed to plugin `collectDirOptions` (e.g. `--results-directory`), redirected to quarantine. */
  redirectedResultsDirs?: string[];
  /** Optional grant-line note from plugins (e.g. "4 MSBuild nodes"). */
  note?: string;
}

export interface TranslateOptions {
  cwd: string;
  worktree: string;
  shell: ShellName;
  places: Readonly<Record<string, PathPlace>>;
  cpus: number;
  keepBuildServersWarm: boolean;
  env: Readonly<NodeJS.ProcessEnv>;
  /** Target platform override for case-folding checks (defaults to `process.platform`). */
  platform?: NodeJS.Platform;
  /** Aggregated remote offload rules contributed by active plugins. */
  remoteRules?: ResolvedRemoteRules;
}

/** Lightweight Strategy encapsulating shell-dialect syntax differences (PowerShell / POSIX sh / cmd). */
interface ShellDialect {
  readonly supportsSingleQuote: boolean;
  readonly rejectsPercent: boolean;
  matchLeadingEnv(text: string): { key: string; value: string; consumed: number } | null;
  matchInlineEnvStatement(text: string): { key: string; value: string } | null;
}

const PwshLeadingEnv = /^\$env:([A-Za-z_]\w*)\s*=\s*(?:'([^']*)'|"([^"$`]*)"|([^\s;&|$`"'()\\]+))\s*;\s*/;
const PwshInlineEnv = /^\$env:([A-Za-z_]\w*)\s*=\s*(?:'([^']*)'|"([^"$`]*)"|([^\s;&|$`"'()\\]+))$/;
const PosixLeadingEnv = /^export\s+([A-Za-z_]\w*)=(?:'([^']*)'|"([^"$`\\]*)"|([^\s;&|$`"'()\\]+))\s*;\s*/;

const PwshDialect: ShellDialect = {
  supportsSingleQuote: true,
  rejectsPercent: false,
  matchLeadingEnv(text) {
    const m = PwshLeadingEnv.exec(text);
    return m !== null ? { key: m[1] as string, value: m[2] ?? m[3] ?? m[4] ?? "", consumed: m[0].length } : null;
  },
  matchInlineEnvStatement(text) {
    const m = PwshInlineEnv.exec(text);
    if (m === null || !isSafeEnvKey(m[1] as string)) {
      return null;
    }
    return { key: m[1] as string, value: m[2] ?? m[3] ?? m[4] ?? "" };
  },
};

const PosixDialect: ShellDialect = {
  supportsSingleQuote: true,
  rejectsPercent: false,
  matchLeadingEnv(text) {
    const m = PosixLeadingEnv.exec(text);
    return m !== null ? { key: m[1] as string, value: m[2] ?? m[3] ?? m[4] ?? "", consumed: m[0].length } : null;
  },
  matchInlineEnvStatement: () => null,
};

const CmdDialect: ShellDialect = {
  supportsSingleQuote: false,
  rejectsPercent: true,
  matchLeadingEnv: () => null,
  matchInlineEnvStatement: () => null,
};

const ShellDialects: Readonly<Record<ShellName, ShellDialect>> = {
  pwsh: PwshDialect,
  powershell: PwshDialect,
  bash: PosixDialect,
  sh: PosixDialect,
  cmd: CmdDialect,
};

const BasePathValueOptions = new Set(["-o", "--output"]);
const EnvironmentOptions = new Set(["-e", "--environment"]);
const BaseNonPathValueOptions = EnvironmentOptions;
const BasePathExtensions = new Set(["js", "ts", "json", "yml", "yaml"]);
const BaseAllowedPrograms = new Set(["node", "echo"]);
const BroadRepoDirs = new Set([".", "src", "tests", "docs", "tools", "scripts", ".github", ".agents"]);
const ForbiddenCollectPrefixes = [
  ".git",
  ".vs",
  ".vscode",
  ".claude",
  ".agents",
  ".github",
  "scripts",
  "node_modules",
  "docs/spec",
] as const;
const ForbiddenCollectSegments = new Set([".git", "bin", "obj", "node_modules"]);
const VerifiedSnapshotPattern = /\.verified\.[^/]+$/i;
const WindowsReservedDevice = /^(?:con|prn|aux|nul|com[0-9\u00b2\u00b3\u00b9]|lpt[0-9\u00b2\u00b3\u00b9])(?:\.|$)/i;
const MsbuildBareSwitchPattern = new RegExp(
  String.raw`^\/(?:${[
    "nologo",
    "m",
    "maxcpucount",
    "tl",
    "bl",
    "noconlog",
    "noconsolelogger",
    "noautorsp",
    "restore",
    "t",
    "warnaserror",
    "nowarn",
  ].join("|")})$`,
  "i",
);
const CompositeValueNonPathOptions = new Set(["-p", "--property", "-l", "--logger"]);

function hasLeadingDashSegment(relPosix: string): boolean {
  return relPosix.split("/").some((seg) => seg.startsWith("-"));
}

function hasWindowsReservedSegment(rawPath: string): boolean {
  return rawPath.split(/[\\/]/).some((seg) => seg !== "" && WindowsReservedDevice.test(seg.replace(/:$/, "")));
}

function hasUnsafeDriveOrQuote(token: string): boolean {
  return token.includes('"') || token.includes("'") || (!/^[A-Za-z]:[\\/]/.test(token) && /[A-Za-z]:[\\/]/.test(token));
}

function normKey(p: string): string {
  const resolved = path.resolve(p);
  return process.platform === "win32" ? resolved.toLowerCase() : resolved;
}

function isInside(root: string, target: string): boolean {
  const rel = path.relative(normKey(root), normKey(target));
  return (
    rel === "" || (rel !== ".." && !rel.startsWith(`..${path.sep}`) && !rel.startsWith("../") && !path.isAbsolute(rel))
  );
}

function relativePosix(root: string, absolute: string): string {
  const rel = path.relative(normKey(root), normKey(absolute));
  if (rel === "") {
    return ".";
  }
  const exactRel = path.relative(path.resolve(root), path.resolve(absolute));
  return (exactRel || rel).split(path.sep).join("/");
}

/** Strips outer/segment shell quotes while preserving internal quotes and unescaping PowerShell `''`/`""`. */
function unquoteToken(word: string, shell: ShellName): string | null {
  const isPwsh = shell === "pwsh" || shell === "powershell";
  const supportsSingle = shell !== "cmd";
  const startedWithQuote = word.startsWith('"') || (supportsSingle && word.startsWith("'"));
  let out = "";
  let quote = "";
  for (let i = 0; i < word.length; i++) {
    const ch = word.charAt(i);
    if (quote !== "") {
      if (ch === quote) {
        const doubled = isPwsh && word.charAt(i + 1) === quote;
        if (doubled) {
          out += quote;
          i++;
        } else {
          quote = "";
          if ((isPwsh || shell === "cmd") && startedWithQuote && i < word.length - 1) {
            return null;
          }
        }
      } else {
        out += ch;
      }
    } else if (ch === '"' || (ch === "'" && supportsSingle)) {
      quote = ch;
    } else {
      out += ch;
    }
  }
  return quote === "" ? out : null;
}

function resolveArgPath(token: string, cwd: string): string {
  const isWin = process.platform === "win32" || /^[A-Za-z]:[\\/]/.test(cwd);
  if (isWin) {
    const mapped = /^\/(?:mnt\/)?([A-Za-z])\/(.*)$/.exec(token);
    if (mapped !== null) {
      return path.win32.resolve(cwd, `${mapped[1]}:/${mapped[2]}`);
    }
    return path.win32.resolve(cwd, token);
  }
  return path.resolve(cwd, token);
}

function looksLikePath(
  token: string,
  cwd: string,
  extraExtensions: ReadonlySet<string>,
  allowMsbuildBareSwitch = true,
): boolean {
  if (token === "" || token.startsWith("-") || (allowMsbuildBareSwitch && MsbuildBareSwitchPattern.test(token))) {
    return false;
  }
  if (/[A-Za-z]:[\\/]/.test(token) || token.includes("\\") || token.includes("/")) {
    return true;
  }
  const dot = token.lastIndexOf(".");
  if (dot > 0 && dot < token.length - 1) {
    const ext = token.slice(dot + 1).toLowerCase();
    if (BasePathExtensions.has(ext) || extraExtensions.has(ext)) {
      return true;
    }
  }
  return fs.existsSync(resolveArgPath(token, cwd));
}

function findWorktreeRoot(dir: string): string {
  for (let current = path.resolve(dir); ; current = path.dirname(current)) {
    if (fs.existsSync(path.join(current, ".git"))) {
      return current;
    }
    const parent = path.dirname(current);
    if (parent === current) {
      return "";
    }
  }
}

/** Extracts dialect-specific leading environment assignments; returns null on unsafe keys like `__proto__`. */
function extractLeadingEnv(
  command: string,
  dialect: ShellDialect,
): { env: Record<string, string>; rest: string } | null {
  const env: Record<string, string> = Object.create(null) as Record<string, string>;
  let rest = command.trim();
  for (;;) {
    const hit = dialect.matchLeadingEnv(rest);
    if (hit === null) {
      break;
    }
    if (!isSafeEnvKey(hit.key)) {
      return null;
    }
    env[hit.key] = hit.value;
    rest = rest.slice(hit.consumed);
  }
  return { env, rest };
}

/** Splits on top-level `&&` and `;` outside quotes; returns null on pipes, subshells, or newlines. */
function splitStatements(command: string, dialect: ShellDialect): { op: "&&" | ";"; text: string }[] | null {
  const out: { op: "&&" | ";"; text: string }[] = [];
  let quote = "";
  let start = 0;
  let prevOp: "&&" | ";" = "&&";
  for (let i = 0; i < command.length; i++) {
    const ch = command.charAt(i);
    const next = command.charAt(i + 1);
    if (quote !== "") {
      if (ch === quote) {
        quote = "";
      } else if (ch === "\n" || ch === "\r") {
        return null;
      } else if (quote === '"' && (ch === "`" || ch === "$" || (dialect.rejectsPercent && ch === "%"))) {
        return null;
      }
    } else if (ch === '"' || (ch === "'" && dialect.supportsSingleQuote)) {
      quote = ch;
    } else if (ch === "&" && next === "&") {
      const text = command.slice(start, i).trim();
      if (text !== "") {
        out.push({ op: prevOp, text });
      }
      prevOp = "&&";
      start = i + 2;
      i++;
    } else if (ch === ";") {
      const text = command.slice(start, i).trim();
      if (text !== "") {
        out.push({ op: prevOp, text });
      }
      prevOp = ";";
      start = i + 1;
    } else if (
      ch === "|" ||
      ch === "<" ||
      ch === "`" ||
      ch === "(" ||
      ch === ")" ||
      ch === "$" ||
      (dialect.rejectsPercent && ch === "%") ||
      ch === "\n" ||
      ch === "\r"
    ) {
      return null;
    }
  }
  if (quote !== "") {
    return null;
  }
  const tail = command.slice(start).trim();
  if (tail !== "") {
    out.push({ op: prevOp, text: tail });
  }
  return out.length > 0 ? out : null;
}

interface ParsedStatement {
  op: "&&" | ";";
  program: string;
  args: string[];
  segEnv: Record<string, string>;
  redirections: string[];
  cwd: string;
}

type OptionKind = "path" | "non-path" | "unknown";

function classifyOption(
  rules: ResolvedRemoteRules,
  program: string,
  subcommand: string,
  optionLower: string,
): OptionKind {
  const decision = rules.isOptionValuePath(program, subcommand, optionLower);
  if (
    decision === false ||
    (decision === undefined && (BaseNonPathValueOptions.has(optionLower) || rules.nonPathValueOptions.has(optionLower)))
  ) {
    return "non-path";
  }
  if (decision === true || BasePathValueOptions.has(optionLower) || rules.pathValueOptions.has(optionLower)) {
    return "path";
  }
  return "unknown";
}

function splitInlineOption(token: string): { option: string; sep: string; value: string } | null {
  const match = /^(--?[A-Za-z][A-Za-z0-9-]*|\/[A-Za-z][A-Za-z0-9_-]*)([:=])(.*)$/.exec(token);
  if (match !== null) {
    return { option: match[1] as string, sep: match[2] as string, value: match[3] as string };
  }
  return null;
}

function normalizePlacesMap(places: Readonly<Record<string, PathPlace>>): Map<string, PathPlace> {
  const map = new Map<string, PathPlace>();
  for (const [key, place] of Object.entries(places)) {
    map.set(normKey(key), place);
  }
  return map;
}

function registerPathWorktree(
  token: string,
  currentCwd: string,
  placesMap: ReadonlyMap<string, PathPlace>,
  worktrees: Set<string>,
): boolean {
  if (hasUnsafeDriveOrQuote(token)) {
    return false;
  }
  const abs = resolveArgPath(token, currentCwd);
  const place = placesMap.get(normKey(abs));
  if (place !== undefined) {
    if (place.root === "" || place.ignored) {
      return false;
    }
    worktrees.add(path.resolve(place.root));
    return true;
  }
  const wt = findWorktreeRoot(abs);
  if (wt === "") {
    return false;
  }
  worktrees.add(wt);
  return true;
}

function parseSegEnv(rawEnv: string, shell: ShellName): Record<string, string> | null {
  const segEnv: Record<string, string> = Object.create(null) as Record<string, string>;
  if (rawEnv === "") {
    return segEnv;
  }
  const envWords = rawWords(rawEnv, shell);
  if (envWords === null) {
    return null;
  }
  for (const token of envWords) {
    const eq = token.indexOf("=");
    if (eq <= 0) {
      return null;
    }
    const key = token.slice(0, eq);
    if (!isSafeEnvKey(key)) {
      return null;
    }
    const val = unquoteToken(token.slice(eq + 1), shell);
    if (val === null) {
      return null;
    }
    segEnv[key] = val;
  }
  return segEnv;
}

function stripMatchingQuotes(value: string): { quote: string; inner: string } {
  if (
    value.length >= 2 &&
    ((value.startsWith('"') && value.endsWith('"')) || (value.startsWith("'") && value.endsWith("'")))
  ) {
    return { quote: value.charAt(0), inner: value.slice(1, -1) };
  }
  return { quote: "", inner: value };
}

function transformCommaList(
  text: string,
  cwd: string,
  extraExtensions: ReadonlySet<string>,
  visitPath: (candidate: string) => string | null,
): string | null {
  const parts = text.split(",");
  const out: string[] = [];
  for (const part of parts) {
    const leading = /^\s*/.exec(part)?.[0] ?? "";
    const trailing = /\s*$/.exec(part)?.[0] ?? "";
    const trimmed = part.slice(leading.length, part.length - trailing.length);
    const eq = trimmed.indexOf("=");
    if (eq >= 0) {
      const lhs = transformCommaList(trimmed.slice(0, eq), cwd, extraExtensions, visitPath);
      const rhs = transformCommaList(trimmed.slice(eq + 1), cwd, extraExtensions, visitPath);
      if (lhs === null || rhs === null) {
        return null;
      }
      out.push(`${leading}${lhs}=${rhs}${trailing}`);
      continue;
    }
    const { quote, inner } = stripMatchingQuotes(trimmed);
    if (looksLikePath(inner, cwd, extraExtensions, false)) {
      const next = visitPath(inner);
      if (next === null) {
        return null;
      }
      out.push(`${leading}${quote}${next}${quote}${trailing}`);
    } else {
      out.push(part);
    }
  }
  return out.join(",");
}

function transformCompositeValue(
  value: string,
  cwd: string,
  extraExtensions: ReadonlySet<string>,
  visitPath: (candidate: string) => string | null,
): string | null {
  const segments = value.split(";");
  const out: string[] = [];
  for (const seg of segments) {
    const transformed = transformCommaList(seg, cwd, extraExtensions, visitPath);
    if (transformed === null) {
      return null;
    }
    out.push(transformed);
  }
  return out.join(";");
}

/**
 * Admits one assignment written in the command into `target` under its canonical spelling, or returns false when the
 * env policy keeps the job local (a local-only name or a path value).
 */
function admitCommandEnv(
  target: Record<string, string>,
  key: string,
  value: string,
  rules: ResolvedRemoteRules,
  platform: NodeJS.Platform,
): boolean {
  if (commandEnvLocalReason(key, value, rules, platform) !== null) {
    return false;
  }
  target[canonicalCommandEnvName(key, rules, platform)] = value;
  return true;
}

/** Checks the `NAME=VALUE` of `-e` / `--environment` (as `dotnet test` takes it) against the env policy. */
function admitEnvironmentOption(token: string, rules: ResolvedRemoteRules, platform: NodeJS.Platform): boolean {
  const eq = token.indexOf("=");
  const name = eq > 0 ? token.slice(0, eq) : token;
  const value = eq > 0 ? token.slice(eq + 1) : "";
  return commandEnvLocalReason(name, value, rules, platform) === null;
}

function validateStatementArgs(
  args: readonly string[],
  program: string,
  currentCwd: string,
  placesMap: ReadonlyMap<string, PathPlace>,
  rules: ResolvedRemoteRules,
  worktrees: Set<string>,
  platform: NodeJS.Platform,
): boolean {
  const subcommand = (args[0] ?? "").toLowerCase();
  const checkComposite = (val: string): boolean =>
    transformCompositeValue(val, currentCwd, rules.pathExtensions, (cand) =>
      registerPathWorktree(cand, currentCwd, placesMap, worktrees) ? cand : null,
    ) !== null;
  let prevOption = "";
  let afterDoubleDash = false;
  for (const token of args) {
    if (!afterDoubleDash && token === "--") {
      afterDoubleDash = true;
      prevOption = "";
      continue;
    }
    if (program !== "echo" && token.startsWith("@")) {
      return false;
    }
    if (!afterDoubleDash && prevOption !== "") {
      const prevKind = classifyOption(rules, program, subcommand, prevOption);
      if (EnvironmentOptions.has(prevOption) && program !== "node" && !admitEnvironmentOption(token, rules, platform)) {
        return false;
      }
      if (prevKind === "non-path") {
        if (CompositeValueNonPathOptions.has(prevOption) && !checkComposite(token)) {
          return false;
        }
        prevOption = "";
        continue;
      }
      if (prevKind === "path") {
        if (token.trim() === "" || !registerPathWorktree(token, currentCwd, placesMap, worktrees)) {
          return false;
        }
        prevOption = "";
        continue;
      }
    }
    const inline = splitInlineOption(token);
    if (inline !== null) {
      const optLower = inline.option.toLowerCase();
      const kind = afterDoubleDash ? "unknown" : classifyOption(rules, program, subcommand, optLower);
      if (
        !afterDoubleDash &&
        EnvironmentOptions.has(optLower) &&
        program !== "node" &&
        !admitEnvironmentOption(inline.value, rules, platform)
      ) {
        return false;
      }
      if (kind === "path") {
        if (inline.value.trim() === "" || !registerPathWorktree(inline.value, currentCwd, placesMap, worktrees)) {
          return false;
        }
      } else if ((kind === "unknown" && program !== "echo") || CompositeValueNonPathOptions.has(optLower)) {
        if (!checkComposite(inline.value)) {
          return false;
        }
      }
      prevOption = "";
      continue;
    }
    if (program !== "echo" && token.includes("=")) {
      if (!checkComposite(token)) {
        return false;
      }
      prevOption = "";
      continue;
    }
    if (program !== "echo" && looksLikePath(token, currentCwd, rules.pathExtensions, !afterDoubleDash)) {
      if (!registerPathWorktree(token, currentCwd, placesMap, worktrees)) {
        return false;
      }
      prevOption = "";
      continue;
    }
    prevOption = !afterDoubleDash && token.startsWith("-") ? token.toLowerCase() : "";
  }
  return true;
}

/** Validates programs, tracks leading directory changes, and collects every referenced worktree root. */
function parseAndValidateStatements(
  statements: readonly { op: "&&" | ";"; text: string }[],
  options: TranslateOptions,
  dialect: ShellDialect,
  rules: ResolvedRemoteRules,
  env: Record<string, string>,
  placesMap: ReadonlyMap<string, PathPlace>,
): { parsed: ParsedStatement[]; worktrees: Set<string>; finalCwd: string } | null {
  const platform = options.platform ?? process.platform;
  let currentCwd = path.resolve(options.cwd);
  const worktrees = new Set<string>();
  const parsed: ParsedStatement[] = [];

  for (const stmt of statements) {
    const inlineEnv = dialect.matchInlineEnvStatement(stmt.text);
    if (inlineEnv !== null) {
      if (parsed.length > 0) {
        return null;
      }
      if (!admitCommandEnv(env, inlineEnv.key, inlineEnv.value, rules, platform)) {
        return null;
      }
      continue;
    }
    const parts = segmentParts(stmt.text, options.shell);
    const words = rawWords(parts.core, options.shell);
    if (words === null || words.length === 0) {
      return null;
    }
    const unquoted: string[] = [];
    for (const w of words) {
      const value = unquoteToken(w, options.shell);
      if (value === null) {
        return null;
      }
      unquoted.push(value);
    }
    const progLower = path
      .basename(unquoted[0] as string)
      .toLowerCase()
      .replace(/\.exe$/, "");

    if (progLower === "cd" || progLower === "set-location" || progLower === "sl") {
      if (parsed.length > 0 || unquoted.length !== 2) {
        return null;
      }
      const target = unquoted[1] as string;
      if (target === "" || target.startsWith("-") || target.startsWith("~")) {
        return null;
      }
      const nextCwd = resolveArgPath(target, currentCwd);
      let wt = "";
      try {
        if (!fs.statSync(nextCwd).isDirectory()) {
          return null;
        }
        wt = findWorktreeRoot(nextCwd);
        if (wt === "" || !isInside(fs.realpathSync.native(wt), fs.realpathSync.native(nextCwd))) {
          return null;
        }
      } catch {
        return null;
      }
      currentCwd = nextCwd;
      worktrees.add(wt);
      continue;
    }

    if (!BaseAllowedPrograms.has(progLower) && !rules.allowedPrograms.has(progLower)) {
      return null;
    }
    const rawSegEnv = parseSegEnv(parts.env, options.shell);
    if (rawSegEnv === null) {
      return null;
    }
    const segEnv: Record<string, string> = Object.create(null) as Record<string, string>;
    for (const [key, value] of Object.entries(rawSegEnv)) {
      if (!admitCommandEnv(segEnv, key, value, rules, platform)) {
        return null;
      }
    }
    const args = unquoted.slice(1);
    if (!validateStatementArgs(args, progLower, currentCwd, placesMap, rules, worktrees, platform)) {
      return null;
    }
    parsed.push({ op: stmt.op, program: progLower, args, segEnv, redirections: parts.redirections, cwd: currentCwd });
  }

  return parsed.length > 0 ? { parsed, worktrees, finalCwd: currentCwd } : null;
}

/** Enforces the single-worktree invariant and resolves the remote working directory. */
function resolveSingleWorktree(
  worktrees: ReadonlySet<string>,
  finalCwd: string,
  fallbackWorktree: string,
): { targetWorktree: string; remoteCwdRel: string; remoteBaseDir: string } | null {
  const uniqueRoots = new Map<string, string>();
  for (const wt of worktrees) {
    uniqueRoots.set(normKey(wt), wt);
  }
  if (uniqueRoots.size === 0) {
    const fallback = findWorktreeRoot(finalCwd) || (fallbackWorktree !== "" ? path.resolve(fallbackWorktree) : "");
    if (fallback === "") {
      return null;
    }
    uniqueRoots.set(normKey(fallback), fallback);
  }
  if (uniqueRoots.size !== 1) {
    return null;
  }
  const targetWorktree = [...uniqueRoots.values()][0] as string;
  const remoteCwdRel = isInside(targetWorktree, finalCwd)
    ? relativePosix(targetWorktree, finalCwd).replace(/^\.$/, "")
    : "";
  const remoteBaseDir = remoteCwdRel === "" ? targetWorktree : path.resolve(targetWorktree, remoteCwdRel);
  return { targetWorktree, remoteCwdRel, remoteBaseDir };
}

function translatePathArg(
  rawPath: string,
  cwd: string,
  targetWorktree: string,
  remoteBaseDir: string,
  collectAsDir: boolean,
  addCollect: (rel: string, kind: "file" | "dir", fromCollectDirOption?: boolean) => boolean,
): string | null {
  if (rawPath.trim() === "" || hasUnsafeDriveOrQuote(rawPath)) {
    return null;
  }
  const abs = resolveArgPath(rawPath, cwd);
  if (!isInside(targetWorktree, abs)) {
    return null;
  }
  const wtRel = relativePosix(targetWorktree, abs);
  if (hasLeadingDashSegment(wtRel)) {
    return null;
  }
  if (collectAsDir && !addCollect(wtRel, "dir", true)) {
    return null;
  }
  return path.relative(remoteBaseDir, abs).split(path.sep).join("/") || ".";
}

function translateArgsToPosix(
  stmt: ParsedStatement,
  targetWorktree: string,
  remoteBaseDir: string,
  rules: ResolvedRemoteRules,
  addCollect: (rel: string, kind: "file" | "dir", fromCollectDirOption?: boolean) => boolean,
): string[] | null {
  const subcommand = (stmt.args[0] ?? "").toLowerCase();
  const translateComposite = (val: string): string | null =>
    transformCompositeValue(val, stmt.cwd, rules.pathExtensions, (cand) =>
      translatePathArg(cand, stmt.cwd, targetWorktree, remoteBaseDir, false, addCollect),
    );
  const translated: string[] = [];
  let prevOption = "";
  let afterDoubleDash = false;

  for (let i = 0; i < stmt.args.length; i++) {
    const token = stmt.args[i] as string;
    if (!afterDoubleDash && token === "--") {
      translated.push("--");
      afterDoubleDash = true;
      prevOption = "";
      continue;
    }
    if (!afterDoubleDash && prevOption !== "") {
      const prevKind = classifyOption(rules, stmt.program, subcommand, prevOption);
      if (prevKind === "non-path") {
        if (CompositeValueNonPathOptions.has(prevOption)) {
          const next = translateComposite(token);
          if (next === null) {
            return null;
          }
          translated.push(next);
        } else {
          translated.push(token);
        }
        prevOption = "";
        continue;
      }
      if (prevKind === "path") {
        const rel = translatePathArg(
          token,
          stmt.cwd,
          targetWorktree,
          remoteBaseDir,
          rules.collectDirOptions.has(prevOption),
          addCollect,
        );
        if (rel === null) {
          return null;
        }
        translated.push(rel);
        prevOption = "";
        continue;
      }
    }
    const inline = splitInlineOption(token);
    if (inline !== null) {
      const optLower = inline.option.toLowerCase();
      const kind = afterDoubleDash ? "unknown" : classifyOption(rules, stmt.program, subcommand, optLower);
      if (kind === "path") {
        const rel = translatePathArg(
          inline.value,
          stmt.cwd,
          targetWorktree,
          remoteBaseDir,
          rules.collectDirOptions.has(optLower),
          addCollect,
        );
        if (rel === null) {
          return null;
        }
        translated.push(`${inline.option}${inline.sep}${rel}`);
      } else if ((kind === "unknown" && stmt.program !== "echo") || CompositeValueNonPathOptions.has(optLower)) {
        const next = translateComposite(inline.value);
        if (next === null) {
          return null;
        }
        translated.push(`${inline.option}${inline.sep}${next}`);
      } else {
        translated.push(token);
      }
      prevOption = "";
      continue;
    }
    if (stmt.program !== "echo" && token.includes("=")) {
      const next = translateComposite(token);
      if (next === null) {
        return null;
      }
      translated.push(next);
      prevOption = "";
      continue;
    }
    if (stmt.program !== "echo" && looksLikePath(token, stmt.cwd, rules.pathExtensions, !afterDoubleDash)) {
      const rel = translatePathArg(token, stmt.cwd, targetWorktree, remoteBaseDir, false, addCollect);
      if (rel === null) {
        return null;
      }
      translated.push(rel);
      prevOption = "";
      continue;
    }
    translated.push(token);
    prevOption = !afterDoubleDash && token.startsWith("-") ? token.toLowerCase() : "";
  }
  return translated;
}

function rewriteRedirections(
  redirections: readonly string[],
  cwd: string,
  shell: ShellName,
  targetWorktree: string,
  remoteBaseDir: string,
  addCollect: (rel: string, kind: "file" | "dir") => boolean,
): string[] | null {
  const outRedirs: string[] = [];
  for (const redir of redirections) {
    if (redir === "2>&1" || ((shell === "pwsh" || shell === "powershell") && redir === "*>&1")) {
      outRedirs.push("2>&1");
      continue;
    }
    if ((shell === "bash" || shell === "sh") && /^[12]>&[12]$/.test(redir)) {
      outRedirs.push(redir);
      continue;
    }
    if (/^[0-9&*]?>>?\s*&/.test(redir)) {
      return null;
    }
    const m = /^([12&*]?)(>>?)\s*(.+)$/.exec(redir);
    if (m === null) {
      return null;
    }
    const fd = m[1] === "*" ? "&" : (m[1] ?? "");
    const op = m[2] as string;
    const targetToken = unquoteToken(m[3] as string, shell);
    if (targetToken === null || targetToken === "" || targetToken.startsWith("&")) {
      return null;
    }
    if (targetToken === "/dev/null" && (shell === "bash" || shell === "sh")) {
      outRedirs.push(`${fd}${op} /dev/null`);
      continue;
    }
    const isWindowsShell = shell === "cmd" || shell === "pwsh" || shell === "powershell";
    if (isWindowsShell && /^nul:?$/i.test(targetToken)) {
      outRedirs.push(`${fd}${op} /dev/null`);
      continue;
    }
    if (hasWindowsReservedSegment(targetToken)) {
      return null;
    }
    const abs = resolveArgPath(targetToken, cwd);
    if (!isInside(targetWorktree, abs) || !addCollect(relativePosix(targetWorktree, abs), "file")) {
      return null;
    }
    const rel = path.relative(remoteBaseDir, abs).split(path.sep).join("/");
    outRedirs.push(`${fd}${op} ${bashQuote(rel)}`);
  }
  return outRedirs;
}

/** Rewrites one parsed statement into POSIX bash and registers any collected paths. */
function rewriteStatementToBash(
  stmt: ParsedStatement,
  shell: ShellName,
  targetWorktree: string,
  remoteBaseDir: string,
  warmServers: boolean,
  cpus: number,
  rules: ResolvedRemoteRules,
  addCollect: (rel: string, kind: "file" | "dir", fromCollectDirOption?: boolean) => boolean,
): string | null {
  const subcommand = (stmt.args[0] ?? "").toLowerCase();
  const translatedArgs = translateArgsToPosix(stmt, targetWorktree, remoteBaseDir, rules, addCollect);
  if (translatedArgs === null) {
    return null;
  }

  const rewritten = rules.rewriteStatement({
    program: stmt.program,
    subcommand,
    args: translatedArgs,
    keepBuildServersWarm: warmServers,
    cpus,
  });
  for (const extra of rewritten.collectPaths ?? []) {
    if (!addCollect(extra.path, extra.kind)) {
      return null;
    }
  }
  const outWords = [bashQuote(stmt.program), ...(rewritten.args ?? translatedArgs).map((arg) => bashQuote(arg))];
  const outRedirs = rewriteRedirections(stmt.redirections, stmt.cwd, shell, targetWorktree, remoteBaseDir, addCollect);
  if (outRedirs === null) {
    return null;
  }

  const segEnvPrefix = Object.entries(stmt.segEnv)
    .map(([k, v]) => `${k}=${bashQuote(v)}`)
    .join(" ");
  const envPart = segEnvPrefix !== "" ? `${segEnvPrefix} ` : "";
  const redirPart = outRedirs.length > 0 ? ` ${outRedirs.join(" ")}` : "";
  return `${envPart}${outWords.join(" ")}${redirPart}`;
}

/**
 * Translates `command` into a remote Linux bash execution plan, or returns null when the command cannot be
 * safely and unambiguously executed on a remote Linux worker.
 */
export function translateForRemote(command: string, options: TranslateOptions): RemoteCommandPlan | null {
  if (/--%/.test(command)) {
    return null;
  }
  const dialect = ShellDialects[options.shell];
  const leading = extractLeadingEnv(command, dialect);
  if (leading === null || leading.rest === "") {
    return null;
  }
  const line = readCommandLine(leading.rest);
  if (line === null) {
    if (options.shell !== "cmd") {
      return null;
    }
  } else if (line.piped || line.expands.some(Boolean)) {
    return null;
  }
  const statements = splitStatements(leading.rest, dialect);
  if (statements === null) {
    return null;
  }

  const rules = options.remoteRules ?? EmptyRemoteRules;
  const platform = options.platform ?? process.platform;
  const callerEnv = resolveCallerEnv(options.env, rules, platform);
  if (!callerEnv.ok) {
    return null;
  }
  const env: Record<string, string> = Object.create(null) as Record<string, string>;
  for (const [key, value] of Object.entries(leading.env)) {
    if (!admitCommandEnv(env, key, value, rules, platform)) {
      return null;
    }
  }
  const warmServers =
    options.keepBuildServersWarm && getEnvValue(options.env, KeepBuildServersWarmVariable, platform) !== "0";
  const placesMap = normalizePlacesMap(options.places);

  const validated = parseAndValidateStatements(statements, options, dialect, rules, env, placesMap);
  if (validated === null) {
    return null;
  }
  const target = resolveSingleWorktree(validated.worktrees, validated.finalCwd, options.worktree);
  if (target === null) {
    return null;
  }

  const collectMap = new Map<string, CollectPathSpec>();
  const redirectedResultsDirs = new Set<string>();
  const addCollect = (rel: string, kind: "file" | "dir", fromCollectDirOption = false): boolean => {
    const clean = path.posix
      .normalize(rel)
      .replace(/^(?:\.\/)+/, "")
      .replace(/\/+$/, "");
    if (
      clean === "" ||
      clean === "." ||
      clean === ".." ||
      clean.startsWith("../") ||
      clean.startsWith("/") ||
      hasLeadingDashSegment(clean) ||
      hasWindowsReservedSegment(clean)
    ) {
      return false;
    }
    const lower = clean.toLowerCase();
    if (kind === "dir" && BroadRepoDirs.has(lower)) {
      return false;
    }
    if (ForbiddenCollectPrefixes.some((p) => lower === p || lower.startsWith(`${p}/`))) {
      return false;
    }
    if (lower.split("/").some((seg) => ForbiddenCollectSegments.has(seg))) {
      return false;
    }
    if (kind === "file" && VerifiedSnapshotPattern.test(clean)) {
      return false;
    }
    collectMap.set(`${kind}:${clean}`, { path: clean, kind });
    if (fromCollectDirOption) {
      redirectedResultsDirs.add(clean);
    }
    return true;
  };

  const bashParts: string[] = [];
  for (let i = 0; i < validated.parsed.length; i++) {
    const stmt = validated.parsed[i] as ParsedStatement;
    const core = rewriteStatementToBash(
      stmt,
      options.shell,
      target.targetWorktree,
      target.remoteBaseDir,
      warmServers,
      options.cpus,
      rules,
      addCollect,
    );
    if (core === null) {
      return null;
    }
    if (i > 0) {
      bashParts.push(stmt.op);
    }
    bashParts.push(core);
  }

  for (const [key, value] of Object.entries(rules.defaultEnv)) {
    if (!hasEnvKey(env, key, platform)) {
      env[key] = value;
    }
  }
  for (const [key, value] of Object.entries(callerEnv.forwarded)) {
    if (!hasEnvKey(env, key, platform)) {
      env[key] = value;
    }
  }
  const prepared = rules.prepareRemoteCommand(bashParts.join(" "), {
    cpus: options.cpus,
    keepBuildServersWarm: warmServers,
    env,
    platform,
  });
  for (const [key, value] of Object.entries(prepared.env)) {
    env[key] = value;
  }

  const resultsDirs = [...redirectedResultsDirs];
  return {
    worktree: target.targetWorktree,
    remoteCwdRel: target.remoteCwdRel,
    bashCommand: prepared.command,
    env,
    collectPaths: [...collectMap.values()],
    ...(resultsDirs.length > 0 ? { redirectedResultsDirs: resultsDirs } : {}),
    ...(prepared.note !== undefined ? { note: prepared.note } : {}),
  };
}
