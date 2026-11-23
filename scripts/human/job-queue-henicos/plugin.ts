// Repository plugin for Henicos: test classification, memory budgets, the GPU share of a --gpu run (share-rules.ts),
// --gpu checks/hints, and short command labels.

import {
  PluginApiVersion,
  type CheckResult,
  type Classification,
  type ClientContext,
  type QueuePlugin,
  type ResourceRequest,
  type ShareAnswer,
  isTrivialStep,
  programOf,
  readCommandLine,
} from "../job-queue/plugin-api.ts";
import { isDotnetTest, selectsCategory } from "../job-queue/plugins/dotnet/plugin.ts";
import { cpuOnlyTestProjects, isHeadlessCpuSafe, resolveBudgets, resolveTestsDir, testMemoryMb } from "./budgets.ts";
import { nameFilterShare } from "./name-filter.ts";
import { shareOf } from "./share-rules.ts";

const DefaultBudgetsFile = "tests/memory-budgets.yml";
const DefaultTestsDir = "tests";

// just options and variable assignments may stand between just and the recipe: just -f justfile test, just a=b test
const HenicosTestPattern = new RegExp(
  [
    String.raw`\bjust\b[^;&|\n]*?\s(test[\w-]*|coverage-diff|mutate|long-checks)(\s|$)`,
    String.raw`\b(test-stability|test-memory|test-sanitized|long-checks|mutate|coverage-diff)\.js\b`,
  ].join("|"),
);

/**
 * Every step that does work is a `dotnet test` with `--list-tests`: it runs no test, so the Stress it names is not a
 * stress run (a line that also runs a test, or that cannot be read, is judged as before).
 */
function onlyListsTests(command: string): boolean {
  const line = readCommandLine(command);
  const work = line === null || line.piped ? [] : line.steps.filter((step) => !isTrivialStep(step));
  return (
    work.length > 0 &&
    work.every(
      (step) =>
        programOf(step.words[0] ?? "") === "dotnet" && step.words[1] === "test" && step.words.includes("--list-tests"),
    )
  );
}

/** just recipes that are 100% CPU-only and self-contained on headless Linux. */
const OffloadableJustRecipes = new Set([
  "test-differential-cpu",
  "test-native-stack",
  "test-managed-stack",
  "test-sanitized",
]);

/** Filters targeting Windows-only implementation tests stay on Windows so they do not skip on Linux. */
const WindowsOnlyFilterPattern = /(?:^|[^A-Za-z0-9])(?:Windows|Win32|Msvc|Wmi|Event153)/i;

/**
 * True when `command` is a self-contained CPU test run eligible for transparent offload to a headless Linux worker:
 * - No GPU resource requested, no pipe, no Windows-only filter
 * - Either an allowlisted CPU-only `just` recipe, or plain `dotnet build` / `dotnet test` steps ending in a test
 *   (never a bare `dotnet build` or a standalone `dotnet test --no-build` that relies on local bin/win/)
 * - Every named test project is headless-CPU-safe (GSharpTargetDialect Cpu, pure managed, or Category!=GpuExecution).
 */
function isOffloadableCommand(
  command: string,
  budgets: ReadonlyMap<string, unknown>,
  testsDir: string,
  log: (text: string) => void,
): boolean {
  if (WindowsOnlyFilterPattern.test(command)) {
    return false;
  }
  const line = readCommandLine(command);
  if (line === null || line.piped) {
    return false;
  }
  const work = line.steps.filter((step) => {
    if (isTrivialStep(step)) {
      return false;
    }
    const prog = programOf(step.words[0] ?? "");
    return prog !== "cd" && prog !== "set-location" && prog !== "sl";
  });
  if (work.length === 0) {
    return false;
  }
  // Allowlisted CPU-only just recipes (single step)
  if (work.length === 1 && programOf(work[0]?.words[0] ?? "") === "just") {
    const recipe = work[0]?.words.slice(1).find((w) => !w.startsWith("-") && !w.includes("="));
    return recipe !== undefined && OffloadableJustRecipes.has(recipe);
  }
  // Plain dotnet build/test sequence: must include at least one dotnet test, and any --no-build test must be preceded
  // by a dotnet build in the same command (standalone --no-build reads local bin/win/)
  let sawBuild = false;
  let sawTest = false;
  for (const step of work) {
    if (programOf(step.words[0] ?? "") !== "dotnet") {
      return false;
    }
    const verb = (step.words[1] ?? "").toLowerCase();
    if (verb === "build") {
      sawBuild = true;
    } else if (verb === "test") {
      if (step.words.includes("--list-tests") || step.words.includes("-t")) {
        return false;
      }
      if (step.words.includes("--no-build") && !sawBuild) {
        return false;
      }
      sawTest = true;
    } else {
      return false;
    }
  }
  if (!sawTest) {
    return false;
  }
  return isHeadlessCpuSafe(command, budgets.keys(), testsDir, log);
}

