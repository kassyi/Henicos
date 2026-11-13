// The defaults that follow the machine (MachineProfile): the slots from its cores, the memory pool for test runs from
// its memory, and the memory a test run needs, read from the budgets of tests/memory-budgets.yml; and which test
// projects run on the CPU only (their csproj sets GSharpTargetDialect to Cpu), for the hint of run --gpu.
//
// Measured on 24 cores / 64 GB (a mix of 4 forced rebuilds and 4 test runs of Tests.Cpu): 8 slots (3 cores each) with
// a test run weighing 1 slot finished soonest; 6 slots with test runs weighing 2 left a quarter of the CPU idle. Each
// test run took about its budget in memory. So a test run takes one slot, and the memory pool, not the slots, keeps
// test runs from running out of memory on a smaller machine.

import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { errorCode, errorMessage } from "./history.ts";
import { isTestCommand } from "./shell.ts";
import { prefix } from "./system.ts";

/** The cores a slot stands for. */
export const CoresPerSlot = 3;
export const MinimumSlots = 2;
/** The share of the physical memory that the budgets of the running test runs may add up to. */
export const MemoryPoolShare = 0.5;

const TestsDirectory = path.join(import.meta.dirname, "..", "..", "..", "tests");
const BudgetsFile = path.join(TestsDirectory, "memory-budgets.yml");

/** The budgets of one test assembly, in MB: for a normal run, and for a run with Stress or Benchmark tests. */
export interface Budget {
  normal: number;
  stress: number;
}

/** The machine the queue runs on: its cores, its memory and the test budgets of the checkout. */
export class MachineProfile {
  readonly cores: number;
  readonly totalBytes: number;
  private readonly budgetsFile: string;
  private loaded: Map<string, Budget> | null = null;

  constructor(cores = os.availableParallelism(), totalBytes = os.totalmem(), budgetsFile = BudgetsFile) {
    this.cores = cores;
    this.totalBytes = totalBytes;
    this.budgetsFile = budgetsFile;
  }

  /** The default slots: one per CoresPerSlot cores, at least MinimumSlots. */
  get slots(): number {
    return Math.max(MinimumSlots, Math.round(this.cores / CoresPerSlot));
  }

  /** The default memory pool in MB: MemoryPoolShare of the physical memory. */
  get memoryPoolMb(): number {
    return Math.floor((this.totalBytes / (1024 * 1024)) * MemoryPoolShare);
  }

  /**
   * The budgets of this checkout, read on first use; empty when the file cannot be read (no test runs are then held
   * back by memory).
   */
  get budgets(): ReadonlyMap<string, Budget> {
    if (this.loaded === null) {
      this.loaded = readBudgets(this.budgetsFile);
    }
    return this.loaded;
  }

  /** The memory a command takes from the pool, in MB (see testMemoryMb). */
  testMemoryMb(command: string): number {
    return testMemoryMb(command, isTestCommand(command), this.budgets);
  }

  /** The test projects a command names when every one of them runs on the CPU only (see cpuOnlyTestProjects). */
  cpuOnlyTestProjects(command: string): string[] {
    return cpuOnlyTestProjects(command, this.budgets.keys(), path.dirname(this.budgetsFile));
  }
}

/**
 * The test projects (`names`: those of the budgets file) that the command names, when it names at least one and each of
 * them sets `<GSharpTargetDialect>Cpu</GSharpTargetDialect>` in `<testsDir>/<category>/<name>/<name>.csproj`; else
 * none. A command that names a project whose csproj cannot be read, or that says nothing of the dialect, gets none:
 * the hint only speaks when the project file itself says the tests do not use the GPU.
 */
export function cpuOnlyTestProjects(command: string, names: Iterable<string>, testsDir: string): string[] {
  const lower = command.toLowerCase();
  const named = [...names].filter((name) => lower.includes(name.toLowerCase()));
  if (named.length === 0) {
    return [];
  }
  let categories: string[];
  try {
    categories = fs
      .readdirSync(testsDir, { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .map((entry) => entry.name);
  } catch (error) {
    prefix(`could not read ${testsDir}: ${errorMessage(error)}`);
    return [];
  }
  const runsOnCpu = (name: string): boolean =>
    categories.some((category) => {
      const project = path.join(testsDir, category, name, `${name}.csproj`);
      return (
        fs.existsSync(project) &&
        /<GSharpTargetDialect>\s*Cpu\s*<\/GSharpTargetDialect>/i.test(fs.readFileSync(project, "utf8"))
      );
    });
  return named.every(runsOnCpu) ? named : [];
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

function readBudgets(file: string): Map<string, Budget> {
  try {
    return parseBudgets(fs.readFileSync(file, "utf8"));
  } catch (error) {
    // A checkout without the file (an older one) holds no test run back by memory
    if (errorCode(error) !== "ENOENT") {
      prefix(`could not read ${file}: ${errorMessage(error)}`);
    }
    return new Map();
  }
}

/**
 * The memory a test run needs in MB: the largest budget among the assemblies the command names (a project path or
 * name), or, when it names none (a run of the whole solution, whose test hosts run side by side), the sum of every
 * budget. A command that selects Stress or Benchmark tests (names them, not as `!=`) counts the stress budgets. 0 for
 * a command that runs no tests.
 */
export function testMemoryMb(command: string, isTest: boolean, budgets: ReadonlyMap<string, Budget>): number {
  if (!isTest || budgets.size === 0) {
    return 0;
  }
  // A filter that leaves them out (Category!=Stress, as just test passes) does not select them
  const stress = /\b(Stress|Benchmark)\b/.test(command.replace(/!(=|~)\s*\w+/g, ""));
  const of = (budget: Budget): number => (stress ? budget.stress : budget.normal);
  const lower = command.toLowerCase();
  const named = [...budgets].filter(([name]) => lower.includes(name.toLowerCase())).map(([, budget]) => of(budget));
  if (named.length > 0) {
    return Math.max(...named);
  }
  return [...budgets.values()].reduce((sum, budget) => sum + of(budget), 0);
}
