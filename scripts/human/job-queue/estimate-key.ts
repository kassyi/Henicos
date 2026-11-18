// The keys under which a command with no recorded run is compared with similar ones, built from the words of the
// command (command-reader.ts), not from its text: `normalized` (the work: programs, projects resolved against the
// worktree, the options that change the work), `shape` (the kind of each step, the projects it names, no-build,
// filtered) and `kind` (the kinds of step alone).

import * as path from "node:path";
import { readCommand, type ReadStep } from "./command-reader.ts";
import type { StepContext, StepKeys } from "./plugin-api.ts";

/** Structural plugin hook subset so estimate-key.ts never runtime-imports plugin-host.ts. */
export interface StepKeyProvider {
  stepKeys(step: ReadStep, context: StepContext): StepKeys | undefined;
}

/**
 * Where a project or script argument lies, as the client found it with git (path-places.ts): the root of the worktree
 * that holds it ("" when no git worktree does) and whether that worktree ignores it.
 */
export interface PathPlace {
  root: string;
  ignored: boolean;
}

/**
 * Where the command runs: its directory and the root of its worktree (both as the scheduler holds them), and the places
 * of its path arguments, keyed by the absolute path pathArguments gives (absent from an older client or history entry:
 * a path is then judged against the job's worktree alone).
 */
export interface KeyContext {
  cwd: string;
  worktree: string;
  places?: Readonly<Record<string, PathPlace>>;
}

