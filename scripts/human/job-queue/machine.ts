// The defaults that follow the machine: the slots from its cores, the memory pool for test runs from its memory, and
// the memory a test run needs, read from the budgets of tests/memory-budgets.yml.
//
// Measured on 24 cores / 64 GB (a mix of 4 forced rebuilds and 4 test runs of Tests.Cpu): 8 slots (3 cores each) with
// a test run weighing 1 slot finished soonest; 6 slots with test runs weighing 2 left a quarter of the CPU idle. Each
// test run took about its budget in memory. So a test run takes one slot, and the memory pool, not the slots, keeps
// test runs from running out of memory on a smaller machine.

import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { errorCode, errorMessage } from "./history.ts";
import { prefix } from "./system.ts";

/** The cores a slot stands for. */
export const CoresPerSlot = 3;
export const MinimumSlots = 2;
/** The share of the physical memory that the budgets of the running test runs may add up to. */
export const MemoryPoolShare = 0.5;

const BudgetsFile = path.join(import.meta.dirname, "..", "..", "..", "tests", "memory-budgets.yml");

/** The default slots: one per CoresPerSlot cores, at least MinimumSlots. */
export function defaultSlots(cores = os.availableParallelism()): number {
  return Math.max(MinimumSlots, Math.round(cores / CoresPerSlot));
}

/** The default memory pool in MB: MemoryPoolShare of the physical memory. */
export function defaultMemoryPoolMb(totalBytes = os.totalmem()): number {
  return Math.floor((totalBytes / (1024 * 1024)) * MemoryPoolShare);
}

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

/** The budgets of this checkout; empty when the file cannot be read (no test runs are then held back by memory). */
export function readBudgets(file = BudgetsFile): Map<string, Budget> {
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
