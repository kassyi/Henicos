// Repository plugin for Henicos: test classification, memory budgets, the GPU share of a --gpu run (share-rules.ts),
// remote CPU offload eligibility (offload-probe.ts), --gpu checks/hints, and short command labels.

import * as path from "node:path";
import {
  PluginApiVersion,
  type CheckResult,
  type Classification,
  type ClientContext,
  type OffloadAnswer,
  type OffloadCheckRequest,
  type QueuePlugin,
  type ResourceRequest,
  type ShareAnswer,
  isTrivialStep,
  programOf,
  readCommandLine,
} from "../job-queue/plugin-api.ts";
import { isDotnetTest, selectsCategory } from "../job-queue/plugins/dotnet/plugin.ts";
import { cpuOnlyTestProjects, justPositionals, resolveBudgets, resolveTestsDir, testMemoryMb } from "./budgets.ts";
import { nameFilterShare } from "./name-filter.ts";
import { checkHenicosOffload, classifyOffloadCommand } from "./offload-probe.ts";
import { sharedTurnRefusal } from "./share-refusal.ts";
import { shareDecision } from "./share-rules.ts";

const DefaultBudgetsFile = "tests/memory-budgets.yml";
const DefaultTestsDir = "tests";

// just options and variable assignments may stand between just and the recipe: just -f justfile test, just a=b test
const HenicosTestPattern = new RegExp(
  [
    String.raw`\bjust\b[^;&|\n]*?\s(test[\w-]*|coverage-diff|mutate|diff-barrier-models|time-barrier-models)(\s|$)`,
    String.raw`\b(test-stability|test-memory|test-sanitized|mutate|coverage-diff|compare-barrier-models|time-barrier-models)\.js\b`,
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

export function isHenicosTest(command: string): boolean {
  return isDotnetTest(command) || HenicosTestPattern.test(command);
}

function optionString(context: ClientContext, key: string, fallback: string): string {
  const value = context.options[key];
  return typeof value === "string" && value !== "" ? value : fallback;
}

/** Splits a command on unquoted pipes so checks inspect executed programs on every pipeline stage, not data args. */
function splitPipelineSegments(command: string): string[] {
  const segments: string[] = [];
  let start = 0;
  let quote = "";
  for (let i = 0; i < command.length; i++) {
    const ch = command.charAt(i);
    if (quote !== "") {
      if (ch === "\\" && quote === '"' && i + 1 < command.length) {
        i++;
      } else if (ch === quote) {
        quote = "";
      }
      continue;
    }
    if (ch === "'" || ch === '"') {
      quote = ch;
    } else if (ch === "|") {
      if (command.charAt(i + 1) === "|") {
        i++;
        continue;
      }
      segments.push(command.slice(start, i));
      start = i + 1;
    }
  }
  segments.push(command.slice(start));
  return segments;
}

/** True when any executed step invokes one of `justRecipes` or `nodeScripts` (ignores paths, filters, and data). */
function invokesTarget(command: string, justRecipes: ReadonlySet<string>, nodeScripts: ReadonlySet<string>): boolean {
  for (const segment of splitPipelineSegments(command)) {
    const line = readCommandLine(segment);
    if (line === null) {
      continue;
    }
    for (const step of line.steps) {
      if (isTrivialStep(step)) {
        continue;
      }
      const first = step.words[0] ?? "";
      const prog = programOf(first);
      if (prog === "just") {
        const recipe = justPositionals(step.words)[0];
        if (recipe !== undefined && justRecipes.has(recipe)) {
          return true;
        }
        continue;
      }
      if (prog === "node") {
        const script = step.words.slice(1).find((w) => !w.startsWith("-"));
        if (script !== undefined) {
          const base = path.basename(script.replace(/\\/g, "/")).toLowerCase();
          if (nodeScripts.has(base)) {
            return true;
          }
        }
        continue;
      }
      const directBase = path.basename(first.replace(/\\/g, "/")).toLowerCase();
      if (nodeScripts.has(directBase)) {
        return true;
      }
    }
  }
  return false;
}

const LongChecksRecipes = new Set(["long-checks"]);
const LongChecksScripts = new Set(["long-checks.js"]);
const FuzzDiffRecipes = new Set(["fuzz-diff"]);
const FuzzDiffScripts = new Set(["fuzz-differential.js"]);
const TimeBarrierRecipes = new Set(["time-barrier-models"]);
const TimeBarrierScripts = new Set(["time-barrier-models.js"]);

function selectsKernelBenchmarks(command: string): boolean {
  if (!invokesTarget(command, TimeBarrierRecipes, TimeBarrierScripts)) {
    return false;
  }
  const line = readCommandLine(command);
  if (line === null || line.piped) {
    return false;
  }
  return line.steps.some((step) => !isTrivialStep(step) && step.words.includes("--kernels"));
}

const LongChecksRefusal =
  "long-checks queues each step through job-queue.js itself: " +
  "run 'just long-checks' directly without wrapping it in job-queue.js run.";

const HenicosAllowedEnv = [
  "TEST_SEED",
  "GSHARP_TEST_NO_CUDA",
  "GSHARP_TEST_NO_CPU",
  "GSHARP_TEST_VERBOSE",
  "GSHARP_TEST_MEMORY_BUDGET_MB",
  "GSHARP_TEST_MEMORY_DUMP",
  "GSHARP_TEST_MEMORY_DUMP_AT_END",
  "GSHARP_TEST_BUILD_TIMEOUT_SECONDS",
  "GSHARP_MEMORY_FIXTURE_MB",
  "GSharpNativeSanitizers",
  "GSharpTestHeapHardLimit",
] as const;

// CUDA_PATH / CUDA_PATH_V* / PATH match no list, so global Windows SDK variables neither forward nor block offload.
// Queue infrastructure and machine-wide .NET settings match the local-only prefixes below and are ignored instead.
const HenicosLocalOnlyEnv = [
  "Configuration",
  "Platform",
  "TargetFramework",
  "RuntimeIdentifier",
  "OutDir",
  "OutputPath",
  "BaseIntermediateOutputPath",
  "GSHARP_TEST_MEMORY_REPORT",
  "GSHARP_TEST_BUILD_LOG_DIR",
  "GSHARP_CUDA_INCLUDE_DIR",
  "GSHARP_SIGNING_KEY",
  "GH_TOKEN",
  "GSHARP_CUDA_TRAP_TESTS",
  "GSHARP_CUDA_TRAP_PAUSE_MS",
  "GSHARP_DIFF_FULL_CUDA_TRAPS",
  "GSHARP_DIFF_CUDA_TRAP_TARGET",
  "GSHARP_DIFF_CUDA_TRAP_RESULT_FILE",
  "GSHARP_TEST_ISOLATED_METHOD",
  "FUZZ_DIFF_LAUNCHER_PIDS",
  "CUDA_VISIBLE_DEVICES",
  "CXX",
  "CPATH",
  "CPLUS_INCLUDE_PATH",
  "CCC_OVERRIDE_OPTIONS",
  "GCC_EXEC_PREFIX",
  "COMPILER_PATH",
  "CL",
  "_CL_",
  "INCLUDE",
  "ASAN_OPTIONS",
  "UBSAN_OPTIONS",
  "LD_PRELOAD",
] as const;

// MSBuild reads every environment variable as a property (case-insensitively), and the runtime, VSTest and NuGet
// read their own families, so a caller variable of these families changes what a remote run would build or test.
const HenicosLocalOnlyEnvPrefixes = ["GSharp", "DOTNET_", "COMPlus_", "VSTEST_", "MSBUILD", "NUGET_"] as const;

// Set on every run of a machine (MSBUILDDISABLENODEREUSE=1 avoids file locks on Windows; DOTNET_ROOT and
// DOTNET_INSTALL_DIR are set by actions/setup-dotnet and SDK installs, DOTNET_MULTILEVEL_LOOKUP by the GitHub runner
// images, and the worker resolves its own SDK) or queue infrastructure: neither forwarded nor a reason to run locally.
const HenicosIgnoredEnv = [
  "MSBUILDDISABLENODEREUSE",
  "DOTNET_CLI_UI_LANGUAGE",
  "DOTNET_CLI_TELEMETRY_OPTOUT",
  "DOTNET_NOLOGO",
  "DOTNET_SKIP_FIRST_TIME_EXPERIENCE",
  "DOTNET_ROOT",
  "DOTNET_INSTALL_DIR",
  "DOTNET_MULTILEVEL_LOOKUP",
] as const;
const HenicosIgnoredEnvPrefixes = ["GSHARP_JOB_QUEUE_", "GSHARP_GPU_"] as const;

const plugin = {
  name: "./scripts/human/job-queue-henicos",
  apiVersion: PluginApiVersion,

  check(command: string, resource: ResourceRequest | null, context: ClientContext): CheckResult | undefined {
    if (invokesTarget(command, LongChecksRecipes, LongChecksScripts)) {
      // long-checks.js is a slot-free orchestrator that queues each step through job-queue.js itself; wrapping it in
      // an outer queued job wastes a slot and can deadlock small-slot pools when inner steps ask for --weight 2+.
      return { refuse: LongChecksRefusal };
    }
    if (resource === null || resource.name !== "gpu") {
      return undefined;
    }
    if (invokesTarget(command, FuzzDiffRecipes, FuzzDiffScripts)) {
      // It takes the GPU lock itself for its GPU steps; holding it here would make it wait for ever
      return {
        refuse: "fuzz-diff takes the GPU lock itself: queue it without --gpu.",
      };
    }
    if (!resource.forcedExclusive) {
      const refusal = sharedTurnRefusal(command, context.shell, context.options);
      if (refusal !== null) {
        return { refuse: refusal };
      }
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
    const decision = resource?.name === "gpu" ? shareDecision(command, context) : undefined;
    if (!isHenicosTest(command)) {
      return decision !== undefined ? { share: decision.share, shareReason: decision.reason } : undefined;
    }
    const budgets = resolveBudgets(
      context.worktree,
      context.configRoot,
      optionString(context, "budgetsFile", DefaultBudgetsFile),
      context.log,
    );
    // The test list of the name-filter share names the exclusive categories to look for; it runs no test, so it uses
    // the normal budget
    const memoryMb = testMemoryMb(
      command,
      true,
      budgets,
      (cmd) => !onlyListsTests(cmd) && (selectsCategory(cmd, ["Stress", "Benchmark"]) || selectsKernelBenchmarks(cmd)),
    );
    const testsDir = resolveTestsDir(
      context.worktree,
      context.configRoot,
      optionString(context, "testsDir", DefaultTestsDir),
    );
    const baseCwd = context.cwd !== "" ? context.cwd : context.worktree;
    const offload =
      resource === null ? classifyOffloadCommand(command, testsDir, baseCwd, context.log) : { offloadable: false };
    return {
      test: true,
      memoryMb,
      ...(decision !== undefined ? { share: decision.share, shareReason: decision.reason } : {}),
      offloadable: offload.offloadable,
      ...(offload.localReason !== undefined ? { localReason: offload.localReason } : {}),
    };
  },

  // A --gpu run whose filter names tests only is shared when the runner lists no Stress, Benchmark or GpuTrap test
  // among them (name-filter.ts); asked only after classify answered anything but "shared"
  checkShare(command: string, context: ClientContext): Promise<ShareAnswer | undefined> {
    return nameFilterShare(command, context);
  },

  // Asked only after synchronous offload gates pass: probes `Platform=Windows` via `--list-tests` and gates
  // `just test-sanitized` (Option A: auto runs locally on Windows; `--remote` opts into Linux ASan+UBSan).
  checkOffload(command: string, request: OffloadCheckRequest, context: ClientContext): Promise<OffloadAnswer> {
    return checkHenicosOffload(command, request, context);
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

  remoteOffload() {
    return {
      defaultEnv: { GSHARP_TEST_NO_CUDA: "1" },
      allowedEnv: HenicosAllowedEnv,
      localOnlyEnv: HenicosLocalOnlyEnv,
      localOnlyEnvPrefixes: HenicosLocalOnlyEnvPrefixes,
      ignoredEnv: HenicosIgnoredEnv,
      ignoredEnvPrefixes: HenicosIgnoredEnvPrefixes,
      syncExcludePrefixes: ["src/Compiler/Kassyi.GSharp.Compiler/StackLinqEmbedded/"],
      resetProcessNames: ["cc1plus", "clang++", "g++", "ld", "lld", "ninja"],
      resetCleanDirs: ["src/Compiler/Kassyi.GSharp.Compiler/StackLinqEmbedded"],
    };
  },
} satisfies QueuePlugin;

export default plugin;
