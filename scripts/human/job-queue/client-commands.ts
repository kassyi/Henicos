// The client modes other than run: status, cancel, move and set-weight, start, wait and stop, and how they print a job.

import { CancelledExitCode, LostExitCode, UsageExitCode } from "./constants.ts";
import { OlderSchedulerHint, SchedulerLauncher, delay, exchange, request, type Endpoint } from "./client-connect.ts";
import {
  numberField,
  type ClientMessage,
  type Incoming,
  type Outcome,
  type StatusJob,
  type StatusReply,
} from "./protocol.ts";
import { prefix } from "./system.ts";

function gigabytes(mb: number): string {
  return (mb / 1024).toFixed(1);
}

function formatJob(job: StatusJob, now: number): string {
  const since = job.grantedAt || job.queuedAt;
  const minutes = Math.round((now - since) / 60000);
  // != null: a scheduler of protocol 2 (an older checkout) sends none of the placement and dependency fields
  const flags = [
    job.gpu ? " gpu" : "",
    job.gpuTurn ? " gpu-turn" : "",
    job.memoryMb ? ` ${gigabytes(job.memoryMb)}GB` : "",
    job.orphan ? " client-lost" : "",
    job.pin != null ? " pinned" : "",
    job.before != null ? ` before #${job.before}` : "",
    job.after != null ? ` after #${job.after}${job.keepIfFailed ? " (even if it fails)" : ""}` : "",
  ].join("");
  const outlook = describeOutlook(job, now);
  const blocked = job.blockedBy != null ? `waits for #${job.blockedBy} to end; ` : "";
  const detail = outlook || blocked ? `\n      ${blocked}${outlook}` : "";
  return `  #${job.id} w${job.weight}${flags} ${minutes}m pid ${job.pid} ${job.cwd}${detail}\n      ${job.command}`;
}

/** "2 ahead, start ~20:31 (in 6m), end ~20:35 (in 10m)" for a waiting job, "end ~20:35 (in 4m)" for a running one. */
export function describeOutlook(outlook: object, now: number): string {
  const view = outlook as Partial<Record<string, unknown>>;
  const { startAt, endAt } = view;
  if (
    typeof startAt !== "number" ||
    typeof endAt !== "number" ||
    !Number.isFinite(startAt) ||
    !Number.isFinite(endAt)
  ) {
    return "";
  }
  const clock = (time: number): string => new Date(time).toTimeString().slice(0, 5);
  const minutes = (ms: number): string => `${Math.max(0, Math.round(ms / 60000))}m`;
  const end = `expected end ~${clock(endAt)} (in ${minutes(endAt - now)})`;
  if (view.state === "running") {
    return end;
  }
  return `${String(view.ahead)} ahead, expected start ~${clock(startAt)} (in ${minutes(startAt - now)}), ${end}`;
}

function printReplyError(reply: Incoming): void {
  process.stdout.write(`${typeof reply.message === "string" ? reply.message : JSON.stringify(reply)}\n`);
  process.exitCode = 1;
}

export async function status(port: number, id: number, json: boolean): Promise<void> {
  const raw = await request(port, { type: "status" });
  if (raw !== null && raw.type !== "status") {
    printReplyError(raw);
    return;
  }
  const reply = raw as StatusReply | null;
  if (id > 0) {
    // One job: its state, place and expected times; exit 1 when the scheduler does not know it (ended or never queued)
    const job = reply === null ? undefined : reply.running.concat(reply.waiting).find((j) => j.id === id);
    if (reply === null || job === undefined) {
      process.stdout.write(
        json ? `${JSON.stringify({ id, state: "unknown" })}\n` : `no job #${id} (ended, or never queued)\n`,
      );
      process.exitCode = 1;
      return;
    }
    process.stdout.write(json ? `${JSON.stringify({ now: reply.now, ...job })}\n` : `${formatJob(job, reply.now)}\n`);
    return;
  }
  if (reply === null) {
    process.stdout.write(
      json ? `${JSON.stringify({ running: [], waiting: [] })}\n` : `no scheduler on 127.0.0.1:${port} (no jobs)\n`,
    );
    return;
  }
  if (json) {
    process.stdout.write(`${JSON.stringify(reply)}\n`);
    return;
  }
  const now = reply.now || Date.now();
  // != null: a scheduler of an older checkout has no memory pool
  const memory =
    reply.memoryCapacity != null && reply.memoryUsed != null
      ? `, ${gigabytes(reply.memoryUsed)}/${gigabytes(reply.memoryCapacity)} GB of test memory in use`
      : "";
  const lines = [`scheduler pid ${reply.pid}: ${reply.used}/${reply.capacity} slots${memory}, policy ${reply.policy}`];
  lines.push(`running (${reply.running.length}):`);
  reply.running.forEach((job) => lines.push(formatJob(job, now)));
  lines.push(`waiting (${reply.waiting.length}):`);
  reply.waiting.forEach((job) => lines.push(formatJob(job, now)));
  process.stdout.write(`${lines.join("\n")}\n`);
}

