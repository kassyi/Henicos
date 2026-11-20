// QueuedRun, the queued path of run: register with the scheduler, wait for the grant (or the GPU turn), run the
// command, report its end, and re-register after a scheduler restart. Its effects go through RunHost
// (client-run-host.ts).

import type * as net from "node:net";
import { type ChildProcess } from "node:child_process";
import {
  CancelledExitCode,
  CpusVariable,
  ForecastNoticeMs,
  JobVariable,
  NoticeIntervalMs,
  BaseProtocolVersion,
  TimedOutExitCode,
  TurnVariable,
  UsageExitCode,
} from "./constants.ts";
import { describeOutlook, describeWaitFor } from "./client-commands.ts";
import { OlderSchedulerHint, delay } from "./client-connect.ts";
import { commandEnd, type RunHost } from "./client-run-host.ts";
import type { RunOptions } from "./client.ts";
import { environmentHash, VolatileEnv, type EnvironmentHash } from "./environment-hash.ts";
import { commandKeys, type CommandKeys, type PathPlace } from "./estimate-key.ts";
import { errorMessage } from "./history.ts";
import { resolvePlaces } from "./path-places.ts";
import type { ContentDigest } from "./plugin-api.ts";
import type { BaseClientContext, PluginHost } from "./plugin-host.ts";
import {
  numberField,
  onMessages,
  send,
  shareField,
  wireVersion,
  type ClientMessage,
  type Incoming,
  type Placement,
} from "./protocol.ts";
import type { ShareMode } from "./resources.ts";
import { CommandShell } from "./shell.ts";
import { prefix, resolveWorktree } from "./system.ts";
import { formatJobId } from "./job-id.ts";

/** The non-empty notes joined by "; ". */
function joinNotes(...notes: string[]): string {
  return notes.filter((note) => note !== "").join("; ");
}

/**
 * The queued path of run: register, wait for the grant, run the command, report its end. With `gpuTurn`, the job waits
 * for its turn at the GPU instead of slots, and runs gpu-lock.js with an inner client that registers the command's
 * slots as a GPU holder.
 */
export class QueuedRun {
  private readonly options: RunOptions;
  private readonly placement: Placement;
  private readonly inGpuLock: boolean;
  private readonly gpuTurn: boolean;
  private readonly host: RunHost;
  private readonly plugins?: PluginHost;
  private readonly queuedAt = Date.now();
  private state: "waiting" | "running" | "done" = "waiting";
  private child: ChildProcess | null = null;
  private jobId = 0;
  private spawnedAt = 0;
  private socket: net.Socket | null = null;
  /** The root of the worktree the job runs in, the scheduler's round-robin key; resolved at the first registration. */
  private worktree: string | null = null;
  /** Where the command's projects and scripts lie (git), for the estimate keys; resolved at the first registration. */
  private places: Record<string, PathPlace> | null = null;
  /** Precomputed estimate keys when plugins are active; resolved once at the first registration. */
  private keys: CommandKeys | null = null;
  /** Precomputed memory budget when plugins are active and !gpuTurn; resolved once at the first registration. */
  private memoryMb: number | null = null;
  /**
   * The share a GPU turn asks for when plugins are active, classified once at the first registration: "shared" only
   * when the plugins answer exactly that and --gpu-exclusive is not set. Null until then, and without plugins.
   */
  private share: ShareMode | null = null;
  private registered = true; // false when the scheduler refused this client's protocol version
  private lastNotice = -Infinity;
  private lastPosition = 0;
  private lastStartAt: number | null = null;
  /** What the job was last told it waits for (Outlook.waitFor): a change is told at once. */
  private lastWaitFor: unknown = undefined;
  /**
   * The digest of the worktree's content when the job was queued (plugins' contentDigest), taken once without waiting
   * for it; compared with the one at the start. Null until the first registration, and for a GPU holder.
   */
  private queuedDigest: Promise<ContentDigest | undefined> | null = null;
  /** The hash of the client's environment, for the scheduler's log (environment-hash.ts); taken at the first registration. */
  private environment: EnvironmentHash | null = null;
  /** The pause was told (once per run). */
  private pauseTold = false;
  /** Set once "done" was sent: called with true on the scheduler's receipt, with false when none comes. */
  private doneReceipt: ((received: boolean) => void) | null = null;
  /**
   * The scheduler gives a receipt for "done" (protocol 3); one of an older checkout does not, and is not waited for.
   */
  private receipts = false;
  /** The scheduler cancelled this job: the command is being stopped, and its end decides how it is reported. */
  private cancelRequested = false;

