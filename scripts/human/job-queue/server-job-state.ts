// The decisions about one job record of the scheduler, as pure functions: who may re-bind a record (isSameClient,
// planRegistration), what a closed connection, an observation of the processes or a report means for it (planClose,
// planObservation, planReport), how a job ended (outcomeOfEnd) and what a cancel reaches (cancelTargets). No sockets,
// no clock, no process table: the handlers gather the evidence, call the plan and apply it.

import { ClientGraceMs } from "./constants.ts";
import type { Outcome } from "./protocol.ts";

/** Where a record is: waiting, running, or ended with a provisional outcome (a final one removes the record). */
export type Phase = "waiting" | "running" | "ended";

/** What identifies the client process that owns a job: its pid, when it queued, and its command's process. */
export interface ClientIdentity {
  pid: number;
  /** Date.now() when the client queued; the same in every request and adopt of that process. 0: not sent. */
  queuedAt: number;
  /** The command's process; 0 while it is not known. */
  commandPid: number;
  spawnedAt: number;
}

/** What the plans read of a record. */
export interface JobFacts extends ClientIdentity {
  phase: Phase;
  /** Restored from an older state file as an id only, with no client known. */
  stub: boolean;
  /** No connection is bound to the record. */
  detached: boolean;
  /** When the connection was lost (or the record restored); 0 while attached. */
  detachedAt: number;
  /** When the provisional outcome was set (phase "ended"). */
  provisionalAt: number;
  /** The client sent "done". */
  done: boolean;
  completed: boolean;
  exitCode: number | null;
  /** The scheduler cancelled the running job and waits for its client to stop the command. */
  cancelPending: boolean;
}

/** How a command ended, as "done" or "report" say it. */
export interface CommandEnd {
  /** The command ran to its own end (not stopped by a signal or a cancel). */
  completed: boolean;
  /** null: the client sent none (protocol 2). */
  exitCode: number | null;
  /** The client says the job was cancelled. */
  cancelled: boolean;
}

/** True when `message` comes from the client process that owns `record`. */
export function isSameClient(record: ClientIdentity, message: ClientIdentity): boolean {
  return (
    record.pid === message.pid &&
    // A pid that another process reuses comes with another queuedAt; a protocol 2 client may send none
    (message.queuedAt === 0 || record.queuedAt === message.queuedAt) &&
    // When both sides know the command, it is the same process started at the same time
    (record.commandPid === 0 ||
      message.commandPid === 0 ||
      (record.commandPid === message.commandPid && record.spawnedAt === message.spawnedAt))
  );
}

/** How a job ended: cancelled when the client says so, or the scheduler asked and the command did not succeed first. */
export function outcomeOfEnd(end: CommandEnd, cancelPending: boolean): Outcome {
  const succeeded = end.completed && end.exitCode === 0;
  if (end.cancelled || (cancelPending && !succeeded)) {
    return "cancelled";
  }
  if (end.exitCode === null) {
    return "lost"; // a protocol 2 "done": the command ended, its result is unknown
  }
  return succeeded ? "succeeded" : "failed";
}

/** What a request or adopt does with the record under the id the client brings. */
export type Registration =
  /** A new record: under the id asked for when it is free (or held by a stub), else the next id. */
  | { kind: "create" }
  /**
   * A new record under a new id: the id belongs to another client or has a final outcome. `finalize`: the old record
   * of the same client ends as lost.
   */
  | { kind: "create-as-new-id"; finalize: boolean }
  /** The job was cancelled while its client was away. */
  | { kind: "refuse-cancelled" }
  /** The client's own record: bind the connection to it. */
  | { kind: "rebind"; to: "waiting" | "running"; resendGrant: boolean; resendCancel: boolean };

export function planRegistration(
  type: "request" | "adopt",
  record: JobFacts | undefined,
  final: Outcome | undefined,
  message: ClientIdentity,
): Registration {
  if (record === undefined || record.stub) {
    if (final === "cancelled") {
      return { kind: "refuse-cancelled" };
    }
    return final === undefined ? { kind: "create" } : { kind: "create-as-new-id", finalize: false };
  }
  if (!isSameClient(record, message)) {
    return { kind: "create-as-new-id", finalize: false };
  }
  const resendCancel = record.cancelPending;
  switch (record.phase) {
    case "waiting":
      // An adopt of a waiting record: the grant was not saved before a crash, the command runs
      return { kind: "rebind", to: type === "adopt" ? "running" : "waiting", resendGrant: false, resendCancel };
    case "running":
      // A request of a running record: the client never saw the grant
      return { kind: "rebind", to: "running", resendGrant: type === "request", resendCancel };
    case "ended":
      // A client that waits never had a command; one whose command runs takes its record back
      return type === "adopt"
        ? { kind: "rebind", to: "running", resendGrant: false, resendCancel }
        : { kind: "create-as-new-id", finalize: true };
  }
}

