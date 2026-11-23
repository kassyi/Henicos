// Layer 3 Command Translator:
// Translates a Windows (PowerShell / Git Bash / sh / cmd) CPU command into a POSIX bash plan for a remote Linux
// worker via a `ShellDialect` Strategy Map and a 5-stage fail-closed pipeline.

import * as fs from "node:fs";
import * as path from "node:path";
import { KeepBuildServersWarmVariable, type ShellName } from "../constants.ts";
import { bare, rawWords, segmentParts } from "../command-reader.ts";
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

const PwshLeadingEnv = /^\$env:([A-Za-z_]\w*)\s*=\s*(?:'([^']*)'|"([^"$`]*)"|([^\s;&|]+))\s*;\s*/;
const PwshInlineEnv = /^\$env:([A-Za-z_]\w*)\s*=\s*(?:'([^']*)'|"([^"$`]*)"|([^\s;&|]+))$/;
const PosixLeadingEnv = /^export\s+([A-Za-z_]\w*)=(?:'([^']*)'|"([^"$`\\]*)"|([^\s;&|]+))\s*;\s*/;

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

function looksLikePath(token: string, cwd: string, extraExtensions: ReadonlySet<string>): boolean {
  if (token === "" || token.startsWith("-")) {
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
  return fs.existsSync(path.resolve(cwd, token));
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

/** Stage 1: strips dialect-specific leading environment assignments. */
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

/** Stage 2: splits on top-level `&&` and `;` outside quotes; returns null on pipes, subshells, or newlines. */
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
  words: string[];
  segEnv: Record<string, string>;
  redirections: string[];
  cwd: string;
}

/** Stage 3: validates programs, tracks `cd`, and collects every referenced worktree root. */
function parseAndValidateStatements(
  statements: readonly { op: "&&" | ";"; text: string }[],
  options: TranslateOptions,
  dialect: ShellDialect,
  rules: ResolvedRemoteRules,
  env: Record<string, string>,
): { parsed: ParsedStatement[]; worktrees: Set<string>; finalCwd: string } | null {
  let currentCwd = path.resolve(options.cwd);
  const worktrees = new Set<string>();
  const parsed: ParsedStatement[] = [];

  for (const stmt of statements) {
    const inlineEnv = dialect.matchInlineEnvStatement(stmt.text);
    if (inlineEnv !== null) {
      env[inlineEnv.key] = inlineEnv.value;
      continue;
    }
    const parts = segmentParts(stmt.text, options.shell);
    const words = rawWords(parts.core, options.shell);
    if (words === null || words.length === 0) {
      return null;
    }
    const progLower = path
      .basename(bare(words[0] as string))
      .toLowerCase()
      .replace(/\.exe$/, "");

    if (progLower === "cd" || progLower === "set-location" || progLower === "sl") {
      if (words.length !== 2) {
        return null;
      }
      currentCwd = path.resolve(currentCwd, bare(words[1] as string));
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

    const segEnv: Record<string, string> = Object.create(null) as Record<string, string>;
    if (parts.env !== "") {
      for (const token of parts.env.split(/\s+/)) {
        const eq = token.indexOf("=");
        if (eq <= 0) {
          return null;
        }
        segEnv[token.slice(0, eq)] = bare(token.slice(eq + 1));
      }
    }

    const subcommand = (words[1] ? bare(words[1]) : "").toLowerCase();
    for (let i = 1; i < words.length; i++) {
      const token = bare(words[i] as string);
      if (token === "--") {
        break;
      }
      const prev = i > 1 ? bare(words[i - 1] as string).toLowerCase() : "";
      const decision = prev !== "" ? rules.isOptionValuePath(progLower, subcommand, prev) : undefined;
      const isNonPath =
        decision === false ||
        (decision === undefined && (BaseNonPathValueOptions.has(prev) || rules.nonPathValueOptions.has(prev)));
      if (isNonPath) {
        continue;
      }
      const isPath =
        decision === true ||
        BasePathValueOptions.has(prev) ||
        rules.pathValueOptions.has(prev) ||
        (progLower !== "echo" && looksLikePath(token, currentCwd, rules.pathExtensions));
      if (!isPath) {
        continue;
      }
      const abs = path.resolve(currentCwd, token);
      const place = options.places[abs];
      if (place !== undefined) {
        if (place.root === "" || place.ignored) {
          return null;
        }
        worktrees.add(path.resolve(place.root));
      } else {
        const wt = findWorktreeRoot(abs);
        if (wt === "") {
          return null;
        }
        worktrees.add(wt);
      }
    }

    parsed.push({ op: stmt.op, program: progLower, words, segEnv, redirections: parts.redirections, cwd: currentCwd });
  }

  return parsed.length > 0 ? { parsed, worktrees, finalCwd: currentCwd } : null;
}

/** Stage 4: enforces the single-worktree invariant and resolves the remote working directory. */
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

/** Stage 5: rewrites one parsed statement into POSIX bash and registers any collected paths. */
function rewriteStatementToBash(
  stmt: ParsedStatement,
  targetWorktree: string,
  remoteBaseDir: string,
  warmServers: boolean,
  cpus: number,
  rules: ResolvedRemoteRules,
  addCollect: (rel: string, kind: "file" | "dir") => void,
): string | null {
  const subcommand = (stmt.words[1] ? bare(stmt.words[1]) : "").toLowerCase();
  const translatedArgs: string[] = [];

  for (let i = 1; i < stmt.words.length; i++) {
    const token = bare(stmt.words[i] as string);
    if (token === "--") {
      translatedArgs.push("--", ...stmt.words.slice(i + 1).map((w) => bare(w)));
      break;
    }
    const prev = i > 1 ? bare(stmt.words[i - 1] as string).toLowerCase() : "";
    const decision = prev !== "" ? rules.isOptionValuePath(stmt.program, subcommand, prev) : undefined;
    const isNonPath =
      decision === false ||
      (decision === undefined && (BaseNonPathValueOptions.has(prev) || rules.nonPathValueOptions.has(prev)));

    if (
      !isNonPath &&
      (decision === true ||
        BasePathValueOptions.has(prev) ||
        rules.pathValueOptions.has(prev) ||
        (stmt.program !== "echo" && looksLikePath(token, stmt.cwd, rules.pathExtensions)))
    ) {
      const abs = path.resolve(stmt.cwd, token);
      if (!isInside(targetWorktree, abs)) {
        return null;
      }
      translatedArgs.push(path.relative(remoteBaseDir, abs).split(path.sep).join("/") || ".");
      if (rules.collectDirOptions.has(prev)) {
        addCollect(relativePosix(targetWorktree, abs), "dir");
      }
    } else {
      translatedArgs.push(token);
    }
  }

  const rewritten = rules.rewriteStatement({
    program: stmt.program,
    subcommand,
    args: translatedArgs,
    keepBuildServersWarm: warmServers,
    cpus,
  });
  for (const extra of rewritten.collectPaths ?? []) {
    addCollect(extra.path, extra.kind);
  }
  const outWords = [stmt.program, ...(rewritten.args ?? translatedArgs).map((arg) => bashQuote(arg))];

  const outRedirs: string[] = [];
  for (const redir of stmt.redirections) {
    if (/^[12*]?&\d$/.test(redir) || redir === "2>&1") {
      outRedirs.push("2>&1");
      continue;
    }
    const m = /^([12&*]?)(>>?)\s*(.+)$/.exec(redir);
    if (m === null) {
      return null;
    }
    const fd = m[1] === "*" ? "&" : (m[1] ?? "");
    const abs = path.resolve(stmt.cwd, bare(m[3] as string));
    if (!isInside(targetWorktree, abs)) {
      return null;
    }
    addCollect(relativePosix(targetWorktree, abs), "file");
    outRedirs.push(`${fd}${m[2] as string} ${bashQuote(path.relative(remoteBaseDir, abs).split(path.sep).join("/"))}`);
  }

  const segEnvPrefix = Object.entries(stmt.segEnv)
    .map(([k, v]) => `${k}=${bashQuote(v)}`)
    .join(" ");
  return `${segEnvPrefix ? `${segEnvPrefix} ` : ""}${outWords.join(" ")}${outRedirs.length > 0 ? ` ${outRedirs.join(" ")}` : ""}`;
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

  const validated = parseAndValidateStatements(statements, options, dialect, rules, env);
  if (validated === null) {
    return null;
  }
  const target = resolveSingleWorktree(validated.worktrees, validated.finalCwd, options.worktree);
  if (target === null) {
    return null;
  }

  const collectMap = new Map<string, CollectPathSpec>();
  const addCollect = (rel: string, kind: "file" | "dir"): void => {
    const clean = path.posix.normalize(rel).replace(/\/+$/, "");
    if (clean !== "" && clean !== "." && !clean.startsWith("..")) {
      collectMap.set(`${kind}:${clean}`, { path: clean, kind });
    }
  };

  const bashParts: string[] = [];
  for (let i = 0; i < validated.parsed.length; i++) {
    const stmt = validated.parsed[i] as ParsedStatement;
    const core = rewriteStatementToBash(
      stmt,
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
