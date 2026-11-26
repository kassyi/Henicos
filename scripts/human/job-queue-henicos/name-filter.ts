// The share of a --gpu run whose test filter names tests only (`--filter "FullyQualifiedName~Foo"`): classify cannot tell
// whether such a name reaches a Stress, Benchmark or GpuTrap test, so the test runner is asked. The tests that the
// filter selects and that carry an exclusive category are listed (`dotnet test --list-tests`, run as a CPU job of the
// queue, without the GPU); the run shares only when the runner says that there are none.

import { spawn } from "node:child_process";
import { createHash } from "node:crypto";
import * as fs from "node:fs";
import * as path from "node:path";
import { EntryScript } from "../job-queue/constants.ts";
import { ProcessTable } from "../job-queue/system.ts";
import { errorMessage, type ClientContext, type ShareAnswer, type ShareMode } from "../job-queue/plugin-api.ts";
import {
  nameFilterCheck,
  readEntries,
  shareOptions,
  type DirectoryEntries,
  type NameFilterTest,
} from "./share-rules.ts";

/** What the list job printed (its standard output) and its exit code: null when not started or ended by a signal. */
export interface ListResult {
  exitCode: number | null;
  output: string;
}

/**
 * How the list job ended: "exit" when it ended by itself, "timeout" when it was stopped after ListTimeoutMs (whatever
 * exit code the stop left: `taskkill /T /F` on Windows leaves 1), "spawn-error" when it could not be started.
 */
export type ListEnd = "exit" | "timeout" | "spawn-error";

/** The result of a list job of the queue, with how it ended. */
export interface ListRun extends ListResult {
  ended: ListEnd;
}

/** Runs a test list command as a CPU job of the queue; a self-test replaces it so that no real test run starts. */
export type ListRunner = (listCommand: string, context: ClientContext) => Promise<ListRun>;

/** The list job may take this long (a wait for a slot included) before the run takes the GPU exclusively. */
export const ListTimeoutMs = 170_000;

/**
 * The command that lists the tests selected by both the name filter and an exclusive category. English output, so that
 * the verdict can read the runner's wording. Null when a category name is not a plain word.
 */
export function listCommandOf(test: NameFilterTest, categories: readonly string[]): string | null {
  if (categories.length === 0 || !categories.every((name) => /^[\w.-]+$/.test(name))) {
    return null;
  }
  const exclusive = categories.map((name) => `Category=${name}`).join("|");
  const options = [
    ...(test.configuration !== undefined ? [`-c ${test.configuration}`] : []),
    ...(test.framework !== undefined ? [`-f ${test.framework}`] : []),
  ];
  return [
    `dotnet test "${test.project}" --no-build --list-tests`,
    ...options,
    `--filter "(${test.filter})&(${exclusive})"`,
  ].join(" ");
}

/**
 * The share the output of the list command allows: "shared" only for exit code 0, the runner's "No test matches the
 * given testcase filter" line and no test name (a line indented by four spaces) in the output. Anything else, a build
 * that is missing or a failure included, is "exclusive". `listed` counts the exclusive tests the output names.
 */
export function listVerdict(result: ListResult): { share: ShareMode; listed: number } {
  const listed = result.output.split(/\r?\n/).filter((line) => /^ {4}\S/.test(line)).length;
  const none = result.output.includes("No test matches the given testcase filter");
  return { share: result.exitCode === 0 && none && listed === 0 ? "shared" : "exclusive", listed };
}

/** Runs the list command through `job-queue.js run` (a CPU job, as the shell of the caller reads it). */
export const runListJob: ListRunner = (listCommand, context) =>
  new Promise((resolve) => {
    const args = [EntryScript, "run", "--shell", context.shell, "--", listCommand];
    const child = spawn(process.execPath, args, {
      cwd: context.cwd,
      env: { ...context.env, DOTNET_CLI_UI_LANGUAGE: "en" },
      stdio: ["ignore", "pipe", "ignore"],
      detached: process.platform !== "win32", // its own process group, for killTree
    });
    const chunks: Buffer[] = [];
    child.stdout.on("data", (chunk: Buffer) => chunks.push(chunk));
    let timedOut = false;
    const timer = setTimeout(() => {
      timedOut = true;
      ProcessTable.killTree(child.pid);
    }, ListTimeoutMs);
    const end = (exitCode: number | null, ended: ListEnd): void => {
      clearTimeout(timer);
      resolve({ exitCode, ended, output: Buffer.concat(chunks).toString("utf8") });
    };
    child.on("error", () => end(null, "spawn-error"));
    child.on("close", (code) => end(code, timedOut ? "timeout" : "exit"));
  });

