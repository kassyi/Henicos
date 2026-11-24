// Repository test memory budgets (tests/memory-budgets.yml) and CPU-only test project detection via target-dialect.js.

import * as fs from "node:fs";
import { createRequire } from "node:module";
import * as path from "node:path";
import { errorCode, errorMessage, isTrivialStep, programOf, readCommandLine } from "../job-queue/plugin-api.ts";

interface TestProjectEntry {
  name: string;
  project: string;
}

interface TargetDialectModule {
  testProjectFiles(testsDir: string): TestProjectEntry[];
  dialectOf(projectFile: string): "cpu" | "cuda" | null;
}

const require = createRequire(import.meta.url);
const { testProjectFiles, dialectOf } = require("./target-dialect.js") as TargetDialectModule;

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

/**
 * The memory a test run needs in MB: the largest budget among the assemblies the command names (a project path or
 * name), or, when it names none (a run of the whole solution, whose test hosts run side by side), the sum of every
 * budget. A command that selects Stress or Benchmark tests counts the stress budgets. 0 for a command that runs no
 * tests.
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
  const lower = command.toLowerCase();
  const named = [...budgets].filter(([name]) => lower.includes(name.toLowerCase())).map(([, budget]) => of(budget));
  if (named.length > 0) {
    return Math.max(...named);
  }
  return [...budgets.values()].reduce((sum, budget) => sum + of(budget), 0);
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
  const named = [...names].filter((name) => lower.includes(name.toLowerCase()));
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

function stepExcludesGpuExecution(words: readonly string[]): boolean {
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

function commandExcludesGpuExecution(command: string): boolean {
  const line = readCommandLine(command);
  if (line === null || line.piped) {
    return false;
  }
  const testSteps = line.steps.filter((step, index) => {
    if (line.expands[index] === true && !isTrivialStep(step)) {
      return true;
    }
    return programOf(step.words[0] ?? "") === "dotnet" && (step.words[1] ?? "").toLowerCase() === "test";
  });
  return (
    testSteps.length > 0 &&
    testSteps.every(
      (step) =>
        programOf(step.words[0] ?? "") === "dotnet" &&
        (step.words[1] ?? "").toLowerCase() === "test" &&
        stepExcludesGpuExecution(step.words),
    )
  );
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

/**
 * True when every test project named in `command` (at least one named) can run on a headless Linux CPU worker without
 * CUDA: either `<GSharpTargetDialect>Cpu</GSharpTargetDialect>`, a pure managed test project without
 * `Kassyi.GSharp.Compiler.targets` or CUDA project references (excluding `Kassyi.GSharp.Tests.Integration`), or
 * `command` explicitly filters out `Category!=GpuExecution` without any `|` OR-clause.
 */
export function isHeadlessCpuSafe(
  command: string,
  names: Iterable<string>,
  testsDir: string,
  log: (text: string) => void,
): boolean {
  const lower = command.toLowerCase();
  const named = [...names].filter((name) => lower.includes(name.toLowerCase()));
  if (named.length === 0) {
    return false;
  }
  const excludesGpuExecution = commandExcludesGpuExecution(command);
  let projects: TestProjectEntry[];
  try {
    projects = testProjectFiles(testsDir);
  } catch (error) {
    log(`could not read ${testsDir}: ${errorMessage(error)}`);
    return false;
  }
  const projectsByName = new Map<string, string>();
  for (const { name, project } of projects) {
    projectsByName.set(name, project);
  }
  return named.every((name) => {
    if (name === "Kassyi.GSharp.Tests.Integration") {
      return false;
    }
    const project = projectsByName.get(name);
    if (project === undefined) {
      return false;
    }
    const dialect = dialectOf(project);
    if (dialect === "cpu") {
      return true;
    }
    if (dialect === "cuda") {
      return false;
    }
    if (name === "Henicos.Benchmarks.Tests") {
      return excludesGpuExecution;
    }
    // No GSharpTargetDialect: safe if pure managed (no compiler targets, CudaCapability, or CUDA refs)
    try {
      const content = fs.readFileSync(project, "utf8");
      if (
        !content.includes("Kassyi.GSharp.Compiler.targets") &&
        !content.includes("CudaCapability") &&
        !referencesCudaProject(project, content)
      ) {
        return true;
      }
    } catch {
      return false;
    }
    return excludesGpuExecution;
  });
}
