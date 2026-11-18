// Where the project and script arguments of a command lie, looked up by the client when it queues the job, so that
// the estimate keys (estimate-key.ts) need no file system: the root of the worktree that holds each path and whether
// that worktree ignores it. A project of another worktree then shares its runs with the same project anywhere, and a
// scratch project in an ignored folder or outside every worktree stays apart from the repository's work.

import { spawnSync } from "node:child_process";
import * as fs from "node:fs";
import * as path from "node:path";
import { pathArguments, type KeyContext, type PathPlace, type StepKeyProvider } from "./estimate-key.ts";

/** How long the git call of one worktree may take; a slower one leaves its paths without a place (judged as before). */
const GitTimeoutMs = 5000;

/**
 * The places of the path arguments of a command, keyed as estimate-key.ts looks them up. The worktree of a path is the
 * nearest folder above it that holds `.git` (a folder in a clone, a file in a worktree or submodule), found without
 * git; whether it ignores the path takes one `git check-ignore` per worktree.
 */
export function resolvePlaces(
  command: string,
  context: KeyContext,
  plugins?: StepKeyProvider,
): Record<string, PathPlace> {
  const places: Record<string, PathPlace> = Object.create(null) as Record<string, PathPlace>;
  const byRoot = new Map<string, string[]>();
  for (const absolute of pathArguments(command, context, plugins)) {
    const root = worktreeRoot(absolute);
    if (root === "") {
      places[absolute] = { root, ignored: false };
    } else {
      byRoot.set(root, (byRoot.get(root) ?? []).concat(absolute));
    }
  }
  for (const [root, paths] of byRoot) {
    const ignored = ignoredPaths(root, paths);
    if (ignored !== null) {
      for (const absolute of paths) {
        places[absolute] = { root, ignored: ignored.has(absolute) };
      }
    }
  }
  return places;
}

/** The nearest folder at or above the path that holds `.git`, or "" when none does. */
function worktreeRoot(absolute: string): string {
  for (let current = absolute; ; current = path.dirname(current)) {
    if (fs.existsSync(path.join(current, ".git"))) {
      return current;
    }
    if (path.dirname(current) === current) {
      return "";
    }
  }
}

/** The paths that the worktree at `root` ignores; null when git could not tell (no git, an error, a timeout). */
function ignoredPaths(root: string, paths: string[]): Set<string> | null {
  // Relative to the root: git refuses an absolute path whose drive letter is spelled in another case
  const relative = new Map(
    paths.map((absolute) => [path.relative(root, absolute).split(path.sep).join("/") || ".", absolute]),
  );
  // -z: paths separated by NUL both ways, never quoted (a path with other than ASCII letters would be)
  const git = spawnSync("git", ["check-ignore", "--stdin", "-z"], {
    cwd: root,
    input: [...relative.keys()].join("\0") + "\0",
    encoding: "utf8",
    windowsHide: true,
    timeout: GitTimeoutMs,
  });
  // 0: some are ignored (listed), 1: none is, 128: an error
  if (git.error !== undefined || (git.status !== 0 && git.status !== 1)) {
    return null;
  }
  const ignored = new Set<string>();
  for (const line of git.stdout.split("\0")) {
    const absolute = relative.get(line);
    if (absolute !== undefined) {
      ignored.add(absolute);
    }
  }
  return ignored;
}
