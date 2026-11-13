// The client side of the job queue: run (wait for a slot, run the command, give the slot back), the status, cancel,
// move and set-weight requests, stop, and starting the scheduler on demand.

import * as fs from "node:fs";
import * as net from "node:net";
import * as path from "node:path";
import { spawn, spawnSync, type ChildProcess } from "node:child_process";
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
  LogRotateBytes,
  LostExitCode,
  MsbuildArgsVariable,
  NoticeIntervalMs,
  BaseProtocolVersion,
  ShellVariable,
  TimedOutExitCode,
  UsageExitCode,
  type Policy,
  type ShellName,
} from "./constants.ts";
import { errorCode, errorMessage } from "./history.ts";
import {
  numberField,
  onMessages,
  send,
  wireVersion,
  type ClientMessage,
  type Incoming,
  type Outcome,
  type Placement,
  type StatusJob,
  type StatusReply,
} from "./protocol.ts";
import { joinWords, limitMsbuildNodes, resolveShell, runShell, type ShellResult } from "./shell.ts";
import { killChild, killProcessTree, prefix, queryWindowsProcesses, resolveStateDirectory } from "./system.ts";

/** Where the scheduler listens, and how a client starts it when none does. */
export interface Endpoint {
  port: number;
  slots: number;
  /** The memory pool for test runs, in MB. */
  memoryMb: number;
  policy: Policy;
}

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

/** Starts the scheduler detached in the state directory (never a worktree), its output appended to server.log. */
function startScheduler(endpoint: Endpoint): void {
  const stateDirectory = resolveStateDirectory();
  fs.mkdirSync(stateDirectory, { recursive: true });
  const logPath = path.join(stateDirectory, "server.log");
  try {
    if (fs.statSync(logPath).size > LogRotateBytes) {
      fs.renameSync(logPath, `${logPath}.1`);
    }
  } catch (error) {
    if (errorCode(error) !== "ENOENT") {
      prefix(`could not rotate ${logPath}: ${errorMessage(error)}`);
    }
  }
  const args = [
    EntryScript,
    "serve",
    "--port",
    String(endpoint.port),
    "--slots",
    String(endpoint.slots),
    "--memory-mb",
    String(endpoint.memoryMb),
    "--policy",
    endpoint.policy,
  ];
  args.push("--state-dir", stateDirectory);
  if (process.platform === "win32" && startThroughWmi(args, stateDirectory, logPath)) {
    return;
  }
  const logFd = fs.openSync(logPath, "a");
  const child = spawn(process.execPath, args, {
    cwd: stateDirectory,
    detached: true,
    stdio: ["ignore", logFd, logFd],
    windowsHide: true,
  });
  child.unref();
  fs.closeSync(logFd);
}

/**
 * Windows: starts the scheduler through WMI (Win32_Process.Create), so that it does not belong to the caller's job
 * object. Antigravity runs each command in a job object and ends every process in it when the command ends, which
 * would stop a scheduler started as a plain child. Returns false when WMI fails; the caller then spawns it directly.
 */
function startThroughWmi(args: string[], stateDirectory: string, logPath: string): boolean {
  const quote = (text: string): string => `"${text}"`;
  // cmd.exe treats & ^ ( ) and spaces in a path specially: quote every argument (it still expands %VAR% in quotes)
  const serve = [quote(process.execPath), ...args.map(quote)].join(" ");
  const commandLine = `cmd.exe /d /s /c "${serve} >> ${quote(logPath)} 2>&1"`;
  // A CIM error must fail the script (not exit 0 through `exit $null`), so that the caller falls back
  const script =
    "$ErrorActionPreference = 'Stop'; " +
    "$s = New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{ ShowWindow = [uint16]0 }; " +
    "$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ " +
    "CommandLine = $env:GSHARP_JOB_QUEUE_WMI_COMMAND; CurrentDirectory = $env:GSHARP_JOB_QUEUE_WMI_DIRECTORY; " +
    "ProcessStartupInformation = $s }; " +
    "if ($null -eq $r -or $null -eq $r.ReturnValue) { exit 1 }; exit [int]$r.ReturnValue";
  const result = spawnSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], {
    windowsHide: true,
    timeout: 8000, // well inside the 15 s that connectOrStart waits, so that the fallback still has time
    env: { ...process.env, GSHARP_JOB_QUEUE_WMI_COMMAND: commandLine, GSHARP_JOB_QUEUE_WMI_DIRECTORY: stateDirectory },
  });
  if (result.error || result.status !== 0) {
    const why = result.error ? result.error.message : `status ${result.status}`;
    prefix(`could not start the scheduler through WMI (${why}); starting it as a child`);
    return false;
  }
  return true;
}