  constructor(
    options: RunOptions,
    placement: Placement,
    inGpuLock: boolean,
    gpuTurn: boolean,
    host: RunHost,
    plugins?: PluginHost,
  ) {
    this.options = options;
    this.placement = placement;
    this.inGpuLock = inGpuLock;
    this.gpuTurn = gpuTurn;
    this.host = host;
    this.plugins = plugins;
  }

  async run(): Promise<void> {
    this.host.onSignal("SIGINT", this.onSignal("SIGINT", 130));
    this.host.onSignal("SIGTERM", this.onSignal("SIGTERM", 143));
    this.host.onSignal("SIGHUP", this.onSignal("SIGHUP", 129));

    const timeoutMinutes = this.options.timeoutMinutes;
    if (timeoutMinutes > 0) {
      setTimeout(
        () => {
          if (this.state === "waiting") {
            prefix(`gave up after ${timeoutMinutes} minutes of waiting; see "job-queue.js status"`);
            this.finish(TimedOutExitCode);
          }
        },
        timeoutMinutes * 60 * 1000,
      ).unref();
    }

    await this.attach();
  }

  // completed: the command ran to its own end (not stopped by a signal or a cancel), so its run time may be recorded;
  // cancelled: the job ends as cancelled (a waiting job that the scheduler cancelled, or a command commandEnd says so)
  private finish(code: number, completed = false, cancelled = this.cancelRequested): void {
    if (this.state === "done") {
      return;
    }
    const ran = this.state === "running";
    this.state = "done";
    const current = this.socket;
    const jobId = this.jobId;
    if (current === null || current.destroyed) {
      if (ran && jobId > 0) {
        // No scheduler listens right now (it restarts): report the outcome to the next one, for the jobs that wait for
        // this one, then exit
        void this.host
          .reportEnd(jobId, completed, code, cancelled, this.reporter())
          .finally(() => this.host.exit(code));
        return;
      }
      this.host.exit(code);
      return;
    }
    // "done" first, so that the scheduler frees the slot at once instead of treating the job as a lost client whose
    // command may still run
    send(current, {
      version: BaseProtocolVersion,
      type: "done",
      completed,
      exitCode: code,
      cancelled,
    });
    if (!ran || jobId === 0 || !this.receipts) {
      setTimeout(() => this.host.exit(code), 1000).unref();
      current.end(() => this.host.exit(code));
      return;
    }
    // Then the scheduler's receipt: one that is stopping does not save the outcome (and one of an older checkout sends
    // no receipt), so without it the outcome goes to the next scheduler as a report, for the jobs that wait for this
    // one
    let left = false;
    this.doneReceipt = (received: boolean): void => {
      if (left) {
        return;
      }
      left = true;
      if (received) {
        setTimeout(() => this.host.exit(code), 1000).unref();
        current.end(() => this.host.exit(code));
        return;
      }
      current.destroy();
      void this.host
        .reportEnd(this.jobId, completed, code, cancelled, this.reporter())
        .finally(() => this.host.exit(code));
    };
    setTimeout(() => this.doneReceipt?.(false), 2000);
  }

  /** Who reports an end: this client, as it registered (so that a report never ends another client's job). */
  private reporter(): { pid: number; queuedAt: number } {
    return { pid: process.pid, queuedAt: this.queuedAt };
  }

