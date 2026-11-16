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

const Cpu = "tests/Compiler/App.Cpu";
/** A job in the main checkout of a made-up Windows repository whose worktrees sit in its ignored tmp folder. */
const Main: KeyContext = { cwd: "C:\\work\\repo", worktree: "c:\\work\\repo" };

/** A PowerShell agent wraps its steps in Push-Location / Pop-Location: they move the directory and take no time. */
export function checkEstimateDirectoryMoves(assert: AssertModule): void {
  const keys = (command: string): ReturnType<typeof commandKeys> => commandKeys(command, Main);
  const plain = keys(`dotnet build ${Cpu}`);
  for (const wrapped of [
    `Push-Location C:\\work\\repo; dotnet build ${Cpu}; Pop-Location`,
    `Set-Location -Path C:\\work\\repo; dotnet build ${Cpu}`,
    `pushd tests && dotnet build Compiler/App.Cpu && popd`,
    `Push-Location tests; $env:CI='true'; dotnet build Compiler/App.Cpu; Pop-Location`,
  ]) {
    assert.deepStrictEqual(keys(wrapped), plain, wrapped);
  }
  // Pop-Location goes back: the step after it runs in the directory before the push
  assert.strictEqual(
    keys(`Push-Location tests; Pop-Location; dotnet build ${Cpu}`).normalized,
    plain.normalized,
    "the directory before the push",
  );
  assert.strictEqual(
    keys("cd /c/work/repo/tests && dotnet build Compiler/App.Cpu").normalized,
    plain.normalized,
    "a cd in Git Bash spelling",
  );
  // The paths a client looks up, resolved through the moves
  assert.deepStrictEqual(pathArguments("Push-Location tests; dotnet build Compiler/App.Cpu; node x.js", Main), [
    "C:\\work\\repo\\tests\\Compiler\\App.Cpu",
    "C:\\work\\repo\\tests\\x.js",
  ]);
  assert.deepStrictEqual(pathArguments("dotnet build $(pwd)/X", Main), [], "an unreadable command names none");
}

/** With the places the client sent, a path is keyed from the worktree that holds it and marked where it lies. */
export function checkEstimatePlaces(assert: AssertModule): void {
  const other = "C:\\work\\repo\\tmp\\worktree\\feat-b";
  const probe = "C:\\work\\repo\\tmp\\probe\\Probe.csproj";
  const scratch = "C:\\Temp\\scratch\\Probe.csproj";
  const context: KeyContext = {
    ...Main,
    places: {
      [`${other}\\tests\\Compiler\\App.Cpu`]: { root: other, ignored: false },
      [probe]: { root: "C:\\work\\repo", ignored: true },
      [scratch]: { root: "", ignored: false },
      "C:\\work\\repo\\tmp\\probe\\run.js": { root: "C:\\work\\repo", ignored: true },
    },
  };
  const keys = (command: string): ReturnType<typeof commandKeys> => commandKeys(command, context);
  // A project of another worktree, named from the main checkout: the same key as in its own worktree
  assert.deepStrictEqual(keys(`dotnet build tmp/worktree/feat-b/${Cpu}`), commandKeys(`dotnet build ${Cpu}`, Main));
  assert.strictEqual(keys(`dotnet build ${other}\\tests\\Compiler\\App.Cpu`).normalized, `dotnet build ${Cpu}`);
  // A project the worktree ignores, and one no worktree holds: kinds of their own
  assert.strictEqual(keys("dotnet build tmp/probe/Probe.csproj").kind, "build:ignored");
  assert.strictEqual(keys("dotnet build tmp/probe/Probe.csproj").normalized, "dotnet build tmp/probe/Probe.csproj");
  assert.strictEqual(keys(`dotnet build ${scratch}`).kind, "build:outside");
  assert.strictEqual(keys("node tmp/probe/run.js").kind, "node:ignored");
  // A path the client sent no place for is judged against the job's worktree, as before
  assert.strictEqual(keys(`dotnet build ${Cpu}`).kind, "build");
  assert.strictEqual(keys("dotnet build D:\\elsewhere\\x.csproj").kind, "build:outside");
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
    for (const directory of [path.join(repo, "src", "App"), path.join(nested, "src", "App"), outside]) {
      fs.mkdirSync(directory, { recursive: true });
    }
    git(repo, "init", "-q");
    fs.writeFileSync(path.join(repo, ".gitignore"), "tmp/\n");
    git(nested, "init", "-q");
    const context: KeyContext = { cwd: repo, worktree: repo };
    const command =
      `dotnet build src/App && dotnet build tmp/probe/Probe.csproj && dotnet build ${outside}/Probe.csproj` +
      " && dotnet build tmp/wt/src/App";
    const places = resolvePlaces(command, context);
    const at = (relative: string): string => path.resolve(repo, relative);
    assert.deepStrictEqual(places[at("src/App")], { root: repo, ignored: false }, "tracked folder");
    assert.deepStrictEqual(places[at("tmp/probe/Probe.csproj")], { root: repo, ignored: true }, "ignored, not there");
    assert.deepStrictEqual(places[at(`${outside}/Probe.csproj`)], { root: "", ignored: false }, "no worktree");
    assert.deepStrictEqual(places[at("tmp/wt/src/App")], { root: nested, ignored: false }, "its own worktree");
    const named = resolvePlaces("dotnet build tmp/試験/P.csproj", context);
    assert.deepStrictEqual(named[at("tmp/試験/P.csproj")], { root: repo, ignored: true }, "a name git would quote");
    const kinds = commandKeys(command, { ...context, places }).kind;
    assert.strictEqual(kinds, "build&build:ignored&build:outside&build");
  } finally {
    fs.rmSync(top, { recursive: true, force: true });
  }
}

export const EstimatePlaceChecks: Record<string, (assert: AssertModule) => void> = {
  checkEstimateDirectoryMoves,
  checkEstimatePlaces,
  checkPathPlaces,
};