/** Connects to the scheduler; resolves to the socket, or null when no scheduler listens. */
export function connect(port: number): Promise<net.Socket | null> {
  return new Promise((resolve, reject) => {
    const socket = net.connect(port, "127.0.0.1");
    socket.once("connect", () => {
      socket.removeAllListeners("error");
      // A reset after the caller is done with the socket must not crash the process; callers add their own handlers.
      socket.on("error", () => socket.destroy());
      resolve(socket);
    });
    socket.once("error", (error: NodeJS.ErrnoException) => {
      if (error.code === "ECONNREFUSED") {
        resolve(null);
      } else {
        reject(error);
      }
    });
  });
}

const delay = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms));

/** Connects, starting the scheduler when none listens, for up to 15 seconds. */
async function connectOrStart(endpoint: Endpoint): Promise<net.Socket> {
  let deadline = Date.now() + 15000;
  let extended = false;
  let started = 0;
  for (;;) {
    const socket = await connect(endpoint.port);
    if (socket !== null) {
      return socket;
    }
    if (Date.now() - started > 5000) {
      startScheduler(endpoint);
      started = Date.now();
      // Starting can take seconds (WMI, then a fallback): give the first scheduler time to listen, once only, so that
      // a scheduler that never listens still ends the wait
      if (!extended) {
        extended = true;
        deadline = Math.max(deadline, started + 8000);
      }
    }
    if (Date.now() > deadline) {
      throw new Error(`could not reach or start the scheduler on 127.0.0.1:${endpoint.port}`);
    }
    await delay(200);
  }
}

/** The hint after a version refusal: a scheduler from an older checkout runs and lacks what this message needs. */
const OlderSchedulerHint =
  'the running scheduler comes from an older checkout and lacks this operation; run "job-queue.js stop" when no job ' +
  "runs (running jobs re-register with the next one), or wait until it exits after 30 idle minutes";

/** Sends one message and resolves to the last reply when the scheduler closes, or null when none listens. */
export async function request(
  port: number,
  message: ClientMessage,
  version = wireVersion(message),
): Promise<Incoming | null> {
  const socket = await connect(port);
  if (socket === null) {
    return null;
  }
  return new Promise((resolve, reject) => {
    let reply: Incoming | null = null;
    onMessages(socket, (m) => {
      reply = m;
    });
    socket.on("error", reject);
    socket.on("close", () => resolve(reply));
    send(socket, { version, ...message });
  });
}

/**
 * Reports the end of a job whose connection was lost (the scheduler restarts) to the next scheduler, starting one when
 * none listens (nobody else may start it soon, and the jobs that wait for this one need the outcome). Gives up after a
 * few tries: the command has ended either way.
 */
async function reportEnd(
  endpoint: Endpoint,
  id: number,
  completed: boolean,
  exitCode: number,
  cancelled = false,
): Promise<void> {
  for (let attempt = 0; attempt < 3; attempt++) {
    const socket = await connectOrStart(endpoint).catch(() => null);
    if (socket === null) {
      return;
    }
    const reply = await new Promise<Incoming | null>((resolve) => {
      let last: Incoming | null = null;
      onMessages(socket, (m) => {
        last = m;
      });
      socket.on("close", () => resolve(last));
      const report: ClientMessage = { type: "report", id, completed, exitCode, cancelled };
      send(socket, { version: wireVersion(report), ...report });
    });
    // "reported", or an error from an older scheduler that cannot take it; null: the scheduler went away meanwhile
    if (reply !== null) {
      return;
    }
    await delay(300);
  }
}

/**
 * Windows: stops the process trees that `parent` started, and not `parent` itself; falls back to the whole tree when
 * the processes cannot be read or `parent` started nothing yet.
 */