/** The files of the test assemblies, as far as the fingerprint reads them; a self-test replaces it to stay offline. */
export interface AssemblyFiles {
  /** The modification time in ms, or null when the file cannot be read. */
  mtimeMs(file: string): number | null;
  /** The SHA-256 of the content as hex, or null when the file cannot be read. */
  sha256(file: string): string | null;
  /** The files called `fileName` anywhere under `directory` (none when it is not there). */
  findNamed(directory: string, fileName: string): string[];
}

export const realFiles: AssemblyFiles = {
  mtimeMs(file) {
    try {
      return fs.statSync(file).mtimeMs;
    } catch {
      // A file that is not there (or not readable) cannot be fingerprinted: the caller takes the GPU exclusively
      return null;
    }
  },
  findNamed(directory, fileName) {
    const found: string[] = [];
    const walk = (current: string, depth: number): void => {
      let entries: fs.Dirent[];
      try {
        entries = fs.readdirSync(current, { withFileTypes: true });
      } catch {
        // Not a directory (a build output that is not there): nothing to find
        return;
      }
      for (const entry of entries) {
        const full = path.join(current, entry.name);
        if (entry.isDirectory() && depth < 8) {
          walk(full, depth + 1);
        } else if (entry.isFile() && entry.name.toLowerCase() === fileName.toLowerCase()) {
          found.push(full);
        }
      }
    };
    walk(directory, 0);
    return found;
  },
  sha256(file) {
    try {
      return createHash("sha256").update(fs.readFileSync(file)).digest("hex");
    } catch {
      return null;
    }
  },
};

/**
 * The assemblies the runner says it ran the list for: one `Test run for <path> (<framework>)` line per target
 * framework, as absolute paths (a relative one is taken from `cwd`), without repeats.
 */
export function assembliesOf(output: string, cwd: string): string[] {
  const found = new Set<string>();
  for (const line of output.split(/\r?\n/)) {
    const match = /^Test run for (.+) \([^()]*\)\s*$/.exec(line);
    if (match !== null) {
      found.add(path.resolve(cwd, match[1] as string));
    }
  }
  return [...found];
}

/** The key under which a file is looked up: Windows compares paths without regard to case. */
function keyOf(file: string): string {
  const full = path.resolve(file);
  return process.platform === "win32" ? full.toLowerCase() : full;
}

/**
 * Where the project's assembly lies: `<project folder>/bin` and `<project name>.dll`, from the .csproj it names (or
 * the one in the directory it names). Null when the project file cannot be told.
 */
function projectDll(
  project: string,
  cwd: string,
  entries: DirectoryEntries,
): { binDir: string; fileName: string } | null {
  const full = path.resolve(cwd, project);
  const csproj = /\.csproj$/i.test(full) ? path.basename(full) : entries(full)?.find((name) => /\.csproj$/i.test(name));
  if (csproj === undefined) {
    return null;
  }
  const directory = /\.csproj$/i.test(full) ? path.dirname(full) : full;
  return { binDir: path.join(directory, "bin"), fileName: `${csproj.replace(/\.csproj$/i, "")}.dll` };
}

/**
 * The hash of every `<project name>.dll` under the project's bin, taken before the list job starts (null when one
 * cannot be read). The list may only name files that are in it and still hash the same afterwards: MSBuild copies a
 * build from obj to bin and keeps the compile time as the file's time, so a copy that lands while the list runs shows
 * in neither the time nor a hash taken after the list alone.
 */
function snapshotOf(files: AssemblyFiles, where: { binDir: string; fileName: string }): Map<string, string> | null {
  const snapshot = new Map<string, string>();
  for (const file of files.findNamed(where.binDir, where.fileName)) {
    const hash = files.sha256(file);
    if (hash === null) {
      return null;
    }
    snapshot.set(keyOf(file), hash);
  }
  return snapshot;
}

