// The scheduler process (JobServer): listens on 127.0.0.1, keeps the jobs, grants slots through Scheduler, records
// run times and outcomes, and keeps the slot of a lost client until its command has exited. The connections and the
// handlers of their messages are in server-connection.ts and server-*-messages.ts, the state file in server-store.ts.

import * as net from "node:net";
import * as os from "node:os";
import * as path from "node:path";
import {
  ForecastNoticeMs,
  IdleExitMs,
  KeptOutcomes,
  OrphanPollMs,
  ProtocolVersion,
  RegrantMs,
  StartupGraceMs,
  type Policy,
} from "./constants.ts";
import { RunHistory } from "./history.ts";
import {
  idField,
  numberField,
  optionalNumberField,
  send,
  type Incoming,
  type Outcome,
  type Outlook,
  type ServerMessage,
} from "./protocol.ts";
import { MachineProfile } from "./machine.ts";
import { Scheduler, type Expected } from "./scheduler.ts";
import { Connection } from "./server-connection.ts";
import type { ServerJob } from "./server-job.ts";
import { planObservation, type Phase } from "./server-job-state.ts";

/** What one observation saw of a detached record, and the epoch of the record it read. */
export interface Observation {
  job: ServerJob;
  epoch: number;
  seen: { clientAlive: boolean; commandAlive: boolean | null };
}
import { JobStore } from "./server-store.ts";
import { ProcessTable, resolveStateDirectory } from "./system.ts";

export interface ServeOptions {
  slots: number;
  /** The memory pool for test runs, in MB. */
  memoryMb: number;
  policy: Policy;
  port: number;
  /** '' for the default (resolveStateDirectory); a scheduler started through WMI gets it on its command line. */
  stateDir: string;
  /** Where the log lines go; stdout (server.log) by default. The self-test's in-process schedulers drop them. */
  log?: (text: string) => void;
}

export function serve(options: ServeOptions): void {
  new JobServer(options).start();
}

/** The scheduler process: the job table, the outcomes, the settle step, the timers and the TCP server. */
export class JobServer {
  readonly options: ServeOptions;
  readonly scheduler: Scheduler<ServerJob>;
  /** The machine the scheduler runs on: the budgets of its checkout give the memory each test run takes. */
  readonly machine: MachineProfile;
  readonly history: RunHistory;
  readonly store: JobStore;
  nextId: number;
  idleSince = Date.now();
  /**
   * Every job record without a final outcome, by id: the one index of the jobs (the scheduler's lists hold the waiting
   * and running ones). An id is here or in `outcomes`, never in both.
   */
  readonly records = new Map<number, ServerJob>();
  /** How the jobs that ended went, oldest first, for --after (also across a restart, through jobs-<port>.json). */
  readonly outcomes = new Map<number, Outcome>();
  /** The connections waiting for a job to end (await, wait). */
  readonly awaiters = new Map<number, net.Socket[]>();
  /**
   * The jobs that are not here but have not ended (an earlier scheduler's, or a waiting job whose connection dropped),
   * until when they are waited for. Their dependents are blocked anyway (no outcome); this only bounds the wait: an id
   * that does not come back in time is recorded as lost.
   */
  readonly expectedUntil = new Map<number, number>();
  /** Connections that asked to await a job during the startup grace, when it may not have re-registered yet. */
  readonly pendingAwaits: { socket: net.Socket; id: number }[] = [];
  /** Set by stop: the connections that close from then on were ended by the stop itself. */
  stopping = false;
  // An observation of the detached records is under way (it reads the process table)
  private polling = false;
  readonly server: net.Server;

  constructor(options: ServeOptions) {
    this.options = options;
    // Several schedulers may write to one log for a moment (one stopping, the next starting): tag each line
    this.log =
      options.log ??
      ((text: string): void => void process.stdout.write(`${new Date().toISOString()} [${process.pid}] ${text}\n`));
    // nothing is granted before listening (see listen)
    this.scheduler = new Scheduler<ServerJob>(options.slots, Infinity, options.policy, options.memoryMb);
    this.machine = new MachineProfile();
    void this.machine.budgets; // read now, as the scheduler starts
    const stateDirectory = options.stateDir || resolveStateDirectory();
    this.history = new RunHistory(path.join(stateDirectory, "history.json"), this.log);
    this.nextId = this.history.nextId;
    // The scheduler grants a job --after another on this record alone
    this.scheduler.outcomes = this.outcomes;
    this.store = new JobStore(this, stateDirectory, options.port);
    this.server = net.createServer((socket) => new Connection(this, socket));
  }