async function stopUnder(parent: ChildProcess): Promise<void> {
  const processes = await queryWindowsProcesses();
  const children =
    processes === null ? [] : [...processes].filter(([, info]) => info.ppid === parent.pid).map(([pid]) => pid);
  if (children.length === 0) {
    killChild(parent);
    return;
  }
  children.forEach(killProcessTree);
}

/**
 * --gpu, once the turn came: runs this script again for the same command under gpu-lock.js, which then registers the
 * command's slots as a GPU holder (granted at once). Resolves to its exit like runShell; `onChild` receives
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
    [ShellVariable]: resolveShell(options.shell), // gpu-lock.js runs the inner client through cmd.exe
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
    const shell = resolveShell(options.shell);
    if ((shell === "pwsh" || shell === "powershell") && options.commandWords.includes("--%")) {
      // PowerShell cannot pass a literal --% to a program, quoted or not: it is its stop-parsing token
      usageError("PowerShell cannot pass --% on; give the command as one string or choose another --shell.");
    }
    options.command = joinWords(shell, options.commandWords);
    options.shell = shell; // the --gpu path re-runs this script with the command as one string
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
    const command = cpus > 0 ? limitMsbuildNodes(options.command, cpus) : options.command;
    let child: ChildProcess | null = null;
    const stop = (code: number) => (): void => {
      killChild(child);
      process.exit(code);
    };
    process.on("SIGINT", stop(130));
    process.on("SIGTERM", stop(143));
    const result = await runShell(command, resolveShell(options.shell), process.env, (c) => (child = c));
    process.exit(result.code);
  }
  if (options.gpu && !inGpuLock) {
    if (/fuzz-diff|fuzz-differential|long-checks/.test(options.command)) {
      // They take the GPU lock themselves for their GPU steps; holding it here would make them wait for ever
      usageError("fuzz-diff and long-checks take the GPU lock themselves: queue them without --gpu.");
    }
    // Wait for the turn at the GPU in the scheduler (in order, visible and cancellable), then take the lock: the GPU
    // lock alone gives it to whichever waiter polls first. An --after predecessor blocks the turn there too.
    await runQueued(options, placement, false, true);
    return;
  }
  await runQueued(options, placement, inGpuLock, false);
}

/**
 * The queued path of run: register, wait for the grant, run the command, report its end. With `gpuTurn`, the job waits
 * for its turn at the GPU instead of slots, and runs gpu-lock.js with an inner client that registers the command's
 * slots as a GPU holder.
 */
