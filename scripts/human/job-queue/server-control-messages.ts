// The handlers of the messages that act on the queue rather than on the client's own job: status, cancel, move,
// set-weight, start, pause, resume, await (wait) and stop.

import type * as net from "node:net";
import { idField, numberField, send, type Incoming, type ServerMessage } from "./protocol.ts";
import { formatJobId, pickSuffixMatch } from "./job-id.ts";
import type { Connection, MessageHandler } from "./server-connection.ts";
import { describeResource } from "./resources.ts";
import { describeJob, type ServerJob } from "./server-job.ts";
import type { JobServer } from "./server.ts";

function status(connection: Connection): void {
  const server = connection.server;
  const scheduler = server.scheduler;
  scheduler.order();
  const forecast = scheduler.forecast();
  const view = (j: ServerJob) => ({ ...describeJob(j), ...server.outlook(j, forecast) });
  connection.reply({
    type: "status",
    pid: process.pid,
    now: Date.now(),
    policy: scheduler.policy,
    capacity: scheduler.capacity,
    used: scheduler.used,
    memoryCapacity: scheduler.memoryCapacity,
    memoryUsed: scheduler.memoryUsed,
    paused: scheduler.paused,
    ...(scheduler.paused ? { pausedAt: scheduler.pausedAt } : {}),
    resources: scheduler.resourceStates().map((state) => describeResource(state, scheduler.running)),
    running: scheduler.running.map(view),
    waiting: scheduler.waiting.map(view),
  });
}

/** resolve: the full id behind the last digits of a job id (see pickSuffixMatch), for the commands that take an id. */
function resolve(connection: Connection, message: Incoming): void {
  const server = connection.server;
  const digits = message.digits;
  if (typeof digits !== "string" || !/^\d{1,4}$/.test(digits)) {
    connection.reply({ type: "error", code: "bad-digits", message: "resolve needs one to four digits" });
    return;
  }
  // Every record counts as live, a provisionally lost one too (its client may still report how it ended, and wait
  // keeps waiting for it); the outcomes are the jobs that ended for good
  const live = [...server.records.values()].map((job) => ({
    id: job.id,
    state: job.provisional !== null ? "lost unless its client reports" : job.phase,
  }));
  const picked = pickSuffixMatch(digits, live, server.outcomes.keys());
  connection.reply(
    "error" in picked
      ? { type: "error", code: "unknown-job", message: picked.error }
      : { type: "resolved", id: picked.id },
  );
}

function cancel(connection: Connection, message: Incoming): void {
  const scheduler = connection.server.scheduler;
  const all = scheduler.running.concat(scheduler.waiting);
  const id = idField(message, "id");
  const targets = message.all === true ? all : all.filter((j) => j.id === id);
  void connection.server.cancel(targets).then((cancelled) => connection.reply({ type: "cancel-result", cancelled }));
}

/** move: a manual place for a waiting job; tells its client, so that it brings the place when it re-registers. */
function placeJob(server: JobServer, message: Incoming): ServerMessage {
  const scheduler = server.scheduler;
  const id = idField(message, "id") ?? 0;
  const job = scheduler.waiting.find((other) => other.id === id);
  if (job === undefined) {
    return { type: "error", code: "not-waiting", message: `no waiting job ${formatJobId(id)}` };
  }
  if (message.first === true) {
    scheduler.pinFirst(job);
  } else if (message.reset === true) {
    scheduler.resetPlace(job);
  } else {
    const targetId = idField(message, "before") ?? 0;
    const target = scheduler.waiting.find((other) => other.id === targetId);
    const refusal =
      target === undefined ? `no waiting job ${formatJobId(targetId)}` : scheduler.placeBefore(job, target);
    if (refusal !== null) {
      return { type: "error", code: "not-placed", message: refusal };
    }
  }
  server.log(`move ${formatJobId(job.id)}: pin ${job.pin}, before ${job.before}`);
  return server.placed(job);
}

function move(connection: Connection, message: Incoming): void {
  connection.reply(placeJob(connection.server, message));
}

function reweigh(server: JobServer, message: Incoming): ServerMessage {
  const id = idField(message, "id") ?? 0;
  const job = server.scheduler.waiting.find((other) => other.id === id);
  if (job === undefined) {
    return {
      type: "error",
      code: "not-waiting",
      message: `no waiting job ${formatJobId(id)} (a running job keeps its weight)`,
    };
  }
  if (job.gpuTurn) {
    return {
      type: "error",
      code: "not-placed",
      message: `${formatJobId(id)} waits for the GPU and takes no slots (its command registers its own)`,
    };
  }
  job.weight = server.scheduler.clampWeight(numberField(message, "weight", 1));
  server.log(`weight ${formatJobId(job.id)}: ${job.weight}`);
  return server.placed(job);
}

