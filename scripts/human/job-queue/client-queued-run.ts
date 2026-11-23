// QueuedRun, the queued path of run: register with the scheduler, wait for the grant (or the GPU turn), run the
// command, report its end, and re-register after a scheduler restart. Its effects go through RunHost
// (client-run-host.ts).

import type * as net from "node:net";
import { type ChildProcess } from "node:child_process";
import {
  CancelledExitCode,
  CpusVariable,
  DigestVariable,
  ForecastNoticeMs,
  JobVariable,
  NoticeIntervalMs,
  OffloadVariable,
  ReasonNoticeMs,
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
  remoteField,
  send,
  shareField,
  wireVersion,
  type ClientMessage,
  type Incoming,
  type Placement,
} from "./protocol.ts";
import type { ShareAnswer } from "./plugin-api.ts";
import { translateForRemote } from "./remote-command.ts";
import type { OffloadPreference } from "./remotes.ts";
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
  private queuedAt = Date.now(); // new when the turn queues again as exclusive
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
  /** Precomputed remote CPU offload preference; resolved once at the first registration. */
  private offload: OffloadPreference | null = null;
  /** Remote worker name granted by the scheduler (sent back in adopt after a scheduler restart). */
  private grantedRemote: string | null = null;
  /**
   * The share a GPU turn asks for when plugins are active, classified once at the first registration: "shared" only
   * when the plugins answer exactly that and --gpu-exclusive is not set. Null until then, and without plugins.
   */
  private share: ShareMode | null = null;
  /** The plugin's check that a shared answer still holds (see ShareAnswer), called once the shared turn is granted. */
  private stillValid: ShareAnswer | null = null;
  /** start() has begun for a grant: another grant, as one that arrives twice, does not start it again. */
  private starting = false;
  /** A "downgrade" was sent and not yet answered: the grants that arrive meanwhile are the old shared one. */
  private downgrading = false;
  private registered = true; // false when the scheduler refused this client's protocol version
  private lastNotice = -Infinity;
  private lastPosition = 0;
  private lastStartAt: number | null = null;
  /** What the job was last told it waits for (Outlook.waitFor): a change is told at once. */
  private lastWaitFor: unknown = undefined;
  /** The latest waiting notice whose new reason came too soon after the last notice, and the timer that tells it. */
  private laterWaiting: Incoming | null = null;
  private laterTimer: ReturnType<typeof setTimeout> | null = null;
  /**
   * The digest of the worktree's content when the job was queued (plugins' contentDigest), taken once without waiting
   * for it; compared with the one at the start. Null until the first registration, and for a GPU holder.
   */
  private queuedDigest: Promise<ContentDigest | undefined> | null = null;
  /** The digest at the queue has come (whatever its answer): only then is one taken at the start. */
  private queuedDigestDone = false;
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

    await this.resolveShare();
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

  /**
   * A shared turn that rests on a measurement (ShareAnswer.stillValid) is checked again once it is granted, before the
   * lock is taken: when the measurement no longer holds, the turn asks the scheduler to downgrade it ("downgrade": the
   * same job, under the same id, waits again as an exclusive turn, so that `wait` and `--after` on its id still see
   * the run). Only a scheduler that says so in the grant (`downgrade`) is asked; for an older one, and when the scheduler
   * refuses, the turn takes the older way (requeueAsNewJob). Returns true when the turn waits again, so that start has nothing more to do.
   */
  private async requeueIfStale(granted: Incoming): Promise<boolean> {
    const answer = this.stillValid;
    if (answer === null || this.share !== "shared" || shareField(granted) !== "shared" || this.plugins === undefined) {
      return false;
    }
    const valid = await this.plugins.stillValid(answer);
    if (this.ended()) {
      return true; // cancelled or interrupted meanwhile: finish has dealt with it
    }
    if (valid) {
      return false;
    }
    prefix(
      `the shared GPU turn of ${formatJobId(numberField(granted, "id", this.jobId))} no longer holds; waiting again as exclusive`,
    );
    this.share = "exclusive";
    this.stillValid = null;
    if (granted.downgrade !== true) {
      // A scheduler that does not know "downgrade" would close the connection at the message: the older way
      await this.requeueAsNewJob();
      return true;
    }
    // Grants that arrive until the scheduler answers are the old shared one: ignored
    this.downgrading = true;
    const message: ClientMessage = { type: "downgrade" };
    send(this.socket, { version: wireVersion(message), ...message });
    return true;
  }

  /**
   * The older way back to exclusive, for a scheduler without "downgrade": the grant is given back (a "done" that
   * records no run, under which the old id ends as cancelled) and the turn queues again as a new job.
   */
  private async requeueAsNewJob(): Promise<void> {
    prefix("the scheduler cannot downgrade the turn; queueing it again as a new exclusive job");
    this.downgrading = false;
    this.starting = false;
    const old = this.socket;
    this.socket = null; // its close is then no loss of the scheduler
    if (old !== null && !old.destroyed) {
      send(old, { version: BaseProtocolVersion, type: "done", completed: false, exitCode: 0, cancelled: true });
      old.end();
    }
    this.jobId = 0;
    this.queuedAt = Date.now();
    this.lastPosition = 0;
    this.lastStartAt = null;
    this.lastNotice = -Infinity;
    await this.attach();
  }

  private async start(granted: Incoming): Promise<void> {
    const { options, placement } = this;
    if (this.starting) {
      return; // a grant that arrives twice (or while the re-check runs) starts the turn once
    }
    this.starting = true;
    if (await this.requeueIfStale(granted)) {
      this.starting = false; // the turn queued again: its next grant starts it
      return;
    }
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
    const worktree = this.worktree ?? resolveWorktree(cwd);
    const shell = CommandShell.resolve(options.shell).name;
    const waited = Math.round((Date.now() - this.queuedAt) / 1000);
    const remoteName = remoteField(granted);

    if (remoteName !== undefined && this.host.runRemoteCommand !== undefined) {
      const remoteSpec = (options.remotes ?? []).find((r) => r.name === remoteName);
      const places = this.places ?? resolvePlaces(options.command, { cwd, worktree }, this.plugins);
      const plan =
        remoteSpec === undefined
          ? null
          : translateForRemote(options.command, {
              cwd,
              worktree,
              shell,
              places,
              cpus,
              keepBuildServersWarm: remoteSpec.keepBuildServersWarm,
              env: process.env,
            });
      if (remoteSpec !== undefined && plan !== null) {
        this.grantedRemote = remoteName;
        prefix(
          `granted ${formatJobId(jobId)} on remote ${remoteName} (${remoteSpec.host}) after ${waited}s (${cpus} MSBuild nodes)`,
        );
        const remoteResult = await this.host.runRemoteCommand(remoteSpec, plan, (c) => this.started(c));
        if (!remoteResult.transportFailed || options.remote === true) {
          const end = commandEnd(remoteResult, this.cancelRequested);
          this.finish(end.exitCode, end.completed, end.cancelled);
          return;
        }
        prefix(`remote ${remoteName} transport failed before execution; falling back to local execution`);
        this.grantedRemote = null;
      }
    }

    const prepared = this.plugins?.prepareCommand(
      options.command,
      { jobId, cpus, nested: false },
      {
        cwd,
        worktree,
        configRoot: this.plugins.configRoot,
        shell,
        env: process.env,
        log: prefix,
      },
    );
    const command = prepared?.command ?? options.command;
    const note = prepared?.note ?? `${cpus} cpus`;
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
    // A job granted before its first digest came (a free queue) has nothing to compare: no second git run
    if (queued === null || !this.queuedDigestDone || this.plugins === undefined) {
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
        if (this.state === "waiting" && !this.downgrading) {
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
    this.downgrading = false; // also the answer to a "downgrade"
    this.jobId = numberField(message, "id", this.jobId);
    const jobId = this.jobId;
    const outlook =
      message.state === "waiting" ? joinNotes(describeOutlook(message, Date.now()), describeWaitFor(message)) : "";
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
    // A new reason is told, but no sooner than ReasonNoticeMs after the last notice: a reason that flips back and forth
    // (slots, in line) as jobs come and go would otherwise print a line each time
    const changed = message.waitFor !== this.lastWaitFor;
    const reason = changed && now - this.lastNotice >= ReasonNoticeMs;
    if (!changed) {
      this.laterWaiting = null; // back to the reason told: nothing to tell later
    }
    if (changed && !reason) {
      // Told once the limit has passed, if it still holds then (the latest notice is looked at again)
      this.laterWaiting = message;
      this.laterTimer ??= setTimeout(
        () => {
          this.laterTimer = null;
          const later = this.laterWaiting;
          this.laterWaiting = null;
          if (later !== null && this.state === "waiting") {
            this.onWaiting(later);
          }
        },
        ReasonNoticeMs - (now - this.lastNotice),
      ).unref();
    }
    if (position !== this.lastPosition || moved || reason || now - this.lastNotice >= NoticeIntervalMs) {
      this.lastPosition = position;
      this.lastStartAt = startAt;
      this.lastWaitFor = message.waitFor;
      this.lastNotice = now;
      // This notice tells the latest reason: a reason kept for later is moot, and its timer counted from the last notice
      if (this.laterTimer !== null) {
        clearTimeout(this.laterTimer);
        this.laterTimer = null;
      }
      const outlook = joinNotes(describeOutlook(message, now), describeWaitFor(message));
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
    if (this.downgrading && this.state === "waiting") {
      void this.requeueAsNewJob(); // an older scheduler, or a job that is not a granted shared turn
      return;
    }
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
    // Only the current connection speaks for the job: what a replaced one still delivers is not read
    onMessages(current, (message) => {
      if (current === this.socket) {
        this.onMessage(message);
      }
    });
    current.on("error", () => current.destroy());
    current.on("close", () => this.onClose(current));
  }

  /** What the plugins are told about this job: its directory, worktree and shell (resolved once, git). */
  private contextOf(plugins: PluginHost): BaseClientContext {
    const cwd = process.cwd();
    return {
      cwd,
      worktree: (this.worktree ??= resolveWorktree(cwd)),
      configRoot: plugins.configRoot,
      shell: CommandShell.resolve(this.options.shell).name,
      env: process.env,
      log: prefix,
    };
  }

  /** What the plugins' classify says of the share of this GPU turn; undefined when no plugin has an opinion. */
  private classifyShare(plugins: PluginHost): ShareMode | undefined {
    const resource = { name: "gpu", forcedExclusive: this.options.gpuExclusive === true };
    return plugins.classify(this.options.command, resource, this.contextOf(plugins)).share;
  }

  /**
   * Decides the share of a GPU turn before its first registration: classify (sync), and when no plugin has an opinion
   * (an explicit "shared" or "exclusive" stands, a veto too) and neither --gpu-exclusive nor --after is set, the
   * plugins' checkShare, which may take a while (a test list through the queue), so it runs before the connection is
   * made, not during it. Once per run; a re-registration reuses the answer.
   */
  private async resolveShare(): Promise<void> {
    const plugins = this.plugins;
    if (!this.gpuTurn || plugins === undefined || plugins.plugins.length === 0 || this.share !== null) {
      return;
    }
    let share = this.classifyShare(plugins);
    // --after: the predecessor may still build what the test reads (a run split by run --gpu zeroes it: its build
    // job already waited), so a list now could read the old assembly
    if (share === undefined && this.options.gpuExclusive !== true && this.options.after === 0) {
      const answer = await plugins.checkShare(this.options.command, this.contextOf(plugins));
      share = answer?.share;
      this.stillValid = answer?.share === "shared" && answer.stillValid !== undefined ? answer : null;
    }
    this.share = share === "shared" ? "shared" : "exclusive";
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
    const context = (plugins: PluginHost): BaseClientContext => this.contextOf(plugins);
    let memoryMb: number | undefined;
    let offload: OffloadPreference | undefined;
    if (hasPlugins && !this.gpuTurn) {
      if (this.memoryMb === null || this.offload === null) {
        const plugins = this.plugins as PluginHost;
        const classified = plugins.classify(options.command, null, context(plugins));
        const fallback = classified.test ? (options.defaultTestMemoryMb ?? 0) : 0;
        this.memoryMb = classified.memoryMb ?? fallback;
        const enabledRemotes = (options.remotes ?? []).filter((r) => r.mode !== "off");
        const offloadDisabled =
          this.inGpuLock ||
          options.local === true ||
          process.env[OffloadVariable] === "0" ||
          enabledRemotes.length === 0;
        if (offloadDisabled) {
          if (options.remote === true) {
            prefix("--remote requested, but no enabled remote worker is configured or offload is disabled");
            this.host.exit(UsageExitCode);
            return;
          }
          this.offload = "never";
        } else {
          const shell = CommandShell.resolve(options.shell).name;
          const canTranslate =
            (classified.offloadable === true || options.remote === true) &&
            translateForRemote(options.command, {
              cwd,
              worktree,
              shell,
              places,
              cpus: enabledRemotes[0]?.cpus ?? 1,
              keepBuildServersWarm: enabledRemotes[0]?.keepBuildServersWarm ?? true,
              env: process.env,
            }) !== null;
          if (options.remote === true && !canTranslate) {
            prefix("--remote requested, but this command cannot be safely translated for remote Linux execution");
            this.host.exit(UsageExitCode);
            return;
          }
          this.offload = canTranslate ? (options.remote === true ? "force" : "auto") : "never";
        }
      }
      memoryMb = this.memoryMb;
      offload = this.offload;
    }
    // A GPU turn names its resource and asks for its share; a GPU holder does not (the scheduler ties it to its turn)
    let share: ShareMode | undefined;
    if (hasPlugins && this.gpuTurn) {
      if (this.share === null) {
        // Normally set by resolveShare before the connection; kept for a run that registers without it
        this.share = this.classifyShare(this.plugins as PluginHost) === "shared" ? "shared" : "exclusive";
      }
      share = this.share;
    }
    // The worktree's content as queued, once, while the job waits; a GPU holder starts at once, under a turn that took
    // its own
    const digests = process.env[DigestVariable] !== "0";
    if (
      digests &&
      this.queuedDigest === null &&
      this.state === "waiting" &&
      !this.inGpuLock &&
      this.plugins !== undefined
    ) {
      this.queuedDigest = this.plugins.contentDigest(this.digestContext(this.plugins));
      void this.queuedDigest.then(() => (this.queuedDigestDone = true));
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
      ...(offload !== undefined && offload !== "never" ? { offload } : {}),
      ...(this.state === "running" && this.grantedRemote !== null ? { remote: this.grantedRemote } : {}),
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
      // A pending downgrade is lost with the connection. The turn is exclusive already (the re-check said so): it
      // registers again as exclusive, and a grant that comes after the reconnect (also one that still says shared) is
      // started at once, under an exclusive lock
      this.downgrading = false;
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