  private onSignal(signal: string, code: number): () => void {
    return (): void => {
      prefix(`${signal}: stopping the command and leaving the queue`);
      const running = this.child;
      if (this.gpuTurn && process.platform === "win32" && running !== null && running.exitCode === null) {
        // The console delivers Ctrl+C to gpu-lock.js itself, which releases its lock and ends; killing it first would
        // leave the lock behind. Leave once it has ended.
        running.once("close", () => this.finish(code));
        return;
      }
      this.host.stop(this.child, false);
      this.finish(code);
    };
  }

  /** Tells the scheduler the process of the command that just started. */
  private started(child: ChildProcess): void {
    this.child = child;
    this.spawnedAt = Date.now();
    if (child.pid !== undefined) {
      send(this.socket, {
        version: BaseProtocolVersion,
        type: "started",
        commandPid: child.pid,
        spawnedAt: this.spawnedAt,
      });
    }
  }

  private async start(granted: Incoming): Promise<void> {
    const { options, placement } = this;
    this.state = "running";
    this.jobId = numberField(granted, "id", this.jobId);
    const jobId = this.jobId;
    // Taken beside the start, never before it: the notice must not delay the command
    void this.tellWorktreeChange(jobId);
    if (this.gpuTurn) {
      // Shared only when this turn asked for "shared" and the scheduler echoes exactly that: an older scheduler, or
      // one that counts the turn as exclusive, sends no echo, and the lock is then taken exclusively
      const mode: ShareMode = this.share === "shared" && shareField(granted) === "shared" ? "shared" : "exclusive";
      const waited = Math.round((Date.now() - this.queuedAt) / 1000);
      prefix(`the GPU turn of ${formatJobId(jobId)} came after ${waited}s; taking the GPU lock (${mode})`);
      const result = await this.host.runUnderGpuLock(placement.weight, jobId, mode, (c) => this.started(c));
      const end = commandEnd(result, this.cancelRequested);
      this.finish(end.exitCode, end.completed, end.cancelled);
      return;
    }
    const cpus = numberField(granted, "cpus", 1);
    const cwd = process.cwd();
    const prepared = this.plugins?.prepareCommand(
      options.command,
      { jobId, cpus, nested: false },
      {
        cwd,
        worktree: this.worktree ?? resolveWorktree(cwd),
        configRoot: this.plugins.configRoot,
        shell: CommandShell.resolve(options.shell).name,
        env: process.env,
        log: prefix,
      },
    );
    const command = prepared?.command ?? options.command;
    const note = prepared?.note ?? `${cpus} cpus`;
    const waited = Math.round((Date.now() - this.queuedAt) / 1000);
    prefix(`granted ${formatJobId(jobId)} after ${waited}s (weight ${placement.weight}, ${note})`);
    const env = {
      ...process.env,
      ...prepared?.env,
      [JobVariable]: String(jobId),
      [CpusVariable]: String(cpus),
    };
    const result = await this.host.runCommand(command, env, (c) => this.started(c));
    // The command's result decides, also after a cancel; a run a signal cut short is not recorded
    const end = commandEnd(result, this.cancelRequested);
    this.finish(end.exitCode, end.completed, end.cancelled);
  }

  /**
   * Compares the digest of the worktree's content at the start with the one at the queue, and says so in one line when
   * they differ: the job runs on what is there now, not on what was there when it was queued. Silent when either has
   * no digest.
   */
  private async tellWorktreeChange(jobId: number): Promise<void> {
    const queued = this.queuedDigest;
    if (queued === null || this.plugins === undefined) {
      return;
    }
    const now = await this.plugins.contentDigest(this.digestContext(this.plugins));
    const then = await queued;
    if (now !== undefined && then !== undefined && now.digest !== then.digest) {
      const at = new Date(this.queuedAt).toTimeString().slice(0, 5);
      prefix(`${formatJobId(jobId)}: the worktree changed after it was queued (${at}); it builds what is there now`);
    }
  }