  readonly log: (text: string) => void;

  expect(id: number, until: number): void {
    this.expectedUntil.set(id, until);
  }

  forget(id: number): void {
    this.expectedUntil.delete(id);
  }

  /** The record of a job that has no final outcome yet. */
  find(id: number): ServerJob | undefined {
    return this.records.get(id);
  }

  /** Sends a message to the client of a job, when one is bound. */
  tell(job: ServerJob, message: ServerMessage): void {
    send(job.connection?.socket ?? null, message);
  }

  /**
   * Binds a connection to a record: the connection bound before (a re-registration that came before its close) is
   * unbound and ended, so that its close changes nothing.
   */
  bind(job: ServerJob, connection: Connection): void {
    const old = job.connection;
    if (old !== null && old !== connection) {
      old.job = null;
      old.socket.destroy();
    }
    job.connection = connection;
    connection.job = job;
    job.detached = false;
    job.detachedAt = 0;
    job.epoch++;
  }

  /** The client of a record is away: the record keeps its place or its slots until evidence ends it. */
  unbind(job: ServerJob): void {
    const connection = job.connection;
    if (connection !== null && connection.job === job) {
      connection.job = null;
    }
    job.connection = null;
    job.detached = true;
    job.detachedAt = Date.now();
    job.epoch++;
  }

  /**
   * The invariant of the records, for the self-test: null when it holds, else what breaks it. A record is waiting iff
   * it is in the scheduler's waiting list, running iff in its running list, ended iff in neither and provisional; an
   * id is a record or an outcome, never both; detached mirrors a missing connection.
   */
  checkInvariant(): string | null {
    const scheduler = this.scheduler;
    for (const [id, job] of this.records) {
      const where = `#${id} (${job.phase})`;
      if (job.id !== id) {
        return `${where} is filed under another id`;
      }
      if ((job.phase === "waiting") !== scheduler.waiting.includes(job)) {
        return `${where} and the waiting list disagree`;
      }
      if ((job.phase === "running") !== scheduler.running.includes(job)) {
        return `${where} and the running list disagree`;
      }
      if ((job.phase === "ended") !== (job.provisional !== null)) {
        return `${where} and its provisional outcome disagree`;
      }
      if (this.outcomes.has(id)) {
        return `${where} also has a final outcome`;
      }
      if (job.detached !== (job.connection === null) || (job.connection !== null && job.connection.job !== job)) {
        return `${where} and its connection disagree`;
      }
    }
    const listed = scheduler.waiting.concat(scheduler.running).find((job) => this.records.get(job.id) !== job);
    return listed === undefined ? null : `#${listed.id} is scheduled without a record`;
  }

  /** A job's place and expected times: ahead = waiting jobs before it (0 once it runs). */
  outlook(job: ServerJob, forecast: Map<ServerJob, Expected>): Outlook {
    const expected = forecast.get(job);
    const index = this.scheduler.waiting.indexOf(job);
    return {
      state: index >= 0 ? "waiting" : "running",
      ahead: Math.max(0, index),
      position: index + 1,
      startAt: expected ? expected.startAt : null,
      endAt: expected ? expected.endAt : null,
      blockedBy: this.scheduler.awaitsPredecessor(job) ? job.after : null,
    };
  }

  // Tells each waiting client its place and expected times when the place changed or the start moved by a minute
  private notifyWaiting(): void {
    const scheduler = this.scheduler;
    if (scheduler.waiting.length === 0) {
      return;
    }
    const forecast = scheduler.forecast();
    scheduler.waiting.forEach((job, index) => {
      const view = this.outlook(job, forecast);
      const moved = Math.abs((view.startAt ?? 0) - (job.lastStartAt ?? 0)) >= ForecastNoticeMs;
      if (job.lastPosition !== index || moved) {
        job.lastPosition = index;
        job.lastStartAt = view.startAt;
        this.tell(job, { type: "waiting", id: job.id, ...view, used: scheduler.used, capacity: scheduler.capacity });
      }
    });
  }

  private cpus(job: ServerJob): number {
    return Math.max(1, Math.floor((os.cpus().length / this.scheduler.capacity) * job.weight));
  }

  /** The grant message of a job (also sent again to a client that never saw it). */
  grantMessage(job: ServerJob): ServerMessage {
    return {
      type: "granted",
      id: job.id,
      cpus: this.cpus(job),
      capacity: this.scheduler.capacity,
      protocol: ProtocolVersion,
    };
  }

