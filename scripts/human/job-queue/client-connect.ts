// How a client reaches the scheduler: one-shot requests, and SchedulerLauncher, which connects to the scheduler of an
// endpoint, starts it when none listens, and reports the end of a job to it.

import * as fs from "node:fs";
import * as net from "node:net";
import * as path from "node:path";
import { spawn, spawnSync } from "node:child_process";
import { EntryScript, LogRotateBytes, type Policy } from "./constants.ts";
import { errorCode, errorMessage } from "./history.ts";
import { onMessages, send, wireVersion, type ClientMessage, type Incoming } from "./protocol.ts";
import { prefix, resolveStateDirectory } from "./system.ts";

/** Where the scheduler listens, and how a client starts it when none does. */
export interface Endpoint {
  port: number;
  slots: number;
  /** The memory pool for test runs, in MB. */
  memoryMb: number;
  policy: Policy;
}

export const delay = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms));

/** The hint after a version refusal: a scheduler from an older checkout runs and lacks what this message needs. */
export const OlderSchedulerHint =
  'the running scheduler comes from an older checkout and lacks this operation; run "job-queue.js stop" when no job ' +
  "runs (running jobs re-register with the next one), or wait until it exits after 30 idle minutes";

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

/** Sends one message on a connected socket and resolves to the last reply when the scheduler closes. */
export function exchange(socket: net.Socket, message: ClientMessage): Promise<Incoming | null> {
  return new Promise<Incoming | null>((resolve) => {
    let last: Incoming | null = null;
    onMessages(socket, (m) => {
      last = m;
    });
    socket.on("close", () => resolve(last));
    send(socket, { version: wireVersion(message), ...message });
  });
}

/** The scheduler of an endpoint, started on demand. */
export class SchedulerLauncher {
  readonly endpoint: Endpoint;

  constructor(endpoint: Endpoint) {
    this.endpoint = endpoint;
  }

  /** Connects, starting the scheduler when none listens, for up to 15 seconds. */
  async connectOrStart(): Promise<net.Socket> {
    const endpoint = this.endpoint;
    let deadline = Date.now() + 15000;
    let extended = false;
    let started = 0;
    for (;;) {
      const socket = await connect(endpoint.port);
      if (socket !== null) {
        return socket;
      }
      if (Date.now() - started > 5000) {
        this.start();
        started = Date.now();
        // Starting can take seconds (WMI, then a fallback): give the first scheduler time to listen, once only, so
        // that a scheduler that never listens still ends the wait
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

  /**
   * Reports the end of a job whose connection was lost (the scheduler restarts) to the next scheduler, starting one
   * when none listens (nobody else may start it soon, and the jobs that wait for this one need the outcome). Gives up
   * after a few tries: the command has ended either way.
   */
  async reportEnd(
    id: number,
    completed: boolean,
    exitCode: number,
    cancelled: boolean,
    reporter: { pid: number; queuedAt: number },
  ): Promise<void> {
    for (let attempt = 0; attempt < 3; attempt++) {
      const socket = await this.connectOrStart().catch(() => null);
      if (socket === null) {
        return;
      }
      const reply = await exchange(socket, { type: "report", id, completed, exitCode, cancelled, ...reporter });
      // "reported", or an error from an older scheduler that cannot take it; null: the scheduler went away meanwhile
      if (reply !== null) {
        return;
      }
      await delay(300);
    }
  }

  /** Starts the scheduler detached in the state directory (never a worktree), its output appended to server.log. */
  private start(): void {
    const endpoint = this.endpoint;
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
    if (process.platform === "win32" && SchedulerLauncher.startThroughWmi(args, stateDirectory, logPath)) {
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
  private static startThroughWmi(args: string[], stateDirectory: string, logPath: string): boolean {
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
      env: {
        ...process.env,
        GSHARP_JOB_QUEUE_WMI_COMMAND: commandLine,
        GSHARP_JOB_QUEUE_WMI_DIRECTORY: stateDirectory,
      },
    });
    if (result.error || result.status !== 0) {
      const why = result.error ? result.error.message : `status ${result.status}`;
      prefix(`could not start the scheduler through WMI (${why}); starting it as a child`);
      return false;
    }
    return true;
  }
}
