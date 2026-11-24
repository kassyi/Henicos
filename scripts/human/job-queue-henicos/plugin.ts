// Repository plugin for Henicos: test classification, memory budgets, the GPU share of a --gpu run (share-rules.ts),
// --gpu checks/hints, and short command labels.

import * as path from "node:path";
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

const DotnetStepValueOptions = new Set([
  "-c",
  "--configuration",
  "-f",
  "--framework",
  "-r",
  "--runtime",
  "-a",
  "--arch",
  "--os",
  "-o",
  "--output",
  "-v",
  "--verbosity",
  "--artifacts-path",
  "-p",
  "--property",
  "--filter",
  "-l",
  "--logger",
  "-s",
  "--settings",
  "--results-directory",
  "--collect",
  "-d",
  "--diag",
  "--blame-crash-dump-type",
  "--blame-hang-dump-type",
  "--blame-hang-timeout",
  "-e",
  "--environment",
  "--test-adapter-path",
  "--project",
  "--source",
  "--configfile",
]);

interface DotnetBuildOrTestStep {
  verb: "build" | "test";
  target: string;
  config: string;
  framework: string;
  runtime: string;
  output: string;
  artifactsPath: string;
  noBuild: boolean;
  listTests: boolean;
}

function normalizeTargetPath(rawTarget: string, stepCwd: string): string {
  const mapped = /^\/(?:mnt\/)?([A-Za-z])\/(.*)$/.exec(rawTarget);
  const adjusted = mapped !== null && process.platform === "win32" ? `${mapped[1]}:/${mapped[2]}` : rawTarget;
  const resolved = adjusted === "" ? path.resolve(stepCwd) : path.resolve(stepCwd, adjusted);
  return process.platform === "win32" ? resolved.toLowerCase() : resolved;
}

function dotnetTargetsMatch(buildTarget: string, testTarget: string): boolean {
  if (buildTarget === testTarget) {
    return true;
  }
  const isProj = (p: string): boolean => /\.(?:csproj|fsproj|vbproj)$/i.test(p);
  if (isProj(testTarget) && path.dirname(testTarget) === buildTarget) {
    return true;
  }
  return isProj(buildTarget) && path.dirname(buildTarget) === testTarget;
}

interface MutableDotnetStep {
  verb: "build" | "test";
  config: string | undefined;
  framework: string;
  runtime: string;
  output: string;
  artifactsPath: string;
  projectOpt: string | undefined;
  noBuild: boolean;
  listTests: boolean;
  positional: string[];
}

function setStepConfig(state: MutableDotnetStep, value: string): boolean {
  const next = value.toLowerCase();
  if (next === "" || (state.config !== undefined && state.config !== next)) {
    return false;
  }
  state.config = next;
  return true;
}

function applyInlineDotnetOpt(state: MutableDotnetStep, word: string, stepCwd: string): "matched" | "invalid" | "none" {
  const inlineCfg = /^(?:-c|--configuration)[:=](.+)$/i.exec(word);
  if (inlineCfg !== null) {
    return setStepConfig(state, inlineCfg[1] ?? "") ? "matched" : "invalid";
  }
  const propCfg = /^(?:--|[-/])(?:p|property):configuration=(.+)$/i.exec(word);
  if (propCfg !== null) {
    return setStepConfig(state, propCfg[1] ?? "") ? "matched" : "invalid";
  }
  const inlineProj = /^--project[:=](.+)$/i.exec(word);
  if (inlineProj !== null) {
    if (state.projectOpt !== undefined) {
      return "invalid";
    }
    state.projectOpt = inlineProj[1] ?? "";
    return "matched";
  }
  const inlineFw = /^(?:-f|--framework)[:=](.+)$/i.exec(word);
  if (inlineFw !== null) {
    state.framework = (inlineFw[1] ?? "").toLowerCase();
    return "matched";
  }
  const inlineRt = /^(?:-r|--runtime)[:=](.+)$/i.exec(word);
  if (inlineRt !== null) {
    state.runtime = (inlineRt[1] ?? "").toLowerCase();
    return "matched";
  }
  const inlineOut = /^(?:-o|--output)[:=](.+)$/i.exec(word);
  if (inlineOut !== null) {
    state.output = normalizeTargetPath(inlineOut[1] ?? "", stepCwd);
    return "matched";
  }
  const inlineArt = /^--artifacts-path[:=](.+)$/i.exec(word);
  if (inlineArt !== null) {
    state.artifactsPath = normalizeTargetPath(inlineArt[1] ?? "", stepCwd);
    return "matched";
  }
  return /^(?:--?|\/)[\w-]+[:=]/.test(word) ? "matched" : "none";
}

