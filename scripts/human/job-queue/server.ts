// The scheduler process: listens on 127.0.0.1, keeps the jobs, grants slots through Scheduler, records run times and
// outcomes, and keeps the slot of a lost client until its command has exited.

import * as fs from "node:fs";
import * as net from "node:net";
import * as os from "node:os";
import * as path from "node:path";
import {
  AcceptedProtocolVersions,
  ExpectedJobsMs,
  ForecastNoticeMs,
  IdleExitMs,
  KeptOutcomes,
  OrphanPollMs,
  ProtocolVersion,
  RegrantMs,
  StartupGraceMs,
  type Policy,
} from "./constants.ts";
import { RunHistory, errorCode, errorMessage, isObject, replaceFile } from "./history.ts";
import {
  idField,
  numberField,
  onMessages,
  optionalNumberField,
  send,
  type Incoming,
  type JobView,
  type Outcome,
  type Outlook,
  type ServerMessage,
} from "./protocol.ts";
import { Scheduler, type Expected, type SchedulerJob } from "./scheduler.ts";
import {
  isProcessAlive,
  isProcessGroupAlive,
  killProcessTree,
  queryWindowsProcesses,
  resolveStateDirectory,
  windowsCommandProcesses,
} from "./system.ts";

export interface ServeOptions {
  slots: number;
  policy: Policy;
  port: number;
  /** '' for the default (resolveStateDirectory); a scheduler started through WMI gets it on its command line. */
  stateDir: string;
}

/** A job as the server keeps it: the scheduler's view plus the connection and the command. */
interface ServerJob extends SchedulerJob {
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

const Outcomes: readonly string[] = ["succeeded", "failed", "cancelled", "lost"];

function describeJob(job: ServerJob): JobView {
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
  };
}

