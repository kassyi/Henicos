// The keys under which a command with no recorded run is compared with similar ones, built from the words of the
// command (command-reader.ts), not from its text: `normalized` (the work: programs, projects resolved against the
// worktree, the options that change the work), `shape` (the kind of each step, the projects it names, no-build,
// filtered) and `kind` (the kinds of step alone).

import * as path from "node:path";
import { readCommand, type ReadStep } from "./command-reader.ts";

/** Where the command runs: its directory and the root of its worktree (both as the scheduler holds them). */
export interface KeyContext {
  cwd: string;
  worktree: string;
}

export interface CommandKeys {
  normalized: string;
  shape: string;
  kind: string;
}

/** Steps that take no measurable time of their own (and `$...` steps of PowerShell: assignments). */
const Trivial = new Set([
  "echo",
  "export",
  "set",
  "mkdir",
  "rm",
  "cp",
  "mv",
  "cat",
  "ls",
  "true",
  "sleep",
  "test",
  "grep",
  "sort",
  "cut",
  "head",
  "tail",
  "wc",
  "sed",
  "awk",
]);

// What a dotnet command line says that does not change the work: dropped by name
const DroppedFlags = new Set(["--nologo", "-nologo", "--no-restore", "-tl", "--tl", "--interactive", "--blame"]);
const DroppedFlagPrefixes = ["--blame-"];
const DroppedValueOptions = new Set([
  "-l",
  "--logger",
  "--results-directory",
  "-v",
  "--verbosity",
  "--blame-crash-dump-type",
  "--blame-hang-dump-type",
  "--blame-hang-timeout",
  "-s",
  "--settings",
  "--collect",
  "-d",
  "--diag",
  "-e",
  "--environment",
  "--test-adapter-path",
]);
// MSBuild switches written with a value (-m:4, -nodeReuse:false, -v:q), by name
const DroppedInline = new Set([
  "m",
  "maxcpucount",
  "nodereuse",
  "nr",
  "v",
  "verbosity",
  "l",
  "logger",
  "clp",
  "consoleloggerparameters",
  "tl",
  "nologo",
  "results-directory",
]);
// What changes the work, with its value as the next word, under one name
const KeptValueOptions = new Map([
  ["-c", "-c"],
  ["--configuration", "-c"],
  ["-f", "-f"],
  ["--framework", "-f"],
  ["-r", "-r"],
  ["--runtime", "-r"],
  ["--filter", "--filter"],
]);

const WindowsPath = /^[A-Za-z]:[\\/]/;

/** A path in forward slashes, without a leading `./`. */
function slashes(text: string): string {
  const unified = text.split("\\").join("/");
  return unified.startsWith("./") ? unified.slice(2) : unified;
}

/**
 * A project or script argument as the path from the worktree root when it lies inside the worktree, else as written. A
 * relative argument is resolved against the step's directory (the job's, or the target of a `cd` before it); without a
 * directory (an entry of the history that has none) it stays as written.
 */
function relativeToWorktree(argument: string, context: KeyContext): string {
  const base = context.cwd !== "" ? context.cwd : context.worktree;
  if (base === "") {
    return slashes(argument);
  }
  const style = WindowsPath.test(base) ? path.win32 : path.posix;
  // Git Bash (/c/x) and WSL (/mnt/c/x) spell a Windows path
  const gitBash = style === path.win32 ? /^\/(?:mnt\/)?([A-Za-z])\/(.*)$/.exec(argument) : null;
  const text = gitBash !== null ? `${gitBash[1]}:/${gitBash[2]}` : argument;
  const absolute = style.resolve(base, text);
  const root = context.worktree !== "" ? context.worktree : base;
  const relative = style.relative(root, absolute);
  if (relative === ".." || relative.startsWith(`..${style.sep}`) || style.isAbsolute(relative)) {
    return slashes(argument);
  }
  return relative === "" ? "." : slashes(relative);
}

/** The program of a step: its file name in lower case without `.exe`. */
function programOf(word: string): string {
  const parts = slashes(word).split("/");
  return (parts[parts.length - 1] as string).toLowerCase().replace(/\.exe$/, "");
}

/** The name of a project or script for the shape: its file name without the extension. */
function nameOf(argument: string): string {
  const parts = slashes(argument).split("/");
  return (parts[parts.length - 1] as string).replace(/\.(csproj|slnx?|js|ts|cs)$/, "");
}

interface StepKeys {
  normalized: string;
  shape: string;
  kind: string;
}