  /** The context of the digest hooks: the worktree resolved at the first registration. */
  private digestContext(plugins: PluginHost): BaseClientContext {
    const cwd = process.cwd();
    return {
      cwd,
      worktree: this.worktree ?? resolveWorktree(cwd),
      configRoot: plugins.configRoot,
      shell: CommandShell.resolve(this.options.shell).name,
      env: process.env,
      log: prefix,
    };
  }

  private onMessage(message: Incoming): void {
    switch (message.type) {
      case "queued":
        this.onQueued(message);
        break;
      case "waiting":
        this.onWaiting(message);
        break;
      case "paused":
        this.tellPaused();
        break;
      case "granted":
        this.receipts = numberField(message, "protocol") >= 3;
        if (this.state === "waiting") {
          void this.start(message);
        }
        break;
      case "done-ok":
        this.doneReceipt?.(true);
        break;
      case "adopted":
        this.onAdopted(message);
        break;
      case "updated":
        this.onUpdated(message);
        break;
      case "cancelled":
        this.onCancelled(message);
        break;
      case "error":
        this.onError(message);
        break;
      default:
        break;
    }
  }

  private onQueued(message: Incoming): void {
    this.jobId = numberField(message, "id", this.jobId);
    const jobId = this.jobId;
    const outlook =
      message.state === "waiting" ? joinNotes(describeWaitFor(message), describeOutlook(message, Date.now())) : "";
    // The first waiting notice says the same: skip it
    this.lastPosition = numberField(message, "position");
    this.lastStartAt = typeof message.startAt === "number" ? message.startAt : null;
    this.lastWaitFor = message.waitFor;
    this.lastNotice = Date.now();
    const role = this.options.role ? ` (${this.options.role})` : "";
    prefix(
      `queued ${formatJobId(jobId)}${role}${outlook ? `: ${outlook}` : ""}; "job-queue.js status ${formatJobId(jobId)}" shows it`,
    );
    if (message.paused === true) {
      this.tellPaused();
    }
  }

  /** Once per run: the queue is paused, so the job starts only after "job-queue.js resume". */
  private tellPaused(): void {
    if (!this.pauseTold) {
      this.pauseTold = true;
      prefix(`the queue is paused: ${formatJobId(this.jobId)} starts after "job-queue.js resume"`);
    }
  }

  private onWaiting(message: Incoming): void {
    this.jobId = numberField(message, "id", this.jobId);
    const now = Date.now();
    const startAt = typeof message.startAt === "number" ? message.startAt : null;
    const position = numberField(message, "position");
    const moved = Math.abs((startAt ?? 0) - (this.lastStartAt ?? 0)) >= ForecastNoticeMs;
    const reason = message.waitFor !== this.lastWaitFor;
    if (position !== this.lastPosition || moved || reason || now - this.lastNotice >= NoticeIntervalMs) {
      this.lastPosition = position;
      this.lastStartAt = startAt;
      this.lastWaitFor = message.waitFor;
      this.lastNotice = now;
      const outlook = joinNotes(describeWaitFor(message), describeOutlook(message, now));
      const slots = `${numberField(message, "used")}/${numberField(message, "capacity")} slots in use`;
      prefix(
        `waiting ${formatJobId(this.jobId)}: position ${position}, ${slots} ` +
          `(${Math.round((now - this.queuedAt) / 1000)}s)${outlook ? `; ${outlook}` : ""}`,
      );
    }
  }

  private onAdopted(message: Incoming): void {
    this.receipts = numberField(message, "protocol") >= 3;
    const id = numberField(message, "id", this.jobId);
    if (id !== this.jobId) {
      prefix(`re-registered the running job as ${formatJobId(id)} (was ${formatJobId(this.jobId)})`);
    }
    this.jobId = id;
  }