export function serve(options: ServeOptions): void {
  // nothing is granted before listening (see listen)
  const scheduler = new Scheduler<ServerJob>(options.slots, Infinity, options.policy);
  // Several schedulers may write to one log for a moment (one stopping, the next starting): tag each line
  const log = (text: string): void => {
    process.stdout.write(`${new Date().toISOString()} [${process.pid}] ${text}\n`);
  };
  const stateDirectory = options.stateDir || resolveStateDirectory();
  const history = new RunHistory(path.join(stateDirectory, "history.json"), log);
  let nextId = history.nextId;
  let idleSince = Date.now();
  /** How the jobs that ended went, oldest first, for --after (also across a restart, through jobs-<port>.json). */
  const outcomes = new Map<number, Outcome>();
  // The scheduler grants a job --after another on this record alone
  scheduler.outcomes = outcomes;
  /** The connections waiting for a job to end (await, wait). */
  const awaiters = new Map<number, net.Socket[]>();
  /**
   * The jobs that are not here but have not ended (an earlier scheduler's, or a waiting job whose connection dropped),
   * until when they are waited for. Their dependents are blocked anyway (no outcome); this only bounds the wait: an id
   * that does not come back in time is recorded as lost.
   */
  const expectedUntil = new Map<number, number>();
  const expect = (id: number, until: number): void => {
    expectedUntil.set(id, until);
  };
  const forget = (id: number): void => {
    expectedUntil.delete(id);
  };

  /** A job's place and expected times: ahead = waiting jobs before it (0 once it runs). */
  const outlook = (job: ServerJob, forecast: Map<ServerJob, Expected>): Outlook => {
    const expected = forecast.get(job);
    const index = scheduler.waiting.indexOf(job);
    return {
      state: index >= 0 ? "waiting" : "running",
      ahead: Math.max(0, index),
      position: index + 1,
      startAt: expected ? expected.startAt : null,
      endAt: expected ? expected.endAt : null,
      blockedBy: scheduler.isBlocked(job) ? job.after : null,
    };
  };
  // Tells each waiting client its place and expected times when the place changed or the start moved by a minute
  const notifyWaiting = (): void => {
    if (scheduler.waiting.length === 0) {
      return;
    }
    const forecast = scheduler.forecast();
    scheduler.waiting.forEach((job, index) => {
      const view = outlook(job, forecast);
      const moved = Math.abs((view.startAt ?? 0) - (job.lastStartAt ?? 0)) >= ForecastNoticeMs;
      if (job.lastPosition !== index || moved) {
        job.lastPosition = index;
        job.lastStartAt = view.startAt;
        send(job.socket, { type: "waiting", id: job.id, ...view, used: scheduler.used, capacity: scheduler.capacity });
      }
    });
  };
  const cpus = (job: ServerJob): number =>
    Math.max(1, Math.floor((os.cpus().length / scheduler.capacity) * job.weight));
  const announce = (granted: ServerJob[]): void => {
    for (const job of granted) {
      const flags = `${job.gpu ? " gpu" : ""}${job.gpuTurn ? " gpu-turn" : ""}${job.backfilled ? " backfilled" : ""}`;
      log(`grant #${job.id} weight ${job.weight}${flags} pid ${job.pid}: ${job.command}`);
      send(job.socket, {
        type: "granted",
        id: job.id,
        cpus: cpus(job),
        capacity: scheduler.capacity,
        protocol: ProtocolVersion,
      });
    }
    notifyWaiting();
    if (!scheduler.isIdle()) {
      idleSince = Date.now();
    }
  };
  // Job ids stay the same across a scheduler restart, so that `cancel <id>` read from an earlier `status` never hits
  // another job: a re-registering client brings its id, and the next new id continues from the saved one.
  const chooseId = (wanted: number | null): number => {
    if (wanted !== null && scheduler.find(wanted) === undefined) {
      nextId = Math.max(nextId, wanted + 1);
      return wanted;
    }
    return nextId++;
  };
  const createJob = (socket: net.Socket | null, message: Incoming, id: number): ServerJob => {
    const command = String(message.command ?? "");
    return {
      id,
      socket,
      weight: numberField(message, "weight", 1) || 1,
      gpu: message.gpu === true,
      pid: numberField(message, "pid"),
      commandPid: numberField(message, "commandPid"),
      spawnedAt: numberField(message, "spawnedAt"),
      cwd: String(message.cwd ?? ""),
      command,
      queuedAt: numberField(message, "queuedAt") || Date.now(),
      grantedAt: numberField(message, "grantedAt"),
      estimate: history.estimate(command),
      pin: optionalNumberField(message, "pin"),
      before: idField(message, "before"),
      after: idField(message, "after"),
      keepIfFailed: message.keepIfFailed === true,
      gpuTurn: message.gpuTurn === true,
      lastPosition: -1,
      lastStartAt: null,
      done: false,
      completed: false,
      exitCode: null,
      cancelled: false,
      cancelPending: false,
    };
  };

  // The running jobs with a started command, so that a restarted scheduler still counts the ones whose client is gone
  // (nobody re-registers them), the next id, and the outcomes for --after. Jobs whose client lives re-register.
  const jobsPath = path.join(stateDirectory, `jobs-${options.port}.json`);
  const persist = (): void => {
    if (stopping) {
      return; // the next scheduler may already have read the file: a late write would overwrite what it saved
    }
    const records = scheduler.running
      .filter((job) => job.commandPid > 0)
      .map((job) => ({ ...describeJob(job), spawnedAt: job.spawnedAt }));
    const temporary = `${jobsPath}.${process.pid}.tmp`;
    try {
      fs.mkdirSync(path.dirname(jobsPath), { recursive: true });
      // The jobs that have not ended, so that a restarted scheduler waits for them as predecessors (--after) until they
      // come back: the waiting ones, the running ones, and those still expected from an earlier restart
      const pending = [
        ...new Set([...scheduler.waiting, ...scheduler.running].map((job) => job.id).concat([...expectedUntil.keys()])),
      ];
      // Written aside and renamed over, so that a reader or a crash never sees half a file
      fs.writeFileSync(temporary, JSON.stringify({ nextId, jobs: records, ended: [...outcomes], pending }));
      replaceFile(temporary, jobsPath);
    } catch (error) {
      log(`could not write ${jobsPath}: ${errorMessage(error)}`);
      fs.rmSync(temporary, { force: true });
    }
  };
  const restore = (): void => {
    let saved: unknown = null;
    try {
      saved = JSON.parse(fs.readFileSync(jobsPath, "utf8"));
    } catch (error) {
      if (errorCode(error) !== "ENOENT") {
        log(`could not read ${jobsPath}: ${errorMessage(error)}`);
      }
      return;
    }
    if (!isObject(saved)) {
      return;
    }
    if (typeof saved.nextId === "number" && Number.isInteger(saved.nextId)) {
      nextId = Math.max(nextId, saved.nextId);
    }
    if (Array.isArray(saved.ended)) {
      for (const entry of saved.ended as unknown[]) {
        if (Array.isArray(entry) && Number.isInteger(entry[0]) && Outcomes.includes(String(entry[1]))) {
          outcomes.set(entry[0] as number, entry[1] as Outcome);
        }
      }
    }
    if (Array.isArray(saved.pending)) {
      for (const id of saved.pending as unknown[]) {
        if (typeof id === "number" && Number.isInteger(id) && id > 0 && !outcomes.has(id)) {
          expect(id, Date.now() + ExpectedJobsMs);
        }
      }
    }
    const records = Array.isArray(saved.jobs) ? (saved.jobs as unknown[]) : [];
    for (const record of records) {
      if (!isObject(record) || isProcessAlive(numberField(record, "pid"))) {
        continue; // its client re-registers it
      }
      const job = createJob(null, record, chooseId(idField(record, "id")));
      scheduler.adopt(job);
      forget(job.id);
      log(`restored #${job.id} weight ${job.weight} (client gone, command ${job.commandPid}): ${job.command}`);
    }
    // No persist here: the records of live clients stay in the file until they re-register (the next persist), so
    // that a crash in between does not lose them.
  };

  /**
   * Records how a job ended and tells the connections that wait for it. The jobs --after it follow in settle: they are
   * granted or cancelled on this record, never while an event is still being handled.
   */
  const recordOutcome = (id: number, outcome: Outcome): void => {
    outcomes.delete(id);
    outcomes.set(id, outcome);
    while (outcomes.size > KeptOutcomes) {
      outcomes.delete(outcomes.keys().next().value as number);
    }
    for (const socket of awaiters.get(id) ?? []) {
      send(socket, { type: "ended", id, outcome });
      socket.end();
    }
    awaiters.delete(id);
    forget(id);
  };

  /**
   * Ends a job: records its outcome (null when the same job lives on, re-registered) and frees its slots. It grants
   * nothing: settle does, once per event.
   */
  const release = (job: ServerJob, why: string, outcome: Outcome | null): void => {
    log(`end #${job.id} (${why})`);
    // Only a run seen from its grant to its end: the command ended by itself (not stopped by a signal or a cancel), and
    // the job was not lost or adopted after a restart. Not a GPU turn: its time includes the wait for the GPU lock, and
    // the
    // command under the lock records its own run under the same command text.
    if (
      (why === "done" || why === "client closed") &&
      !job.gpuTurn &&
      job.completed &&
      !job.cancelled &&
      !job.adopted &&
      job.grantedAt > 0
    ) {
      history.record(job.command, Date.now() - job.grantedAt, nextId);
    }
    if (outcome !== null) {
      recordOutcome(job.id, outcome);
    }
    scheduler.detach(job);
    if (scheduler.isIdle()) {
      idleSince = Date.now();
    }
  };

  /**
   * The end of every event (a message, a closed connection, a timer): cancels the waiting jobs whose predecessor did
   * not
   * succeed (along chains), records an unknown predecessor as lost, then grants once and saves the state. Nothing
   * grants
   * while an event changes the jobs, so that no job starts on a state that is half updated.
   */
  const settle = (): void => {
    if (stopping) {
      return;
    }
    for (let changed = true; changed; ) {
      changed = false;
      for (const job of [...scheduler.waiting]) {
        if (job.after === null || job.cancelled || !scheduler.waiting.includes(job)) {
          continue;
        }
        const after = job.after;
        if (
          !outcomes.has(after) &&
          Date.now() >= scheduler.holdUntil &&
          scheduler.find(after) === undefined &&
          !expectedUntil.has(after)
        ) {
          // Not here, not ended, not expected back (a state file that was lost, an outcome dropped as too old)
          log(`#${after} is unknown; counting it as lost for #${job.id}`);
          recordOutcome(after, "lost");
        }
        const outcome = outcomes.get(after);
        if (outcome !== undefined && outcome !== "succeeded" && !job.keepIfFailed) {
          cancelJob(job, `#${after} ${outcome}`);
          changed = true;
        }
      }
    }
    announce(scheduler.grant());
    persist();
  };

  /** How a job whose client closed went: by its exit code when it ran, cancelled when it never started. */
  const closedOutcome = (job: ServerJob): Outcome => {
    if (scheduler.waiting.includes(job)) {
      return "cancelled";
    }
    if (!job.done || job.exitCode === null) {
      return "lost";
    }
    return job.completed && job.exitCode === 0 ? "succeeded" : "failed";
  };

  /** Cancels one job: a waiting job leaves at once; a running one keeps its slot until its client has stopped it. */
  const cancelJob = (job: ServerJob, reason: string): void => {
    log(`cancel #${job.id} (${reason}): ${job.command}`);
    send(job.socket, { type: "cancelled", id: job.id, reason });
    if (scheduler.running.includes(job) && job.socket !== null && !job.socket.destroyed) {
      // The client stops its command and then closes: keep the slot until then, so that the next job does not start
      // beside a command that is still being stopped
      job.cancelPending = true;
      return;
    }
    job.cancelled = true;
    if (scheduler.running.includes(job) || scheduler.waiting.includes(job)) {
      release(job, "cancelled", "cancelled");
    }
  };

  /** The live processes of each orphan's command (Unix: its process group id), or null per job when unsure. */
  const orphanProcesses = async (orphans: ServerJob[]): Promise<(number[] | null)[]> => {
    if (orphans.length === 0) {
      return [];
    }
    const processes = process.platform === "win32" ? await queryWindowsProcesses() : null;
    return orphans.map((job) => {
      if (job.commandPid <= 0) {
        return [];
      }
      if (process.platform !== "win32") {
        return isProcessGroupAlive(job.commandPid) ? [job.commandPid] : [];
      }
      return windowsCommandProcesses(processes, job.commandPid, job.spawnedAt);
    });
  };

  // A running job whose client is gone keeps its slot until its command has exited.
  let polling = false;
  /** Set by stop: the connections that close from then on were ended by the stop itself. */
  let stopping = false;
  setInterval(async () => {
    const orphans = scheduler.running.filter((job) => job.socket === null);
    if (polling || orphans.length === 0) {
      return;
    }
    polling = true;
    try {
      const live = await orphanProcesses(orphans);
      orphans.forEach((job, index) => {
        const processes = live[index];
        if (
          processes !== null &&
          processes !== undefined &&
          processes.length === 0 &&
          scheduler.running.includes(job)
        ) {
          release(job, "the command of a lost client exited", "lost");
        }
      });
      settle();
    } finally {
      polling = false;
    }
  }, OrphanPollMs).unref();

  /** Cancels jobs and resolves to the ids it cancelled: a job that ended meanwhile (while the processes of lost
   * clients were read) is left out. */
  const cancel = async (requested: ServerJob[]): Promise<number[]> => {
    const orphans = requested.filter((job) => job.socket === null && scheduler.running.includes(job));
    const live = await orphanProcesses(orphans);
    orphans.forEach((_, index) => (live[index] ?? []).forEach(killProcessTree));
    const jobs = requested.filter(
      (job) => !job.cancelled && (scheduler.running.includes(job) || scheduler.waiting.includes(job)),
    );
    jobs.forEach((job) => cancelJob(job, "cancel requested"));
    settle();
    return jobs.map((job) => job.id);
  };

  /** Connections that asked to await a job during the startup grace, when it may not have re-registered yet. */
  const pendingAwaits: { socket: net.Socket; id: number }[] = [];
  const awaitJob = (socket: net.Socket, id: number): void => {
    // The job first: a job that runs or waits has not ended, whatever an older record says
    const outcome = outcomes.get(id);
    if (scheduler.find(id) !== undefined || expectedUntil.has(id)) {
      awaiters.set(id, [...(awaiters.get(id) ?? []), socket]);
    } else if (outcome !== undefined) {
      send(socket, { type: "ended", id, outcome });
      socket.end();
    } else if (Date.now() < scheduler.holdUntil) {
      pendingAwaits.push({ socket, id });
    } else {
      reply(socket, {
        type: "error",
        code: "unknown-job",
        message: `no job #${id} (never queued, or ended too long ago)`,
      });
    }
  };

  const reply = (socket: net.Socket, message: ServerMessage): void => {
    send(socket, message);
    socket.end();
  };

  /** move: a manual place for a waiting job; tells its client, so that it brings the place when it re-registers. */
  const move = (message: Incoming): ServerMessage => {
    const id = idField(message, "id") ?? 0;
    const job = scheduler.waiting.find((other) => other.id === id);
    if (job === undefined) {
      return { type: "error", code: "not-waiting", message: `no waiting job #${id}` };
    }
    if (message.first === true) {
      scheduler.pinFirst(job);
    } else if (message.reset === true) {
      scheduler.resetPlace(job);
    } else {
      const targetId = idField(message, "before") ?? 0;
      const target = scheduler.waiting.find((other) => other.id === targetId);
      const refusal = target === undefined ? `no waiting job #${targetId}` : scheduler.placeBefore(job, target);
      if (refusal !== null) {
        return { type: "error", code: "not-placed", message: refusal };
      }
    }
    log(`move #${job.id}: pin ${job.pin}, before ${job.before}`);
    return placed(job);
  };
  const setWeight = (message: Incoming): ServerMessage => {
    const id = idField(message, "id") ?? 0;
    const job = scheduler.waiting.find((other) => other.id === id);
    if (job === undefined) {
      return { type: "error", code: "not-waiting", message: `no waiting job #${id} (a running job keeps its weight)` };
    }
    if (job.gpuTurn) {
      return {
        type: "error",
        code: "not-placed",
        message: `#${id} waits for the GPU and takes no slots (its command registers its own)`,
      };
    }
    job.weight = scheduler.clampWeight(numberField(message, "weight", 1));
    log(`weight #${job.id}: ${job.weight}`);
    return placed(job);
  };
  /** Tells the client its job's new placement, grants by the new order and answers with the job's place. */
  const placed = (job: ServerJob): ServerMessage => {
    send(job.socket, { type: "updated", id: job.id, weight: job.weight, pin: job.pin, before: job.before });
    settle();
    return { type: "move-result", id: job.id, position: scheduler.waiting.indexOf(job) + 1 };
  };

  const server = net.createServer((socket) => {
    let job: ServerJob | null = null;
    socket.on("error", () => socket.destroy());
    // Every event ends with settle: the grants and the cascades follow the state the event left
    socket.on("close", () => {
      // A connection without a job (status, cancel, wait) changed nothing when it closed
      const hadJob = job !== null;
      closed();
      if (hadJob) {
        settle();
      }
    });
    const closed = (): void => {
      if (job === null || job.cancelled) {
        return;
      }
      if (stopping && !job.done) {
        // The stop ended this connection: the client registers again with the next scheduler, so the job has not ended
        // (no outcome, no cascade to the jobs that wait for it)
        return;
      }
      if (job.cancelPending) {
        // As in "done": a command that succeeded before the cancel reached it (its report said so) stays succeeded
        const cancelled = job;
        job = null;
        const succeeded = cancelled.done && cancelled.completed && cancelled.exitCode === 0;
        cancelled.cancelled = !succeeded;
        release(cancelled, "cancelled", succeeded ? "succeeded" : "cancelled");
        return;
      }
      if (scheduler.waiting.includes(job) && !job.done && isProcessAlive(job.pid)) {
        // Only the connection dropped; the client queues again under the same id. It has not ended: no outcome, and
        // the jobs that wait for it keep waiting until it is back (or counts as lost after ExpectedJobsMs)
        const reconnecting = job;
        job = null;
        reconnecting.socket = null;
        expect(reconnecting.id, Date.now() + ExpectedJobsMs);
        release(reconnecting, "its client reconnects", null);
        return;
      }
      const lost = job;
      job = null;
      lost.socket = null;
      if (scheduler.running.includes(lost) && !lost.done && lost.commandPid > 0) {
        // The poll releases the slot once no process of the command is left.
        log(`client of #${lost.id} is gone; holding its slot while command ${lost.commandPid} runs`);
        return;
      }
      release(lost, "client closed", closedOutcome(lost));
    };
    onMessages(socket, (message) => {
      handle(message);
      if (message.type !== "status") {
        settle();
      }
    });
    const handle = (message: Incoming): void => {
      if (!AcceptedProtocolVersions.includes(Number(message.version))) {
        reply(socket, {
          type: "error",
          code: "version",
          message:
            `protocol ${String(message.version)} does not match the scheduler's ${ProtocolVersion}; ` +
            'run "job-queue.js stop" when no job runs',
        });
        return;
      }
      switch (message.type) {
        case "request":
        case "adopt": {
          if (job !== null) {
            return;
          }
          if (message.type === "adopt") {
            // The same client lost only its connection: the slot the scheduler kept for it is this job, under the same
            // id. Adopt first, so that removing the kept job hands no slot to a waiting job. The old connection may not
            // have closed yet (its close can arrive after this adopt): match by client pid whatever the socket state,
            // and mark the old job so that its later close does nothing.
            const pid = numberField(message, "pid");
            const kept = scheduler.running.filter((other) => other.pid === pid);
            const keptId = kept.length > 0 ? (kept[0] as ServerJob).id : null;
            // A re-registering job started its command before this scheduler: its run time counts from that start
            job = createJob(
              socket,
              { ...message, grantedAt: numberField(message, "spawnedAt") },
              keptId ?? chooseId(idField(message, "id")),
            );
            scheduler.adopt(job);
            outcomes.delete(job.id); // the job runs: an older record of its id no longer holds
            forget(job.id);
            kept.forEach((other) => {
              other.cancelled = true;
              release(other, `re-registered as #${(job as ServerJob).id}`, null);
            });
            log(`adopt #${job.id} weight ${job.weight} pid ${job.pid} command ${job.commandPid}: ${job.command}`);
            send(socket, { type: "adopted", id: job.id, protocol: ProtocolVersion });
            idleSince = Date.now();
            break;
          }
          const created = createJob(socket, { ...message, grantedAt: 0 }, chooseId(idField(message, "id")));
          const after = created.after;
          // A predecessor was queued before this job, so its id is lower: a later or the same id would wait for ever
          // (on itself, or in a cycle of two), also during the startup grace, when the predecessor is not checked yet
          if (after !== null && after >= created.id) {
            reply(socket, {
              type: "error",
              code: "unknown-job",
              message: `--after: #${after} was not queued before #${created.id}`,
            });
            return;
          }
          const unknown = after !== null && scheduler.find(after) === undefined && !expectedUntil.has(after);
          // An id it never knew is a usage error; a predecessor that already ended is handled like any other: the job
          // is
          // queued, and settle grants or cancels it on the recorded outcome
          if (after !== null && unknown && Date.now() >= scheduler.holdUntil && !outcomes.has(after)) {
            reply(socket, {
              type: "error",
              code: "unknown-job",
              message: `--after: no job #${after} (never queued, or ended too long ago)`,
            });
            return;
          }
          forget(created.id);
          outcomes.delete(created.id); // the job is here again: an older record of its id no longer holds
          job = created;
          log(
            `queue #${job.id} weight ${job.weight}${job.gpu ? " gpu" : ""}` +
              `${after !== null ? ` after #${after}` : ""} pid ${job.pid}: ${job.command}`,
          );
          scheduler.add(job);
          // The id first, so that the caller can ask about its job (status <id>) whatever happens next; settle grants
          send(socket, { type: "queued", id: job.id, ...outlook(job, scheduler.forecast()) });
          break;
        }
        case "started":
          if (job !== null) {
            job.commandPid = numberField(message, "commandPid");
            job.spawnedAt = numberField(message, "spawnedAt") || Date.now();
          }
          break;
        case "done":
          if (job !== null) {
            job.done = true;
            job.completed = message.completed === true;
            // Protocol 2 clients send no exit code: their outcome is unknown ('lost')
            job.exitCode = typeof message.exitCode === "number" ? message.exitCode : null;
            if (!stopping && scheduler.running.includes(job)) {
              // The command has ended: free the slot and record the outcome now (settle saves it at the end of this
              // message), then give the receipt. A stopping scheduler saves nothing and gives none: the client then
              // reports the outcome to the next one.
              const ended = job;
              job = null;
              // Cancelled: the client says so (also to a scheduler that restarted meanwhile and has no cancelPending),
              // or
              // this scheduler cancelled it and the command did not succeed first (a success that crosses the cancel
              // wins)
              const succeeded = ended.completed && ended.exitCode === 0;
              const cancelled = message.cancelled === true || (ended.cancelPending && !succeeded);
              const outcome: Outcome = cancelled ? "cancelled" : closedOutcome(ended);
              ended.cancelled = outcome === "cancelled"; // a cancel the client carried out: no run time
              release(ended, "done", outcome);
              send(socket, { type: "done-ok", id: ended.id });
            }
          }
          break;
        case "status": {
          scheduler.order();
          const forecast = scheduler.forecast();
          const view = (j: ServerJob) => ({ ...describeJob(j), ...outlook(j, forecast) });
          reply(socket, {
            type: "status",
            pid: process.pid,
            now: Date.now(),
            policy: scheduler.policy,
            capacity: scheduler.capacity,
            used: scheduler.used,
            running: scheduler.running.map(view),
            waiting: scheduler.waiting.map(view),
          });
          break;
        }
        case "cancel": {
          const all = scheduler.running.concat(scheduler.waiting);
          const id = idField(message, "id");
          const targets = message.all === true ? all : all.filter((j) => j.id === id);
          void cancel(targets).then((cancelled) => reply(socket, { type: "cancel-result", cancelled }));
          break;
        }
        case "move":
          reply(socket, move(message));
          break;
        case "set-weight":
          reply(socket, setWeight(message));
          break;
        case "await": {
          const id = idField(message, "id");
          if (id === null) {
            reply(socket, { type: "error", code: "unknown-job", message: "await needs a job id" });
          } else {
            awaitJob(socket, id);
          }
          break;
        }
        case "report": {
          // The end of a job whose command ended while no scheduler listened (its client could not send "done"): its
          // outcome, for the jobs that wait for it. Only for a job that is neither held nor already ended.
          // A job held for a lost client (its connection dropped, the scheduler stayed) ends with the report too.
          const id = idField(message, "id");
          const held = id === null ? undefined : scheduler.find(id);
          const outcome: Outcome =
            message.cancelled === true
              ? "cancelled"
              : message.completed === true && message.exitCode === 0
                ? "succeeded"
                : "failed";
          if (id !== null && held === undefined && !outcomes.has(id)) {
            log(`report #${id}: ${outcome}`);
            recordOutcome(id, outcome);
          } else if (held !== undefined && held.socket === null && scheduler.running.includes(held)) {
            release(held, `its lost client reported: ${outcome}`, outcome);
          } else if (held !== undefined && scheduler.running.includes(held) && !held.done) {
            // Its old connection has not closed here yet (the client saw the close first): end the job with this
            // outcome when it does
            held.done = true;
            held.completed = message.completed === true;
            held.exitCode = typeof message.exitCode === "number" ? message.exitCode : null;
            held.cancelPending = held.cancelPending || message.cancelled === true;
          }
          reply(socket, { type: "reported", id: id ?? 0 });
          break;
        }
        case "stop": {
          log("stop requested");
          stopping = true;
          send(socket, { type: "stopping", running: scheduler.running.length, waiting: scheduler.waiting.length });
          // Deliver what was sent to the clients (a cancel just before the stop) before leaving: end every connection
          // and exit when the clients have closed their side too, or after a second. Exiting once our data is only
          // flushed is not enough: on Windows a process that exits with unread data from a client (a "started" sent
          // right after a grant) resets the connection, and the client can lose the messages it has not read yet.
          server.close();
          const open = scheduler.running
            .concat(scheduler.waiting)
            .map((j) => j.socket)
            .filter((s): s is net.Socket => s !== null);
          for (const sockets of awaiters.values()) {
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
          break;
        }
        default:
          reply(socket, { type: "error", message: `unknown message type ${String(message.type)}` });
      }
    };
  });

  server.on("error", (error: NodeJS.ErrnoException) => {
    if (error.code === "EADDRINUSE") {
      log(`port ${options.port} is in use; another scheduler runs`);
      process.exit(0);
    }
    log(`server error: ${error.message}`);
    process.exit(1);
  });
  server.listen(options.port, "127.0.0.1", () => {
    log(
      `scheduler pid ${process.pid} on 127.0.0.1:${options.port}, ${scheduler.capacity} slots, ` +
        `${os.cpus().length} cores, policy ${scheduler.policy}`,
    );
    // The grace for re-registration starts once clients can connect; only the scheduler that owns the port restores.
    // It ends in the timer, not at a time: nothing is granted (holdUntil stays Infinity) before settle has run with the
    // re-registered jobs.
    restore();
    setTimeout(() => {
      scheduler.holdUntil = Date.now();
      pendingAwaits.splice(0).forEach(({ socket, id }) => awaitJob(socket, id));
      settle();
    }, StartupGraceMs);
  });
  // The response ratios change with time alone: a waiting job can overtake the first one and fit in the free slots
  // between two events, so grant again now and then while jobs wait
  setInterval(() => {
    // A job that did not come back in time is lost: its dependents follow that outcome
    const now = Date.now();
    for (const [id, until] of [...expectedUntil]) {
      if (until <= now && scheduler.find(id) === undefined) {
        log(`#${id} did not come back`);
        recordOutcome(id, "lost");
      }
    }
    settle();
  }, RegrantMs).unref();
  setInterval(() => {
    if (scheduler.isIdle() && Date.now() - idleSince >= IdleExitMs) {
      log("idle; exiting");
      process.exit(0);
    }
  }, 60 * 1000).unref();
}
