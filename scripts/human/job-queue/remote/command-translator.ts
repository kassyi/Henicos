// Layer 3 Command Translator:
// Translates a Windows (PowerShell / Git Bash / sh / cmd) CPU command into a POSIX bash plan for a remote Linux
// worker via a `ShellDialect` Strategy Map and a five-step fail-closed pipeline.

import * as fs from "node:fs";
import * as path from "node:path";
import { KeepBuildServersWarmVariable, type ShellName } from "../constants.ts";
import { rawWords, segmentParts } from "../command-reader.ts";
import type { PathPlace } from "../estimate-key.ts";
import type { CollectPathSpec } from "./artifact-collector.ts";
import { EmptyRemoteRules, type ResolvedRemoteRules } from "./rules.ts";
import { bashQuote } from "./spec.ts";

export interface RemoteCommandPlan {
  /** Single local git worktree root to sync to the remote workspace. */
  worktree: string;
  /** Working directory relative to `worktree` (POSIX slashes, "" for worktree root). */
  remoteCwdRel: string;
  /** POSIX bash command line to execute inside `<workspace>/<remoteCwdRel>`. */
  bashCommand: string;
  /** Environment variables parsed from leading assignments + plugin remote defaults. */
  env: Record<string, string>;
  /** Relative POSIX paths (e.g. plugin collectDirOptions or redirection targets) to pull back after execution. */
  collectPaths: CollectPathSpec[];
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
  /** Aggregated remote offload rules contributed by active plugins. */
  remoteRules?: ResolvedRemoteRules;
}

/** Lightweight Strategy encapsulating shell-dialect syntax differences (PowerShell / POSIX sh / cmd). */
interface ShellDialect {
  readonly supportsSingleQuote: boolean;
  matchLeadingEnv(text: string): { key: string; value: string; consumed: number } | null;
  matchInlineEnvStatement(text: string): { key: string; value: string } | null;
}

const PwshLeadingEnv = /^\$env:([A-Za-z_]\w*)\s*=\s*(?:'([^']*)'|"([^"$`]*)"|([^\s;&|$`"'()\\]+))\s*;\s*/;
const PwshInlineEnv = /^\$env:([A-Za-z_]\w*)\s*=\s*(?:'([^']*)'|"([^"$`]*)"|([^\s;&|$`"'()\\]+))$/;
const PosixLeadingEnv = /^export\s+([A-Za-z_]\w*)=(?:'([^']*)'|"([^"$`\\]*)"|([^\s;&|$`"'()\\]+))\s*;\s*/;

const PwshDialect: ShellDialect = {
  supportsSingleQuote: true,
  matchLeadingEnv(text) {
    const m = PwshLeadingEnv.exec(text);
    return m !== null ? { key: m[1] as string, value: m[2] ?? m[3] ?? m[4] ?? "", consumed: m[0].length } : null;
  },
  matchInlineEnvStatement(text) {
    const m = PwshInlineEnv.exec(text);
    return m !== null ? { key: m[1] as string, value: m[2] ?? m[3] ?? m[4] ?? "" } : null;
  },
};

const PosixDialect: ShellDialect = {
  supportsSingleQuote: true,
  matchLeadingEnv(text) {
    const m = PosixLeadingEnv.exec(text);
    return m !== null ? { key: m[1] as string, value: m[2] ?? m[3] ?? m[4] ?? "", consumed: m[0].length } : null;
  },
  matchInlineEnvStatement: () => null,
};