function setWeight(connection: Connection, message: Incoming): void {
  connection.reply(reweigh(connection.server, message));
}

/**
 * start: grants a waiting job at once, over the pools; refused for a GPU turn, for a job whose client is away (nobody
 * would receive the grant) and before its predecessor ended.
 */
function startNow(server: JobServer, message: Incoming): ServerMessage {
  const id = idField(message, "id") ?? 0;
  const job = server.find(id);
  if (job === undefined || job.phase !== "waiting") {
    return { type: "error", code: "not-waiting", message: `no waiting job ${formatJobId(id)}` };
  }
  if (job.gpuTurn) {
    return {
      type: "error",
      code: "not-started",
      message: `${formatJobId(id)} waits for the GPU, which runs one job at a time; move ${formatJobId(id)} --first puts it next`,
    };
  }
  if (job.detached) {
    return {
      type: "error",
      code: "not-started",
      message:
        `the client of ${formatJobId(id)} is away (its connection dropped or the scheduler restarted): nobody would receive ` +
        "the grant; start it once its client is back",
    };
  }
  if (server.scheduler.awaitsPredecessor(job)) {
    return {
      type: "error",
      code: "not-started",
      message: `${formatJobId(id)} waits for ${formatJobId(job.after ?? 0)} to end`,
    };
  }
  job.startNow = true;
  server.log(`start ${formatJobId(job.id)} over the pools`);
  // Granted by the settle that follows every message (Connection), like a move
  return { type: "start-result", id: job.id };
}

function start(connection: Connection, message: Incoming): void {
  connection.reply(startNow(connection.server, message));
}

/**
 * pause and resume: the one flag the decision step reads (Scheduler.grant); the settle that follows every message
 * grants (resume) or keeps back (pause) and saves the state, so the pause survives a restart.
 */
function setPaused(server: JobServer, paused: boolean): ServerMessage {
  const scheduler = server.scheduler;
  const changed = scheduler.paused !== paused;
  if (changed) {
    scheduler.paused = paused;
    scheduler.pausedAt = paused ? Date.now() : 0;
    server.log(paused ? "pause: nothing new is granted" : "resume");
    if (paused) {
      // Each waiting client hears of it once; a client queued later is told in its "queued"
      scheduler.waiting.forEach((job) => server.tell(job, { type: "paused", at: scheduler.pausedAt }));
    }
  }
  return {
    type: "pause-result",
    paused,
    changed,
    running: scheduler.running.length,
    waiting: scheduler.waiting.length,
    pausedAt: scheduler.pausedAt,
  };
}

function pause(connection: Connection): void {
  connection.reply(setPaused(connection.server, true));
}

function resume(connection: Connection): void {
  connection.reply(setPaused(connection.server, false));
}

function awaitEnd(connection: Connection, message: Incoming): void {
  const id = idField(message, "id");
  if (id === null) {
    connection.reply({ type: "error", code: "unknown-job", message: "await needs a job id" });
  } else {
    connection.server.awaitJob(connection.socket, id);
  }
}

function stop(connection: Connection): void {
  const { server, socket } = connection;
  const scheduler = server.scheduler;
  server.log("stop requested");
  server.stopping = true;
  server.probes.stopAll();
  send(socket, { type: "stopping", running: scheduler.running.length, waiting: scheduler.waiting.length });
  // Deliver what was sent to the clients (a cancel just before the stop) before leaving: end every connection and exit
  // when the clients have closed their side too, or after a second. Exiting once our data is only flushed is not
  // enough: on Windows a process that exits with unread data from a client (a "started" sent right after a grant)
  // resets the connection, and the client can lose the messages it has not read yet.
  server.server.close();
  const open = scheduler.running
    .concat(scheduler.waiting)
    .map((j) => j.connection?.socket ?? null)
    .filter((s): s is net.Socket => s !== null);
  for (const sockets of server.awaiters.values()) {
    open.push(...sockets);
  }
  let pending = open.length + 1;
  const closed = (): void => {
    pending--;
    if (pending === 0) {
      process.exit(0);
    }
  };
  setTimeout(() => process.exit(0), 1000).unref();
  for (const s of [socket, ...open]) {
    if (s.destroyed) {
      closed();
      continue;
    }
    s.once("close", closed);
    s.end();
  }
}

export const ControlMessages: Readonly<Record<string, MessageHandler>> = {
  status,
  resolve,
  cancel,
  move,
  "set-weight": setWeight,
  start,
  pause,
  resume,
  await: awaitEnd,
  stop,
};
