// The run time of a command that has no record of its own, taken from similar commands, and the log that says how
// good the estimates were.

import * as fs from "node:fs";
import * as path from "node:path";
import { commandKeys, type CommandKeys, type KeyContext } from "./estimate-key.ts";
import { LongestRunMs } from "./constants.ts";
import type { Estimate } from "./scheduler.ts";

/** Where an estimate came from: the command's own runs, a similar command, or nothing (UnknownTypicalMs). */
export type EstimateLevel = "exact" | "normalized" | "shape" | "kind" | "none";

/** The fallback levels, narrow to wide. */
const Levels: readonly (readonly [EstimateLevel, (keys: CommandKeys) => string])[] = [
  ["normalized", (keys) => keys.normalized],
  ["shape", (keys) => keys.shape],
  ["kind", (keys) => keys.kind],
];

export const AccuracyEntries = 1000;
const AccuracyLevels: readonly EstimateLevel[] = ["exact", "normalized", "shape", "kind", "none"];

export function median(values: readonly number[]): number {
  const sorted = [...values].sort((a, b) => a - b);
  const middle = sorted.length >> 1;
  return sorted.length % 2 === 1
    ? (sorted[middle] as number)
    : ((sorted[middle - 1] as number) + (sorted[middle] as number)) / 2;
}

/** The recorded runs of one command, as the history keeps them. */
export interface RecordedRuns {
  runs: number[];
  /**
   * The keys of the command, made when its run was recorded, in the directory and worktree it ran in. An entry of an
   * older history has none: its keys are made from the text alone, with relative paths as written.
   */
  keys?: CommandKeys;
}

/** The keys of an entry of the history that has none of its own: its text alone, no directory. */
const NoContext: KeyContext = { cwd: "", worktree: "" };

/**
 * The commands of the history grouped by each fallback key, built on first use after a change. The estimate of a group
 * is the median of its commands' typical times (and of their longest times), so one command with many runs does not
 * outvote the others.
 */
export class FallbackIndex {
  private readonly _commands: Record<string, RecordedRuns>;
  private _groups: Map<string, string[]>[] | null = null;

  constructor(commands: Record<string, RecordedRuns>) {
    this._commands = commands;
  }

  /** Forget the groups: the history changed. */
  invalidate(): void {
    this._groups = null;
  }

  lookup(command: string, context: KeyContext): (Estimate & { from: EstimateLevel }) | null {
    const wanted = commandKeys(command, context);
    const groups = (this._groups ??= Levels.map(([, keyOf]) => {
      const map = new Map<string, string[]>();
      for (const [key, entry] of Object.entries(this._commands)) {
        const group = keyOf(entry.keys ?? commandKeys(key, NoContext));
        if (group !== "") {
          map.set(group, (map.get(group) ?? []).concat(key));
        }
      }
      return map;
    }));
    for (const [i, [level, keyOf]] of Levels.entries()) {
      const group = keyOf(wanted);
      const keys = group === "" ? undefined : (groups[i] as Map<string, string[]>).get(group);
      if (keys !== undefined) {
        const runs = keys.map((key) => (this._commands[key] as RecordedRuns).runs);
        return {
          typicalMs: median(runs.map(median)),
          longestMs: median(runs.map((r) => Math.max(...r))),
          from: level,
        };
      }
    }
    return null;
  }
}

type AccuracyEntry = [at: number, level: EstimateLevel, predictedMs: number, actualMs: number];

/**
 * The last estimates against the run times that followed, in <state dir>/accuracy.json, for `status --accuracy`:
 * [time, level, predicted typical ms, actual ms] per run that ended by itself.
 */
export class AccuracyLog {
  readonly file: string;
  readonly log: (text: string) => void;
  entries: AccuracyEntry[] = [];

  constructor(file: string, log: (text: string) => void) {
    this.file = file;
    this.log = log;
    this.entries = AccuracyLog.read(file);
  }

  /** The valid entries of a file; an unreadable or damaged one gives none. */
  static read(file: string): AccuracyEntry[] {
    try {
      const saved: unknown = JSON.parse(fs.readFileSync(file, "utf8"));
      const list = Array.isArray(saved) ? (saved as unknown[]) : [];
      return list.filter(isEntry).slice(-AccuracyEntries);
    } catch {
      // No file yet, or damaged: the log only informs, so it starts empty
      return [];
    }
  }

  add(level: EstimateLevel, predictedMs: number, actualMs: number, now = Date.now()): void {
    this.entries = this.entries
      .concat([[now, level, Math.round(predictedMs), Math.round(Math.min(actualMs, LongestRunMs))]])
      .slice(-AccuracyEntries);
    const temporary = `${this.file}.${process.pid}.tmp`;
    try {
      fs.mkdirSync(path.dirname(this.file), { recursive: true });
      fs.writeFileSync(temporary, JSON.stringify(this.entries));
      fs.renameSync(temporary, this.file);
    } catch (error) {
      // The log only informs: a write that fails costs an entry, never the job
      fs.rmSync(temporary, { force: true });
      this.log(`could not write ${this.file}: ${error instanceof Error ? error.message : String(error)}`);
    }
  }
}

function isEntry(value: unknown): value is AccuracyEntry {
  return (
    Array.isArray(value) &&
    value.length === 4 &&
    AccuracyLevels.includes(value[1] as EstimateLevel) &&
    [value[0], value[2], value[3]].every((n) => typeof n === "number" && Number.isFinite(n) && n >= 0)
  );
}

export interface LevelAccuracy {
  level: EstimateLevel;
  runs: number;
  /** The median of |predicted - actual| / actual, in percent. */
  medianErrorPercent: number;
  /** The share of runs whose estimate was within 50% of the actual time, in percent. */
  within50Percent: number;
}

/** The accuracy per level, for the levels that have runs. */
export function summarizeAccuracy(entries: readonly AccuracyEntry[]): LevelAccuracy[] {
  const result: LevelAccuracy[] = [];
  for (const level of AccuracyLevels) {
    const errors = entries
      .filter((entry) => entry[1] === level && entry[3] > 0)
      .map((entry) => Math.abs(entry[2] - entry[3]) / entry[3]);
    if (errors.length > 0) {
      result.push({
        level,
        runs: errors.length,
        medianErrorPercent: Math.round(median(errors) * 100),
        within50Percent: Math.round((errors.filter((e) => e <= 0.5).length / errors.length) * 100),
      });
    }
  }
  return result;
}

export function formatAccuracy(rows: readonly LevelAccuracy[]): string {
  if (rows.length === 0) {
    return "no estimate has been judged yet (a run that ends by itself adds one)";
  }
  const lines = ["level       runs  median error  within 50%"];
  for (const row of rows) {
    const runs = String(row.runs).padStart(4);
    const error = `${row.medianErrorPercent}%`.padStart(12);
    const within = `${row.within50Percent}%`.padStart(10);
    lines.push(`${row.level.padEnd(10)}  ${runs}  ${error}  ${within}`);
  }
  return lines.join("\n");
}