const CmdDialect: ShellDialect = {
  supportsSingleQuote: false,
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
const BaseNonPathValueOptions = new Set(["-e", "--environment"]);
const BasePathExtensions = new Set(["js", "ts", "json", "yml", "yaml"]);
const BaseAllowedPrograms = new Set(["node", "echo"]);
const BroadRepoDirs = new Set([".", "src", "tests", "docs", "tools", "scripts", ".github", ".agents"]);
const MsbuildSwitchPattern = new RegExp(
  [
    String.raw`^\/[A-Za-z][A-Za-z0-9_-]*[:=]`,
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
  ].join("|"),
  "i",
);

function normKey(p: string): string {
  const resolved = path.resolve(p);
  return process.platform === "win32" ? resolved.toLowerCase() : resolved;
}

function isInside(root: string, target: string): boolean {
  const rel = path.relative(normKey(root), normKey(target));
  return rel === "" || (!rel.startsWith("..") && !path.isAbsolute(rel));
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

function looksLikePath(token: string, cwd: string, extraExtensions: ReadonlySet<string>): boolean {
  if (token === "" || token.startsWith("-") || MsbuildSwitchPattern.test(token)) {
    return false;
  }
  if (/^[A-Za-z]:[\\/]/.test(token) || token.includes("\\") || token.includes("/")) {
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

/** Extracts dialect-specific leading environment assignments. */
function extractLeadingEnv(command: string, dialect: ShellDialect): { env: Record<string, string>; rest: string } {
  const env: Record<string, string> = Object.create(null) as Record<string, string>;
  let rest = command.trim();
  for (;;) {
    const hit = dialect.matchLeadingEnv(rest);
    if (hit === null) {
      break;
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
      } else if (quote === '"' && (ch === "`" || (ch === "$" && next === "("))) {
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
    } else if (ch === "|" || ch === "<" || ch === "`" || ch === "(" || ch === ")" || ch === "\n" || ch === "\r") {
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
  const longMatch = /^(--[A-Za-z][A-Za-z0-9-]*)([:=])(.*)$/.exec(token);
  if (longMatch !== null) {
    return { option: longMatch[1] as string, sep: longMatch[2] as string, value: longMatch[3] as string };
  }
  const shortMatch = /^(-[A-Za-z][A-Za-z0-9-]*)=(.*)$/.exec(token);
  if (shortMatch !== null) {
    return { option: shortMatch[1] as string, sep: "=", value: shortMatch[2] as string };
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
    const val = unquoteToken(token.slice(eq + 1), shell);
    if (val === null) {
      return null;
    }
    segEnv[token.slice(0, eq)] = val;
  }
  return segEnv;
}

function isInlinePathOption(
  kind: OptionKind,
  program: string,
  value: string,
  cwd: string,
  rules: ResolvedRemoteRules,
): boolean {
  return (
    kind === "path" || (kind === "unknown" && program !== "echo" && looksLikePath(value, cwd, rules.pathExtensions))
  );
}

function validateStatementArgs(
  args: readonly string[],
  program: string,
  currentCwd: string,
  placesMap: ReadonlyMap<string, PathPlace>,
  rules: ResolvedRemoteRules,
  worktrees: Set<string>,
): boolean {
  const subcommand = (args[0] ?? "").toLowerCase();
  let prevOption = "";
  for (const token of args) {
    if (token === "--") {
      break;
    }
    if (prevOption !== "") {
      const prevKind = classifyOption(rules, program, subcommand, prevOption);
      if (prevKind === "non-path") {
        prevOption = "";
        continue;
      }
      if (prevKind === "path") {
        if (!registerPathWorktree(token, currentCwd, placesMap, worktrees)) {
          return false;
        }
        prevOption = "";
        continue;
      }
    }
    const inline = splitInlineOption(token);
    if (inline !== null) {
      const kind = classifyOption(rules, program, subcommand, inline.option.toLowerCase());
      if (kind === "path" && inline.value === "") {
        return false;
      }
      if (
        isInlinePathOption(kind, program, inline.value, currentCwd, rules) &&
        !registerPathWorktree(inline.value, currentCwd, placesMap, worktrees)
      ) {
        return false;
      }
      prevOption = "";
      continue;
    }
    if (program !== "echo" && looksLikePath(token, currentCwd, rules.pathExtensions)) {
      if (!registerPathWorktree(token, currentCwd, placesMap, worktrees)) {
        return false;
      }
      prevOption = "";
      continue;
    }
    prevOption = token.startsWith("-") ? token.toLowerCase() : "";
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
  let currentCwd = path.resolve(options.cwd);
  const worktrees = new Set<string>();
  const parsed: ParsedStatement[] = [];

  for (const stmt of statements) {
    const inlineEnv = dialect.matchInlineEnvStatement(stmt.text);
    if (inlineEnv !== null) {
      if (parsed.length > 0) {
        return null;
      }
      env[inlineEnv.key] = inlineEnv.value;
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
      currentCwd = resolveArgPath(unquoted[1] as string, currentCwd);
      const wt = findWorktreeRoot(currentCwd);
      if (wt === "") {
        return null;
      }
      worktrees.add(wt);
      continue;
    }

    if (!BaseAllowedPrograms.has(progLower) && !rules.allowedPrograms.has(progLower)) {
      return null;
    }
    const segEnv = parseSegEnv(parts.env, options.shell);
    if (segEnv === null) {
      return null;
    }
    const args = unquoted.slice(1);
    if (!validateStatementArgs(args, progLower, currentCwd, placesMap, rules, worktrees)) {
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
  addCollect: (rel: string, kind: "file" | "dir") => boolean,
): string | null {
  const abs = resolveArgPath(rawPath, cwd);
  if (!isInside(targetWorktree, abs)) {
    return null;
  }
  if (collectAsDir && !addCollect(relativePosix(targetWorktree, abs), "dir")) {
    return null;
  }
  return path.relative(remoteBaseDir, abs).split(path.sep).join("/") || ".";
}

function translateArgsToPosix(
  stmt: ParsedStatement,
  targetWorktree: string,
  remoteBaseDir: string,
  rules: ResolvedRemoteRules,
  addCollect: (rel: string, kind: "file" | "dir") => boolean,
): string[] | null {
  const subcommand = (stmt.args[0] ?? "").toLowerCase();
  const translated: string[] = [];
  let prevOption = "";

  for (let i = 0; i < stmt.args.length; i++) {
    const token = stmt.args[i] as string;
    if (token === "--") {
      translated.push("--", ...stmt.args.slice(i + 1));
      break;
    }
    if (prevOption !== "") {
      const prevKind = classifyOption(rules, stmt.program, subcommand, prevOption);
      if (prevKind === "non-path") {
        translated.push(token);
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
      const kind = classifyOption(rules, stmt.program, subcommand, optLower);
      if (isInlinePathOption(kind, stmt.program, inline.value, stmt.cwd, rules)) {
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
      } else {
        translated.push(token);
      }
      prevOption = "";
      continue;
    }
    if (stmt.program !== "echo" && looksLikePath(token, stmt.cwd, rules.pathExtensions)) {
      const rel = translatePathArg(token, stmt.cwd, targetWorktree, remoteBaseDir, false, addCollect);
      if (rel === null) {
        return null;
      }
      translated.push(rel);
      prevOption = "";
      continue;
    }
    translated.push(token);
    prevOption = token.startsWith("-") ? token.toLowerCase() : "";
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
    if (/^[12*]?&\d$/.test(redir) || redir === "2>&1") {
      outRedirs.push("2>&1");
      continue;
    }
    const m = /^([12&*]?)(>>?)\s*(.+)$/.exec(redir);
    if (m === null) {
      return null;
    }
    const fd = m[1] === "*" ? "&" : (m[1] ?? "");
    const targetToken = unquoteToken(m[3] as string, shell);
    if (targetToken === null) {
      return null;
    }
    const abs = resolveArgPath(targetToken, cwd);
    if (!isInside(targetWorktree, abs) || !addCollect(relativePosix(targetWorktree, abs), "file")) {
      return null;
    }
    const rel = path.relative(remoteBaseDir, abs).split(path.sep).join("/");
    outRedirs.push(`${fd}${m[2] as string} ${bashQuote(rel)}`);
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
  addCollect: (rel: string, kind: "file" | "dir") => boolean,
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
  if (leading.rest === "") {
    return null;
  }
  const statements = splitStatements(leading.rest, dialect);
  if (statements === null) {
    return null;
  }

  const rules = options.remoteRules ?? EmptyRemoteRules;
  const env: Record<string, string> = Object.assign(Object.create(null) as Record<string, string>, leading.env);
  const warmServers = options.keepBuildServersWarm && options.env[KeepBuildServersWarmVariable] !== "0";
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
  const addCollect = (rel: string, kind: "file" | "dir"): boolean => {
    const clean = path.posix.normalize(rel).replace(/\/+$/, "");
    if (clean === "" || clean === "." || clean.startsWith("..")) {
      return false;
    }
    if (kind === "dir" && BroadRepoDirs.has(clean.toLowerCase())) {
      return false;
    }
    collectMap.set(`${kind}:${clean}`, { path: clean, kind });
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
    env[key] ??= value;
  }
  const prepared = rules.prepareRemoteCommand(bashParts.join(" "), {
    cpus: options.cpus,
    keepBuildServersWarm: warmServers,
    env,
  });
  for (const [key, value] of Object.entries(prepared.env)) {
    env[key] = value;
  }

  return {
    worktree: target.targetWorktree,
    remoteCwdRel: target.remoteCwdRel,
    bashCommand: prepared.command,
    env,
    collectPaths: [...collectMap.values()],
    ...(prepared.note !== undefined ? { note: prepared.note } : {}),
  };
}