function applySpaceDotnetOpt(state: MutableDotnetStep, lower: string, val: string, stepCwd: string): boolean {
  if (lower === "-c" || lower === "--configuration") {
    return setStepConfig(state, val);
  }
  if (lower === "-f" || lower === "--framework") {
    state.framework = val.toLowerCase();
  } else if (lower === "-r" || lower === "--runtime") {
    state.runtime = val.toLowerCase();
  } else if (lower === "-o" || lower === "--output") {
    state.output = normalizeTargetPath(val, stepCwd);
  } else if (lower === "--artifacts-path") {
    state.artifactsPath = normalizeTargetPath(val, stepCwd);
  } else if (lower === "--project") {
    if (state.projectOpt !== undefined) {
      return false;
    }
    state.projectOpt = val;
  } else if (lower === "-p" || lower === "--property") {
    const m = /^configuration=(.+)$/i.exec(val);
    if (m !== null && !setStepConfig(state, m[1] ?? "")) {
      return false;
    }
  }
  return true;
}

function parseDotnetBuildOrTestStep(words: readonly string[], stepCwd: string): DotnetBuildOrTestStep | null {
  const verb = (words[1] ?? "").toLowerCase();
  if (verb !== "build" && verb !== "test") {
    return null;
  }
  const state: MutableDotnetStep = {
    verb,
    config: undefined,
    framework: "",
    runtime: "",
    output: "",
    artifactsPath: "",
    projectOpt: undefined,
    noBuild: false,
    listTests: false,
    positional: [],
  };
  for (let i = 2; i < words.length; i++) {
    const word = words[i] ?? "";
    const lower = word.toLowerCase();
    if (word === "--") {
      break;
    }
    if (lower === "--no-build") {
      state.noBuild = true;
      continue;
    }
    if (lower === "--list-tests" || lower === "-t") {
      state.listTests = true;
      continue;
    }
    const inlineStatus = applyInlineDotnetOpt(state, word, stepCwd);
    if (inlineStatus === "invalid") {
      return null;
    }
    if (inlineStatus === "matched") {
      continue;
    }
    if (DotnetStepValueOptions.has(lower)) {
      const val = words[++i];
      if (val === undefined || !applySpaceDotnetOpt(state, lower, val, stepCwd)) {
        return null;
      }
      continue;
    }
    if (!word.startsWith("-") && !/^\/[A-Za-z]/.test(word)) {
      state.positional.push(word);
    }
  }
  if (state.positional.length > 1 || (state.projectOpt !== undefined && state.positional.length > 0)) {
    return null;
  }
  return {
    verb: state.verb,
    target: normalizeTargetPath(state.projectOpt ?? state.positional[0] ?? "", stepCwd),
    config: state.config ?? "debug",
    framework: state.framework,
    runtime: state.runtime,
    output: state.output,
    artifactsPath: state.artifactsPath,
    noBuild: state.noBuild,
    listTests: state.listTests,
  };
}

function buildMatchesTest(build: DotnetBuildOrTestStep, test: DotnetBuildOrTestStep): boolean {
  return (
    dotnetTargetsMatch(build.target, test.target) &&
    build.config === test.config &&
    build.framework === test.framework &&
    build.runtime === test.runtime &&
    build.output === test.output &&
    build.artifactsPath === test.artifactsPath
  );
}

