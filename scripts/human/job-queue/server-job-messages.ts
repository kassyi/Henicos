// The handlers of the messages about a client's own job: request and adopt (register it), started, done, and report
// (the end of a job whose command ended while no scheduler listened).

import { ProtocolVersion } from "./constants.ts";
import { idField, numberField, send, type Incoming, type Outcome } from "./protocol.ts";
import type { Connection, MessageHandler } from "./server-connection.ts";
import type { ServerJob } from "./server-job.ts";

/** request: a new job, or a waiting job that queues again after a restart under its id. */
function request(connection: Connection, message: Incoming): void {
  const { server, socket } = connection;
  const scheduler = server.scheduler;
  if (connection.job !== null) {
    return;
  }
  const created = server.createJob(socket, { ...message, grantedAt: 0 }, server.chooseId(idField(message, "id")));
  const after = created.after;
  // A predecessor was queued before this job, so its id is lower: a later or the same id would wait for ever
  // (on itself, or in a cycle of two), also during the startup grace, when the predecessor is not checked yet
  if (after !== null && after >= created.id) {
    connection.reply({
      type: "error",
      code: "unknown-job",
      message: `--after: #${after} was not queued before #${created.id}`,
    });
    return;
  }
  const unknown = after !== null && scheduler.find(after) === undefined && !server.expectedUntil.has(after);
  // An id it never knew is a usage error; a predecessor that already ended is handled like any other: the job is
  // queued, and settle grants or cancels it on the recorded outcome
  if (after !== null && unknown && Date.now() >= scheduler.holdUntil && !server.outcomes.has(after)) {
    connection.reply({
      type: "error",
      code: "unknown-job",
      message: `--after: no job #${after} (never queued, or ended too long ago)`,
    });
    return;
  }
  server.forget(created.id);
  server.outcomes.delete(created.id); // the job is here again: an older record of its id no longer holds
  const job = created;
  connection.job = job;
  server.log(
    `queue #${job.id} weight ${job.weight}${job.gpu ? " gpu" : ""}` +
      `${after !== null ? ` after #${after}` : ""} pid ${job.pid}: ${job.command}`,
  );
  scheduler.add(job);
  // The id first, so that the caller can ask about its job (status <id>) whatever happens next; settle grants
  send(socket, { type: "queued", id: job.id, ...server.outlook(job, scheduler.forecast()) });
}

/** adopt: a running job re-registers after its connection dropped or the scheduler restarted. */
function adopt(connection: Connection, message: Incoming): void {
  const { server, socket } = connection;
  const scheduler = server.scheduler;
  if (connection.job !== null) {
    return;
  }
  // The same client lost only its connection: the slot the scheduler kept for it is this job, under the same id.
  // Adopt first, so that removing the kept job hands no slot to a waiting job. The old connection may not have closed
  // yet (its close can arrive after this adopt): match by client pid whatever the socket state, and mark the old job
  // so that its later close does nothing.
  const pid = numberField(message, "pid");
  const kept = scheduler.running.filter((other) => other.pid === pid);
  const keptId = kept.length > 0 ? (kept[0] as ServerJob).id : null;
  // A re-registering job started its command before this scheduler: its run time counts from that start
  const job = server.createJob(
    socket,
    { ...message, grantedAt: numberField(message, "spawnedAt") },
    keptId ?? server.chooseId(idField(message, "id")),
  );
  connection.job = job;
  scheduler.adopt(job);
  server.outcomes.delete(job.id); // the job runs: an older record of its id no longer holds
  server.forget(job.id);
  kept.forEach((other) => {
    other.cancelled = true;
    server.release(other, `re-registered as #${job.id}`, null);
  });
  server.log(`adopt #${job.id} weight ${job.weight} pid ${job.pid} command ${job.commandPid}: ${job.command}`);
  send(socket, { type: "adopted", id: job.id, protocol: ProtocolVersion });
  server.idleSince = Date.now();
}

/** started: the client started the command (its process, for the slot of a lost client). */
function started(connection: Connection, message: Incoming): void {
  const job = connection.job;
  if (job !== null) {
    job.commandPid = numberField(message, "commandPid");
    job.spawnedAt = numberField(message, "spawnedAt") || Date.now();
  }
}

/** done: the command ended; the slot is freed, the outcome recorded, and the receipt given. */
function done(connection: Connection, message: Incoming): void {
  const { server, socket } = connection;
  const job = connection.job;
  if (job === null) {
    return;
  }
  job.done = true;
  job.completed = message.completed === true;
  // Protocol 2 clients send no exit code: their outcome is unknown ('lost')
  job.exitCode = typeof message.exitCode === "number" ? message.exitCode : null;
  if (!server.stopping && server.scheduler.running.includes(job)) {
    // The command has ended: free the slot and record the outcome now (settle saves it at the end of this message),
    // then give the receipt. A stopping scheduler saves nothing and gives none: the client then reports the outcome to
    // the next one.
    const ended = job;
    connection.job = null;
    // Cancelled: the client says so (also to a scheduler that restarted meanwhile and has no cancelPending), or this
    // scheduler cancelled it and the command did not succeed first (a success that crosses the cancel wins)
    const succeeded = ended.completed && ended.exitCode === 0;
    const cancelled = message.cancelled === true || (ended.cancelPending && !succeeded);
    const outcome: Outcome = cancelled ? "cancelled" : server.closedOutcome(ended);
    ended.cancelled = outcome === "cancelled"; // a cancel the client carried out: no run time
    server.release(ended, "done", outcome);
    send(socket, { type: "done-ok", id: ended.id });
  }
}

/**
 * report: the end of a job whose command ended while no scheduler listened (its client could not send "done"): its
 * outcome, for the jobs that wait for it. Only for a job that is neither held nor already ended. A job held for a lost
 * client (its connection dropped, the scheduler stayed) ends with the report too.
 */
function report(connection: Connection, message: Incoming): void {
  const server = connection.server;
  const scheduler = server.scheduler;
  const id = idField(message, "id");
  const held = id === null ? undefined : scheduler.find(id);
  const outcome: Outcome =
    message.cancelled === true
      ? "cancelled"
      : message.completed === true && message.exitCode === 0
        ? "succeeded"
        : "failed";
  if (id !== null && held === undefined && !server.outcomes.has(id)) {
    server.log(`report #${id}: ${outcome}`);
    server.recordOutcome(id, outcome);
  } else if (held !== undefined && held.socket === null && scheduler.running.includes(held)) {
    server.release(held, `its lost client reported: ${outcome}`, outcome);
  } else if (held !== undefined && scheduler.running.includes(held) && !held.done) {
    // Its old connection has not closed here yet (the client saw the close first): end the job with this outcome when
    // it does
    held.done = true;
    held.completed = message.completed === true;
    held.exitCode = typeof message.exitCode === "number" ? message.exitCode : null;
    held.cancelPending = held.cancelPending || message.cancelled === true;
  }
  connection.reply({ type: "reported", id: id ?? 0 });
}

export const JobMessages: Readonly<Record<string, MessageHandler>> = { request, adopt, started, done, report };