  private announce(granted: ServerJob[]): void {
    for (const job of granted) {
      job.phase = "running";
      job.epoch++;
      const flags = `${job.gpu ? " gpu" : ""}${job.gpuTurn ? " gpu-turn" : ""}${job.backfilled ? " backfilled" : ""}`;
      this.log(`grant #${job.id} weight ${job.weight}${flags} pid ${job.pid}: ${job.command}`);
      this.tell(job, this.grantMessage(job));
    }
    this.notifyWaiting();
    if (!this.scheduler.isIdle()) {
      this.idleSince = Date.now();
    }
  }

  // Job ids stay the same across a scheduler restart, so that `cancel <id>` read from an earlier `status` never hits
  // another job: a re-registering client brings its id, and the next new id continues from the saved one.
  chooseId(wanted: number | null): number {
    if (wanted !== null && !this.records.has(wanted)) {
      this.nextId = Math.max(this.nextId, wanted + 1);
      return wanted;
    }
    return this.nextId++;
  }

  /** A record from a registration or a saved record, not bound to a connection yet (bind attaches it). */
  createJob(message: Incoming, id: number, phase: Phase): ServerJob {
    const command = String(message.command ?? "");
    return {
      id,
      phase,
      connection: null,
      detached: true,
      detachedAt: 0,
      provisional: null,
      provisionalAt: 0,
      epoch: 0,
      turn: idField(message, "turn"),
      stub: false,
      weight: numberField(message, "weight", 1) || 1,
      gpu: message.gpu === true,
      pid: numberField(message, "pid"),
      commandPid: numberField(message, "commandPid"),
      spawnedAt: numberField(message, "spawnedAt"),
      cwd: String(message.cwd ?? ""),
      command,
      queuedAt: numberField(message, "queuedAt") || Date.now(),
      grantedAt: numberField(message, "grantedAt"),
      estimate: this.history.estimate(command),
      pin: optionalNumberField(message, "pin"),
      before: idField(message, "before"),
      after: idField(message, "after"),
      keepIfFailed: message.keepIfFailed === true,
      gpuTurn: message.gpuTurn === true,
      memoryMb: this.machine.testMemoryMb(command),
      lastPosition: -1,
      lastStartAt: null,
      done: false,
      completed: false,
      exitCode: null,
      cancelPending: false,
    };
  }

  /**
   * Records how a job ended and tells the connections that wait for it. The jobs --after it follow in settle: they are
   * granted or cancelled on this record, never while an event is still being handled.
   */
  recordOutcome(id: number, outcome: Outcome): void {
    const outcomes = this.outcomes;
    outcomes.delete(id);
    outcomes.set(id, outcome);
    while (outcomes.size > KeptOutcomes) {
      outcomes.delete(outcomes.keys().next().value as number);
    }
    for (const socket of this.awaiters.get(id) ?? []) {
      send(socket, { type: "ended", id, outcome });
      socket.end();
    }
    this.awaiters.delete(id);
    this.forget(id);
  }

  /**
   * Ends a job with a final outcome: frees its slots, removes its record, unbinds its connection and tells the
   * connections that wait for it. It grants nothing: settle does, once per event.
   */
  finish(job: ServerJob, outcome: Outcome, why: string): void {
    this.log(`end #${job.id}: ${outcome} (${why})`);
    if (this.records.get(job.id) === job) {
      this.records.delete(job.id);
    }
    const connection = job.connection;
    if (connection !== null && connection.job === job) {
      connection.job = null;
    }
    job.connection = null;
    // Only a run seen from its grant to its end: the command ended by itself (not stopped by a signal or a cancel), and
    // the job was not lost or adopted after a restart. Not a GPU turn: its time includes the wait for the GPU lock, and
    // the command under the lock records its own run under the same command text.
    if (
      (why === "done" || why === "client closed") &&
      job.phase === "running" &&
      !job.gpuTurn &&
      job.completed &&
      outcome !== "cancelled" &&
      !job.adopted &&
      job.grantedAt > 0
    ) {
      this.history.record(job.command, Date.now() - job.grantedAt, this.nextId);
    }
    this.scheduler.detach(job);
    this.recordOutcome(job.id, outcome);
    if (this.scheduler.isIdle()) {
      this.idleSince = Date.now();
    }
  }