/**
 * True when `command` is a self-contained CPU test run eligible for transparent offload to a headless Linux worker:
 * - No GPU resource requested, no pipe, no Windows-only filter
 * - Either an allowlisted CPU-only `just` recipe, or plain `dotnet build` / `dotnet test` steps ending in a test
 *   (never a bare `dotnet build` or a `--no-build` test without a matching preceding build target & configuration)
 * - Every named test project is headless-CPU-safe (GSharpTargetDialect Cpu, pure managed, or Category!=GpuExecution).
 */
function isOffloadableCommand(
  command: string,
  budgets: ReadonlyMap<string, unknown>,
  testsDir: string,
  cwd: string,
  log: (text: string) => void,
): boolean {
  if (WindowsOnlyFilterPattern.test(command)) {
    return false;
  }
  const line = readCommandLine(command);
  if (line === null || line.piped) {
    return false;
  }
  const cdCmds = new Set(["cd", "chdir", "set-location", "sl"]);
  let currentCwd = path.resolve(cwd);
  const workSteps: { words: string[]; cwd: string }[] = [];
  for (const step of line.steps) {
    const prog = programOf(step.words[0] ?? "");
    if (cdCmds.has(prog)) {
      if (workSteps.length > 0) {
        return false;
      }
      const target = step.words.slice(1).find((w) => !w.startsWith("-"));
      if (target === undefined) {
        return false;
      }
      currentCwd = normalizeTargetPath(target, currentCwd);
      continue;
    }
    if (!isTrivialStep(step)) {
      workSteps.push({ words: step.words, cwd: currentCwd });
    }
  }
  if (workSteps.length === 0) {
    return false;
  }
  // Allowlisted CPU-only just recipes (single step)
  if (workSteps.length === 1 && programOf(workSteps[0]?.words[0] ?? "") === "just") {
    const recipe = workSteps[0]?.words.slice(1).find((w) => !w.startsWith("-") && !w.includes("="));
    return recipe !== undefined && OffloadableJustRecipes.has(recipe);
  }
  // Plain dotnet build/test sequence: must include at least one dotnet test, and any --no-build test must follow a
  // matching dotnet build (target + -c config) in the same command (standalone --no-build reads local bin/win/)
  const builtSteps: DotnetBuildOrTestStep[] = [];
  let sawTest = false;
  for (const step of workSteps) {
    if (programOf(step.words[0] ?? "") !== "dotnet") {
      return false;
    }
    const parsed = parseDotnetBuildOrTestStep(step.words, step.cwd);
    if (parsed === null) {
      return false;
    }
    if (parsed.verb === "build") {
      builtSteps.push(parsed);
    } else {
      if (parsed.listTests) {
        return false;
      }
      if (parsed.noBuild && !builtSteps.some((b) => buildMatchesTest(b, parsed))) {
        return false;
      }
      sawTest = true;
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
    // The test list of the name-filter share names the exclusive categories to look for; it runs no test, so it uses
    // the normal budget
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
    const baseCwd = context.cwd !== "" ? context.cwd : context.worktree;
    const offloadable =
      resource === null ? isOffloadableCommand(command, budgets, testsDir, baseCwd, context.log) : false;
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

  remoteOffload() {
    return {
      defaultEnv: { GSHARP_TEST_NO_CUDA: "1" },
      syncExcludePrefixes: ["src/Compiler/Kassyi.GSharp.Compiler/StackLinqEmbedded/"],
      resetProcessNames: ["cc1plus", "clang++", "g++", "ld", "lld", "ninja"],
      resetCleanDirs: ["src/Compiler/Kassyi.GSharp.Compiler/StackLinqEmbedded"],
    };
  },
} satisfies QueuePlugin;

export default plugin;
