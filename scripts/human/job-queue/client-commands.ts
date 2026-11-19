// The client modes other than run: status, cancel, move and set-weight, start, pause and resume, wait and stop, and
// how they print a job.

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
import { AccuracyLog, formatAccuracy, summarizeAccuracy } from "./estimate-fallback.ts";
import { prefix, resolveStateDirectory } from "./system.ts";
import * as path from "node:path";
import { formatJobId, type JobRef } from "./job-id.ts";

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
    job.share === "shared" ? " shared" : "",
    job.memoryMb ? ` ${gigabytes(job.memoryMb)}GB` : "",
    job.orphan ? " client-lost" : "",
    job.pin != null ? " pinned" : "",
    job.before != null ? ` before ${formatJobId(job.before)}` : "",
    job.after != null ? ` after ${formatJobId(job.after)}${job.keepIfFailed ? " (even if it fails)" : ""}` : "",
  ].join("");
  const outlook = describeOutlook(job, now);
  // An older scheduler says only what a job waits for after another (blockedBy)
  const waits = describeWaitFor(job) || (job.blockedBy != null ? `waits for ${formatJobId(job.blockedBy)} to end` : "");
  const detail = outlook || waits ? `\n      ${waits}${waits && outlook ? "; " : ""}${outlook}` : "";
  return `  ${formatJobId(job.id)} w${job.weight}${flags} ${minutes}m pid ${job.pid} ${job.cwd}${detail}\n      ${job.command}`;
}

/** The latest end is told when it is this much after the expected end: a shorter gap says nothing new. */
const LatestEndNoticeMs = 60 * 1000;

/**
 * "2 ahead, expected start ~20:31 (in 6m), expected end ~20:35 (in 10m)" for a waiting job, "expected end ~20:35 (in
 * 4m)" for a running one; ", at the latest ~20:52" follows when the longest recorded run would end a minute or more
 * later.
 */
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
  const { latestEndAt } = view;
  const latest =
    typeof latestEndAt === "number" && Number.isFinite(latestEndAt) && latestEndAt - endAt >= LatestEndNoticeMs
      ? `, at the latest ~${clock(latestEndAt)}`
      : "";
  const end = `expected end ~${clock(endAt)} (in ${minutes(endAt - now)})${latest}`;
  if (view.state === "running") {
    return end;
  }
  return `${String(view.ahead)} ahead, expected start ~${clock(startAt)} (in ${minutes(startAt - now)}), ${end}`;
}

/**
 * What a waiting job waits for (Outlook.waitFor) in words: "waits for CPU slots", "waits for a turn at gpu", "waits
 * for j0821 to end"; "" when the scheduler did not say (a running job, an older scheduler).
 */
export function describeWaitFor(outlook: object): string {
  const view = outlook as Partial<Record<string, unknown>>;
  const resource = typeof view.waitResource === "string" ? view.waitResource : "gpu";
  switch (view.waitFor) {
    case "after":
      return typeof view.blockedBy === "number"
        ? `waits for ${formatJobId(view.blockedBy)} to end`
        : "waits for its predecessor";
    case "away":
      return "waits for its client to come back";
    case "restart":
      return "waits for the running jobs to re-register after a scheduler restart";
    case "paused":
      return "waits for the queue to resume";
    case "gpu":
      return `waits for a turn at ${resource}`;
    case "gpu-memory":
      return `waits for free memory on ${resource}`;
    case "slots":
      return "waits for CPU slots";
    case "memory":
      return "waits for the memory budget of the test runs";
    case "in-line":
      return "waits for the jobs that go first";
    default:
      return "";
  }
}

function printReplyError(reply: Incoming): void {
  process.stdout.write(`${typeof reply.message === "string" ? reply.message : JSON.stringify(reply)}\n`);
  process.exitCode = 1;
}

/**
 * The full id a person's reference names: a full number as it is, the last digits through the scheduler (resolve),
 * which knows the jobs that run, wait or ended. Says what the digits stood for on stderr (never on stdout, which
 * --json keeps clean) and ends the process with `failCode` when no job matches. An older scheduler does not know
 * "resolve": the digits then count as the full id, as before.
 */
