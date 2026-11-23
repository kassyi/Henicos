// Translates a Windows (PowerShell / Git Bash / sh) CPU build/test command into a POSIX bash command for a remote
// Linux worker. Fail-closed: returns null whenever any syntax, path, or multi-worktree boundary cannot be translated
// unambiguously, so the job falls back to local execution without error.

import * as fs from "node:fs";
import * as path from "node:path";
import { KeepBuildServersWarmVariable, type ShellName } from "./constants.ts";
import { bare, rawWords, segmentParts } from "./command-reader.ts";
import type { PathPlace } from "./estimate-key.ts";
import { limitMsbuildNodes } from "./plugins/dotnet/msbuild-nodes.ts";

export interface RemoteCommandPlan {
  /** Single local git worktree root to sync to the remote workspace. */
  worktree: string;
  /** Working directory relative to `worktree` (POSIX slashes, "" for worktree root). */
  remoteCwdRel: string;
  /** POSIX bash command line to execute inside `<workspace>/<remoteCwdRel>`. */
  bashCommand: string;
  /** Environment variables parsed from leading assignments + remote defaults. */
  env: Record<string, string>;
  /** Relative POSIX paths (e.g. --results-directory or redirection targets) to pull back after execution. */
  collectPaths: string[];
}

export interface TranslateOptions {
  cwd: string;
  worktree: string;
  shell: ShellName;
  places: Readonly<Record<string, PathPlace>>;
  cpus: number;
  keepBuildServersWarm: boolean;
  env: Readonly<NodeJS.ProcessEnv>;
}

/** Single-quotes a string for POSIX bash. */
export function bashQuote(value: string): string {
  if (/^[A-Za-z0-9_@%+=:,./-]+$/.test(value)) {
    return value;
  }
  return `'${value.replace(/'/g, "'\\''")}'`;
}

/** Normalizes a path for case-insensitive comparison on Windows. */
function normKey(p: string): string {
  const resolved = path.resolve(p);
  return process.platform === "win32" ? resolved.toLowerCase() : resolved;
}

/** True when `target` lies inside (or equals) `root`. */
function isInside(root: string, target: string): boolean {
  const rel = path.relative(normKey(root), normKey(target));
  return rel === "" || (!rel.startsWith("..") && !path.isAbsolute(rel));
}

/** Converts an absolute path inside `root` into a POSIX path relative to `root` (or "." for root itself). */
function relativePosix(root: string, absolute: string): string {
  const rel = path.relative(normKey(root), normKey(absolute));
  if (rel === "") {
    return ".";
  }
  // Preserve original casing from `absolute` by slicing the matching prefix length
  const exactRel = path.relative(path.resolve(root), path.resolve(absolute));
  return (exactRel || rel).split(path.sep).join("/");
}

/** Parses leading PowerShell `$env:KEY = 'val';` or bash `export KEY=val;` statements. */
function extractLeadingEnv(command: string, shell: ShellName): { env: Record<string, string>; rest: string } | null {
  const env: Record<string, string> = Object.create(null) as Record<string, string>;
  let rest = command.trim();
  const isPwsh = shell === "pwsh" || shell === "powershell";

  for (;;) {
    if (isPwsh) {
      const m = /^\$env:([A-Za-z_]\w*)\s*=\s*(?:'([^']*)'|"([^"$`]*)"|([^\s;&|]+))\s*;\s*/.exec(rest);
      if (m !== null) {
        const key = m[1] as string;
        const val = m[2] ?? m[3] ?? m[4] ?? "";
        env[key] = val;
        rest = rest.slice(m[0].length);
        continue;
      }
    } else if (shell === "bash" || shell === "sh") {
      const m = /^export\s+([A-Za-z_]\w*)=(?:'([^']*)'|"([^"$`\\]*)"|([^\s;&|]+))\s*;\s*/.exec(rest);
      if (m !== null) {
        const key = m[1] as string;
        const val = m[2] ?? m[3] ?? m[4] ?? "";
        env[key] = val;
        rest = rest.slice(m[0].length);
        continue;
      }
    }
    break;
  }
  return { env, rest };
}

