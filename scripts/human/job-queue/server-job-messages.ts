// The handlers of the messages about a client's own job: request and adopt (register it), started, done, and report
// (the end of a job whose command ended while no scheduler listened).

import { ProtocolVersion } from "./constants.ts";
import { idField, numberField, send, type Incoming } from "./protocol.ts";
import type { Connection, MessageHandler } from "./server-connection.ts";
import type { ServerJob } from "./server-job.ts";
import { outcomeOfEnd, planRegistration, type ClientIdentity, type CommandEnd } from "./server-job-state.ts";

/** How the command ended, as "done" and "report" say it. */
function endOf(message: Incoming): CommandEnd {
  return {
    completed: message.completed === true,
    // Protocol 2 clients send no exit code: their outcome is unknown ('lost')
    exitCode: typeof message.exitCode === "number" ? message.exitCode : null,
    cancelled: message.cancelled === true,
  };
}

/** What a request or adopt says about the client that sends it. */
function identityOf(message: Incoming): ClientIdentity {
  return {
    pid: numberField(message, "pid"),
    queuedAt: numberField(message, "queuedAt"),
    commandPid: numberField(message, "commandPid"),
    spawnedAt: numberField(message, "spawnedAt"),
  };
}

/**
 * Plans a request or adopt against the record under the id the client brings, and applies what the plan does not
 * leave to the caller: a refusal, a re-bind, or the end of an old record. Returns the id to create a record under, or
 * null when the message is answered.
 */
function register(connection: Connection, message: Incoming, type: "request" | "adopt"): number | null {
  const server = connection.server;
  const wanted = idField(message, "id");
  const record = wanted === null ? undefined : server.find(wanted);
  const final = wanted === null ? undefined : server.outcomes.get(wanted);
  const plan = planRegistration(type, record, final, identityOf(message));
  switch (plan.kind) {
    case "refuse-cancelled":
      server.log(`${type} #${String(wanted)}: cancelled while its client was away`);
      connection.reply({ type: "cancelled", id: wanted ?? 0, reason: "cancelled while its client was away" });
      return null;
    case "rebind":
      if (record !== undefined) {
        rebind(connection, record, message, plan);
      }
      return null;
    case "create-as-new-id":
      if (plan.finalize && record !== undefined) {
        server.finish(record, "lost", `its client queued again as a new job`);
      }
      return server.nextId++;
    case "create":
      if (record !== undefined && record.stub) {
        // The job of an earlier scheduler is back: its record replaces the id-only one
        server.scheduler.detach(record);
        server.records.delete(record.id);
      }
      return server.chooseId(wanted);
  }
}

/** The client's own record gets the new connection; its weight, place and dependency stay the server's. */
function rebind(
  connection: Connection,
  job: ServerJob,
  message: Incoming,
  plan: { to: "waiting" | "running"; resendGrant: boolean; resendCancel: boolean },
): void {
  const { server, socket } = connection;
  const scheduler = server.scheduler;
  server.bind(job, connection);
  server.forget(job.id);
  if (job.commandPid === 0 && numberField(message, "commandPid") > 0) {
    job.commandPid = numberField(message, "commandPid");
    job.spawnedAt = numberField(message, "spawnedAt");
  }
  if (plan.to === "running" && job.phase !== "running") {
    // Its command runs: the grant was not saved before a crash, or a provisional end is taken back
    scheduler.detach(job);
    job.provisional = null;
    job.grantedAt = job.spawnedAt;
    job.phase = "running";
    scheduler.adopt(job);
  }
  server.log(`re-bind #${job.id} (${job.phase}) pid ${job.pid}: ${job.command}`);
  if (job.phase === "waiting") {
    job.lastPosition = -1;
    send(socket, { type: "queued", id: job.id, ...server.outlook(job, scheduler.forecast()) });
  } else if (plan.resendGrant) {
    send(socket, server.grantMessage(job));
  } else {
    send(socket, { type: "adopted", id: job.id, protocol: ProtocolVersion });
  }
  if (plan.resendCancel) {
    send(socket, { type: "cancelled", id: job.id, reason: "cancel requested" });
  }
  server.idleSince = Date.now();
}

/** request: a new job, or a waiting job that queues again (after a restart, or a dropped connection) under its id. */
function request(connection: Connection, message: Incoming): void {
  const { server, socket } = connection;
  const scheduler = server.scheduler;
  if (connection.job !== null) {
    return;
  }
  const id = register(connection, message, "request");
  if (id === null) {
    return;
  }
  const created = server.createJob({ ...message, grantedAt: 0 }, id, "waiting");
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
  const unknown = after !== null && server.find(after) === undefined && !server.expectedUntil.has(after);
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
  const job = created;
  server.records.set(job.id, job);
  server.bind(job, connection);
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
  if (connection.job !== null) {
    return;
  }
  // Its own record (held while the client was away) is re-bound under its id; a record of another client with the
  // same pid is never touched
  const id = register(connection, message, "adopt");
  if (id === null) {
    return;
  }
  // A re-registering job started its command before this scheduler: its run time counts from that start
  const job = server.createJob({ ...message, grantedAt: numberField(message, "spawnedAt") }, id, "running");
  server.forget(job.id);
  server.records.set(job.id, job);
  server.bind(job, connection);
  server.scheduler.adopt(job);
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
    const outcome = outcomeOfEnd(endOf(message), ended.cancelPending);
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
  const held = id === null ? undefined : server.find(id);
  const outcome = outcomeOfEnd(endOf(message), false);
  if (id !== null && held === undefined && !server.outcomes.has(id)) {
    server.log(`report #${id}: ${outcome}`);
    server.recordOutcome(id, outcome);
  } else if (held !== undefined && held.connection === null && scheduler.running.includes(held)) {
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
