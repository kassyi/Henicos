// What the job queue reads and changes outside itself: the state directory, and the processes of the machine
// (ProcessTable: a snapshot of the Windows processes, liveness, stopping a process tree).

import * as path from "node:path";
import { spawn, spawnSync, type ChildProcess } from "node:child_process";
import { StateDirectoryVariable } from "./constants.ts";
import { errorCode, errorMessage } from "./history.ts";

/** Writes a line to stderr, marked as the queue's own output among the command's. */
export function prefix(text: string): void {
  process.stderr.write(`[job-queue] ${text}\n`);
}

/**
 * The main checkout's tmp/job-queue, whichever worktree runs the script (as gpu-lock.js finds its lock);
 * GSHARP_JOB_QUEUE_STATE_DIR overrides it (self-test).
 */
export function resolveStateDirectory(): string {
  const configured = process.env[StateDirectoryVariable];
  if (configured) {
    return path.resolve(configured);
  }
  let base = path.resolve(import.meta.dirname, "..", "..", "..");
  const git = spawnSync("git", ["rev-parse", "--path-format=absolute", "--git-common-dir"], {
    cwd: import.meta.dirname,
    encoding: "utf8",
    windowsHide: true,
  });
  if (!git.error && git.status === 0) {
    const commonDir = git.stdout.trim();
    if (commonDir !== "" && path.isAbsolute(commonDir)) {
      base = path.dirname(path.resolve(commonDir));
    }
  }
  return path.join(base, "tmp", "job-queue");
}

/**
 * A directory as the key of the scheduler's round-robin: absolute, and on Windows in one letter case (git prints
 * `P:/x`, a shell may be in `p:\x`). An empty directory stays empty.
 */
export function worktreeKey(directory: string): string {
  if (directory === "") {
    return "";
  }
  const resolved = path.resolve(directory);
  return process.platform === "win32" ? resolved.toLowerCase() : resolved;
}

/** The root of the worktree that holds `directory` (git rev-parse --show-toplevel), or the directory itself. */
export function resolveWorktree(directory: string): string {
  const git = spawnSync("git", ["rev-parse", "--show-toplevel"], {
    cwd: directory,
    encoding: "utf8",
    windowsHide: true,
  });
  const top = !git.error && git.status === 0 ? git.stdout.trim() : "";
  return worktreeKey(top !== "" ? top : directory);
}

/** Slack for comparing a process creation time with the recorded spawn time (as in gpu-lock.js). */
const SpawnToleranceMs = 5000;

export interface ProcessInfo {
  ppid: number;
  /** Creation time in ms since the epoch, or -1 when Windows did not report it. */
  created: number;
}

/** The processes of the machine: a snapshot read on Windows, and the liveness and stopping of processes anywhere. */
export class ProcessTable {
  /** pid -> { ppid, created } */
  readonly processes: ReadonlyMap<number, ProcessInfo>;

  constructor(processes: ReadonlyMap<number, ProcessInfo>) {
    this.processes = processes;
  }

  /**
   * Windows: reads every process as pid -> { ppid, created } through PowerShell, without blocking the scheduler.
   * Resolves to null when the query fails or looks incomplete; callers then treat commands as alive.
   */
  static query(): Promise<ProcessTable | null> {
    const script =
      "Get-CimInstance Win32_Process | ForEach-Object { $c = -1; " +
      "if ($_.CreationDate) { $c = ([DateTimeOffset]$_.CreationDate).ToUnixTimeMilliseconds() }; " +
      '"$($_.ProcessId) $($_.ParentProcessId) $c" }';
    return new Promise((resolve) => {
      let output = "";
      const query = spawn("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], {
        windowsHide: true,
      });
      const timer = setTimeout(() => query.kill(), 60000);
      query.stdout.setEncoding("utf8");
      query.stdout.on("data", (chunk: string) => (output += chunk));
      query.on("error", () => resolve(null));
      query.on("close", (code) => {
        clearTimeout(timer);
        if (code !== 0) {
          resolve(null);
          return;
        }
        const processes = new Map<number, ProcessInfo>();
        for (const line of output.split(/\r?\n/)) {
          const [pid, ppid, created] = line.trim().split(" ").map(Number);
          if (Number.isInteger(pid) && Number.isInteger(ppid) && Number.isFinite(created)) {
            processes.set(pid as number, { ppid: ppid as number, created: created as number });
          }
        }
        resolve(processes.has(process.pid) ? new ProcessTable(processes) : null);
      });
    });
  }

  /** The processes whose parent is `pid`. */
  childrenOf(pid: number | undefined): number[] {
    return [...this.processes].filter(([, info]) => info.ppid === pid).map(([child]) => child);
  }

  /**
   * The live processes that belong to a command started as `pid` at `spawnedAt`: the shell itself and its direct
   * children (Win32_Process keeps a dead parent's pid in ParentProcessId; when the client dies, Windows ends the shell
   * but not what it started). Creation times exclude processes that only reuse the pid.
   */
  commandProcesses(pid: number, spawnedAt: number): number[] {
    const earliest = spawnedAt - SpawnToleranceMs;
    const latest = spawnedAt + SpawnToleranceMs;
    const found: number[] = [];
    let reusedFrom = Infinity;
    const shell = this.processes.get(pid);
    if (shell !== undefined) {
      if (shell.created < 0 || shell.created <= latest) {
        found.push(pid);
      } else {
        reusedFrom = shell.created;
      }
    }
    for (const [child, info] of this.processes) {
      if (
        child !== pid &&
        info.ppid === pid &&
        (info.created < 0 || (info.created >= earliest && info.created < reusedFrom))
      ) {
        found.push(child);
      }
    }
    return found;
  }

  /** True unless the process is known to be gone. EPERM means alive. */
  static isAlive(pid: number): boolean {
    return Number.isInteger(pid) && pid > 0 && ProcessTable.signalZero(pid);
  }

  /** Unix: true unless the command's process group is known to be gone. EPERM means alive. */
  static isGroupAlive(pid: number): boolean {
    return Number.isInteger(pid) && pid > 0 && ProcessTable.signalZero(-pid);
  }

  private static signalZero(target: number): boolean {
    if (!Number.isInteger(target) || target === 0) {
      return false;
    }
    try {
      process.kill(target, 0);
      return true;
    } catch (error) {
      return errorCode(error) === "EPERM";
    }
  }

  /** Stops a process and everything it started (Unix: its process group, which the command leads). */
  static killTree(pid: number | undefined): void {
    if (pid === undefined || !Number.isInteger(pid) || pid <= 0) {
      return;
    }
    if (process.platform === "win32") {
      spawnSync("taskkill", ["/PID", String(pid), "/T", "/F"], { stdio: "ignore", windowsHide: true });
      return;
    }
    try {
      process.kill(-pid, "SIGTERM");
    } catch (error) {
      if (errorCode(error) !== "ESRCH") {
        prefix(`could not stop the command's process group ${pid}: ${errorMessage(error)}`);
      }
    }
  }

  static killChild(child: ChildProcess | null): void {
    if (child !== null && child.exitCode === null) {
      ProcessTable.killTree(child.pid);
    }
  }
}