/** A context that also collects the absolute paths of the arguments it resolves (pathArguments). */
interface WalkContext extends KeyContext {
  collect?: string[];
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

const WindowsPath = /^[A-Za-z]:[\\/]/;

/** A path in forward slashes, without a leading `./`. */
function slashes(text: string): string {
  const unified = text.split("\\").join("/");
  return unified.startsWith("./") ? unified.slice(2) : unified;
}

/**
 * The kind suffix of a step whose project or script is no work of a repository: `:outside` when no git worktree holds
 * it (a scratch project in a temporary folder), `:ignored` when the worktree that holds it ignores it (a probe under an
 * ignored folder). The kind keeps such steps apart from the repository's builds and tests.
 */
type Where = "" | ":outside" | ":ignored";
const OutsideKind: Where = ":outside";
const IgnoredKind: Where = ":ignored";

/** A project or script argument as the key spells it, and where it lies. */
interface KeyPath {
  text: string;
  where: Where;
}

/** A path relative to a root, or null when it lies outside it. */
function inside(style: typeof path.win32, root: string, absolute: string): string | null {
  const relative = style.relative(root, absolute);
  if (relative === ".." || relative.startsWith(`..${style.sep}`) || style.isAbsolute(relative)) {
    return null;
  }
  return relative === "" ? "." : slashes(relative);
}

/**
 * A project or script argument as the key spells it. With the place the client found for it: the path from the root of
 * the worktree that holds it (so that the same project shares its runs across worktrees), marked when that worktree
 * ignores it or no worktree holds it. Without one: the path from the job's worktree root when it lies inside, else as
 * written and marked outside. A relative argument is resolved against the step's directory (the job's, or the target of
 * a `cd` before it); without a directory (an entry of the history that has none) it stays as written, not marked.
 */
function relativeToWorktree(argument: string, context: WalkContext): KeyPath {
  const base = context.cwd !== "" ? context.cwd : context.worktree;
  if (base === "") {
    return { text: slashes(argument), where: "" };
  }
  const style = WindowsPath.test(base) ? path.win32 : path.posix;
  const absolute = absoluteOf(argument, base);
  context.collect?.push(absolute);
  const place = context.places?.[absolute];
  if (place !== undefined) {
    const relative = place.root === "" ? null : inside(style, place.root, absolute);
    return relative === null
      ? { text: slashes(argument), where: OutsideKind }
      : { text: relative, where: place.ignored ? IgnoredKind : "" };
  }
  const relative = inside(style, context.worktree !== "" ? context.worktree : base, absolute);
  return relative === null
    ? { text: slashes(argument), where: context.worktree !== "" ? OutsideKind : "" }
    : { text: relative, where: "" };
}

/** The absolute path of an argument from the directory it is resolved in (Git Bash and WSL spellings read). */
function absoluteOf(argument: string, base: string): string {
  const style = WindowsPath.test(base) ? path.win32 : path.posix;
  // Git Bash (/c/x) and WSL (/mnt/c/x) spell a Windows path
  const gitBash = style === path.win32 ? /^\/(?:mnt\/)?([A-Za-z])\/(.*)$/.exec(argument) : null;
  const text = gitBash !== null ? `${gitBash[1]}:/${gitBash[2]}` : argument;
  return style.resolve(base, text);
}

/** The program of a step: its file name in lower case without `.exe`. */
function programOf(word: string): string {
  const parts = slashes(word).split("/");
  return (parts[parts.length - 1] as string).toLowerCase().replace(/\.exe$/, "");
}

/** The name of a script for the shape: its file name without the extension. */
function nameOf(argument: string): string {
  const parts = slashes(argument).split("/");
  return (parts[parts.length - 1] as string).replace(/\.(js|ts)$/, "");
}

function stepKeys(step: ReadStep, context: WalkContext, plugins?: StepKeyProvider): StepKeys {
  const words = step.words;
  const stepContext: StepContext = {
    cwd: context.cwd,
    resolvePath: (argument: string) => relativeToWorktree(argument, context),
  };
  const provided = plugins?.stepKeys(step, stepContext);
  if (provided !== undefined) {
    return provided;
  }
  const program = programOf(words[0] as string);
  if (program === "node") {
    // Code given inline (-e, -p) is no script of the repository: a probe or a sleep, its own kind
    if (words.some((word, i) => i > 0 && /^(?:-e|-p|--eval|--print)$/.test(word))) {
      return { normalized: words.join(" "), shape: "node:eval", kind: "node:eval" };
    }
    const at = words.findIndex((word, i) => i > 0 && !word.startsWith("-"));
    const script: KeyPath = at > 0 ? relativeToWorktree(words[at] as string, context) : { text: "", where: "" };
    const rest = at > 0 ? words.slice(at + 1) : words.slice(1);
    return {
      normalized: ["node", script.text, ...rest].join(" "),
      shape: `node:${nameOf(script.text)}`,
      kind: `node${script.where}`,
    };
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
export function commandKeys(command: string, context: KeyContext, plugins?: StepKeyProvider): CommandKeys {
  return walk(command, context, plugins) ?? { normalized: "", shape: "", kind: firstProgram(command) };
}

/**
 * The absolute paths of the project and script arguments of a command, resolved as commandKeys resolves them: the
 * paths a client looks up with git (path-places.ts) and sends as the places of its job. None when the command cannot
 * be read or has no directory.
 */
export function pathArguments(command: string, context: KeyContext, plugins?: StepKeyProvider): string[] {
  const collect: string[] = [];
  walk(command, { cwd: context.cwd, worktree: context.worktree, collect }, plugins);
  return [...new Set(collect)];
}

// The steps that change the directory (cd, Set-Location, Push-Location and their aliases), and those that go back to
// the one before (Pop-Location): a PowerShell agent wraps its commands in them, and they take no time of their own
const DirectoryChanges = new Set(["cd", "chdir", "set-location", "sl", "push-location", "pushd"]);
const DirectoryReturns = new Set(["pop-location", "popd"]);
const Pushes = new Set(["push-location", "pushd"]);

/** The keys of a readable command, the steps walked in order with the directory they run in; null when unreadable. */
function walk(command: string, context: WalkContext, plugins?: StepKeyProvider): CommandKeys | null {
  const read = readCommand(command);
  if (read === null) {
    return null;
  }
  const steps: StepKeys[] = [];
  let directory = context.cwd;
  const pushed: string[] = [];
  for (const step of read) {
    const first = step.words[0] as string;
    const program = programOf(first);
    if (DirectoryChanges.has(program)) {
      if (Pushes.has(program)) {
        pushed.push(directory);
      }
      // The target is the first word that is no option (Set-Location -Path x, Push-Location -LiteralPath x)
      const target = step.words.slice(1).find((word) => !word.startsWith("-"));
      directory = target === undefined ? directory : directory === "" ? target : absoluteOf(target, directory);
    } else if (DirectoryReturns.has(program)) {
      directory = pushed.pop() ?? directory;
    } else if (!first.startsWith("$") && !Trivial.has(program)) {
      steps.push(stepKeys(step, { ...context, cwd: directory }, plugins));
    }
  }
  return {
    normalized: steps.map((step) => step.normalized).join(" ; "),
    shape: steps.map((step) => step.shape).join("&"),
    kind: steps.map((step) => step.kind).join("&"),
  };
}