  /**
   * The command of a detached running record is gone, but its client may still report how it ended: free the slots and
   * keep the record as provisionally lost (not an outcome: dependents and wait keep waiting).
   */
  freeSlots(job: ServerJob, why: string): void {
    this.log(`#${job.id}: ${why}; lost unless its client reports`);
    this.scheduler.detach(job);
    job.phase = "ended";
    job.provisional = "lost";
    job.provisionalAt = Date.now();
    job.epoch++;
    if (this.scheduler.isIdle()) {
      this.idleSince = Date.now();
    }
  }

  /**
   * The end of every event (a message, a closed connection, a timer): cancels the waiting jobs whose predecessor did
   * not succeed (along chains), records an unknown predecessor as lost, then grants once and saves the state. Nothing
   * grants while an event changes the jobs, so that no job starts on a state that is half updated.
   */
  settle(): void {
    if (this.stopping) {
      return;
    }
    const scheduler = this.scheduler;
    for (let changed = true; changed; ) {
      changed = false;
      for (const job of [...scheduler.waiting]) {
        if (job.after === null || !scheduler.waiting.includes(job)) {
          continue;
        }
        const after = job.after;
        if (
          !this.outcomes.has(after) &&
          Date.now() >= scheduler.holdUntil &&
          this.find(after) === undefined &&
          !this.expectedUntil.has(after)
        ) {
          // Not here, not ended, not expected back (a state file that was lost, an outcome dropped as too old)
          this.log(`#${after} is unknown; counting it as lost for #${job.id}`);
          this.recordOutcome(after, "lost");
        }
        const outcome = this.outcomes.get(after);
        if (outcome !== undefined && outcome !== "succeeded" && !job.keepIfFailed) {
          this.cancelJob(job, `#${after} ${outcome}`);
          changed = true;
        }
      }
    }
    this.announce(scheduler.grant());
    this.store.persist();
  }

  /** Cancels one job: a waiting job leaves at once; a running one keeps its slot until its client has stopped it. */
  cancelJob(job: ServerJob, reason: string): void {
    this.log(`cancel #${job.id} (${reason}): ${job.command}`);
    this.tell(job, { type: "cancelled", id: job.id, reason });
    if (job.phase === "running" && job.connection !== null && !job.connection.socket.destroyed) {
      // The client stops its command and then closes: keep the slot until then, so that the next job does not start
      // beside a command that is still being stopped
      job.cancelPending = true;
      return;
    }
    if (job.phase === "running" || job.phase === "waiting") {
      this.finish(job, "cancelled", "cancelled");
    }
  }

  /** The live processes of each orphan's command (Unix: its process group id), or null per job when unsure. */
  private async orphanProcesses(orphans: ServerJob[]): Promise<(number[] | null)[]> {
    if (orphans.length === 0) {
      return [];
    }
    const table = process.platform === "win32" ? await ProcessTable.query() : null;
    return orphans.map((job) => {
      if (job.commandPid <= 0) {
        return [];
      }
      if (process.platform !== "win32") {
        return ProcessTable.isGroupAlive(job.commandPid) ? [job.commandPid] : [];
      }
      return table === null ? null : table.commandProcesses(job.commandPid, job.spawnedAt);
    });
  }

  /**
   * Observes the records whose client is away: whether the client process lives and, for a running record with a known
   * command, whether a process of the command is left. A step applies only to the record as it was read: one that was
   * re-bound or changed meanwhile (its epoch moved) is left alone. One settle at the end.
   */
  private async observeDetached(): Promise<void> {
    const detached = [...this.records.values()].filter((job) => job.connection === null);
    if (this.polling || detached.length === 0) {
      return;
    }
    this.polling = true;
    try {
      const epochs = detached.map((job) => job.epoch);
      const withCommand = detached.filter((job) => job.phase === "running" && job.commandPid > 0);
      const live = await this.orphanProcesses(withCommand);
      const observations = detached.map((job, index): Observation => {
        const at = withCommand.indexOf(job);
        const processes = at < 0 ? [] : live[at];
        return {
          job,
          epoch: epochs[index] as number,
          seen: {
            clientAlive: ProcessTable.isAlive(job.pid),
            commandAlive: processes === null || processes === undefined ? null : processes.length > 0,
          },
        };
      });
      this.applyObservations(observations, Date.now());
      this.settle();
    } finally {
      this.polling = false;
    }
  }

  /** Applies the plan of each observation to its record, unless the record changed since it was read. */
  applyObservations(observations: Observation[], now: number): void {
    for (const { job, epoch, seen } of observations) {
      if (job.epoch !== epoch || this.records.get(job.id) !== job) {
        continue;
      }
      const step = planObservation(job, seen, now);
      if (step === "keep") {
        continue;
      }
      if ("finalize" in step) {
        this.finish(
          job,
          step.finalize,
          seen.clientAlive ? "its client did not come back in time" : "its client is gone",
        );
      } else if (step.final) {
        this.finish(job, "lost", "the command of a lost client exited");
      } else {
        this.freeSlots(job, "the command of a lost client exited");
      }
    }
  }

