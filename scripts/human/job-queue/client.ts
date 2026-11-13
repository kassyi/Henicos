// The run mode of the job queue (QueuedRun): wait for a slot or the GPU turn, run the command, give the slot back,
// and stay registered across a scheduler restart. The other modes are in client-commands.ts, the connection to the
// scheduler in client-connect.ts.

import type * as net from "node:net";
import { spawn, type ChildProcess } from "node:child_process";
import {
  CancelledExitCode,
  CommandVariable,
  CpusVariable,
  EntryScript,
  ForecastNoticeMs,
  GpuLockScript,
  GpuLockHeldVariable,
  InGpuLockVariable,
  JobVariable,
  MsbuildArgsVariable,
  NoticeIntervalMs,
  BaseProtocolVersion,
  ShellVariable,
  TimedOutExitCode,
  UsageExitCode,
  type ShellName,
} from "./constants.ts";
import { describeOutlook } from "./client-commands.ts";
import { OlderSchedulerHint, SchedulerLauncher, delay, type Endpoint } from "./client-connect.ts";
import { errorMessage } from "./history.ts";
import { MachineProfile } from "./machine.ts";
import {
  numberField,
  onMessages,
  send,
  wireVersion,
  type ClientMessage,
  type Incoming,
  type Placement,
} from "./protocol.ts";
import { CommandShell, type ShellResult } from "./shell.ts";
import { ProcessTable, prefix } from "./system.ts";

export interface RunOptions extends Endpoint {
  command: string;
  /** Words after `--` that the caller's shell already split, when there were several; null for one quoted string. */
  commandWords: string[] | null;
  /** 0 for the default weight of the command. */
  weight: number;
  gpu: boolean;
  shell: ShellName | "";
  timeoutMinutes: number;
  /** --after: the job that must end first; 0 for none. */
  after: number;
  keepIfFailed: boolean;
}

/** Ends the process with a usage error. */
export function usageError(message: string): never {
  process.stderr.write(`${message}\nRun with --help for the usage.\n`);
  process.exit(UsageExitCode);
}

/**
 * Windows: stops the process trees that `parent` started, and not `parent` itself; falls back to the whole tree when
 * the processes cannot be read or `parent` started nothing yet.
 */
async function stopUnder(parent: ChildProcess): Promise<void> {
  const table = await ProcessTable.query();
  const children = table === null ? [] : table.childrenOf(parent.pid);
  if (children.length === 0) {
    ProcessTable.killChild(parent);
    return;
  }
  children.forEach(ProcessTable.killTree);
}

/**
 * --gpu, once the turn came: runs this script again for the same command under gpu-lock.js, which then registers the
 * command's slots as a GPU holder (granted at once). Resolves to its exit like CommandShell.run; `onChild` receives
 * gpu-lock.js.
 */
function runGpuLocked(
  options: RunOptions,
  weight: number,
  onChild: (child: ChildProcess) => void,
): Promise<ShellResult> {
  const innerArgs = [`"${process.execPath}"`, `"${EntryScript}"`, "run", "--weight", String(weight)];
  innerArgs.push("--port", String(options.port));
  if (options.timeoutMinutes > 0) {
    innerArgs.push("--timeout-minutes", String(options.timeoutMinutes));
  }
  innerArgs.push("--command-from-env");
  const env = {
    ...process.env,
    [InGpuLockVariable]: "1",
    [CommandVariable]: options.command,
    [ShellVariable]: CommandShell.resolve(options.shell).name, // gpu-lock.js runs the inner client through cmd.exe
  };
  const args = [GpuLockScript];
  if (options.timeoutMinutes > 0) {
    args.push("--timeout-minutes", String(options.timeoutMinutes));
  }
  args.push("--", innerArgs.join(" "));
  return new Promise((resolve) => {
    // Unix: its own process group, so that a cancel stops gpu-lock.js and everything under it (Windows: taskkill /T)
    const child = spawn(process.execPath, args, { stdio: "inherit", env, detached: process.platform !== "win32" });
    child.on("error", (error) => {
      prefix(`could not start gpu-lock.js: ${error.message}`);
      resolve({ code: 1, signaled: false });
    });
    if (child.pid !== undefined) {
      onChild(child);
    }
    child.on("close", (code, signal) => resolve({ code: code === null ? 1 : code, signaled: signal !== null }));
  });
}