/** Splits a command on top-level `&&` and `;` outside quotes; returns null on `||`, `|`, `$()`, backticks, `<`. */
function splitStatements(command: string, shell: ShellName): { op: "&&" | ";"; text: string }[] | null {
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
    } else if (ch === '"' || (ch === "'" && shell !== "cmd")) {
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

/** Options whose next argument is a filesystem path (translated to relative POSIX when inside the target worktree). */
const PathValueOptions = new Set([
  "--results-directory",
  "-s",
  "--settings",
  "-o",
  "--output",
  "--artifacts-path",
  "--project",
  "-p", // note: dotnet run -p <proj>, while dotnet test -p is --property (handled below)
]);

/** Options whose next argument is NOT a path (e.g. --filter, -c, -f, -e, --logger). */
const NonPathValueOptions = new Set([
  "--filter",
  "-c",
  "--configuration",
  "-f",
  "--framework",
  "-r",
  "--runtime",
  "-a",
  "--arch",
  "--os",
  "-v",
  "--verbosity",
  "-l",
  "--logger",
  "--collect",
  "-e",
  "--environment",
  "--blame-crash-dump-type",
  "--blame-hang-dump-type",
  "--blame-hang-timeout",
  "--property",
]);

/** Looks like a filesystem path argument (project file, directory with slash/backslash, or existing file). */
function looksLikePath(token: string, cwd: string): boolean {
  if (token === "" || token.startsWith("-")) {
    return false;
  }
  if (/^[A-Za-z]:[\\/]/.test(token) || token.includes("\\") || token.includes("/")) {
    return true;
  }
  if (/\.(csproj|fsproj|vbproj|sln|slnx|slnf|runsettings|js|ts|json|yml|yaml|cs)$/i.test(token)) {
    return true;
  }
  return fs.existsSync(path.resolve(cwd, token));
}

/** Finds the nearest directory containing `.git` at or above `dir`, or "" if none. */
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

const AllowedPrograms = new Set(["dotnet", "just", "node", "echo"]);

/**
 * Translates `command` into a remote Linux bash execution plan, or returns null when the command cannot be
 * safely and unambiguously executed on a remote Linux worker.
 */
export function translateForRemote(command: string, options: TranslateOptions): RemoteCommandPlan | null {
  if (/--%/.test(command)) {
    return null;
  }
  const leading = extractLeadingEnv(command, options.shell);
  if (leading === null || leading.rest === "") {
    return null;
  }
  const statements = splitStatements(leading.rest, options.shell);
  if (statements === null) {
    return null;
  }

  const env: Record<string, string> = Object.assign(Object.create(null) as Record<string, string>, leading.env);
  const warmServers = options.keepBuildServersWarm && options.env[KeepBuildServersWarmVariable] !== "0";

  // Pass 1: track cwd, validate every program, and determine the single target worktree root
  let currentCwd = path.resolve(options.cwd);
  const worktrees = new Set<string>();
  const parsedStatements: {
    op: "&&" | ";";
    program: string;
    words: string[];
    segEnv: Record<string, string>;
    redirections: string[];
    cwd: string;
  }[] = [];

  for (const stmt of statements) {
    // Handle inline $env:KEY='val' statements in PowerShell between commands
    if (options.shell === "pwsh" || options.shell === "powershell") {
      const m = /^\$env:([A-Za-z_]\w*)\s*=\s*(?:'([^']*)'|"([^"$`]*)"|([^\s;&|]+))$/.exec(stmt.text);
      if (m !== null) {
        env[m[1] as string] = m[2] ?? m[3] ?? m[4] ?? "";
        continue;
      }
    }
    const parts = segmentParts(stmt.text, options.shell);
    const words = rawWords(parts.core, options.shell);
    if (words === null || words.length === 0) {
      return null;
    }
    const rawProg = bare(words[0] as string);
    const progLower = path
      .basename(rawProg)
      .toLowerCase()
      .replace(/\.exe$/, "");

    // Handle directory change statements (cd / Set-Location)
    if (progLower === "cd" || progLower === "set-location" || progLower === "sl") {
      if (words.length !== 2) {
        return null;
      }
      const targetDir = bare(words[1] as string);
      currentCwd = path.resolve(currentCwd, targetDir);
      const wt = findWorktreeRoot(currentCwd);
      if (wt === "") {
        return null;
      }
      worktrees.add(wt);
      continue;
    }

    if (!AllowedPrograms.has(progLower)) {
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

    // Check path arguments against worktrees
    const subcommand = (words[1] ? bare(words[1]) : "").toLowerCase();
    for (let i = 1; i < words.length; i++) {
      const token = bare(words[i] as string);
      if (token === "--") {
        break;
      }
      const prev = i > 1 ? bare(words[i - 1] as string).toLowerCase() : "";
      if (NonPathValueOptions.has(prev)) {
        continue;
      }
      if (prev === "-p" && progLower === "dotnet" && subcommand !== "run") {
        continue;
      }
      if (PathValueOptions.has(prev) || (progLower !== "echo" && looksLikePath(token, currentCwd))) {
        const abs = path.resolve(currentCwd, token);
        const place = options.places[abs];
        if (place !== undefined) {
          if (place.root === "" || place.ignored) {
            return null; // outside any worktree or git-ignored scratch path
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
    }

    parsedStatements.push({
      op: stmt.op,
      program: progLower,
      words,
      segEnv,
      redirections: parts.redirections,
      cwd: currentCwd,
    });
  }

  if (parsedStatements.length === 0) {
    return null;
  }

  // Determine the single target worktree
  if (worktrees.size === 0) {
    const fallback = findWorktreeRoot(currentCwd) || (options.worktree !== "" ? path.resolve(options.worktree) : "");
    if (fallback === "") {
      return null;
    }
    worktrees.add(fallback);
  }
  // Normalize worktree paths for deduplication on Windows
  const uniqueRoots = new Map<string, string>();
  for (const wt of worktrees) {
    uniqueRoots.set(normKey(wt), wt);
  }
  if (uniqueRoots.size !== 1) {
    return null; // spans multiple worktrees: must stay local
  }
  const targetWorktree = [...uniqueRoots.values()][0] as string;

  // Compute remote working directory relative to targetWorktree (if caller's cwd is inside targetWorktree, keep it;
  // otherwise run from the target worktree root — e.g. main checkout running `dotnet test <other-worktree>/tests/...`)
  const remoteCwdRel = isInside(targetWorktree, currentCwd)
    ? relativePosix(targetWorktree, currentCwd).replace(/^\.$/, "")
    : "";
  const remoteBaseDir = remoteCwdRel === "" ? targetWorktree : path.resolve(targetWorktree, remoteCwdRel);

  // Pass 2: rewrite each statement into POSIX bash
  const collectPaths: string[] = [];
  const bashParts: string[] = [];

  for (let s = 0; s < parsedStatements.length; s++) {
    const stmt = parsedStatements[s] as (typeof parsedStatements)[number];
    const subcommand = (stmt.words[1] ? bare(stmt.words[1]) : "").toLowerCase();
    const outWords: string[] = [stmt.program];

    for (let i = 1; i < stmt.words.length; i++) {
      const raw = stmt.words[i] as string;
      const token = bare(raw);

      if (token === "--") {
        outWords.push("--", ...stmt.words.slice(i + 1).map((w) => bashQuote(bare(w))));
        break;
      }

      // Optionally keep VBCSCompiler warm on Linux by dropping Windows file-lock workarounds
      if (warmServers && stmt.program === "dotnet") {
        const lower = token.toLowerCase();
        if (lower === "--disable-build-servers" || /^[-/](nodereuse|nr)[:=]false$/.test(lower)) {
          continue;
        }
      }

      const prev = i > 1 ? bare(stmt.words[i - 1] as string).toLowerCase() : "";
      const isNonPathVal =
        NonPathValueOptions.has(prev) || (prev === "-p" && stmt.program === "dotnet" && subcommand !== "run");

      if (
        !isNonPathVal &&
        (PathValueOptions.has(prev) || (stmt.program !== "echo" && looksLikePath(token, stmt.cwd)))
      ) {
        const abs = path.resolve(stmt.cwd, token);
        if (!isInside(targetWorktree, abs)) {
          return null;
        }
        const relToBase = path.relative(remoteBaseDir, abs).split(path.sep).join("/") || ".";
        outWords.push(bashQuote(relToBase));
        if (prev === "--results-directory") {
          collectPaths.push(relativePosix(targetWorktree, abs));
        }
      } else {
        outWords.push(bashQuote(token));
      }
    }

    // Rewrite output redirections (e.g., `> out.log`, `2>&1`)
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
      const op = m[2] as string;
      const target = bare(m[3] as string);
      const abs = path.resolve(stmt.cwd, target);
      if (!isInside(targetWorktree, abs)) {
        return null;
      }
      const relToBase = path.relative(remoteBaseDir, abs).split(path.sep).join("/");
      collectPaths.push(relativePosix(targetWorktree, abs));
      outRedirs.push(`${fd}${op} ${bashQuote(relToBase)}`);
    }

    const segEnvPrefix = Object.entries(stmt.segEnv)
      .map(([k, v]) => `${k}=${bashQuote(v)}`)
      .join(" ");
    const core = `${segEnvPrefix ? `${segEnvPrefix} ` : ""}${outWords.join(" ")}${outRedirs.length > 0 ? ` ${outRedirs.join(" ")}` : ""}`;
    if (s > 0) {
      bashParts.push(stmt.op);
    }
    bashParts.push(core);
  }

  let bashCommand = bashParts.join(" ");
  if (options.cpus > 0) {
    bashCommand = limitMsbuildNodes(bashCommand, options.cpus);
  }

  // Ensure standard headless CI/test environment variables on the remote Linux server
  env.DiffEngine_Disabled ??= "true";
  env.GSHARP_TEST_NO_CUDA ??= "1";
  if (options.cpus > 0) {
    env.GSHARP_JOB_QUEUE_MSBUILD_ARGS = `-m:${options.cpus}`;
    env.MSBUILDCORELIMIT ??= String(options.cpus);
    env.MSBUILDNODECOREALLOCATIONWEIGHT ??= "100";
  }

  return {
    worktree: targetWorktree,
    remoteCwdRel,
    bashCommand,
    env,
    collectPaths: [...new Set(collectPaths)],
  };
}
