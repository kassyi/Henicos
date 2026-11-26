// The messages between the clients and the scheduler: one JSON object per line over a TCP connection to 127.0.0.1.

import type { Socket } from "node:net";
import { BaseProtocolVersion, ProtocolVersion, type RemoteMode } from "./constants.ts";
import { MaxEnvKeys } from "./environment-hash.ts";
import type { CommandKeys, PathPlace } from "./estimate-key.ts";
import { isRemoteName, type OffloadPreference } from "./remote/spec.ts";
import { isResourceName, type ShareMode } from "./resources.ts";
import type { WaitFor } from "./scheduler.ts";

export const MaxMemoryMb = 2 ** 20;
export const MaxKeyLength = 2000;
export const MaxResultsDirs = 64;
export const MaxResultsDirLength = 512;

/** How a job ended, as --after reads it. `lost`: its client died and its command exited with an unknown code. */
export type Outcome = "succeeded" | "failed" | "cancelled" | "lost";

/** Summary of quarantined remote artifacts pulled back for a job. */
export interface QuarantineSummary {
  dir: string;
  files: number;
  bytes: number;
  resultsRedirected: boolean;
  resultsDirs?: string[];
}

/** Ended job quarantine metadata returned by `status <id>` when available. */
export interface EndedJobStatus {
  id: number;
  outcome?: Outcome;
  quarantine: QuarantineSummary;
}

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
  /**
   * Where the command's project and script arguments lie (the worktree that holds each, whether it ignores it), keyed
   * by absolute path, for the estimate keys; absent from older clients and when the command names none.
   */
  places?: Record<string, PathPlace>;
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
  /** Memory budget the job takes from the test memory pool, in MB (absent from older clients). */
  memoryMb?: number;
  /** Estimate fallback keys computed by the client (absent from older clients). */
  keys?: CommandKeys;
  /** The turn resource of a --gpu run or a holder (a name of resources.ts; absent from older clients: "gpu"). */
  resource?: string;
  /** "shared": the turn may run beside other shared turns; anything else, and absent, is exclusive. */
  share?: ShareMode;
  /** Optional short reason token for the share decision (absent from older clients and plugins). */
  shareReason?: string;
  /** Remote CPU offload preference: auto (eligible), force (--remote), never (--local / ineligible). */
  offload?: OffloadPreference;
  /** The remote worker name a running job is adopting on after a scheduler restart. */
  remote?: string;
  /** The hash of the client's environment and the hash of each variable's value by name (environment-hash.ts). */
  envHash?: string;
  envKeys?: Record<string, string>;
}

export type ClientMessage =
  | ({ type: "request" | "adopt" } & JobRequest)
  | { type: "started"; commandPid: number; spawnedAt: number }
  /** completed: the command ran to its own end (not stopped by a signal or a cancel). */
  | { type: "done"; completed: boolean; exitCode: number; cancelled?: boolean; quarantine?: QuarantineSummary }
  | { type: "status"; id?: number }
  /** The full id of the newest job whose id ends in these 1 to 4 digits (an older scheduler answers with an error). */
  | { type: "resolve"; digits: string }
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
   * A granted shared GPU turn gives up its share (the client's check that it may share no longer holds): the same
   * job, under the same id, waits again as an exclusive turn. Answered with "queued", or an error when the job is not
   * a granted shared turn (an older scheduler answers "unknown message type").
   */
  | { type: "downgrade"; shareReason?: string }
  /**
   * A remotely granted job whose transport failed before execution gives up its remote slot and waits again under
   * the same id for a local CPU slot (`offload` becomes "never"). Answered with "queued", or an error when not a
   * remote job (an older scheduler answers "unknown message type").
   */
  | { type: "fallback-local"; id: number }
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
      quarantine?: QuarantineSummary;
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
  /** The client sent no worktree: `worktree` is its directory (status --here then looks for it below the worktree). */
  worktreeFromCwd?: true;
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
  /** The turn resource it uses and how it shares it (absent from older schedulers, and when none was asked). */
  resource?: string;
  share?: "shared";
  shareReason?: string;
  /** For a GPU holder: the id of the --gpu run whose turn started it (absent when it has none). */
  turn?: number;
  /** The remote worker name when running on a remote worker (absent for local runs). */
  remote?: string;
  /** Remote offload preference (for state persistence across scheduler restarts). */
  offload?: OffloadPreference;
}

/** A job's place and expected times: ahead = waiting jobs before it (0 once it runs). */
export interface Outlook {
  state: "waiting" | "running";
  ahead: number;
  position: number;
  startAt: number | null;
  endAt: number | null;
  /** The end at its longest recorded run time (absent without a record, and from older schedulers). */
  latestEndAt?: number;
  /** The predecessor (--after) it waits for, while that one has not ended. */
  blockedBy: number | null;
  /** What a waiting job waits for (Scheduler.waitFor; absent once it runs, and from older schedulers). */
  waitFor?: WaitFor;
  /** The turn resource a job that waits for a turn ("gpu", "gpu-memory") waits for. */
  waitResource?: string;
}

