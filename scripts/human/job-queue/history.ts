// The run times of earlier commands, kept on disk, so that the scheduler can order, backfill and forecast by them.

import * as fs from "node:fs";
import * as path from "node:path";
import { HistoryCommands, HistoryRunsPerCommand, LongestRunMs } from "./constants.ts";
import type { Estimate } from "./scheduler.ts";

interface HistoryEntry {
  runs: number[];
  lastUsed: number;
}

/**
 * The last runs of each command text that ran to the end (not cancelled, not lost), in <state dir>/history.json, with a
 * second copy of the next job id for when jobs-<port>.json is lost.
 */
export class RunHistory {
  readonly file: string;
  readonly log: (text: string) => void;
  /**
   * Keyed by command text: a plain object without a prototype, so that "constructor" or "__proto__" stay plain keys.
   */
  commands: Record<string, HistoryEntry> = Object.create(null) as Record<string, HistoryEntry>;
  nextId = 1;

  constructor(file: string, log: (text: string) => void) {
    this.file = file;
    this.log = log;
    try {
      // Keep only well-formed entries, so that a damaged file costs its records, never the scheduler
      const saved: unknown = JSON.parse(fs.readFileSync(file, "utf8"));
      const record = isObject(saved) ? saved : null;
      const commands = record !== null && isObject(record.commands) ? record.commands : null;
      if (commands !== null) {
        for (const [key, entry] of Object.entries(commands)) {
          const runs = isObject(entry) && Array.isArray(entry.runs) ? (entry.runs as unknown[]) : [];
          const valid = runs.filter(isRunTime).slice(-HistoryRunsPerCommand);
          if (valid.length > 0) {
            const lastUsed = isObject(entry) && Number.isFinite(entry.lastUsed) ? (entry.lastUsed as number) : 0;
            this.commands[key] = { runs: valid, lastUsed };
          }
        }
      }
      const nextId = record !== null ? record.nextId : undefined;
      if (typeof nextId === "number" && Number.isInteger(nextId) && nextId > 0) {
        this.nextId = nextId;
      }
    } catch (error) {
      if (errorCode(error) !== "ENOENT") {
        log(`could not read ${file}: ${errorMessage(error)}`);
      }
    }
  }

  static key(command: string): string {
    return command.trim().replace(/\s+/g, " ");
  }

  /** The median and the longest of the command's recorded runs, or null when it has none. */
  estimate(command: string): Estimate | null {
    const entry = this.commands[RunHistory.key(command)];
    if (entry === undefined || entry.runs.length === 0) {
      return null;
    }
    const runs = [...entry.runs].sort((a, b) => a - b);
    const middle = runs.length >> 1;
    const typicalMs =
      runs.length % 2 === 1 ? (runs[middle] as number) : ((runs[middle - 1] as number) + (runs[middle] as number)) / 2;
    return { typicalMs, longestMs: runs[runs.length - 1] as number };
  }

  record(command: string, ms: number, nextId = this.nextId, now = Date.now()): void {
    this.nextId = Math.max(this.nextId, nextId);
    const key = RunHistory.key(command);
    const entry = this.commands[key] ?? { runs: [], lastUsed: 0 };
    entry.runs = entry.runs.concat(Math.min(Math.round(ms), LongestRunMs)).slice(-HistoryRunsPerCommand);
    entry.lastUsed = now;
    this.commands[key] = entry;
    const keys = Object.keys(this.commands);
    if (keys.length > HistoryCommands) {
      const lastUsed = (name: string): number => (this.commands[name] as HistoryEntry).lastUsed;
      keys
        .sort((a, b) => lastUsed(a) - lastUsed(b))
        .slice(0, keys.length - HistoryCommands)
        .forEach((old) => delete this.commands[old]);
    }
    const temporary = `${this.file}.${process.pid}.tmp`;
    try {
      fs.mkdirSync(path.dirname(this.file), { recursive: true });
      fs.writeFileSync(temporary, JSON.stringify({ nextId: this.nextId, commands: this.commands }));
      replaceFile(temporary, this.file);
    } catch (error) {
      this.log(`could not write ${this.file}: ${errorMessage(error)}`);
      fs.rmSync(temporary, { force: true });
    }
  }
}

/**
 * Renames `from` over `to`. Windows refuses to replace a file that another process has open (a reader, a virus
 * scanner): retry briefly.
 */
export function replaceFile(from: string, to: string): void {
  for (let attempt = 1; ; attempt++) {
    try {
      fs.renameSync(from, to);
      return;
    } catch (error) {
      const code = errorCode(error);
      if (attempt >= 10 || (code !== "EPERM" && code !== "EACCES" && code !== "EBUSY")) {
        throw error;
      }
      Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 20 * attempt);
    }
  }
}

function isRunTime(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value) && value >= 0 && value <= LongestRunMs;
}

export function isObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

export function errorCode(error: unknown): string | undefined {
  return isObject(error) && typeof error.code === "string" ? error.code : undefined;
}

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
