// The scheduler's state file (jobs-<port>.json): the running jobs with a started command, the next id, the jobs that
// have not ended and the outcomes for --after, written after every event and read once when the scheduler starts.

import * as fs from "node:fs";
import * as path from "node:path";
import { ClientGraceMs } from "./constants.ts";
import { errorCode, errorMessage, isObject, replaceFile } from "./history.ts";
import { idField, numberField, type Outcome } from "./protocol.ts";
import { describeJob } from "./server-job.ts";
import type { JobServer } from "./server.ts";
import { ProcessTable } from "./system.ts";

const Outcomes: readonly string[] = ["succeeded", "failed", "cancelled", "lost"];

export class JobStore {
  readonly path: string;
  private readonly server: JobServer;

  constructor(server: JobServer, stateDirectory: string, port: number) {
    this.server = server;
    this.path = path.join(stateDirectory, `jobs-${port}.json`);
  }

  // The running jobs with a started command, so that a restarted scheduler still counts the ones whose client is gone
  // (nobody re-registers them), the next id, and the outcomes for --after. Jobs whose client lives re-register.
  persist(): void {
    const server = this.server;
    if (server.stopping) {
      return; // the next scheduler may already have read the file: a late write would overwrite what it saved
    }
    const jobsPath = this.path;
    const records = server.scheduler.running
      .filter((job) => job.commandPid > 0)
      .map((job) => ({ ...describeJob(job), spawnedAt: job.spawnedAt }));
    const temporary = `${jobsPath}.${process.pid}.tmp`;
    try {
      fs.mkdirSync(path.dirname(jobsPath), { recursive: true });
      // The jobs that have not ended, so that a restarted scheduler waits for them as predecessors (--after) until they
      // come back: the waiting ones, the running ones, and those still expected from an earlier restart
      const pending = [
        ...new Set(
          [...server.scheduler.waiting, ...server.scheduler.running]
            .map((job) => job.id)
            .concat([...server.expectedUntil.keys()]),
        ),
      ];
      // Written aside and renamed over, so that a reader or a crash never sees half a file
      fs.writeFileSync(
        temporary,
        JSON.stringify({ nextId: server.nextId, jobs: records, ended: [...server.outcomes], pending }),
      );
      replaceFile(temporary, jobsPath);
    } catch (error) {
      server.log(`could not write ${jobsPath}: ${errorMessage(error)}`);
      fs.rmSync(temporary, { force: true });
    }
  }

  restore(): void {
    const server = this.server;
    const jobsPath = this.path;
    let saved: unknown = null;
    try {
      saved = JSON.parse(fs.readFileSync(jobsPath, "utf8"));
    } catch (error) {
      if (errorCode(error) !== "ENOENT") {
        server.log(`could not read ${jobsPath}: ${errorMessage(error)}`);
      }
      return;
    }
    if (!isObject(saved)) {
      return;
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
    if (Array.isArray(saved.pending)) {
      for (const id of saved.pending as unknown[]) {
        if (typeof id === "number" && Number.isInteger(id) && id > 0 && !server.outcomes.has(id)) {
          server.expect(id, Date.now() + ClientGraceMs);
        }
      }
    }
    const records = Array.isArray(saved.jobs) ? (saved.jobs as unknown[]) : [];
    for (const record of records) {
      if (!isObject(record) || ProcessTable.isAlive(numberField(record, "pid"))) {
        continue; // its client re-registers it
      }
      const job = server.createJob(record, server.chooseId(idField(record, "id")), "running");
      job.detachedAt = Date.now();
      server.records.set(job.id, job);
      server.scheduler.adopt(job);
      server.forget(job.id);
      server.log(`restored #${job.id} weight ${job.weight} (client gone, command ${job.commandPid}): ${job.command}`);
    }
    // No persist here: the records of live clients stay in the file until they re-register (the next persist), so
    // that a crash in between does not lose them.
  }
}
