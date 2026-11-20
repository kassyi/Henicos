// The share of a --gpu run whose test filter names tests only (`--filter "FullyQualifiedName~Foo"`): classify cannot tell
// whether such a name reaches a Stress, Benchmark or GpuTrap test, so the test runner is asked. The tests that the
// filter selects and that carry an exclusive category are listed (`dotnet test --list-tests`, run as a CPU job of the
// queue, without the GPU); the run shares only when the runner says that there are none.

import { spawn } from "node:child_process";
import { EntryScript } from "../job-queue/constants.ts";
import { ProcessTable } from "../job-queue/system.ts";
import { errorMessage, type ClientContext, type ShareMode } from "../job-queue/plugin-api.ts";
import { nameFilterCheck, shareOptions, type DirectoryEntries, type NameFilterTest } from "./share-rules.ts";

/** What the list job printed (its standard output) and how it ended; exitCode is null when it did not end by itself. */
export interface ListResult {
  exitCode: number | null;
  output: string;
}

/** Runs a test list command as a CPU job of the queue; a self-test replaces it so that no real test run starts. */
export type ListRunner = (listCommand: string, context: ClientContext) => Promise<ListResult>;

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
    const timer = setTimeout(() => ProcessTable.killTree(child.pid), ListTimeoutMs);
    const end = (exitCode: number | null): void => {
      clearTimeout(timer);
      resolve({ exitCode, output: Buffer.concat(chunks).toString("utf8") });
    };
    child.on("error", () => end(null));
    child.on("close", (code) => end(code));
  });

/**
 * The checkShare answer of the henicos plugin: undefined when the command is not a name-only filtered test run,
 * else "shared" or "exclusive" from the listed tests, with one line saying which and how many exclusive tests it saw.
 */
export async function nameFilterShare(
  command: string,
  context: ClientContext,
  run: ListRunner = runListJob,
  entries?: DirectoryEntries,
): Promise<ShareMode | undefined> {
  const test = nameFilterCheck(command, context, entries);
  const options = shareOptions(context.options);
  if (test === null || options === null) {
    return undefined;
  }
  const listCommand = listCommandOf(test, options.exclusiveCategories);
  if (listCommand === null) {
    return "exclusive";
  }
  const started = performance.now();
  let verdict: { share: ShareMode; listed: number };
  try {
    verdict = listVerdict(await run(listCommand, context));
  } catch (error) {
    context.log(`GPU share: exclusive (the test list failed: ${errorMessage(error)})`);
    return "exclusive";
  }
  const seconds = ((performance.now() - started) / 1000).toFixed(1);
  context.log(`GPU share: ${verdict.share} (list of exclusive tests: ${verdict.listed}, ${seconds} s)`);
  return verdict.share;
}
