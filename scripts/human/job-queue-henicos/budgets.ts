// Repository test memory budgets (tests/memory-budgets.yml) and CPU-only test project detection via target-dialect.js.

import * as fs from "node:fs";
import { createRequire } from "node:module";
import * as path from "node:path";
import {
  errorCode,
  errorMessage,
  isTrivialStep,
  programOf,
  readCommandLine,
  type ReadStep,
} from "../job-queue/plugin-api.ts";

interface TestProjectEntry {
  name: string;
  project: string;
}

interface TargetDialectModule {
  testProjectFiles(testsDir: string): TestProjectEntry[];
  dialectOf(projectFile: string): "cpu" | "cuda" | null;
  remoteOffloadOf(projectFile: string): string | null;
}

const require = createRequire(import.meta.url);
const { testProjectFiles, dialectOf, remoteOffloadOf } = require("./target-dialect.js") as TargetDialectModule;

/** The budgets of one test assembly, in MB: for a normal run, and for a run with Stress or Benchmark tests. */
export interface Budget {
  normal: number;
  stress: number;
}

/** Reads `<assembly>: <MB>[, stress <MB>]` lines; anything else (comments, blank lines) is skipped. */
export function parseBudgets(text: string): Map<string, Budget> {
  const budgets = new Map<string, Budget>();
  for (const line of text.split(/\r?\n/)) {
    const match = /^([\w.]+):\s*(\d+)(?:\s*,\s*stress\s+(\d+))?\s*(?:#.*)?$/.exec(line);
    if (match) {
      const normal = Number(match[2]);
      budgets.set(match[1] as string, { normal, stress: match[3] ? Number(match[3]) : normal });
    }
  }
  return budgets;
}

function readBudgetsFile(file: string, log: (text: string) => void): Map<string, Budget> | null {
  try {
    return parseBudgets(fs.readFileSync(file, "utf8"));
  } catch (error) {
    if (errorCode(error) === "ENOENT") {
      return null;
    }
    log(`could not read ${file}: ${errorMessage(error)}`);
    return new Map();
  }
}

/**
 * Reads the budgets file from the job's worktree first, falling back to configRoot when absent there; returns an empty
 * map when neither exists (no test runs are then held back by memory).
 */
export function resolveBudgets(
  worktree: string,
  configRoot: string,
  relativePath: string,
  log: (text: string) => void,
): Map<string, Budget> {
  const inWorktree = path.resolve(worktree, relativePath);
  const fromWorktree = readBudgetsFile(inWorktree, log);
  if (fromWorktree !== null) {
    return fromWorktree;
  }
  const inConfigRoot = path.resolve(configRoot, relativePath);
  if (inConfigRoot !== inWorktree) {
    const fromConfigRoot = readBudgetsFile(inConfigRoot, log);
    if (fromConfigRoot !== null) {
      return fromConfigRoot;
    }
  }
  return new Map();
}

/** Resolves the tests directory from worktree when present, otherwise configRoot. */
export function resolveTestsDir(worktree: string, configRoot: string, relativePath: string): string {
  const inWorktree = path.resolve(worktree, relativePath);
  if (fs.existsSync(inWorktree)) {
    return inWorktree;
  }
  return path.resolve(configRoot, relativePath);
}

const JustValueOptions = new Set([
  "-f",
  "--justfile",
  "-d",
  "--working-directory",
  "--dotenv-path",
  "--dotenv-filename",
  "--shell",
  "--shell-arg",
  "--color",
  "--chooser",
  "--command-color",
  "--list-heading",
  "--list-prefix",
]);

/** Extracts positional arguments (recipe first) of a `just` invocation, skipping value options and `k=v` overrides. */
export function justPositionals(words: readonly string[]): string[] {
  const out: string[] = [];
  for (let i = 1; i < words.length; i++) {
    const arg = words[i] ?? "";
    if (out.length === 0) {
      if (JustValueOptions.has(arg)) {
        i++;
        continue;
      }
      if (arg === "--set") {
        i += 2;
        continue;
      }
      if (arg === "--") {
        out.push(...words.slice(i + 1));
        break;
      }
      if (arg.startsWith("-") || /^\w[\w-]*=/.test(arg)) {
        continue;
      }
    }
    out.push(arg);
  }
  return out;
}

const SingleProjectJustRecipes: Readonly<Record<string, string>> = {
  "test-differential-cpu": "Kassyi.GSharp.Tests.Cpu",
  "test-native-stack": "Kassyi.GSharp.Tests.Cpu",
  "test-managed-stack": "Kassyi.GSharp.Tests.Cpu",
  "test-sanitized": "Kassyi.GSharp.Tests.Cpu",
  "diff-barrier-models": "Kassyi.GSharp.Tests.Cpu",
  "time-barrier-models": "Kassyi.GSharp.Tests.Cpu",
  "test-differential-cuda": "Kassyi.GSharp.Tests.Cuda",
  "test-checked": "Kassyi.GSharp.Tests.Cuda",
  "test-gpu-trap": "Kassyi.GSharp.Tests.Cuda",
};

const MutatePhaseProjects: Readonly<Record<string, string>> = {
  gpumath: "Kassyi.GSharp.Stdlib.GpuMath.Tests",
  compiler: "Kassyi.GSharp.Tests.Cpu",
  "compiler-cuda": "Kassyi.GSharp.Tests.Cuda",
};

export function implicitProjectOfStep(words: readonly string[]): string | null {
  const prog = programOf(words[0] ?? "");
  if (prog === "just") {
    const positional = justPositionals(words);
    const recipe = positional[0];
    if (recipe === undefined) {
      return null;
    }
    const direct = SingleProjectJustRecipes[recipe];
    if (direct !== undefined) {
      return direct;
    }
    if (recipe === "mutate") {
      const phase = positional[1] === undefined || positional[1] === "" ? "gpumath" : positional[1];
      return MutatePhaseProjects[phase] ?? null;
    }
    return null;
  }
  if (prog === "node") {
    const scriptIdx = words.findIndex((w, i) => i > 0 && !w.startsWith("-"));
    if (scriptIdx < 0) {
      return null;
    }
    const base = path.basename((words[scriptIdx] as string).replace(/\\/g, "/")).toLowerCase();
    if (base === "test-sanitized.js" || base === "compare-barrier-models.js" || base === "time-barrier-models.js") {
      return "Kassyi.GSharp.Tests.Cpu";
    }
    if (base === "mutate.js") {
      let phase = "gpumath";
      for (let i = scriptIdx + 1; i < words.length; i++) {
        const arg = words[i] as string;
        if (arg === "--") {
          break;
        }
        if (arg === "--phase" && i + 1 < words.length) {
          phase = words[++i] as string;
        } else if (arg.startsWith("--phase=")) {
          phase = arg.slice("--phase=".length);
        }
      }
      return MutatePhaseProjects[phase] ?? null;
    }
  }
  return null;
}

function isStepTestCommand(step: ReadStep): boolean {
  const prog = programOf(step.words[0] ?? "");
  if (prog === "dotnet") {
    const verb = (step.words[1] ?? "").toLowerCase();
    return verb === "test" || verb === "vstest" || verb === "stryker";
  }
  if (prog === "dotnet-stryker") {
    return true;
  }
  if (prog === "just") {
    const recipe = justPositionals(step.words)[0] ?? "";
    return /^(?:test[\w-]*|coverage-diff|mutate|diff-barrier-models|time-barrier-models)$/.test(recipe);
  }
  if (prog === "node") {
    const script = step.words.slice(1).find((w) => !w.startsWith("-"));
    if (script === undefined) {
      return false;
    }
    const base = path.basename(script.replace(/\\/g, "/")).toLowerCase();
    return /^(?:test-stability|test-memory|test-sanitized|mutate|coverage-diff|compare-barrier-models|time-barrier-models)\.js$/.test(
      base,
    );
  }
  return false;
}

/**
 * Resolves the implied single-assembly test projects for known `just` recipes and scripts that do not spell their
 * `.csproj` name on the command line (e.g. `just test-sanitized` -> `Kassyi.GSharp.Tests.Cpu`), or null when any
 * test step targets the whole solution or is unrecognized. Non-test steps (such as `dotnet build`) are ignored.
 */
export function implicitTestProjects(command: string): string[] | null {
  const line = readCommandLine(command);
  if (line === null || line.piped) {
    return null;
  }
  const work = line.steps.filter((step) => !isTrivialStep(step) && isStepTestCommand(step));
  if (work.length === 0) {
    return null;
  }
  const projects: string[] = [];
  for (const step of work) {
    const project = implicitProjectOfStep(step.words);
    if (project === null) {
      return null;
    }
    if (!projects.includes(project)) {
      projects.push(project);
    }
  }
  return projects;
}

/**
 * The memory a test run needs in MB: the largest budget among the assemblies the command names (a project path or
 * name, or a known single-project `just` recipe / script), or, when any test step names none (a run of the whole
 * solution, whose test hosts run side by side), the sum of every budget. A command that selects Stress or Benchmark
 * tests counts the stress budgets. 0 for a command that runs no tests.
 */
export function testMemoryMb(
  command: string,
  isTest: boolean,
  budgets: ReadonlyMap<string, Budget>,
  isStress: (command: string) => boolean,
): number {
  if (!isTest || budgets.size === 0) {
    return 0;
  }
  const stress = isStress(command);
  const of = (budget: Budget): number => (stress ? budget.stress : budget.normal);
  const sumAll = (): number => [...budgets.values()].reduce((sum, budget) => sum + of(budget), 0);

  const line = readCommandLine(command);
  if (line !== null && !line.piped) {
    const testSteps = line.steps.filter((step) => !isTrivialStep(step) && isStepTestCommand(step));
    if (testSteps.length > 0) {
      let maxMb = 0;
      for (const step of testSteps) {
        const stepLower = step.words.join(" ").toLowerCase();
        const named = [...budgets]
          .filter(([name]) => stepLower.includes(name.toLowerCase()))
          .map(([, budget]) => of(budget));
        if (named.length > 0) {
          maxMb = Math.max(maxMb, ...named);
          continue;
        }
        const implied = implicitProjectOfStep(step.words);
        const impliedBudget = implied !== null ? budgets.get(implied) : undefined;
        if (impliedBudget !== undefined) {
          maxMb = Math.max(maxMb, of(impliedBudget));
          continue;
        }
        return sumAll();
      }
      return maxMb;
    }
  }

  const lower = command.toLowerCase();
  const named = [...budgets].filter(([name]) => lower.includes(name.toLowerCase())).map(([, budget]) => of(budget));
  if (named.length > 0) {
    return Math.max(...named);
  }
  return sumAll();
}

/**
 * The test projects (`names`: those of the budgets file) that the command names, when it names at least one and each
 * of them sets `<GSharpTargetDialect>Cpu</GSharpTargetDialect>` in `<testsDir>/<category>/<name>/<name>.csproj`; else
 * none.
 */
export function cpuOnlyTestProjects(
  command: string,
  names: Iterable<string>,
  testsDir: string,
  log: (text: string) => void,
): string[] {
  const lower = command.toLowerCase();
  const known = [...names];
  let named = known.filter((name) => lower.includes(name.toLowerCase()));
  if (named.length === 0) {
    const implied = implicitTestProjects(command);
    if (implied !== null) {
      named = implied.filter((name) => known.includes(name));
    }
  }
  if (named.length === 0) {
    return [];
  }
  let projects: TestProjectEntry[];
  try {
    projects = testProjectFiles(testsDir);
  } catch (error) {
    log(`could not read ${testsDir}: ${errorMessage(error)}`);
    return [];
  }
  const projectsByName = new Map<string, string>();
  for (const { name, project } of projects) {
    projectsByName.set(name, project);
  }
  const runsOnCpu = (name: string): boolean => {
    const project = projectsByName.get(name);
    return project !== undefined && dialectOf(project) === "cpu";
  };
  return named.every(runsOnCpu) ? named : [];
}

export function stepExcludesGpuExecution(words: readonly string[]): boolean {
  const filters: string[] = [];
  let afterDoubleDash = false;
  for (let i = 2; i < words.length; i++) {
    const raw = words[i] ?? "";
    const lower = raw.toLowerCase();
    if (lower.includes("testcasefilter")) {
      return false;
    }
    if (raw === "--") {
      afterDoubleDash = true;
      continue;
    }
    if (afterDoubleDash) {
      continue;
    }
    if (lower === "--filter") {
      const value = words[++i];
      if (value === undefined) {
        return false;
      }
      filters.push(value);
    } else if (lower.startsWith("--filter=")) {
      filters.push(raw.slice("--filter=".length));
    } else if (lower.startsWith("--filter")) {
      return false;
    }
  }
  if (filters.length !== 1) {
    return false;
  }
  const normalized = (filters[0] ?? "").toLowerCase().replace(/\s+/g, "");
  if (normalized.includes("|")) {
    return false;
  }
  return new Set(normalized.split("&")).has("category!=gpuexecution");
}

function referencesCudaProject(projectFile: string, content: string): boolean {
  const dir = path.dirname(projectFile);
  for (const match of content.matchAll(/<ProjectReference\s+[^>]*Include\s*=\s*"([^"]+)"/gi)) {
    const rel = (match[1] ?? "").split("\\").join("/");
    if (rel === "") {
      continue;
    }
    if (dialectOf(path.resolve(dir, rel)) === "cuda") {
      return true;
    }
  }
  return false;
}

