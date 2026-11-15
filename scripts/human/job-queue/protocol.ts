// The messages between the clients and the scheduler: one JSON object per line over a TCP connection to 127.0.0.1.

import type { Socket } from "node:net";
import { BaseProtocolVersion, ProtocolVersion } from "./constants.ts";

/** How a job ended, as --after reads it. `lost`: its client died and its command exited with an unknown code. */
export type Outcome = "succeeded" | "failed" | "cancelled" | "lost";

/** What a client asks to be queued with, and brings again when it re-registers after a scheduler restart. */
export interface JobRequest {
  weight: number;
  gpu: boolean;
  pid: number;
  commandPid: number;
  spawnedAt: number;
  cwd: string;
  /** The root of the worktree of cwd (absent from older clients: the scheduler then takes cwd). */
  worktree?: string;
  /** The agent chat that queued it (CLAUDE_CODE_SESSION_ID, ANTIGRAVITY_CONVERSATION_ID; absent from other callers). */
  session?: string;
  command: string;
  queuedAt: number;
  /** The job id it had, kept across a restart; 0 for a new job. */
  id: number;
  pin: number | null;
  before: number | null;
  after: number | null;
  keepIfFailed: boolean;
  /** run --gpu: wait for the turn at the GPU (see SchedulerJob.gpuTurn). */
  gpuTurn: boolean;
  /** A GPU holder: the id of the --gpu run whose turn started it (absent from older clients). */
  turn?: number | null;
}

export type ClientMessage =
  | ({ type: "request" | "adopt" } & JobRequest)
  | { type: "started"; commandPid: number; spawnedAt: number }
  /** completed: the command ran to its own end (not stopped by a signal or a cancel). */
  | { type: "done"; completed: boolean; exitCode: number; cancelled?: boolean }
  | { type: "status" }
  | { type: "cancel"; id?: number; all?: boolean }
  | { type: "stop" }
  | { type: "move"; id: number; first?: boolean; before?: number; reset?: boolean }
  | { type: "set-weight"; id: number; weight: number }
  /** Grants a waiting job at once, over the slots and the memory pool (not a GPU turn, not before its predecessor). */
  | { type: "start"; id: number }
  /** Grants nothing new until resume; the running jobs finish. */
  | { type: "pause" }
  | { type: "resume" }
  /**
   * Resolves when the job has ended, with its outcome (the --gpu path waits for its predecessor before the GPU lock).
   */
  | { type: "await"; id: number }
  /**
   * The end of a job whose command ended while its client had no connection, for the jobs that wait for it. pid and
   * queuedAt name the client (absent from older clients), so that a report never ends another client's job.
   */
  | {
      type: "report";
      id: number;
      completed: boolean;
      exitCode: number;
      cancelled?: boolean;
      pid?: number;
      queuedAt?: number;
    };

/** A job as `status` shows it. */
export interface JobView {
  id: number;
  weight: number;
  gpu: boolean;
  pid: number;
  commandPid: number | null;
  orphan: boolean;
  cwd: string;
  /** The worktree it takes its turns for (absent from older schedulers). */
  worktree?: string;
  /** The agent chat that queued it, for the chat panel (absent when none was known, and from older schedulers). */
  session?: string;
  command: string;
  queuedAt: number;
  grantedAt: number | null;
  typicalMs: number | null;
  /** Where typicalMs came from: exact, normalized, shape, kind, or none (absent from older schedulers). */
  estimateFrom?: string;
  pin: number | null;
  before: number | null;
  after: number | null;
  keepIfFailed: boolean;
  gpuTurn: boolean;
  /** The memory budget it takes from the pool, in MB (absent from older schedulers). */
  memoryMb?: number;
}

/** A job's place and expected times: ahead = waiting jobs before it (0 once it runs). */
export interface Outlook {
  state: "waiting" | "running";
  ahead: number;
  position: number;
  startAt: number | null;
  endAt: number | null;
  /** The predecessor (--after) it waits for, while that one has not ended. */
  blockedBy: number | null;
}

export type StatusJob = JobView & Outlook;

export interface StatusReply {
  type: "status";
  pid: number;
  now: number;
  policy: string;
  capacity: number;
  used: number;
  /** The memory pool for test runs and what the running jobs take of it, in MB (absent from older schedulers). */
  memoryCapacity?: number;
  memoryUsed?: number;
  /** The queue is paused (pause): nothing waiting is granted, `start <id>` still is. Absent from older schedulers. */
  paused?: boolean;
  /** When it was paused, ms since the epoch; present while paused. */
  pausedAt?: number;
  running: StatusJob[];
  waiting: StatusJob[];
}

