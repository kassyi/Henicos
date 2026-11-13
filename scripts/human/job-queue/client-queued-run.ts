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
  MsbuildArgsVariable,
  NoticeIntervalMs,
  BaseProtocolVersion,
  TimedOutExitCode,
  TurnVariable,
  UsageExitCode,
} from "./constants.ts";
import { describeOutlook } from "./client-commands.ts";
import { OlderSchedulerHint, delay } from "./client-connect.ts";
import { commandEnd, type RunHost } from "./client-run-host.ts";
import type { RunOptions } from "./client.ts";
import { errorMessage } from "./history.ts";
import {
  numberField,
  onMessages,
  send,
  wireVersion,
  type ClientMessage,
  type Incoming,
  type Placement,
} from "./protocol.ts";
import { CommandShell } from "./shell.ts";
import { prefix } from "./system.ts";

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
  private readonly queuedAt = Date.now();
  private state: "waiting" | "running" | "done" = "waiting";
  private child: ChildProcess | null = null;
  private jobId = 0;
  private spawnedAt = 0;
  private socket: net.Socket | null = null;
  private registered = true; // false when the scheduler refused this client's protocol version
  private lastNotice = -Infinity;
  private lastPosition = 0;
  private lastStartAt: number | null = null;
  /** Set once "done" was sent: called with true on the scheduler's receipt, with false when none comes. */
  private doneReceipt: ((received: boolean) => void) | null = null;
  /**
   * The scheduler gives a receipt for "done" (protocol 3); one of an older checkout does not, and is not waited for.
   */
  private receipts = false;
  /** The scheduler cancelled this job: the command is being stopped, and its end decides how it is reported. */
  private cancelRequested = false;

  constructor(options: RunOptions, placement: Placement, inGpuLock: boolean, gpuTurn: boolean, host: RunHost) {
    this.options = options;
    this.placement = placement;
    this.inGpuLock = inGpuLock;
    this.gpuTurn = gpuTurn;
    this.host = host;
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
    if (this.gpuTurn) {
      prefix(
        `the GPU turn of #${jobId} came after ${Math.round((Date.now() - this.queuedAt) / 1000)}s; taking the GPU lock`,
      );
      const result = await this.host.runUnderGpuLock(placement.weight, jobId, (c) => this.started(c));
      const end = commandEnd(result, this.cancelRequested);
      this.finish(end.exitCode, end.completed, end.cancelled);
      return;
    }
    const cpus = numberField(granted, "cpus", 1);
    const command = CommandShell.limitMsbuildNodes(options.command, cpus);
    const waited = Math.round((Date.now() - this.queuedAt) / 1000);
    prefix(`granted #${jobId} after ${waited}s (weight ${placement.weight}, ${cpus} MSBuild nodes)`);
    const env = {
      ...process.env,
      [JobVariable]: String(jobId),
      [CpusVariable]: String(cpus),
      [MsbuildArgsVariable]: `-m:${cpus}`,
    };
    const result = await this.host.runCommand(command, env, (c) => this.started(c));
    // The command's result decides, also after a cancel; a run a signal cut short is not recorded
    const end = commandEnd(result, this.cancelRequested);
    this.finish(end.exitCode, end.completed, end.cancelled);
  }

  private onMessage(message: Incoming): void {
    switch (message.type) {
      case "queued":
        this.onQueued(message);
        break;
      case "waiting":
        this.onWaiting(message);
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
    const outlook = message.state === "waiting" ? describeOutlook(message, Date.now()) : "";
    // The first waiting notice says the same: skip it
    this.lastPosition = numberField(message, "position");
    this.lastStartAt = typeof message.startAt === "number" ? message.startAt : null;
    this.lastNotice = Date.now();
    prefix(`queued #${jobId}${outlook ? `: ${outlook}` : ""}; "job-queue.js status ${jobId}" shows it`);
  }

  private onWaiting(message: Incoming): void {
    this.jobId = numberField(message, "id", this.jobId);
    const now = Date.now();
    const startAt = typeof message.startAt === "number" ? message.startAt : null;
    const position = numberField(message, "position");
    const moved = Math.abs((startAt ?? 0) - (this.lastStartAt ?? 0)) >= ForecastNoticeMs;
    if (position !== this.lastPosition || moved || now - this.lastNotice >= NoticeIntervalMs) {
      this.lastPosition = position;
      this.lastStartAt = startAt;
      this.lastNotice = now;
      const outlook = describeOutlook(message, now);
      const slots = `${numberField(message, "used")}/${numberField(message, "capacity")} slots in use`;
      prefix(
        `waiting #${this.jobId}: position ${position}, ${slots} ` +
          `(${Math.round((now - this.queuedAt) / 1000)}s)${outlook ? `; ${outlook}` : ""}`,
      );
    }
  }

  private onAdopted(message: Incoming): void {
    this.receipts = numberField(message, "protocol") >= 3;
    const id = numberField(message, "id", this.jobId);
    if (id !== this.jobId) {
      prefix(`re-registered the running job as #${id} (was #${this.jobId})`);
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
      `job #${numberField(message, "id", this.jobId)} was cancelled${reason}` +
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
        .runUnderGpuLock(this.placement.weight, 0, (c) => (this.child = c))
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
    const registration: ClientMessage = {
      type: this.state === "running" ? "adopt" : "request",
      weight: placement.weight,
      gpu: this.inGpuLock,
      pid: process.pid,
      commandPid: running !== null && running.pid !== undefined ? running.pid : 0,
      spawnedAt: this.spawnedAt,
      cwd: process.cwd(),
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