/** One dotnet step: the verb, the projects, the options that change the work. */
function dotnetKeys(words: string[], context: KeyContext): StepKeys {
  const verb = words[1] ?? "";
  const projects: string[] = [];
  const options: string[] = [];
  let extra: string[] = [];
  let noBuild = false;
  let filtered = false;
  for (let i = 2; i < words.length; i++) {
    const word = words[i] as string;
    const lower = word.toLowerCase();
    const inline = /^(?:--?|\/)([\w-]+)[:=](.*)$/.exec(word);
    if (word === "--") {
      extra = words.slice(i + 1);
      break;
    }
    if (inline !== null) {
      const name = (inline[1] as string).toLowerCase();
      if (name === "filter") {
        filtered = true;
        options.push(`--filter ${inline[2] ?? ""}`);
      } else if (!DroppedInline.has(name)) {
        options.push(word);
      }
    } else if (word.startsWith("-")) {
      const kept = KeptValueOptions.get(lower);
      if (kept !== undefined) {
        filtered = filtered || kept === "--filter";
        options.push(`${kept} ${words[++i] ?? ""}`);
      } else if (DroppedValueOptions.has(lower)) {
        i++;
      } else if (!DroppedFlags.has(lower) && !DroppedFlagPrefixes.some((prefix) => lower.startsWith(prefix))) {
        noBuild = noBuild || lower === "--no-build";
        options.push(word);
      }
    } else {
      projects.push(relativeToWorktree(word, context));
    }
  }
  const named = [...projects.map(nameOf)].sort().join("+");
  const work = ["dotnet", verb, ...projects, ...options.sort(), ...(extra.length > 0 ? ["--", ...extra] : [])];
  if (verb !== "build" && verb !== "test") {
    return { normalized: work.join(" "), shape: `dotnet:${verb}`, kind: "dotnet" };
  }
  const flags = `${noBuild ? ":nb" : ""}${verb === "test" && filtered ? ":f" : ""}`;
  return { normalized: work.join(" "), shape: `${verb}:${named}${flags}`, kind: verb };
}

function stepKeys(step: ReadStep, context: KeyContext): StepKeys {
  const words = step.words;
  const program = programOf(words[0] as string);
  if (program === "dotnet") {
    return dotnetKeys(words, context);
  }
  if (program === "just") {
    const recipe = words.slice(1).find((word) => !word.startsWith("-")) ?? "";
    return { normalized: words.join(" "), shape: `just:${recipe}`, kind: "just" };
  }
  if (program === "node") {
    const at = words.findIndex((word, i) => i > 0 && !word.startsWith("-"));
    const script = at > 0 ? relativeToWorktree(words[at] as string, context) : "";
    const rest = at > 0 ? words.slice(at + 1) : words.slice(1);
    return { normalized: ["node", script, ...rest].join(" "), shape: `node:${nameOf(script)}`, kind: "node" };
  }
  return { normalized: words.join(" "), shape: program, kind: program };
}

/** The first program of a command text that cannot be read, for the kind alone. */
function firstProgram(command: string): string {
  const word = command.trim().split(/\s+/)[0] ?? "";
  return word === "" ? "" : programOf(word.replace(/["']/g, ""));
}

/**
 * The keys of a command. A command that cannot be read (command-reader.ts) has no normalized key and no shape, only
 * the kind of its first program; one of trivial steps has none at all.
 */
export function commandKeys(command: string, context: KeyContext): CommandKeys {
  const read = readCommand(command);
  if (read === null) {
    return { normalized: "", shape: "", kind: firstProgram(command) };
  }
  const steps: StepKeys[] = [];
  let directory = context.cwd;
  for (const step of read) {
    const first = step.words[0] as string;
    if (programOf(first) === "cd") {
      const target = step.words[1];
      directory = target === undefined ? directory : directory === "" ? target : relativeAbsolute(directory, target);
    } else if (!first.startsWith("$") && !Trivial.has(programOf(first))) {
      steps.push(stepKeys(step, { cwd: directory, worktree: context.worktree }));
    }
  }
  return {
    normalized: steps.map((step) => step.normalized).join(" ; "),
    shape: steps.map((step) => step.shape).join("&"),
    kind: steps.map((step) => step.kind).join("&"),
  };
}

/** The directory a `cd` leads to, from the directory it ran in (both as the scheduler holds them). */
function relativeAbsolute(directory: string, target: string): string {
  const style = WindowsPath.test(directory) ? path.win32 : path.posix;
  return style.resolve(directory, target);
}