export function isHenicosTest(command: string): boolean {
  return isDotnetTest(command) || HenicosTestPattern.test(command);
}

function optionString(context: ClientContext, key: string, fallback: string): string {
  const value = context.options[key];
  return typeof value === "string" && value !== "" ? value : fallback;
}

const plugin = {
  name: "./scripts/human/job-queue-henicos",
  apiVersion: PluginApiVersion,

  check(command: string, resource: ResourceRequest | null, context: ClientContext): CheckResult | undefined {
    if (resource === null || resource.name !== "gpu") {
      return undefined;
    }
    if (/fuzz-diff|fuzz-differential|long-checks/.test(command)) {
      // They take the GPU lock themselves for their GPU steps; holding it here would make them wait for ever
      return {
        refuse: "fuzz-diff and long-checks take the GPU lock themselves: queue them without --gpu.",
      };
    }
    const budgets = resolveBudgets(
      context.worktree,
      context.configRoot,
      optionString(context, "budgetsFile", DefaultBudgetsFile),
      context.log,
    );
    const testsDir = resolveTestsDir(
      context.worktree,
      context.configRoot,
      optionString(context, "testsDir", DefaultTestsDir),
    );
    const cpuOnly = cpuOnlyTestProjects(command, budgets.keys(), testsDir, context.log);
    if (cpuOnly.length > 0) {
      // A hint, not a refusal: the command may use the GPU in a way the project file does not show
      return {
        hints: [
          `--gpu, but ${cpuOnly.join(", ")} ${cpuOnly.length === 1 ? "runs" : "run"} on the CPU only ` +
            "(GSharpTargetDialect Cpu): " +
            "this job waits for the GPU and keeps the GPU jobs behind it waiting; queue it without --gpu",
        ],
      };
    }
    return undefined;
  },

  classify(command: string, resource: ResourceRequest | null, context: ClientContext): Classification | undefined {
    // Asked only for a --gpu run (share-rules.ts); a run that does not ask for the GPU has no share
    const share = resource?.name === "gpu" ? shareOf(command, context) : undefined;
    if (!isHenicosTest(command)) {
      return share !== undefined ? { share } : undefined;
    }
    const budgets = resolveBudgets(
      context.worktree,
      context.configRoot,
      optionString(context, "budgetsFile", DefaultBudgetsFile),
      context.log,
    );
    // The test list of the name-filter share names the exclusive categories to look for; it runs no test, so it takes the
    // normal budget
    const memoryMb = testMemoryMb(
      command,
      true,
      budgets,
      (cmd) => !onlyListsTests(cmd) && selectsCategory(cmd, ["Stress", "Benchmark"]),
    );
    const testsDir = resolveTestsDir(
      context.worktree,
      context.configRoot,
      optionString(context, "testsDir", DefaultTestsDir),
    );
    const offloadable = resource === null ? isOffloadableCommand(command, budgets, testsDir, context.log) : false;
    return share !== undefined ? { test: true, memoryMb, share, offloadable } : { test: true, memoryMb, offloadable };
  },

  // A --gpu run whose filter names tests only is shared when the runner lists no Stress, Benchmark or GpuTrap test
  // among them (name-filter.ts); asked only after classify answered anything but "shared"
  checkShare(command: string, context: ClientContext): Promise<ShareAnswer | undefined> {
    return nameFilterShare(command, context);
  },

  shortName(_command: string, short: string): string | undefined {
    const trimmed = short.trim();
    if (trimmed === "") {
      return undefined;
    }
    const next = trimmed
      .split(/\s+/)
      .map((word) => word.replace(/^Kassyi\.GSharp\./, ""))
      .join(" ");
    return next === trimmed ? undefined : next;
  },
} satisfies QueuePlugin;

export default plugin;