async function runQueued(
  options: RunOptions,
  placement: Placement,
  inGpuLock: boolean,
  gpuTurn: boolean,
): Promise<void> {
  const queuedAt = Date.now();
  let state: "waiting" | "running" | "done" = "waiting";
  let child: ChildProcess | null = null;
  let jobId = 0;
  let spawnedAt = 0;
  let socket: net.Socket | null = null;
  let registered = true; // false when the scheduler refused this client's protocol version
  let lastNotice = -Infinity;
  let lastPosition = 0;
  let lastStartAt: number | null = null;

  /** Set once "done" was sent: called with true on the scheduler's receipt, with false when none comes. */
  let doneReceipt: ((received: boolean) => void) | null = null;
  /**
   * The scheduler gives a receipt for "done" (protocol 3); one of an older checkout does not, and is not waited for.
   */
  let receipts = false;
  /** The scheduler cancelled this job: its end is reported as cancelled, not as a failure. */
  let wasCancelled = false;
  // completed: the command ran to its own end (not stopped by a signal or a cancel), so its run time may be recorded
  const finish = (code: number, completed = false): void => {
    if (state === "done") {
      return;
    }
    const ran = state === "running";
    state = "done";
    const current = socket;
    if (current === null || current.destroyed) {
      if (ran && jobId > 0) {
        // No scheduler listens right now (it restarts): report the outcome to the next one, for the jobs that wait for
        // this one, then exit
        void reportEnd(options, jobId, completed, code, wasCancelled).finally(() => process.exit(code));
        return;
      }
      process.exit(code);
    }
    // "done" first, so that the scheduler frees the slot at once instead of treating the job as a lost client whose
    // command may still run
    send(current, { version: BaseProtocolVersion, type: "done", completed, exitCode: code, cancelled: wasCancelled });
    if (!ran || jobId === 0 || !receipts) {
      setTimeout(() => process.exit(code), 1000).unref();
      current.end(() => process.exit(code));
      return;
    }
    // Then the scheduler's receipt: one that is stopping does not save the outcome (and one of an older checkout sends
    // no receipt), so without it the outcome goes to the next scheduler as a report, for the jobs that wait for this
    // one
    let left = false;
    doneReceipt = (received: boolean): void => {
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
      void reportEnd(options, jobId, completed, code, wasCancelled).finally(() => process.exit(code));
    };
    setTimeout(() => doneReceipt?.(false), 2000);
  };
  const onSignal = (signal: string, code: number) => (): void => {
    prefix(`${signal}: stopping the command and leaving the queue`);
    const running = child as ChildProcess | null;
    if (gpuTurn && process.platform === "win32" && running !== null && running.exitCode === null) {
      // The console delivers Ctrl+C to gpu-lock.js itself, which releases its lock and ends; killing it first would
      // leave the lock behind. Leave once it has ended.
      running.once("close", () => finish(code));
      return;
    }
    killChild(child);
    finish(code);
  };
  process.on("SIGINT", onSignal("SIGINT", 130));
  process.on("SIGTERM", onSignal("SIGTERM", 143));
  process.on("SIGHUP", onSignal("SIGHUP", 129));

  if (options.timeoutMinutes > 0) {
    setTimeout(
      () => {
        if (state === "waiting") {
          prefix(`gave up after ${options.timeoutMinutes} minutes of waiting; see "job-queue.js status"`);
          finish(TimedOutExitCode);
        }
      },
      options.timeoutMinutes * 60 * 1000,
    ).unref();
  }

  const start = async (granted: Incoming): Promise<void> => {
    state = "running";
    jobId = numberField(granted, "id", jobId);
    if (gpuTurn) {
      prefix(
        `the GPU turn of #${jobId} came after ${Math.round((Date.now() - queuedAt) / 1000)}s; taking the GPU lock`,
      );
      const { code, signaled } = await runGpuLocked(options, placement.weight, (c) => {
        child = c;
        spawnedAt = Date.now();
        if (c.pid !== undefined) {
          send(socket, { version: BaseProtocolVersion, type: "started", commandPid: c.pid, spawnedAt });
        }
      });
      finish(code, !signaled);
      return;
    }
    const cpus = numberField(granted, "cpus", 1);
    const command = limitMsbuildNodes(options.command, cpus);
    const waited = Math.round((Date.now() - queuedAt) / 1000);
    prefix(`granted #${jobId} after ${waited}s (weight ${placement.weight}, ${cpus} MSBuild nodes)`);
    const env = {
      ...process.env,
      [JobVariable]: String(jobId),
      [CpusVariable]: String(cpus),
      [MsbuildArgsVariable]: `-m:${cpus}`,
    };
    const { code, signaled } = await runShell(command, resolveShell(options.shell), env, (c) => {
      child = c;
      spawnedAt = Date.now();
      if (c.pid !== undefined) {
        send(socket, { version: BaseProtocolVersion, type: "started", commandPid: c.pid, spawnedAt });
      }
    });
    finish(code, !signaled); // a run a signal cut short is not recorded
  };

  const onMessage = (message: Incoming): void => {
    switch (message.type) {
      case "queued": {
        jobId = numberField(message, "id", jobId);
        const outlook = message.state === "waiting" ? describeOutlook(message, Date.now()) : "";
        // The first waiting notice says the same: skip it
        lastPosition = numberField(message, "position");
        lastStartAt = typeof message.startAt === "number" ? message.startAt : null;
        lastNotice = Date.now();
        prefix(`queued #${jobId}${outlook ? `: ${outlook}` : ""}; "job-queue.js status ${jobId}" shows it`);
        break;
      }
      case "waiting": {
        jobId = numberField(message, "id", jobId);
        const now = Date.now();
        const startAt = typeof message.startAt === "number" ? message.startAt : null;
        const position = numberField(message, "position");
        const moved = Math.abs((startAt ?? 0) - (lastStartAt ?? 0)) >= ForecastNoticeMs;
        if (position !== lastPosition || moved || now - lastNotice >= NoticeIntervalMs) {
          lastPosition = position;
          lastStartAt = startAt;
          lastNotice = now;
          const outlook = describeOutlook(message, now);
          const slots = `${numberField(message, "used")}/${numberField(message, "capacity")} slots in use`;
          prefix(
            `waiting #${jobId}: position ${position}, ${slots} ` +
              `(${Math.round((now - queuedAt) / 1000)}s)${outlook ? `; ${outlook}` : ""}`,
          );
        }
        break;
      }
      case "granted":
        receipts = numberField(message, "protocol") >= 3;
        if (state === "waiting") {
          void start(message);
        }
        break;
      case "done-ok":
        doneReceipt?.(true);
        break;
      case "adopted": {
        receipts = numberField(message, "protocol") >= 3;
        const id = numberField(message, "id", jobId);
        if (id !== jobId) {
          prefix(`re-registered the running job as #${id} (was #${jobId})`);
        }
        jobId = id;
        break;
      }
      case "updated":
        // A move or set-weight: brought along when this client re-registers after a scheduler restart. A GPU turn
        // weighs nothing in the scheduler; it keeps the weight its command registers with.
        if (!gpuTurn) {
          placement.weight = numberField(message, "weight", placement.weight);
        }
        placement.pin = typeof message.pin === "number" ? message.pin : null;
        placement.before = typeof message.before === "number" ? message.before : null;
        break;
      case "cancelled": {
        if (state === "done") {
          break; // the command had already ended
        }
        wasCancelled = true;
        const reason = message.reason ? ` (${String(message.reason)})` : "";
        prefix(
          `job #${numberField(message, "id", jobId)} was cancelled${reason}` +
            (state === "running" ? "; stopping the command" : ""),
        );
        const running = child as ChildProcess | null;
        if (running !== null && running.exitCode === null && running.signalCode === null) {
          // Leave (and so free the slot) once the command has really ended
          const leave = (): void => finish(CancelledExitCode);
          running.once("close", leave);
          setTimeout(leave, 10000).unref();
          if (gpuTurn && process.platform === "win32") {
            // Stop what runs under gpu-lock.js, not gpu-lock.js itself (taskkill /F would leave its lock behind): it
            // then releases the lock and ends
            void stopUnder(running);
          } else {
            killChild(running);
          }
        } else {
          finish(CancelledExitCode);
        }
        break;
      }
      case "error": {
        if (gpuTurn && message.code === "version" && state === "waiting" && options.after === 0) {
          // A scheduler of an older checkout has no GPU turns: take the GPU lock as such schedulers expect (the
          // command then registers as a GPU holder), without the fair order
          prefix("the scheduler of an older checkout has no GPU turns; waiting for the GPU lock directly");
          registered = false;
          state = "running";
          void runGpuLocked(options, placement.weight, (c) => (child = c)).then(({ code }) => process.exit(code));
          break;
        }
        prefix(`scheduler: ${String(message.message)}${message.code === "version" ? `; ${OlderSchedulerHint}` : ""}`);
        if (state === "waiting") {
          process.exit(message.code === "unknown-job" ? UsageExitCode : 1);
        }
        if (message.code === "version") {
          registered = false; // the command keeps running, unregistered; do not reconnect again
        }
        break;
      }
      default:
        break;
    }
  };

  // The state changes while attach awaits (finish runs from the command's close): read it through a call, which the
  // compiler does not narrow
  const ended = (): boolean => state === "done";
  const attach = async (): Promise<void> => {
    for (;;) {
      if (ended()) {
        return; // the command ended while this client reconnected: finish reports it (reportEnd)
      }
      try {
        socket = await connectOrStart(options);
        break;
      } catch (error) {
        if (state === "running") {
          // The command runs regardless; keep trying to register it so that the next jobs count its weight.
          await delay(5000);
          continue;
        }
        if (ended()) {
          return;
        }
        prefix(errorMessage(error));
        process.exit(1);
      }
    }
    if (ended()) {
      // The command ended during the connect: registering now would queue a job that no longer exists under its id
      socket.destroy();
      socket = null;
      return;
    }
    const current = socket;
    const running = child as ChildProcess | null;
    const registration: ClientMessage = {
      type: state === "running" ? "adopt" : "request",
      weight: placement.weight,
      gpu: inGpuLock,
      pid: process.pid,
      commandPid: running !== null && running.pid !== undefined ? running.pid : 0,
      spawnedAt,
      cwd: process.cwd(),
      command: options.command,
      queuedAt,
      id: jobId, // keeps the job id across a scheduler restart
      pin: placement.pin,
      before: placement.before,
      after: options.after > 0 ? options.after : null,
      keepIfFailed: options.keepIfFailed,
      gpuTurn,
    };
    send(current, { version: wireVersion(registration), ...registration });
    onMessages(current, onMessage);
    current.on("error", () => current.destroy());
    current.on("close", () => {
      if (state === "done" && socket === current) {
        doneReceipt?.(false); // closed before the receipt: the scheduler went away with "done" unsaved
      }
      if (state !== "done" && socket === current) {
        socket = null;
        if (!registered) {
          return;
        }
        prefix(`lost the scheduler; ${state === "running" ? "re-registering the running job" : "queueing again"}`);
        // Running jobs re-register first, inside the scheduler's startup grace; waiting ones queue a little later.
        setTimeout(() => void attach(), state === "running" ? 100 : 800);
      }
    });
  };

  await attach();
}

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
    job.memoryMb ? ` ${gigabytes(job.memoryMb)}GB` : "",
    job.orphan ? " client-lost" : "",
    job.pin != null ? " pinned" : "",
    job.before != null ? ` before #${job.before}` : "",
    job.after != null ? ` after #${job.after}${job.keepIfFailed ? " (even if it fails)" : ""}` : "",
  ].join("");
  const outlook = describeOutlook(job, now);
  const blocked = job.blockedBy != null ? `waits for #${job.blockedBy} to end; ` : "";
  const detail = outlook || blocked ? `\n      ${blocked}${outlook}` : "";
  return `  #${job.id} w${job.weight}${flags} ${minutes}m pid ${job.pid} ${job.cwd}${detail}\n      ${job.command}`;
}

