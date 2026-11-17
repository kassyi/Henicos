// Checks of the shared library modules:
// main-checkout.js, process-liveness.js, and target-dialect.js.

import * as defaultAssert from "node:assert";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { spawnSync } from "node:child_process";
import { createRequire } from "node:module";
import type { MainCheckoutModule, ProcessEntry, ProcessLivenessModule, TargetDialectModule } from "./lib/types.ts";

const require = createRequire(import.meta.url);
const { mainCheckout } = require("./lib/main-checkout.js") as MainCheckoutModule;
const { commandProcesses } = require("./lib/process-liveness.js") as ProcessLivenessModule;
const { dialectOf } = require("../job-queue-henicos/target-dialect.js") as TargetDialectModule;

/**
 * 1. Temporary git repo with worktree: mainCheckout returns main repo root. Non-git dir returns fallback.
 */
function checkMainCheckout(assert: typeof defaultAssert): void {
  const tempBase = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-main-checkout-"));
  try {
    const mainRepo = path.join(tempBase, "main");
    const worktreeRepo = path.join(tempBase, "worktree");
    const nonGitDir = path.join(tempBase, "nongit");
    fs.mkdirSync(mainRepo);
    fs.mkdirSync(nonGitDir);

    const runGit = (args: string[], cwd: string): void => {
      const res = spawnSync("git", args, { cwd, encoding: "utf8", windowsHide: true });
      if (res.error || res.status !== 0) {
        throw new Error(`git ${args.join(" ")} failed: ${res.error?.message || res.stderr}`);
      }
    };

    runGit(["init", "-b", "main"], mainRepo);
    runGit(["config", "user.email", "test@example.com"], mainRepo);
    runGit(["config", "user.name", "test"], mainRepo);
    fs.writeFileSync(path.join(mainRepo, "init.txt"), "hello");
    runGit(["add", "."], mainRepo);
    runGit(["commit", "-m", "init"], mainRepo);
    runGit(["worktree", "add", worktreeRepo], mainRepo);

    const fallback = path.join(tempBase, "fallback");
    const resolvedFromWorktree = mainCheckout(worktreeRepo, fallback);
    assert.strictEqual(
      path.resolve(resolvedFromWorktree).toLowerCase(),
      path.resolve(mainRepo).toLowerCase(),
      "mainCheckout from worktree should return the main repository directory",
    );

    const resolvedFromNonGit = mainCheckout(nonGitDir, fallback);
    assert.strictEqual(
      resolvedFromNonGit,
      fallback,
      "mainCheckout from a non-git directory should return fallbackRoot",
    );
  } finally {
    try {
      fs.rmSync(tempBase, { recursive: true, force: true });
    } catch {
      // Windows git lock cleanup tolerance
    }
  }
}

/**
 * 2. PID reuse: when shell creation is 6s after spawn, shell and its children are excluded.
 */
function checkCommandProcessesPidReuse(assert: typeof defaultAssert): void {
  const spawnedAt = 100_000;
  // Shell pid 100 was created at 106_000 (> spawnedAt + 5000ms).
  // Child pid 101 was created at 107_000 (> reusedFrom = 106_000).
  const table = new Map<number, ProcessEntry>([
    [100, { ppid: 1, created: 106_000 }],
    [101, { ppid: 100, created: 107_000 }],
  ]);
  const procs = commandProcesses(table, 100, spawnedAt);
  assert.deepStrictEqual(procs, [], "reused shell and its children must not be counted");
}

/**
 * 3. Child process whose creation time is unknown (-1) is counted.
 */
function checkCommandProcessesUnknownCreation(assert: typeof defaultAssert): void {
  const spawnedAt = 100_000;
  // Shell pid 300 created at 100_000.
  // Child pid 301 created at -1 (creation timestamp not reported).
  const table = new Map<number, ProcessEntry>([
    [300, { ppid: 1, created: 100_000 }],
    [301, { ppid: 300, created: -1 }],
  ]);
  const procs = commandProcesses(table, 300, spawnedAt);
  assert.deepStrictEqual(procs, [300, 301], "child with creation time -1 must be counted");
}

/**
 * 4. dialectOf: <GSharpTargetDialect> cpu </GSharpTargetDialect> (lowercase, whitespace) -> "cpu", missing -> null.
 */
function checkDialectOf(assert: typeof defaultAssert): void {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-dialect-test-"));
  try {
    const cpuProj = path.join(tempDir, "cpu.csproj");
    fs.writeFileSync(
      cpuProj,
      "<Project><PropertyGroup>\r\n  <GSharpTargetDialect> cpu </GSharpTargetDialect>\r\n</PropertyGroup></Project>",
    );
    assert.strictEqual(dialectOf(cpuProj), "cpu", "lowercase dialect with whitespace must return 'cpu'");

    const noneProj = path.join(tempDir, "none.csproj");
    fs.writeFileSync(
      noneProj,
      "<Project><PropertyGroup>\r\n  <TargetFramework>net9.0</TargetFramework>\r\n</PropertyGroup></Project>",
    );
    assert.strictEqual(dialectOf(noneProj), null, "project without GSharpTargetDialect must return null");
  } finally {
    try {
      fs.rmSync(tempDir, { recursive: true, force: true });
    } catch {
      // Cleanup tolerance
    }
  }
}

export async function checkLib(assert: typeof defaultAssert = defaultAssert): Promise<void> {
  checkMainCheckout(assert);
  checkCommandProcessesPidReuse(assert);
  checkCommandProcessesUnknownCreation(assert);
  checkDialectOf(assert);
}