export async function resolveJobRef(
  endpoint: Endpoint,
  ref: JobRef,
  failCode = 1,
  startScheduler = false,
): Promise<number> {
  if (ref.kind === "exact") {
    return ref.id;
  }
  const message: ClientMessage = { type: "resolve", digits: ref.digits };
  let reply: Incoming | null = null;
  if (startScheduler) {
    // wait and run --after start the scheduler as they do with a full number (it restores the outcomes it saved), and
    // ask the next one when it goes away (a restart)
    const launcher = new SchedulerLauncher(endpoint);
    for (let attempt = 0; attempt < 5 && reply === null; attempt++) {
      reply = await exchange(await launcher.connectOrStart(), message);
      if (reply === null) {
        await delay(800);
      }
    }
  } else {
    reply = await request(endpoint.port, message);
  }
  if (reply === null) {
    prefix(`no scheduler running; j${ref.digits} cannot be resolved (give the full job number)`);
    process.exit(1);
  }
  if (reply.type === "resolved") {
    const id = numberField(reply, "id");
    // Whenever the digits are not the whole id: a wrong match is noticed (873 that stood for 9873 included)
    if (id !== Number(ref.digits)) {
      prefix(`${formatJobId(id)} = job ${id}`);
    }
    return id;
  }
  if (isOlderSchedulerReply(reply)) {
    prefix(
      `the scheduler is older and cannot resolve j${ref.digits}; taking ${Number(ref.digits)} as the full job number`,
    );
    return Number(ref.digits);
  }
  prefix(typeof reply.message === "string" ? reply.message : JSON.stringify(reply));
  return process.exit(failCode);
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
        json ? `${JSON.stringify({ id, state: "unknown" })}\n` : `no job ${id} (ended, or never queued)\n`,
      );
      process.exitCode = 1;
      return;
    }
    process.stdout.write(
      json
        ? `${JSON.stringify({ now: reply.now, ...job })}\n`
        : `${formatJobId(job.id)} = job ${job.id}\n${formatJob(job, reply.now)}\n`,
    );
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
  if (reply.paused === true) {
    const since = reply.pausedAt ? ` since ${new Date(reply.pausedAt).toTimeString().slice(0, 5)}` : "";
    lines.push(
      `PAUSED${since}: no waiting job starts until "job-queue.js resume" (start <id> still works); the expected ` +
        "starts below count from a resume now",
    );
  }
  lines.push(`running (${reply.running.length}):`);
  reply.running.forEach((job) => lines.push(formatJob(job, now)));
  lines.push(`waiting (${reply.waiting.length}):`);
  reply.waiting.forEach((job) => lines.push(formatJob(job, now)));
  process.stdout.write(`${lines.join("\n")}\n`);
}

/** status --accuracy: the logged estimates against the run times that followed, per source of the estimate. */
export function accuracy(json: boolean): Promise<void> {
  const rows = summarizeAccuracy(AccuracyLog.read(path.join(resolveStateDirectory(), "accuracy.json")));
  process.stdout.write(
    json
      ? `${JSON.stringify(rows)}
`
      : `${formatAccuracy(rows)}
`,
  );
  return Promise.resolve();
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
    process.stdout.write(id === "all" ? "no jobs to cancel\n" : `no job ${id}\n`);
    process.exitCode = id === "all" ? 0 : 1;
    return;
  }
  process.stdout.write(`cancelled ${cancelled.map((one) => `${formatJobId(one)}`).join(" ")}\n`);
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
    position > 0
      ? `${formatJobId(numberField(reply, "id"))} is now at position ${position}\n`
      : `${formatJobId(numberField(reply, "id"))} was granted\n`,
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
  process.stdout.write(`${formatJobId(numberField(reply, "id"))} started over the pool\n`);
}

/** The scheduler's refusal of a message it does not know, with the hint to stop the older scheduler. */
function isOlderSchedulerReply(reply: Incoming): boolean {
  return (
    reply.type === "error" && typeof reply.message === "string" && reply.message.startsWith("unknown message type")
  );
}

/**
 * pause: the scheduler grants nothing new until resume; running jobs finish. With `untilIdle` it then blocks until no
 * job runs (exit 0), so that `pause --until-idle && stop` restarts the scheduler once the running jobs are done.
 */
export async function pause(port: number, untilIdle: boolean): Promise<void> {
  const reply = await request(port, { type: "pause" });
  if (reply === null) {
    process.stdout.write("no scheduler running; nothing to pause\n");
    process.exitCode = 1;
    return;
  }
  if (isOlderSchedulerReply(reply)) {
    process.stdout.write(`scheduler: ${String(reply.message)}; ${OlderSchedulerHint}\n`);
    process.exitCode = 1;
    return;
  }
  if (reply.type !== "pause-result") {
    printReplyError(reply);
    return;
  }
  const running = numberField(reply, "running");
  const waiting = numberField(reply, "waiting");
  process.stdout.write(
    `${reply.changed === true ? "paused" : "already paused"}: ${running} running jobs finish, ` +
      `${waiting} waiting jobs wait; "job-queue.js resume" lifts it\n`,
  );
  if (!untilIdle) {
    return;
  }
  for (let left = running; left > 0; ) {
    await delay(1000);
    const raw = await request(port, { type: "status" });
    if (raw === null || raw.type !== "status") {
      break; // the scheduler is gone: nothing runs under it
    }
    left = (raw as unknown as StatusReply).running.length;
  }
  process.stdout.write("no job runs\n");
}

/** resume: lifts the pause; the scheduler grants the waiting jobs as usual. */
export async function resume(port: number): Promise<void> {
  const reply = await request(port, { type: "resume" });
  if (reply === null) {
    process.stdout.write("no scheduler running (not paused)\n");
    return;
  }
  if (isOlderSchedulerReply(reply)) {
    process.stdout.write(`scheduler: ${String(reply.message)}; ${OlderSchedulerHint}\n`);
    process.exitCode = 1;
    return;
  }
  if (reply.type !== "pause-result") {
    printReplyError(reply);
    return;
  }
  process.stdout.write(reply.changed === true ? "resumed\n" : "was not paused\n");
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
      process.stdout.write(json ? `${JSON.stringify({ id, outcome })}\n` : `${formatJobId(id)} ${outcome}\n`);
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