export async function run(options: RunOptions): Promise<void> {
  if (options.commandWords !== null) {
    const shell = CommandShell.resolve(options.shell);
    if (shell.isPowerShell && options.commandWords.includes("--%")) {
      // PowerShell cannot pass a literal --% to a program, quoted or not: it is its stop-parsing token
      usageError("PowerShell cannot pass --% on; give the command as one string or choose another --shell.");
    }
    options.command = shell.join(options.commandWords);
    options.shell = shell.name; // the --gpu path re-runs this script with the command as one string
  }
  const placement: Placement = {
    weight: options.weight > 0 ? options.weight : 1,
    pin: null,
    before: null,
  };
  // This client runs inside the GPU lock: the --gpu wrapper started it, or gpu-lock.js runs the command that called it
  const inGpuLock = process.env[InGpuLockVariable] === "1" || Boolean(process.env[GpuLockHeldVariable]);

  if (options.after > 0 && (process.env[JobVariable] || inGpuLock)) {
    // Waiting for another job while holding slots or the GPU lock can deadlock: that job may wait for them. (The --gpu
    // wrapper resolves --after before it takes the lock and does not pass it to the client it starts inside.)
    usageError("--after cannot be used inside a queued job or the GPU lock; queue the chain from outside.");
  }
  if (process.env[JobVariable] && (inGpuLock || !options.gpu)) {
    // Inside a queued job: its slot covers this command, with the same node limit.
    const cpus = Number(process.env[CpusVariable]) || 0;
    const command = cpus > 0 ? CommandShell.limitMsbuildNodes(options.command, cpus) : options.command;
    let child: ChildProcess | null = null;
    const stop = (code: number) => (): void => {
      ProcessTable.killChild(child);
      process.exit(code);
    };
    process.on("SIGINT", stop(130));
    process.on("SIGTERM", stop(143));
    const result = await CommandShell.resolve(options.shell).run(command, process.env, (c) => (child = c));
    process.exit(result.code);
  }
  if (options.gpu && !inGpuLock) {
    if (/fuzz-diff|fuzz-differential|long-checks/.test(options.command)) {
      // They take the GPU lock themselves for their GPU steps; holding it here would make them wait for ever
      usageError("fuzz-diff and long-checks take the GPU lock themselves: queue them without --gpu.");
    }
    const cpuOnly = new MachineProfile().cpuOnlyTestProjects(options.command);
    if (cpuOnly.length > 0) {
      // A hint, not a refusal: the command may use the GPU in a way the project file does not show
      prefix(
        `--gpu, but ${cpuOnly.join(", ")} ${cpuOnly.length === 1 ? "runs" : "run"} on the CPU only ` +
          "(GSharpTargetDialect Cpu): " +
          "this job waits for the GPU and keeps the GPU jobs behind it waiting; queue it without --gpu",
      );
    }
    // Wait for the turn at the GPU in the scheduler (in order, visible and cancellable), then take the lock: the GPU
    // lock alone gives it to whichever waiter polls first. An --after predecessor blocks the turn there too.
    await new QueuedRun(options, placement, false, true).run();
    return;
  }
  await new QueuedRun(options, placement, inGpuLock, false).run();
}

/**
 * The queued path of run: register, wait for the grant, run the command, report its end. With `gpuTurn`, the job waits
 * for its turn at the GPU instead of slots, and runs gpu-lock.js with an inner client that registers the command's
 * slots as a GPU holder.
 */
class QueuedRun {
  private readonly options: RunOptions;
  private readonly placement: Placement;
  private readonly inGpuLock: boolean;
  private readonly gpuTurn: boolean;
  private readonly launcher: SchedulerLauncher;
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
  /** The scheduler cancelled this job: its end is reported as cancelled, not as a failure. */
  private wasCancelled = false;