/** What a closed connection means for its record. */
export type CloseStep = "ignore" | "detach" | { end: Outcome };

export function planClose(job: JobFacts, stopping: boolean, clientAlive: boolean): CloseStep {
  if (stopping && !job.done) {
    return "ignore"; // the stop ended the connection; the client registers again with the next scheduler
  }
  if (job.phase === "ended") {
    return "ignore";
  }
  if (job.done) {
    // A waiting job whose client left on its own (a timeout, Ctrl+C) never ran
    return {
      end: job.phase === "waiting" ? "cancelled" : outcomeOfEnd({ ...job, cancelled: false }, job.cancelPending),
    };
  }
  if (job.cancelPending) {
    return { end: "cancelled" };
  }
  if (job.phase === "waiting" && !clientAlive) {
    return { end: "cancelled" }; // it never ran and its client is gone
  }
  // A waiting record keeps its place, a running one its slots, until the client comes back or the evidence says
  return "detach";
}

/** What an observation of a detached record's processes leads to. */
export type ObservationStep =
  | "keep"
  /**
   * The command is gone: free its slots; `final` when no report can come any more, else the record waits as
   * provisionally lost.
   */
  | { freeSlots: true; final: boolean }
  | { finalize: "lost" };

export function planObservation(
  job: JobFacts,
  seen: { clientAlive: boolean; commandAlive: boolean | null },
  now: number,
): ObservationStep {
  const graceOver = (since: number): boolean => now - since >= ClientGraceMs;
  switch (job.phase) {
    case "waiting":
      if (job.stub) {
        return graceOver(job.detachedAt) ? { finalize: "lost" } : "keep"; // a stub has no pid to check
      }
      return !seen.clientAlive || graceOver(job.detachedAt) ? { finalize: "lost" } : "keep";
    case "running":
      if (job.commandPid > 0) {
        // null (the process table could not be read) counts as alive
        return seen.commandAlive === false ? { freeSlots: true, final: !seen.clientAlive } : "keep";
      }
      // The command is not known: the client adopts with it while it lives, within the grace
      return !seen.clientAlive || graceOver(job.detachedAt) ? { freeSlots: true, final: true } : "keep";
    case "ended":
      return !seen.clientAlive || graceOver(job.provisionalAt) ? { finalize: "lost" } : "keep";
  }
}

/** What a report of a job's end does. */
export type ReportStep = "ignore" | { end: Outcome };

export function planReport(
  record: JobFacts | undefined,
  final: Outcome | undefined,
  reporter: { pid: number; queuedAt: number },
  end: CommandEnd,
): ReportStep {
  if (final !== undefined) {
    return "ignore"; // final is final
  }
  if (record === undefined || record.stub) {
    return { end: outcomeOfEnd(end, false) }; // an id the scheduler does not hold
  }
  if (reporter.pid !== 0 && !isSameClient(record, { ...reporter, commandPid: 0, spawnedAt: 0 })) {
    return "ignore"; // another client's record
  }
  if (record.phase === "waiting") {
    return "ignore"; // a job that never ran has no end to report
  }
  // A provisional "lost" is replaced; a held running record ends
  return { end: outcomeOfEnd(end, record.cancelPending) };
}

/**
 * The jobs a cancel reaches: the requested ones; for each GPU holder, the --gpu run whose turn started it, so that the
 * run ends as cancelled rather than as failed; and for each turn, the GPU holders it started, so that their clients
 * stop the command themselves. The kill of the process tree under gpu-lock.js alone can miss them on Windows once a
 * process between them has exited, and an orphaned holder keeps the GPU from every later turn.
 */
export function cancelTargets<J extends { id: number; turn: number | null }>(requested: J[], all: J[]): J[] {
  const targets = [...requested];
  const add = (job: J | undefined): void => {
    if (job !== undefined && !targets.includes(job)) {
      targets.push(job);
    }
  };
  for (const job of requested) {
    add(job.turn === null ? undefined : all.find((other) => other.id === job.turn));
    all.filter((other) => other.turn === job.id).forEach(add);
  }
  return targets;
}