/**
 * The hash of each listed assembly, in order, or null when one is not in the snapshot, differs from it, is missing or
 * was written after `sinceMs`.
 */
function fingerprintOf(
  files: AssemblyFiles,
  assemblies: readonly string[],
  snapshot: ReadonlyMap<string, string>,
  sinceMs: number,
): string[] | null {
  const hashes: string[] = [];
  for (const file of assemblies) {
    // Hashed first, then its time read: a write at any moment after the list started shows in the time
    const hash = files.sha256(file);
    const modified = files.mtimeMs(file);
    if (hash === null || modified === null || modified > sinceMs || snapshot.get(keyOf(file)) !== hash) {
      return null;
    }
    hashes.push(hash);
  }
  return hashes;
}

/**
 * The checkShare answer of the henicos plugin: undefined when the command is not a name-only filtered test run, else
 * "shared" or "exclusive" from the listed tests, with one line saying which and how many exclusive tests it saw.
 * "shared" also needs the assemblies the list read: the runner must name them, none may have been written since the
 * list job started, and the answer carries a `stillValid` that hashes them again when the turn is granted, so a
 * rebuild while the turn waited sends it back as exclusive.
 */
export async function nameFilterShare(
  command: string,
  context: ClientContext,
  run: ListRunner = runListJob,
  entries?: DirectoryEntries,
  files: AssemblyFiles = realFiles,
): Promise<ShareAnswer | undefined> {
  const test = nameFilterCheck(command, context, entries);
  const options = shareOptions(context.options);
  if (test === null || options === null) {
    return undefined;
  }
  const listCommand = listCommandOf(test, options.exclusiveCategories);
  if (listCommand === null) {
    return { share: "exclusive", reason: "list-unsupported" };
  }
  const where = projectDll(test.project, context.cwd, entries ?? readEntries);
  const snapshot = where === null ? null : snapshotOf(files, where);
  if (snapshot === null || snapshot.size === 0) {
    context.log("GPU share: exclusive (no built assembly of the project to fingerprint)");
    return { share: "exclusive", reason: "no-snapshot" };
  }
  const startedAt = Date.now();
  const started = performance.now();
  let result: ListRun;
  try {
    result = await run(listCommand, context);
  } catch (error) {
    context.log(`GPU share: exclusive (the test list failed: ${errorMessage(error)})`);
    return { share: "exclusive", reason: "list-failed" };
  }
  const verdict = listVerdict(result);
  const seconds = ((performance.now() - started) / 1000).toFixed(1);
  const say = (share: ShareMode, why: string, reason: string): ShareAnswer => {
    context.log(`GPU share: ${share} (list of exclusive tests: ${verdict.listed}, ${seconds} s${why})`);
    return { share, reason };
  };
  if (verdict.share === "exclusive") {
    const reason =
      verdict.listed > 0
        ? `list:${verdict.listed}`
        : result.ended === "timeout"
          ? "list-timeout"
          : result.ended === "spawn-error"
            ? "list-spawn-error"
            : result.exitCode !== 0
              ? "list-failed"
              : "list-no-marker";
    return say("exclusive", "", reason);
  }
  const assemblies = assembliesOf(result.output, context.cwd);
  if (assemblies.length === 0) {
    return say("exclusive", "; the runner named no assembly", "no-assemblies");
  }
  const hashes = fingerprintOf(files, assemblies, snapshot, startedAt);
  if (hashes === null) {
    return say(
      "exclusive",
      "; an assembly is not the one built before the list, or was written during it",
      "hash-mismatch",
    );
  }
  context.log(
    `GPU share: shared (list of exclusive tests: 0, ${seconds} s, ${assemblies.length} assembl${assemblies.length === 1 ? "y" : "ies"} fingerprinted)`,
  );
  return {
    share: "shared",
    reason: "list:0",
    stillValid: () =>
      Promise.resolve(
        assemblies.every((file, n) => {
          const now = files.sha256(file);
          return now !== null && now === hashes[n];
        }),
      ),
  };
}