  constructor(options: RunOptions, placement: Placement, inGpuLock: boolean, gpuTurn: boolean) {
    this.options = options;
    this.placement = placement;
    this.inGpuLock = inGpuLock;
    this.gpuTurn = gpuTurn;
    this.launcher = new SchedulerLauncher(options);
  }

  async run(): Promise<void> {
    process.on("SIGINT", this.onSignal("SIGINT", 130));
    process.on("SIGTERM", this.onSignal("SIGTERM", 143));
    process.on("SIGHUP", this.onSignal("SIGHUP", 129));

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

  // completed: the command ran to its own end (not stopped by a signal or a cancel), so its run time may be recorded
  private finish(code: number, completed = false): void {
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
        void this.launcher.reportEnd(jobId, completed, code, this.wasCancelled).finally(() => process.exit(code));
        return;
      }
      process.exit(code);
    }
    // "done" first, so that the scheduler frees the slot at once instead of treating the job as a lost client whose
    // command may still run
    send(current, {
      version: BaseProtocolVersion,
      type: "done",
      completed,
      exitCode: code,
      cancelled: this.wasCancelled,
    });
    if (!ran || jobId === 0 || !this.receipts) {
      setTimeout(() => process.exit(code), 1000).unref();
      current.end(() => process.exit(code));
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
        setTimeout(() => process.exit(code), 1000).unref();
        current.end(() => process.exit(code));
        return;
      }
      current.destroy();
      void this.launcher.reportEnd(this.jobId, completed, code, this.wasCancelled).finally(() => process.exit(code));
    };
    setTimeout(() => this.doneReceipt?.(false), 2000);
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
      ProcessTable.killChild(this.child);
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
      const { code, signaled } = await runGpuLocked(options, placement.weight, (c) => this.started(c));
      this.finish(code, !signaled);
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
    const shell = CommandShell.resolve(options.shell);
    const { code, signaled } = await shell.run(command, env, (c) => this.started(c));
    this.finish(code, !signaled); // a run a signal cut short is not recorded
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
    this.wasCancelled = true;
    const reason = message.reason ? ` (${String(message.reason)})` : "";
    prefix(
      `job #${numberField(message, "id", this.jobId)} was cancelled${reason}` +
        (this.state === "running" ? "; stopping the command" : ""),
    );
    const running = this.child;
    if (running !== null && running.exitCode === null && running.signalCode === null) {
      // Leave (and so free the slot) once the command has really ended
      const leave = (): void => this.finish(CancelledExitCode);
      running.once("close", leave);
      setTimeout(leave, 10000).unref();
      if (this.gpuTurn && process.platform === "win32") {
        // Stop what runs under gpu-lock.js, not gpu-lock.js itself (taskkill /F would leave its lock behind): it then
        // releases the lock and ends
        void stopUnder(running);
      } else {
        ProcessTable.killChild(running);
      }
    } else {
      this.finish(CancelledExitCode);
    }
  }

  private onError(message: Incoming): void {
    if (this.gpuTurn && message.code === "version" && this.state === "waiting" && this.options.after === 0) {
      // A scheduler of an older checkout has no GPU turns: take the GPU lock as such schedulers expect (the command
      // then registers as a GPU holder), without the fair order
      prefix("the scheduler of an older checkout has no GPU turns; waiting for the GPU lock directly");
      this.registered = false;
      this.state = "running";
      void runGpuLocked(this.options, this.placement.weight, (c) => (this.child = c)).then(({ code }) =>
        process.exit(code),
      );
      return;
    }
    prefix(`scheduler: ${String(message.message)}${message.code === "version" ? `; ${OlderSchedulerHint}` : ""}`);
    if (this.state === "waiting") {
      process.exit(message.code === "unknown-job" ? UsageExitCode : 1);
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
        this.socket = await this.launcher.connectOrStart();
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
        process.exit(1);
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
