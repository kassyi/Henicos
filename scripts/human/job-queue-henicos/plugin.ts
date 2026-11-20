// Repository plugin for Henicos: test classification, memory budgets, the GPU share of a --gpu run (share-rules.ts),
// --gpu checks/hints, and short command labels.

import {
  PluginApiVersion,
  type CheckResult,
  type Classification,
  type ClientContext,
  type QueuePlugin,
  type ResourceRequest,
  type ShareMode,
} from "../job-queue/plugin-api.ts";
import { isDotnetTest, selectsCategory } from "../job-queue/plugins/dotnet/plugin.ts";
import { cpuOnlyTestProjects, resolveBudgets, resolveTestsDir, testMemoryMb } from "./budgets.ts";
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
    const memoryMb = testMemoryMb(command, true, budgets, (cmd) => selectsCategory(cmd, ["Stress", "Benchmark"]));
    return share !== undefined ? { test: true, memoryMb, share } : { test: true, memoryMb };
  },

  // A --gpu run whose filter names tests only is shared when the runner lists no Stress, Benchmark or GpuTrap test
  // among them (name-filter.ts); asked only after classify answered anything but "shared"
  checkShare(command: string, context: ClientContext): Promise<ShareMode | undefined> {
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