  /** Cancels jobs and resolves to the ids it cancelled: a job that ended meanwhile (while the processes of lost
   * clients were read) is left out. */
  async cancel(requested: ServerJob[]): Promise<number[]> {
    const orphans = requested.filter((job) => job.connection === null && job.phase === "running");
    const live = await this.orphanProcesses(orphans);
    orphans.forEach((_, index) => (live[index] ?? []).forEach(ProcessTable.killTree));
    // A job that ended meanwhile has no record; a provisional one has no command left to stop
    const jobs = requested.filter(
      (job) => this.records.get(job.id) === job && (job.phase === "running" || job.phase === "waiting"),
    );
    jobs.forEach((job) => this.cancelJob(job, "cancel requested"));
    this.settle();
    return jobs.map((job) => job.id);
  }

  awaitJob(socket: net.Socket, id: number): void {
    // The job first: a job that runs or waits has not ended, whatever an older record says
    const outcome = this.outcomes.get(id);
    if (this.find(id) !== undefined || this.expectedUntil.has(id)) {
      this.awaiters.set(id, [...(this.awaiters.get(id) ?? []), socket]);
    } else if (outcome !== undefined) {
      send(socket, { type: "ended", id, outcome });
      socket.end();
    } else if (Date.now() < this.scheduler.holdUntil) {
      this.pendingAwaits.push({ socket, id });
    } else {
      this.reply(socket, {
        type: "error",
        code: "unknown-job",
        message: `no job #${id} (never queued, or ended too long ago)`,
      });
    }
  }

  reply(socket: net.Socket, message: ServerMessage): void {
    send(socket, message);
    socket.end();
  }

  /** Tells the client its job's new placement, grants by the new order and answers with the job's place. */
  placed(job: ServerJob): ServerMessage {
    this.tell(job, { type: "updated", id: job.id, weight: job.weight, pin: job.pin, before: job.before });
    this.settle();
    return { type: "move-result", id: job.id, position: this.scheduler.waiting.indexOf(job) + 1 };
  }

  /** A job that did not come back in time is lost: its dependents follow that outcome. */
  private expireExpected(): void {
    const now = Date.now();
    for (const [id, until] of [...this.expectedUntil]) {
      if (until <= now && this.find(id) === undefined) {
        this.log(`#${id} did not come back`);
        this.recordOutcome(id, "lost");
      }
    }
  }

  /** Starts the timers and listens; the state file is restored once the port is ours. */
  start(): void {
    const { options, scheduler } = this;
    setInterval(() => void this.observeDetached(), OrphanPollMs).unref();
    this.server.on("error", (error: NodeJS.ErrnoException) => {
      if (error.code === "EADDRINUSE") {
        this.log(`port ${options.port} is in use; another scheduler runs`);
        process.exit(0);
      }
      this.log(`server error: ${error.message}`);
      process.exit(1);
    });
    this.server.listen(options.port, "127.0.0.1", () => {
      this.log(
        `scheduler pid ${process.pid} on 127.0.0.1:${options.port}, ${scheduler.capacity} slots, ` +
          `${os.cpus().length} cores, ${Math.round(scheduler.memoryCapacity)} MB for test runs ` +
          `(${this.machine.budgets.size} budgets), policy ${scheduler.policy}`,
      );
      // The grace for re-registration starts once clients can connect; only the scheduler that owns the port restores.
      // It ends in the timer, not at a time: nothing is granted (holdUntil stays Infinity) before settle has run with
      // the re-registered jobs.
      this.store.restore();
      setTimeout(() => {
        scheduler.holdUntil = Date.now();
        this.pendingAwaits.splice(0).forEach(({ socket, id }) => this.awaitJob(socket, id));
        this.settle();
      }, StartupGraceMs);
    });
    // The response ratios change with time alone: a waiting job can overtake the first one and fit in the free slots
    // between two events, so grant again now and then while jobs wait
    setInterval(() => {
      this.expireExpected();
      this.settle();
    }, RegrantMs).unref();
    setInterval(() => {
      if (scheduler.isIdle() && Date.now() - this.idleSince >= IdleExitMs) {
        this.log("idle; exiting");
        process.exit(0);
      }
    }, 60 * 1000).unref();
  }
}