export type StatusJob = JobView & Outlook;

/** One turn resource as status shows it. */
export interface ResourceStatus {
  name: string;
  slots: number;
  /** The ids of the shared turns that run. */
  shared: number[];
  /** The ids of the jobs that use it exclusively: exclusive turns, and holders no running turn started. */
  exclusive: number[];
  /** The last probe; absent when none ran. */
  probe?: { ok: boolean; value: number | null; at: number; why?: string };
}

/** One remote CPU worker as status shows it. */
export interface RemoteStatus {
  name: string;
  host: string;
  slots: number;
  cpus: number;
  memoryMb: number;
  mode: RemoteMode;
  /** The ids of the jobs running on this remote worker. */
  running: number[];
  /** The last health/memory probe; absent when none ran. */
  probe?: { ok: boolean; value: number | null; at: number; why?: string };
}

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
  /** The turn resources and who uses them (absent from older schedulers). */
  resources?: ResourceStatus[];
  /** Configured remote CPU workers and who runs on them (absent when none are configured or from older schedulers). */
  remotes?: RemoteStatus[];
  running: StatusJob[];
  waiting: StatusJob[];
  /** Present when `status` requested a specific ended job id that has quarantine metadata. */
  endedJob?: EndedJobStatus;
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
  /**
   * protocol: the scheduler's version (from 3 on; an older one sends none and gives no receipt for "done"). share:
   * "shared" only when the turn was asked to share and the scheduler counts it as shared (an older one never says it).
   * downgrade: true on such a grant, when the scheduler knows the "downgrade" message (an older one never says it, and
   * closes the connection of a message it does not know).
   */
  | {
      type: "granted";
      id: number;
      cpus: number;
      capacity: number;
      protocol?: number;
      share?: "shared";
      downgrade?: true;
      /** Set when the job was granted on a configured remote CPU worker instead of a local CPU slot. */
      remote?: string;
    }
  | { type: "adopted"; id: number; protocol?: number }
  | { type: "cancelled"; id: number; reason?: string }
  | ({ type: "updated"; id: number } & Placement)
  | { type: "error"; code?: string; message: string }
  | StatusReply
  | { type: "cancel-result"; cancelled: number[] }
  | { type: "stopping"; running: number; waiting: number }
  | { type: "move-result"; id: number; position: number }
  | { type: "start-result"; id: number }
  | { type: "resolved"; id: number }
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
    case "downgrade":
    case "fallback-local":
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

/** The well-formed entries of a `places` field (JobRequest.places); undefined when there are none. */
export function placesField(message: Incoming): Record<string, PathPlace> | undefined {
  const value = message.places;
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return undefined;
  }
  // Without a prototype, so that a path named "__proto__" stays a plain key
  const places: Record<string, PathPlace> = Object.create(null) as Record<string, PathPlace>;
  let any = false;
  for (const [key, place] of Object.entries(value as Record<string, unknown>)) {
    if (typeof place === "object" && place !== null) {
      const { root, ignored } = place as Record<string, unknown>;
      if (typeof root === "string" && typeof ignored === "boolean") {
        places[key] = { root, ignored };
        any = true;
      }
    }
  }
  return any ? places : undefined;
}

/** A memory budget a client or a plugin may give: a finite number of MB in 0..2^20. */
export function isMemoryMb(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value) && value >= 0 && value <= MaxMemoryMb;
}

/** Estimate keys a client or a plugin may give: three strings of at most 2000 characters each. */
export function isCommandKeys(value: unknown): value is CommandKeys {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return false;
  }
  const { normalized, shape, kind } = value as Record<string, unknown>;
  return (
    typeof normalized === "string" &&
    normalized.length <= MaxKeyLength &&
    typeof shape === "string" &&
    shape.length <= MaxKeyLength &&
    typeof kind === "string" &&
    kind.length <= MaxKeyLength
  );
}

/** The `memoryMb` of a JobRequest; undefined when missing or invalid. */
export function memoryMbField(message: Incoming): number | undefined {
  return isMemoryMb(message.memoryMb) ? message.memoryMb : undefined;
}

/** The `keys` of a JobRequest, copied without other fields; undefined when missing or invalid. */
export function keysField(message: Incoming): CommandKeys | undefined {
  const value = message.keys;
  return isCommandKeys(value) ? { normalized: value.normalized, shape: value.shape, kind: value.kind } : undefined;
}

/** The `resource` of a JobRequest; undefined when missing or not shaped like a resource name. */
export function resourceField(message: Incoming): string | undefined {
  return isResourceName(message.resource) ? message.resource : undefined;
}

