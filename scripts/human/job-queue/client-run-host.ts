// The effects of a queued run behind one interface (RunHost): the scheduler connection, the command's process and
// the client process. ProcessRunHost is the real one; the self-test drives a QueuedRun with a fake. Also how a
// command's end is reported after a cancel (commandEnd) and how a GPU turn runs its command under gpu-lock.js.

import type * as net from "node:net";
import { spawn, type ChildProcess } from "node:child_process";
import {
  CancelledExitCode,
  CommandVariable,
  EntryScript,
  GpuLockScript,
  InGpuLockVariable,
  ShellVariable,
  TurnVariable,
} from "./constants.ts";
import { SchedulerLauncher } from "./client-connect.ts";
import type { RunOptions } from "./client.ts";
import { CommandShell, type ShellResult } from "./shell.ts";
import { ProcessTable, prefix } from "./system.ts";

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
  turn: number,
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
    [TurnVariable]: turn > 0 ? String(turn) : "",
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

/**
 * How a command's end is reported after a cancel may have reached it: the command's result decides. A command that
 * ended with 0 by itself succeeded, also when the cancel crossed its end (a killed command never ends with 0 without a
 * signal: Windows' taskkill /F ends it with 1, Unix a signal); any other end after a cancel is the cancel's.
 */
export function commandEnd(
  result: ShellResult,
  cancelRequested: boolean,
): { exitCode: number; completed: boolean; cancelled: boolean } {
  const cancelled = cancelRequested && !(result.code === 0 && !result.signaled);
  return {
    exitCode: cancelled ? CancelledExitCode : result.code,
    completed: !result.signaled && !cancelled,
    cancelled,
  };
}

/**
 * The effects of a queued run: the scheduler connection, the command's process and the client process itself. The run
 * passes ProcessRunHost; the self-test passes a fake, so that it can drive a client in its own process.
 */
export interface RunHost {
  /** Connects to the scheduler, starting it when none listens (SchedulerLauncher.connectOrStart). */
  connect(): Promise<net.Socket>;
  /** Reports the end of a job to the next scheduler (SchedulerLauncher.reportEnd). */
  reportEnd(
    id: number,
    completed: boolean,
    exitCode: number,
    cancelled: boolean,
    reporter: { pid: number; queuedAt: number },
  ): Promise<void>;
  /** Runs the command in the caller's shell (CommandShell.run). */
  runCommand(command: string, env: NodeJS.ProcessEnv, onChild: (child: ChildProcess) => void): Promise<ShellResult>;
  /** Runs the command under gpu-lock.js for a GPU turn (`turn`: the run's job id, 0 when unknown). */
  runUnderGpuLock(weight: number, turn: number, onChild: (child: ChildProcess) => void): Promise<ShellResult>;
  /** Stops the command: what runs under gpu-lock.js when `underGpuLock` (Windows), else the whole tree. */
  stop(child: ChildProcess | null, underGpuLock: boolean): void;
  /** Ends the process (process.exit); a fake records the code and returns. */
  exit(code: number): void;
  onSignal(signal: NodeJS.Signals, handler: () => void): void;
}

/** The RunHost of a real run: the scheduler of the endpoint, the caller's shell, this process. */
export class ProcessRunHost implements RunHost {
  private readonly options: RunOptions;
  private readonly launcher: SchedulerLauncher;

  constructor(options: RunOptions) {
    this.options = options;
    this.launcher = new SchedulerLauncher(options);
  }

  connect(): Promise<net.Socket> {
    return this.launcher.connectOrStart();
  }

  reportEnd(
    id: number,
    completed: boolean,
    exitCode: number,
    cancelled: boolean,
    reporter: { pid: number; queuedAt: number },
  ): Promise<void> {
    return this.launcher.reportEnd(id, completed, exitCode, cancelled, reporter);
  }

  runCommand(command: string, env: NodeJS.ProcessEnv, onChild: (child: ChildProcess) => void): Promise<ShellResult> {
    return CommandShell.resolve(this.options.shell).run(command, env, onChild);
  }

  runUnderGpuLock(weight: number, turn: number, onChild: (child: ChildProcess) => void): Promise<ShellResult> {
    return runGpuLocked(this.options, weight, turn, onChild);
  }

  stop(child: ChildProcess | null, underGpuLock: boolean): void {
    if (underGpuLock && process.platform === "win32" && child !== null) {
      // Stop what runs under gpu-lock.js, not gpu-lock.js itself (taskkill /F would leave its lock behind): it then
      // releases the lock and ends
      void stopUnder(child);
    } else {
      ProcessTable.killChild(child);
    }
  }

  exit(code: number): void {
    process.exit(code);
  }

  onSignal(signal: NodeJS.Signals, handler: () => void): void {
    process.on(signal, handler);
  }
}
