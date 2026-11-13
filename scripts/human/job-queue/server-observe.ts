// The observation of the job records whose client is away (DetachedObserver): every OrphanPollMs it reads whether
// each client process lives and whether a process of each running command is left, and applies planObservation
// (server-job-state.ts) to the records that did not change meanwhile.

import type { ServerJob } from "./server-job.ts";
import { planObservation } from "./server-job-state.ts";
import type { JobServer } from "./server.ts";
import { ProcessTable } from "./system.ts";

/** What one observation saw of a detached record, and the epoch of the record it read. */
export interface Observation {
  job: ServerJob;
  epoch: number;
  seen: { clientAlive: boolean; commandAlive: boolean | null };
}

/** The live processes of each job's command (Unix: its process group id), or null per job when unsure. */
export async function commandProcesses(jobs: ServerJob[]): Promise<(number[] | null)[]> {
  if (jobs.length === 0) {
    return [];
  }
  const table = process.platform === "win32" ? await ProcessTable.query() : null;
  return jobs.map((job) => {
    if (job.commandPid <= 0) {
      return [];
    }
    if (process.platform !== "win32") {
      return ProcessTable.isGroupAlive(job.commandPid) ? [job.commandPid] : [];
    }
    return table === null ? null : table.commandProcesses(job.commandPid, job.spawnedAt);
  });
}

/** Observes the detached records of a scheduler and ends, frees or keeps each on the evidence. */
export class DetachedObserver {
  private readonly server: JobServer;
  // An observation is under way (it reads the process table)
  private polling = false;

  constructor(server: JobServer) {
    this.server = server;
  }

  /**
   * Observes the records whose client is away: whether the client process lives and, for a running record with a known
   * command, whether a process of the command is left. A step applies only to the record as it was read: one that was
   * re-bound or changed meanwhile (its epoch moved) is left alone. One settle at the end.
   */
  async observe(): Promise<void> {
    const server = this.server;
    const detached = [...server.records.values()].filter((job) => job.connection === null);
    if (this.polling || detached.length === 0) {
      return;
    }
    this.polling = true;
    try {
      const epochs = detached.map((job) => job.epoch);
      const withCommand = detached.filter((job) => job.phase === "running" && job.commandPid > 0);
      const live = await commandProcesses(withCommand);
      const observations = detached.map((job, index): Observation => {
        const at = withCommand.indexOf(job);
        const processes = at < 0 ? [] : live[at];
        return {
          job,
          epoch: epochs[index] as number,
          seen: {
            clientAlive: ProcessTable.isAlive(job.pid),
            commandAlive: processes === null || processes === undefined ? null : processes.length > 0,
          },
        };
      });
      this.apply(observations, Date.now());
      server.settle();
    } finally {
      this.polling = false;
    }
  }

  /** Applies the plan of each observation to its record, unless the record changed since it was read. */
  apply(observations: Observation[], now: number): void {
    const server = this.server;
    for (const { job, epoch, seen } of observations) {
      if (job.epoch !== epoch || server.records.get(job.id) !== job) {
        continue;
      }
      const step = planObservation(job, seen, now);
      if (step === "keep") {
        continue;
      }
      if ("finalize" in step) {
        server.finish(
          job,
          step.finalize,
          seen.clientAlive ? "its client did not come back in time" : "its client is gone",
        );
      } else if (step.final) {
        server.finish(job, "lost", "the command of a lost client exited");
      } else {
        server.freeSlots(job, "the command of a lost client exited");
      }
    }
  }
}
