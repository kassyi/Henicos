// The scheduler's state file (jobs-<port>.json): every job record (running, waiting, provisionally ended), the next id
// and the final outcomes for --after, written after every event and read once when the scheduler starts.

import * as fs from "node:fs";
import * as path from "node:path";
import { errorCode, errorMessage, isObject, replaceFile } from "./history.ts";
import type { Outcome } from "./protocol.ts";
import { describeJob, type ServerJob } from "./server-job.ts";
import type { Phase } from "./server-job-state.ts";
import type { JobServer } from "./server.ts";

const Outcomes: readonly string[] = ["succeeded", "failed", "cancelled", "lost"];

/** The format of the file this scheduler writes; a file without one was written by an older scheduler. */
export const StoreFormat = 2;

/** How restore went: "current" when a file of this format was read, so that no startup grace is needed. */
export type Restored = "current" | "older" | "none";

export class JobStore {
  readonly path: string;
  private readonly server: JobServer;

  constructor(server: JobServer, stateDirectory: string, port: number) {
    this.server = server;
    this.path = path.join(stateDirectory, `jobs-${port}.json`);
  }

  /** A record as the file keeps it: what status shows, plus what a restore needs. */
  private static describe(job: ServerJob): Record<string, unknown> {
    return {
      ...describeJob(job),
      spawnedAt: job.spawnedAt,
      turn: job.turn,
      ...(job.stub ? { stub: true } : {}),
      ...(job.startNow === true ? { startNow: true } : {}),
      ...(job.provisional !== null ? { provisional: job.provisional } : {}),
    };
  }

  /**
   * Every record, so that a restarted scheduler counts the slots of the running jobs from its first event and keeps the
   * places of the waiting ones, the next id, and the final outcomes for --after. `jobs`, `ended`, `nextId` and
   * `pending` keep the names and meaning an older scheduler reads.
   */
  persist(): void {
    const server = this.server;
    if (server.stopping) {
      return; // the next scheduler may already have read the file: a late write would overwrite what it saved
    }
    const jobsPath = this.path;
    const records = [...server.records.values()];
    const of = (phase: Phase) => records.filter((job) => job.phase === phase).map(JobStore.describe);
    const temporary = `${jobsPath}.${process.pid}.tmp`;
    try {
      fs.mkdirSync(path.dirname(jobsPath), { recursive: true });
      // Written aside and renamed over, so that a reader or a crash never sees half a file
      fs.writeFileSync(
        temporary,
        JSON.stringify({
          format: StoreFormat,
          nextId: server.nextId,
          jobs: of("running"),
          waiting: of("waiting"),
          provisional: of("ended"),
          ended: [...server.outcomes],
          // Only for an older scheduler, which waits for these ids as predecessors (--after) until they come back
          pending: records.map((job) => job.id),
        }),
      );
      replaceFile(temporary, jobsPath);
    } catch (error) {
      server.log(`could not write ${jobsPath}: ${errorMessage(error)}`);
      fs.rmSync(temporary, { force: true });
    }
  }

  /**
   * Restores the file: every saved record is held (running ones count their slots, waiting ones keep their place,
   * provisional ones wait for a report) until its client re-binds it or the evidence ends it. A file of an older
   * scheduler has only the running jobs as records; its other pending ids become stubs.
   */
  restore(): Restored {
    const server = this.server;
    const jobsPath = this.path;
    let saved: unknown = null;
    try {
      saved = JSON.parse(fs.readFileSync(jobsPath, "utf8"));
    } catch (error) {
      if (errorCode(error) !== "ENOENT") {
        server.log(`could not read ${jobsPath}: ${errorMessage(error)}`);
      }
      return "none";
    }
    if (!isObject(saved)) {
      return "none";
    }
    if (typeof saved.nextId === "number" && Number.isInteger(saved.nextId)) {
      server.nextId = Math.max(server.nextId, saved.nextId);
    }
    if (Array.isArray(saved.ended)) {
      for (const entry of saved.ended as unknown[]) {
        if (Array.isArray(entry) && Number.isInteger(entry[0]) && Outcomes.includes(String(entry[1]))) {
          server.outcomes.set(entry[0] as number, entry[1] as Outcome);
        }
      }
    }
    const list = (name: string): unknown[] => (Array.isArray(saved[name]) ? (saved[name] as unknown[]) : []);
    const restore = (phase: Phase) => (record: unknown) => {
      if (isObject(record)) {
        server.restoreRecord(record, phase);
      }
    };
    list("jobs").forEach(restore("running"));
    if (saved.format === StoreFormat) {
      list("waiting").forEach(restore("waiting"));
      list("provisional").forEach(restore("ended"));
      return "current";
    }
    // An older scheduler's file: the ids that had not ended are kept as stubs, so that --after, wait and the cascade
    // keep working until the job comes back or the grace is over
    for (const id of list("pending")) {
      if (typeof id === "number" && Number.isInteger(id) && id > 0 && server.find(id) === undefined) {
        server.restoreRecord({ id, stub: true }, "waiting");
      }
    }
    return "older";
  }
}