  private onUpdated(message: Incoming): void {
    // A move or set-weight: brought along when this client re-registers after a scheduler restart. A GPU turn weighs
    // nothing in the scheduler; it keeps the weight its command registers with.
    if (!this.gpuTurn) {
      this.placement.weight = numberField(message, "weight", this.placement.weight);
    }
    this.placement.pin = typeof message.pin === "number" ? message.pin : null;
    this.placement.before = typeof message.before === "number" ? message.before : null;
  }

  private onCancelled(message: Incoming): void {
    if (this.state === "done") {
      return; // the command had already ended
    }
    this.cancelRequested = true;
    const reason = message.reason ? ` (${String(message.reason)})` : "";
    prefix(
      `job ${formatJobId(numberField(message, "id", this.jobId))} was cancelled${reason}` +
        (this.state === "running" ? "; stopping the command" : ""),
    );
    if (this.state === "waiting") {
      this.finish(CancelledExitCode);
      return;
    }
    // The command runs: stop it, and leave (so freeing the slot) once it has really ended, with what commandEnd makes
    // of its result in start(). A command that ended with 0 before the cancel reached it succeeded.
    const running = this.child;
    if (running !== null && running.exitCode === null && running.signalCode === null) {
      setTimeout(() => this.finish(CancelledExitCode, false, true), 10000).unref(); // a command that does not stop
      this.host.stop(running, this.gpuTurn);
    }
  }

  private onError(message: Incoming): void {
    if (this.gpuTurn && message.code === "version" && this.state === "waiting" && this.options.after === 0) {
      // A scheduler of an older checkout has no GPU turns: take the GPU lock as such schedulers expect (the command
      // then registers as a GPU holder), without the fair order
      prefix("the scheduler of an older checkout has no GPU turns; waiting for the GPU lock directly");
      this.registered = false;
      this.state = "running";
      void this.host
        .runUnderGpuLock(this.placement.weight, 0, "exclusive", (c) => (this.child = c))
        .then(({ code }) => this.host.exit(code));
      return;
    }
    prefix(`scheduler: ${String(message.message)}${message.code === "version" ? `; ${OlderSchedulerHint}` : ""}`);
    if (this.state === "waiting") {
      this.host.exit(message.code === "unknown-job" ? UsageExitCode : 1);
      return;
    }
    if (message.code === "version") {
      this.registered = false; // the command keeps running, unregistered; do not reconnect again
    }
  }

  // The state changes while attach awaits (finish runs from the command's close): read it through a call, which the
  // compiler does not narrow
  private ended(): boolean {
    return this.state === "done";
  }

  private async attach(): Promise<void> {
    for (;;) {
      if (this.ended()) {
        return; // the command ended while this client reconnected: finish reports it (reportEnd)
      }
      try {
        this.socket = await this.host.connect();
        break;
      } catch (error) {
        if (this.state === "running") {
          // The command runs regardless; keep trying to register it so that the next jobs count its weight.
          await delay(5000);
          continue;
        }
        if (this.ended()) {
          return;
        }
        prefix(errorMessage(error));
        this.host.exit(1);
        return;
      }
    }
    const current = this.socket;
    if (this.ended()) {
      // The command ended during the connect: registering now would queue a job that no longer exists under its id
      current.destroy();
      this.socket = null;
      return;
    }
    this.register(current);
    onMessages(current, (message) => this.onMessage(message));
    current.on("error", () => current.destroy());
    current.on("close", () => this.onClose(current));
  }

