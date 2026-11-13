// A job as the scheduler process keeps it (the scheduler's view plus the connection and the command), and how status
// and the state file show it.

import type { JobView, Outcome } from "./protocol.ts";
import type { SchedulerJob } from "./scheduler.ts";
import type { Connection } from "./server-connection.ts";
import type { JobFacts } from "./server-job-state.ts";

/**
 * A job as the server keeps it: the scheduler's view plus the connection and the command. One record per id, from the
 * first request (or its restore) until a final outcome is recorded.
 */
export interface ServerJob extends SchedulerJob, JobFacts {
  /** The one connection bound to the record; null while detached (the client is away). */
  connection: Connection | null;
  /** Mirrors connection === null, for the scheduler (a detached waiting job is not granted). */
  detached: boolean;
  /** A "lost" that a report may still replace (phase "ended"). */
  provisional: Outcome | null;
  /** Incremented on every re-bind and phase change: an asynchronous observation applies only to the epoch it read. */
  epoch: number;
  /** For a GPU holder: the id of the --gpu turn whose command registered it; null otherwise. */
  turn: number | null;
  pid: number;
  commandPid: number;
  spawnedAt: number;
  cwd: string;
  command: string;
  keepIfFailed: boolean;
  lastPosition: number;
  lastStartAt: number | null;
  done: boolean;
  completed: boolean;
  /** The command's exit code from "done"; null when the client did not send one (protocol 2, or no "done"). */
  exitCode: number | null;
  cancelled: boolean;
  cancelPending: boolean;
}

export function describeJob(job: ServerJob): JobView {
  return {
    id: job.id,
    weight: job.weight,
    gpu: job.gpu,
    pid: job.pid,
    commandPid: job.commandPid || null,
    orphan: job.connection === null,
    cwd: job.cwd,
    command: job.command,
    queuedAt: job.queuedAt,
    grantedAt: job.grantedAt || null,
    typicalMs: job.estimate ? job.estimate.typicalMs : null,
    pin: job.pin,
    before: job.before,
    after: job.after,
    keepIfFailed: job.keepIfFailed,
    gpuTurn: job.gpuTurn === true,
    memoryMb: job.memoryMb ?? 0,
  };
}