/** "2 ahead, start ~20:31 (in 6m), end ~20:35 (in 10m)" for a waiting job, "end ~20:35 (in 4m)" for a running one. */
function describeOutlook(outlook: object, now: number): string {
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
  const end = `expected end ~${clock(endAt)} (in ${minutes(endAt - now)})`;
  if (view.state === "running") {
    return end;
  }
  return `${String(view.ahead)} ahead, expected start ~${clock(startAt)} (in ${minutes(startAt - now)}), ${end}`;
}

function printReplyError(reply: Incoming): void {
  process.stdout.write(`${typeof reply.message === "string" ? reply.message : JSON.stringify(reply)}\n`);
  process.exitCode = 1;
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
        json ? `${JSON.stringify({ id, state: "unknown" })}\n` : `no job #${id} (ended, or never queued)\n`,
      );
      process.exitCode = 1;
      return;
    }
    process.stdout.write(json ? `${JSON.stringify({ now: reply.now, ...job })}\n` : `${formatJob(job, reply.now)}\n`);
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
  lines.push(`running (${reply.running.length}):`);
  reply.running.forEach((job) => lines.push(formatJob(job, now)));
  lines.push(`waiting (${reply.waiting.length}):`);
  reply.waiting.forEach((job) => lines.push(formatJob(job, now)));
  process.stdout.write(`${lines.join("\n")}\n`);
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
    process.stdout.write(id === "all" ? "no jobs to cancel\n" : `no job #${id}\n`);
    process.exitCode = id === "all" ? 0 : 1;
    return;
  }
  process.stdout.write(`cancelled ${cancelled.map((one) => `#${one}`).join(" ")}\n`);
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
    position > 0 ? `#${String(reply.id)} is now at position ${position}\n` : `#${String(reply.id)} was granted\n`,
  );
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
  for (;;) {
    const socket = await connectOrStart(endpoint);
    const reply = await new Promise<Incoming | null>((resolve) => {
      let last: Incoming | null = null;
      onMessages(socket, (m) => {
        last = m;
      });
      socket.on("close", () => resolve(last));
      send(socket, { version: wireVersion({ type: "await", id }), type: "await", id });
    });
    if (reply !== null && reply.type === "ended") {
      const outcome = String(reply.outcome) as Outcome;
      process.stdout.write(json ? `${JSON.stringify({ id, outcome })}\n` : `#${id} ${outcome}\n`);
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