/** Finds the `.csproj` path for a named project under `testsDir`, or null if absent. */
export function findTestProjectFile(projectName: string, testsDir: string, log: (text: string) => void): string | null {
  let projects: TestProjectEntry[];
  try {
    projects = testProjectFiles(testsDir);
  } catch (error) {
    log(`could not read ${testsDir}: ${errorMessage(error)}`);
    return null;
  }
  const lower = projectName.toLowerCase();
  return projects.find((entry) => entry.name.toLowerCase() === lower)?.project ?? null;
}

/**
 * Evaluates whether a single resolved test `.csproj` can run on a headless Linux CPU worker without CUDA.
 * Checks `<GSharpRemoteOffload>local</GSharpRemoteOffload>` BEFORE `<GSharpTargetDialect>Cpu</GSharpTargetDialect>`.
 */
export function checkProjectHeadlessCpuSafety(
  projectFile: string,
  projectName: string,
  excludesGpuExecution: boolean,
): { safe: boolean; localReason?: string } {
  if (remoteOffloadOf(projectFile) === "local") {
    return {
      safe: false,
      localReason: `project ${projectName} is marked local (GSharpRemoteOffload=local)`,
    };
  }
  if (projectName === "Kassyi.GSharp.Tests.Integration") {
    return {
      safe: false,
      localReason: `project ${projectName} runs locally`,
    };
  }
  const dialect = dialectOf(projectFile);
  if (dialect === "cpu") {
    return { safe: true };
  }
  if (dialect === "cuda") {
    return { safe: false };
  }
  if (projectName === "Henicos.Benchmarks.Tests") {
    return { safe: excludesGpuExecution };
  }
  // No GSharpTargetDialect: safe if pure managed (no compiler targets, CudaCapability, or CUDA refs)
  try {
    const content = fs.readFileSync(projectFile, "utf8");
    if (
      !content.includes("Kassyi.GSharp.Compiler.targets") &&
      !content.includes("CudaCapability") &&
      !referencesCudaProject(projectFile, content)
    ) {
      return { safe: true };
    }
  } catch {
    return { safe: false };
  }
  return { safe: excludesGpuExecution };
}