/** The placement a client keeps, so that it brings it when it re-registers. */
export interface Placement {
  weight: number;
  pin: number | null;
  before: number | null;
}

export type ServerMessage =
  | ({ type: "queued"; id: number; paused?: boolean } & Outlook)
  /** The queue was paused while the job waits (sent once to each waiting client). */
  | { type: "paused"; at: number }
  | ({ type: "waiting"; id: number; used: number; capacity: number } & Outlook)
  /** protocol: the scheduler's version (from 3 on; an older one sends none and gives no receipt for "done"). */
  | { type: "granted"; id: number; cpus: number; capacity: number; protocol?: number }
  | { type: "adopted"; id: number; protocol?: number }
  | { type: "cancelled"; id: number; reason?: string }
  | ({ type: "updated"; id: number } & Placement)
  | { type: "error"; code?: string; message: string }
  | StatusReply
  | { type: "cancel-result"; cancelled: number[] }
  | { type: "stopping"; running: number; waiting: number }
  | { type: "move-result"; id: number; position: number }
  | { type: "start-result"; id: number }
  /** The answer of pause and resume: the queue's state now, and whether this call changed it. */
  | { type: "pause-result"; paused: boolean; changed: boolean; running: number; waiting: number; pausedAt: number }
  | { type: "ended"; id: number; outcome: Outcome }
  | { type: "reported"; id: number }
  /** The scheduler has recorded and saved the outcome of a "done". */
  | { type: "done-ok"; id: number };

/**
 * The protocol version a message is sent with: 3 only for what version 3 added (move, set-weight, await, a job with a
 * place or a predecessor), 2 otherwise, so that a scheduler started from an older checkout still serves plain runs, and
 * refuses (instead of ignoring) what it cannot do. Extra fields of a version 2 message (the exit code in "done") are
 * read by a version 3 scheduler and ignored by an older one. A running job's re-registration (adopt) is always version
 * 2: its place and predecessor no longer matter, and an older scheduler must still count its slots.
 */
export function wireVersion(message: ClientMessage): number {
  switch (message.type) {
    case "move":
    case "set-weight":
    case "start":
    case "pause":
    case "resume":
    case "await":
    case "report":
      return ProtocolVersion;
    case "request":
      return message.after !== null || message.pin !== null || message.before !== null || message.gpuTurn
        ? ProtocolVersion
        : BaseProtocolVersion;
    default:
      return BaseProtocolVersion;
  }
}

/** A received message: parsed JSON, checked field by field where it is used. */
export type Incoming = Record<string, unknown> & { type?: unknown; version?: unknown };

/** Splits a socket stream into JSON messages, one per line. */
export function onMessages(socket: Socket, handler: (message: Incoming) => void): void {
  let buffer = "";
  socket.setEncoding("utf8");
  socket.on("data", (chunk: string) => {
    buffer += chunk;
    let newline = buffer.indexOf("\n");
    while (newline >= 0) {
      const line = buffer.slice(0, newline);
      buffer = buffer.slice(newline + 1);
      newline = buffer.indexOf("\n");
      if (line.trim() === "") {
        continue;
      }
      let message: unknown;
      try {
        message = JSON.parse(line);
      } catch (error) {
        socket.destroy(new Error(`invalid message: ${error instanceof Error ? error.message : String(error)}`));
        return;
      }
      if (message === null || typeof message !== "object" || Array.isArray(message)) {
        socket.destroy(new Error("invalid message: not an object"));
        return;
      }
      handler(message as Incoming);
    }
  });
}

export function send(socket: Socket | null, message: ServerMessage | (ClientMessage & { version: number })): void {
  if (socket !== null && !socket.destroyed) {
    socket.write(`${JSON.stringify(message)}\n`);
  }
}

/** A number field of a received message, or the fallback when it is missing or not a finite number. */
export function numberField(message: Incoming, name: string, fallback = 0): number {
  const value = Number(message[name]);
  return Number.isFinite(value) ? value : fallback;
}

/** A positive integer field (a job id), or null. */
export function idField(message: Incoming, name: string): number | null {
  const value = Number(message[name]);
  return Number.isInteger(value) && value > 0 ? value : null;
}

/** A finite number field that may be null (a pin), or null. */
export function optionalNumberField(message: Incoming, name: string): number | null {
  const value = message[name];
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}
