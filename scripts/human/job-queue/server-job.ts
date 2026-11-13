// A job as the scheduler process keeps it (the scheduler's view plus the connection and the command), and how status
// and the state file show it.

import type * as net from "node:net";
import type { JobView } from "./protocol.ts";
import type { SchedulerJob } from "./scheduler.ts";

/** A job as the server keeps it: the scheduler's view plus the connection and the command. */
export interface ServerJob extends SchedulerJob {
  socket: net.Socket | null;
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
    orphan: job.socket === null,
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