export async function cancel(port: number, id: number | "all"): Promise<void> {
  const reply = await request(port, id === "all" ? { type: "cancel", all: true } : { type: "cancel", id });
  if (reply === null) {
    process.stdout.write("no scheduler running (no jobs)\n");
    return;
  }
  if (reply.type !== "cancel-result" || !Array.isArray(reply.cancelled)) {
    printReplyError(reply);
    return;
  }
  const cancelled = reply.cancelled as number[];
  if (cancelled.length === 0) {
    process.stdout.write(id === "all" ? "no jobs to cancel\n" : `no job #${id}\n`);
    process.exitCode = id === "all" ? 0 : 1;
    return;
  }
  process.stdout.write(`cancelled ${cancelled.map((one) => `#${one}`).join(" ")}\n`);
}

/** move and set-weight: print the job's new place, or the scheduler's refusal (exit 1). */
export async function place(
  port: number,
  message: ClientMessage & ({ type: "move" } | { type: "set-weight" }),
): Promise<void> {
  const reply = await request(port, message);
  if (reply === null) {
    process.stdout.write("no scheduler running (no jobs)\n");
    process.exitCode = 1;
    return;
  }
  if (reply.type !== "move-result") {
    printReplyError(reply);
    return;
  }
  const position = numberField(reply, "position");
  process.stdout.write(
    position > 0 ? `#${String(reply.id)} is now at position ${position}\n` : `#${String(reply.id)} was granted\n`,
  );
}

/** start: grants a waiting job at once, over the slots and the memory pool; prints the refusal otherwise (exit 1). */
export async function startNow(port: number, id: number): Promise<void> {
  const reply = await request(port, { type: "start", id });
  if (reply === null) {
    process.stdout.write("no scheduler running (no jobs)\n");
    process.exitCode = 1;
    return;
  }
  if (reply.type === "error" && typeof reply.message === "string" && reply.message.startsWith("unknown message type")) {
    process.stdout.write(`scheduler: ${reply.message}; ${OlderSchedulerHint}\n`);
    process.exitCode = 1;
    return;
  }
  if (reply.type !== "start-result") {
    printReplyError(reply);
    return;
  }
  process.stdout.write(`#${String(reply.id)} started over the pool\n`);
}

/**
 * wait: blocks until job `id` has ended and exits with its outcome (0 succeeded, 1 failed, 4 cancelled, 5 lost, 2 for
 * an id the scheduler does not know). An agent runs it in the background to be called when the job ends. Keeps
 * waiting across a scheduler restart; a job that already ended answers from the recorded outcome.
 */
export async function wait(endpoint: Endpoint, id: number, json: boolean): Promise<void> {
  const exitCodes: Record<Outcome, number> = {
    succeeded: 0,
    failed: 1,
    cancelled: CancelledExitCode,
    lost: LostExitCode,
  };
  const launcher = new SchedulerLauncher(endpoint);
  for (;;) {
    const socket = await launcher.connectOrStart();
    const reply = await exchange(socket, { type: "await", id });
    if (reply !== null && reply.type === "ended") {
      const outcome = String(reply.outcome) as Outcome;
      process.stdout.write(json ? `${JSON.stringify({ id, outcome })}\n` : `#${id} ${outcome}\n`);
      process.exit(exitCodes[outcome] ?? 1);
    }
    if (reply !== null && reply.type === "error") {
      prefix(`scheduler: ${String(reply.message)}${reply.code === "version" ? `; ${OlderSchedulerHint}` : ""}`);
      process.exit(reply.code === "unknown-job" ? UsageExitCode : 1);
    }
    await delay(800); // the scheduler went away (a restart): ask the next one
  }
}

export async function stop(port: number): Promise<void> {
  const reply = await request(port, { type: "stop" });
  if (reply === null) {
    process.stdout.write("no scheduler running\n");
    return;
  }
  if (reply.type !== "stopping") {
    printReplyError(reply);
    return;
  }
  const running = numberField(reply, "running");
  const waiting = numberField(reply, "waiting");
  process.stdout.write(
    `scheduler stopped (${running} running jobs re-register with the next one, ${waiting} waiting jobs queue again)\n`,
  );
}