  /** Registers the job on a new connection: a request while it waits, an adopt once its command runs. */
  private register(current: net.Socket): void {
    const { options, placement } = this;
    const running = this.child;
    // Resolved once per job (git), kept for the re-registrations after a scheduler restart
    const cwd = process.cwd();
    const worktree = (this.worktree ??= resolveWorktree(cwd));
    const places = (this.places ??= resolvePlaces(options.command, { cwd, worktree }, this.plugins));
    const hasPlugins = this.plugins !== undefined && this.plugins.plugins.length > 0;
    const keys = hasPlugins
      ? (this.keys ??= commandKeys(options.command, { cwd, worktree, places }, this.plugins))
      : undefined;
    const context = (plugins: PluginHost): BaseClientContext => ({
      cwd,
      worktree,
      configRoot: plugins.configRoot,
      shell: CommandShell.resolve(options.shell).name,
      env: process.env,
      log: prefix,
    });
    let memoryMb: number | undefined;
    if (hasPlugins && !this.gpuTurn) {
      if (this.memoryMb === null) {
        const plugins = this.plugins as PluginHost;
        const classified = plugins.classify(options.command, null, context(plugins));
        const fallback = classified.test ? (options.defaultTestMemoryMb ?? 0) : 0;
        this.memoryMb = classified.memoryMb ?? fallback;
      }
      memoryMb = this.memoryMb;
    }
    // A GPU turn names its resource and asks for its share; a GPU holder does not (the scheduler ties it to its turn)
    let share: ShareMode | undefined;
    if (hasPlugins && this.gpuTurn) {
      if (this.share === null) {
        const plugins = this.plugins as PluginHost;
        const resource = { name: "gpu", forcedExclusive: options.gpuExclusive === true };
        const classified = plugins.classify(options.command, resource, context(plugins));
        this.share = classified.share === "shared" ? "shared" : "exclusive";
      }
      share = this.share;
    }
    // The worktree's content as queued, once, while the job waits; a GPU holder starts at once, under a turn that took
    // its own
    if (this.queuedDigest === null && this.state === "waiting" && !this.inGpuLock && this.plugins !== undefined) {
      this.queuedDigest = this.plugins.contentDigest(this.digestContext(this.plugins));
    }
    // Without the variables that differ per run by nature, the core's and the plugins' (a chat session id)
    const environment = (this.environment ??= environmentHash(process.env, [
      ...VolatileEnv,
      ...(this.plugins?.volatileEnv(context(this.plugins)) ?? []),
    ]));
    const registration: ClientMessage = {
      type: this.state === "running" ? "adopt" : "request",
      weight: placement.weight,
      gpu: this.inGpuLock,
      pid: process.pid,
      commandPid: running !== null && running.pid !== undefined ? running.pid : 0,
      spawnedAt: this.spawnedAt,
      cwd,
      worktree,
      ...(Object.keys(places).length > 0 ? { places } : {}),
      ...(keys !== undefined ? { keys } : {}),
      ...(memoryMb !== undefined ? { memoryMb } : {}),
      ...(this.gpuTurn ? { resource: "gpu" } : {}),
      ...(share !== undefined ? { share } : {}),
      envHash: environment.hash,
      envKeys: environment.keys,
      // The chat that queued it (the plugins read it from the environment), so that the live view can show its jobs on its row
      session: this.plugins === undefined ? undefined : this.plugins.session(context(this.plugins)),
      command: options.command,
      queuedAt: this.queuedAt,
      id: this.jobId, // keeps the job id across a scheduler restart
      pin: placement.pin,
      before: placement.before,
      after: options.after > 0 ? options.after : null,
      keepIfFailed: options.keepIfFailed,
      gpuTurn: this.gpuTurn,
      // A GPU holder names the --gpu run that started it: a cancel of the holder cancels that run
      turn: this.inGpuLock ? Number(process.env[TurnVariable]) || null : null,
    };
    send(current, { version: wireVersion(registration), ...registration });
  }

  private onClose(current: net.Socket): void {
    if (this.state === "done" && this.socket === current) {
      this.doneReceipt?.(false); // closed before the receipt: the scheduler went away with "done" unsaved
    }
    if (this.state !== "done" && this.socket === current) {
      this.socket = null;
      if (!this.registered) {
        return;
      }
      const running = this.state === "running";
      prefix(`lost the scheduler; ${running ? "re-registering the running job" : "queueing again"}`);
      // Running jobs re-register first, inside the scheduler's startup grace; waiting ones queue a little later.
      setTimeout(() => void this.attach(), running ? 100 : 800);
    }
  }
}