/**
 * The `envHash` and `envKeys` of a JobRequest; undefined when missing or not shaped as environment-hash.ts makes them.
 */
export function environmentField(message: Incoming): { hash: string; keys: Record<string, string> } | undefined {
  const { envHash, envKeys } = message;
  if (typeof envHash !== "string" || !/^[0-9a-f]{1,64}$/.test(envHash)) {
    return undefined;
  }
  if (typeof envKeys !== "object" || envKeys === null || Array.isArray(envKeys)) {
    return undefined;
  }
  const entries = Object.entries(envKeys as Record<string, unknown>);
  if (entries.length > MaxEnvKeys) {
    return undefined;
  }
  // Without a prototype, so that a variable named "__proto__" stays a plain key
  const keys = Object.create(null) as Record<string, string>;
  for (const [name, value] of entries) {
    if (name.length > 256 || typeof value !== "string" || !/^[0-9a-f]{1,64}$/.test(value)) {
      return undefined;
    }
    keys[name] = value;
  }
  return { hash: envHash, keys };
}

/** The `share` of a JobRequest: "shared" for exactly that string, undefined (exclusive) for anything else. */
export function shareField(message: Incoming): "shared" | undefined {
  return message.share === "shared" ? "shared" : undefined;
}

const ShareReasonPattern = /^[a-z0-9:_-]{1,64}$/;

/**
 * Validates a share reason token: 1..64 lowercase ASCII/digit/`:`/`_`/`-` characters (no spaces, parens, or
 * newlines), and never containing `"shared"` unless `share === "shared"` so `grant` log lines remain unambiguous.
 */
export function sanitizeShareReason(value: unknown, share?: ShareMode): string | undefined {
  if (typeof value !== "string" || !ShareReasonPattern.test(value)) {
    return undefined;
  }
  if (share !== "shared" && value.includes("shared")) {
    return undefined;
  }
  return value;
}

/**
 * The `shareReason` of a JobRequest, downgrade message, or saved job, checked against the share it goes with (not the
 * message's own `share`); undefined when missing or invalid.
 */
export function shareReasonField(message: Incoming, share: ShareMode | undefined): string | undefined {
  return sanitizeShareReason(message.shareReason, share);
}

/** The `offload` preference of a JobRequest; undefined when missing or invalid. */
export function offloadField(message: Incoming): OffloadPreference | undefined {
  return message.offload === "auto" || message.offload === "force" || message.offload === "never"
    ? message.offload
    : undefined;
}

/** The `remote` worker name of a JobRequest or saved job; undefined when missing or invalid. */
export function remoteField(message: Incoming): string | undefined {
  return isRemoteName(message.remote) ? message.remote : undefined;
}

/** Validates and copies a QuarantineSummary object; undefined when missing or invalid. */
export function parseQuarantineSummary(value: unknown): QuarantineSummary | undefined {
  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    return undefined;
  }
  const raw = value as Record<string, unknown>;
  if (
    typeof raw.dir !== "string" ||
    raw.dir.length === 0 ||
    raw.dir.length > MaxKeyLength ||
    /[\x00-\x1f\x7f-\x9f]/.test(raw.dir)
  ) {
    return undefined;
  }
  if (typeof raw.files !== "number" || !Number.isInteger(raw.files) || raw.files < 0 || raw.files > 1_000_000) {
    return undefined;
  }
  if (
    typeof raw.bytes !== "number" ||
    !Number.isInteger(raw.bytes) ||
    raw.bytes < 0 ||
    raw.bytes > 10 * 1024 * 1024 * 1024
  ) {
    return undefined;
  }
  if (typeof raw.resultsRedirected !== "boolean") {
    return undefined;
  }
  let resultsDirs: string[] | undefined;
  if (raw.resultsDirs !== undefined) {
    if (!Array.isArray(raw.resultsDirs) || raw.resultsDirs.length > MaxResultsDirs) {
      return undefined;
    }
    const clean: string[] = [];
    for (const item of raw.resultsDirs) {
      if (
        typeof item !== "string" ||
        item.length === 0 ||
        item.length > MaxResultsDirLength ||
        /[\x00-\x1f\x7f-\x9f]/.test(item)
      ) {
        return undefined;
      }
      clean.push(item);
    }
    if (clean.length > 0) {
      if (!raw.resultsRedirected) {
        return undefined;
      }
      resultsDirs = clean;
    }
  }
  return {
    dir: raw.dir,
    files: raw.files,
    bytes: raw.bytes,
    resultsRedirected: raw.resultsRedirected,
    ...(resultsDirs !== undefined ? { resultsDirs } : {}),
  };
}

/** The `quarantine` summary of a `done`, `report`, or saved job message; undefined when missing or invalid. */
export function quarantineField(message: Incoming): QuarantineSummary | undefined {
  return parseQuarantineSummary(message.quarantine);
}
