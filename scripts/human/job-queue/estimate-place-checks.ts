// Checks of where the estimate keys put a project or script (estimate-key.ts with the places of path-places.ts): the
// directory moves of a PowerShell wrapper, the places the client sends, and the git lookup on a real repository made
// for the check. Run by the self-test through EstimateChecks. Every path in them is made up or temporary.

import type * as Assert from "node:assert";
import { spawnSync } from "node:child_process";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { commandKeys, pathArguments, type KeyContext } from "./estimate-key.ts";
import { resolvePlaces } from "./path-places.ts";

type AssertModule = typeof Assert;

const Script = "scripts/human/app.js";
/** A job in the main checkout of a made-up Windows repository whose worktrees sit in its ignored tmp folder. */
const Main: KeyContext = { cwd: "C:\\work\\repo", worktree: "c:\\work\\repo" };

/** A PowerShell agent wraps its steps in Push-Location / Pop-Location: they move the directory and take no time. */
export function checkEstimateDirectoryMoves(assert: AssertModule): void {
  const keys = (command: string): ReturnType<typeof commandKeys> => commandKeys(command, Main);
  const plain = keys(`node ${Script}`);
  for (const wrapped of [
    `Push-Location C:\\work\\repo; node ${Script}; Pop-Location`,
    `Set-Location -Path C:\\work\\repo; node ${Script}`,
    `pushd scripts && node human/app.js && popd`,
    `Push-Location scripts; $env:CI='true'; node human/app.js; Pop-Location`,
  ]) {
    assert.deepStrictEqual(keys(wrapped), plain, wrapped);
  }
  // Pop-Location goes back: the step after it runs in the directory before the push
  assert.strictEqual(
    keys(`Push-Location scripts; Pop-Location; node ${Script}`).normalized,
    plain.normalized,
    "the directory before the push",
  );
  assert.strictEqual(
    keys("cd /c/work/repo/scripts && node human/app.js").normalized,
    plain.normalized,
    "a cd in Git Bash spelling",
  );
  // The paths a client looks up, resolved through the moves
  assert.deepStrictEqual(pathArguments("Push-Location scripts; node human/app.js; node x.js", Main), [
    "C:\\work\\repo\\scripts\\human\\app.js",
    "C:\\work\\repo\\scripts\\x.js",
  ]);
  assert.deepStrictEqual(pathArguments("node $(pwd)/x.js", Main), [], "an unreadable command names none");
}

/** With the places the client sent, a path is keyed from the worktree that holds it and marked where it lies. */
export function checkEstimatePlaces(assert: AssertModule): void {
  const other = "C:\\work\\repo\\tmp\\worktree\\feat-b";
  const probe = "C:\\work\\repo\\tmp\\probe\\run.js";
  const scratch = "C:\\Temp\\scratch\\probe.js";
  const context: KeyContext = {
    ...Main,
    places: {
      [`${other}\\scripts\\human\\app.js`]: { root: other, ignored: false },
      [probe]: { root: "C:\\work\\repo", ignored: true },
      [scratch]: { root: "", ignored: false },
    },
  };
  const keys = (command: string): ReturnType<typeof commandKeys> => commandKeys(command, context);
  // A script of another worktree, named from the main checkout: the same key as in its own worktree
  assert.deepStrictEqual(keys(`node tmp/worktree/feat-b/${Script}`), commandKeys(`node ${Script}`, Main));
  assert.strictEqual(keys(`node ${other}\\${Script}`).normalized, `node ${Script}`);
  // A script the worktree ignores, and one no worktree holds: kinds of their own
  assert.strictEqual(keys("node tmp/probe/run.js").kind, "node:ignored");
  assert.strictEqual(keys("node tmp/probe/run.js").normalized, "node tmp/probe/run.js");
  assert.strictEqual(keys(`node ${scratch}`).kind, "node:outside");
  // A path the client sent no place for is judged against the job's worktree, as before
  assert.strictEqual(keys(`node ${Script}`).kind, "node");
  assert.strictEqual(keys("node D:\\elsewhere\\x.js").kind, "node:outside");
}

/** git: a temporary repository with an ignored folder, a worktree-like repository inside it and a folder outside. */
export function checkPathPlaces(assert: AssertModule): void {
  // The long form of the path (a Windows runner's TEMP may be an 8.3 name such as RUNNER~1), as git prints its roots
  const top = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-places-")));
  const git = (cwd: string, ...args: string[]): void => {
    const result = spawnSync("git", args, { cwd, windowsHide: true });
    assert.strictEqual(result.status, 0, `git ${args.join(" ")}`);
  };
  try {
    const repo = path.join(top, "repo");
    const nested = path.join(repo, "tmp", "wt");
    const outside = path.join(top, "plain");
    for (const directory of [path.join(repo, "src", "app"), path.join(nested, "src", "app"), outside]) {
      fs.mkdirSync(directory, { recursive: true });
    }
    git(repo, "init", "-q");
    fs.writeFileSync(path.join(repo, ".gitignore"), "tmp/\n");
    git(nested, "init", "-q");
    const context: KeyContext = { cwd: repo, worktree: repo };
    const command =
      `node src/app/run.js && node tmp/probe/probe.js && node ${outside}/probe.js` + " && node tmp/wt/src/app/run.js";
    const places = resolvePlaces(command, context);
    const at = (relative: string): string => path.resolve(repo, relative);
    assert.deepStrictEqual(places[at("src/app/run.js")], { root: repo, ignored: false }, "tracked folder");
    assert.deepStrictEqual(places[at("tmp/probe/probe.js")], { root: repo, ignored: true }, "ignored, not there");
    assert.deepStrictEqual(places[at(`${outside}/probe.js`)], { root: "", ignored: false }, "no worktree");
    assert.deepStrictEqual(places[at("tmp/wt/src/app/run.js")], { root: nested, ignored: false }, "its own worktree");
    const named = resolvePlaces("node tmp/試験/p.js", context);
    assert.deepStrictEqual(named[at("tmp/試験/p.js")], { root: repo, ignored: true }, "a name git would quote");
    const kinds = commandKeys(command, { ...context, places }).kind;
    assert.strictEqual(kinds, "node&node:ignored&node:outside&node");
  } finally {
    fs.rmSync(top, { recursive: true, force: true });
  }
}

export const EstimatePlaceChecks: Record<string, (assert: AssertModule) => void> = {
  checkEstimateDirectoryMoves,
  checkEstimatePlaces,
  checkPathPlaces,
};
