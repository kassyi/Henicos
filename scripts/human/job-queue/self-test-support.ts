// The fixture of the job queue self-test (QueueFixture): a private state directory and environment, test schedulers
// on private ports, clients, polling of the schedulers' status, and the commands the checks run; and raw
// registrations for the protocol checks.

import * as assert from "node:assert";
import * as fs from "node:fs";
import * as net from "node:net";
import * as os from "node:os";
import * as path from "node:path";
import { spawn, spawnSync, type ChildProcess } from "node:child_process";
import {
  CpusVariable,
  EntryScript,
  GpuLockHeldVariable,
  InGpuLockVariable,
  JobVariable,
  ProtocolVersion,
  ShellVariable,
  StateDirectoryVariable,
} from "./constants.ts";
import { connect, request } from "./client-connect.ts";
import { onMessages, type Incoming, type StatusJob, type StatusReply } from "./protocol.ts";
import { ProcessTable } from "./system.ts";

export const delay = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Stops a process the test started, with what it started: Windows ends the tree; on Unix a client stops its command's
 * process group on SIGTERM (the test does not start its children as group leaders, so a group kill would miss them).
 */
export function stopChild(child: ChildProcess): void {
  if (process.platform === "win32") {
    ProcessTable.killTree(child.pid);
  } else {
    child.kill("SIGTERM");
  }
}

/**
 * Removes a test directory. A scheduler that has not exited yet can still hold it open on Windows: retry, and only warn
 * when it stays, so that a cleanup failure never hides the failure of the test itself.
 */
export function removeDirectory(directory: string): void {
  try {
    fs.rmSync(directory, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
  } catch (error) {
    process.stderr.write(`could not remove ${directory}: ${error instanceof Error ? error.message : String(error)}
`);
  }
}

/** A child process with a promise of its exit code. */
export type Client = ChildProcess & { done: Promise<number | null> };

/** The pid of a process that has exited: a client or a command that is gone. */
export function deadPid(): number {
  const pid = Number(spawnSync(process.execPath, ["-e", "process.stdout.write(String(process.pid))"]).stdout);
  assert.ok(pid > 0 && !ProcessTable.isAlive(pid), "a pid of a process that has exited");
  return pid;
}

/** A request message as a client sends it, for the raw protocol checks. */
export function registration(command: string, weight: number, pid: number) {
  return {
    type: "request" as const,
    weight,
    gpu: false,
    pid,
    commandPid: 0,
    spawnedAt: 0,
    cwd: "",
    command,
    queuedAt: Date.now(),
    id: 0,
    pin: null,
    before: null,
    after: null,
    keepIfFailed: false,
    gpuTurn: false,
  };
}

/** A raw protocol connection for the checks: what it received, and a wait for a message of a type. */
export interface RawClient {
  readonly socket: net.Socket;
  /** Every message received so far, in order. */
  readonly messages: Incoming[];
  /** Resolves once the socket has closed. */
  readonly closed: Promise<void>;
  /** Sends one message, with protocol version 3 unless another is given. */
  send(message: Record<string, unknown>, version?: number): void;
  /** Resolves to the first message of this type, or null once the socket closed or `ms` passed without one. */
  until(type: string, ms?: number): Promise<Incoming | null>;
  close(): void;
}

/** Connects a raw protocol client to a scheduler. */
export function connectRaw(port: number): Promise<RawClient> {
  return new Promise((resolve, reject) => {
    const socket = net.connect(port, "127.0.0.1");
    const messages: Incoming[] = [];
    const wakers: (() => void)[] = [];
    const wake = (): void => wakers.splice(0).forEach((w) => w());
    const closed = new Promise<void>((done) => socket.on("close", () => done()));
    void closed.then(wake);
    onMessages(socket, (message) => {
      messages.push(message);
      wake();
    });
    socket.once("error", reject);
    socket.once("connect", () => {
      socket.removeListener("error", reject);
      socket.on("error", () => socket.destroy());
      resolve({
        socket,
        messages,
        closed,
        send: (message, version = ProtocolVersion) => {
          socket.write(`${JSON.stringify({ version, ...message })}
`);
        },
        until: async (type, ms = 10000) => {
          const deadline = Date.now() + ms;
          for (;;) {
            const found = messages.find((m) => m.type === type);
            if (found !== undefined) {
              return found;
            }
            if (socket.destroyed || Date.now() > deadline) {
              return null;
            }
            await new Promise<void>((woken) => {
              wakers.push(woken);
              setTimeout(woken, 100);
            });
          }
        },
        close: () => socket.destroy(),
      });
    });
  });
}

/**
 * What the self-test runs against: a private state directory and environment, the schedulers and clients it started
 * (stopped by cleanup), and the helpers that start a scheduler, ask its status and wait for a state. The helpers are
 * bound, so that a part can take the ones it uses out of the fixture.
 */
export class QueueFixture {
  readonly dir: string;
  readonly env: NodeJS.ProcessEnv;
  readonly children: ChildProcess[] = [];
  readonly servers: ChildProcess[] = [];
  readonly ports: number[] = [];

  constructor() {
    this.dir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-self-test-"));
    const env: NodeJS.ProcessEnv = { ...process.env };
    delete env[JobVariable];
    delete env[InGpuLockVariable];
    delete env[CpusVariable];
    delete env[GpuLockHeldVariable]; // a self-test run under gpu-lock.js must still queue its GPU-less clients
    env[StateDirectoryVariable] = this.dir; // a scheduler that a client starts logs here, not into the main checkout
    // The test commands below are written for cmd / sh; the shells themselves are checked in part 6.
    env[ShellVariable] = process.platform === "win32" ? "cmd" : "sh";
    // --gpu jobs take a private GPU lock, never the machine's (gpu-lock.js reads GSHARP_GPU_LOCK_PATH), without the
    // pool readings
    env.GSHARP_GPU_LOCK_PATH = path.join(this.dir, "gpu-lock");
    env.GSHARP_GPU_POOL_WATCH = "0";
    this.env = env;
  }

  /** Starts a scheduler on a private port and resolves once it accepts connections. */
  readonly startScheduler = async (port: number, slots: number, env = this.env): Promise<ChildProcess> => {
    const server = spawn(process.execPath, [EntryScript, "serve", "--port", String(port), "--slots", String(slots)], {
      stdio: "ignore",
      windowsHide: true,
      env,
    });
    this.servers.push(server);
    for (let i = 0; i < 50; i++) {
      const probe = await connect(port);
      if (probe !== null) {
        probe.destroy();
        break;
      }
      await delay(100);
    }
    return server;
  };

  /** A raw protocol connection to the scheduler on `port`. */
  readonly rawClient = (port: number): Promise<RawClient> => connectRaw(port);

  /** Records a process the test started, so that cleanup stops it. */
  readonly track = <T extends ChildProcess>(child: T): T => {
    this.children.push(child);
    return child;
  };

  // --slots: a scheduler that a client restarts gets the same capacity as the test one.
  readonly client = (port: number, command: string, extra: string[] = []): Client => {
    const args = [EntryScript, "run", "--port", String(port), "--slots", "2", "--weight", "1", ...extra, "--", command];
    const child = spawn(process.execPath, args, { stdio: "ignore", windowsHide: true, env: this.env }) as Client;
    this.children.push(child);
    child.done = new Promise((resolve) => child.on("close", resolve));
    return child;
  };

  readonly sleeper = (ms: number): string => `"${process.execPath}" -e "setTimeout(function(){},${ms})"`;

  // A command that ends with `code` once `file` exists, so that a check decides when a job ends
  readonly gated = (code: number, file: string): string =>
    `"${process.execPath}" -e "var f=process.argv[1];` +
    `setInterval(function(){if(require('fs').existsSync(f))process.exit(${code})},50)" "${file}"`;

  readonly open = (file: string): void => fs.writeFileSync(file, "");

  // A command that writes `file` once it runs, then sleeps: the client reports its shell's process at once, and a
  // client killed before the shell started the command ends the shell with it, so that no command is left to hold a
  // slot
  readonly started = (ms: number, file: string): string =>
    `"${process.execPath}" -e "require('fs').writeFileSync(process.argv[1],'');` +
    `setTimeout(function(){},${ms})" "${file}"`;

  readonly whenExists = async (file: string): Promise<void> => {
    for (let i = 0; i < 150 && !fs.existsSync(file); i++) {
      await delay(100);
    }
    assert.ok(fs.existsSync(file), `the command started: ${file}`);
  };

  readonly exiting = (code: number, ms = 0): string =>
    `"${process.execPath}" -e "setTimeout(function(){process.exit(${code})},${ms})"`;

  readonly statusOf = async (port: number): Promise<StatusReply> => {
    const reply = await request(port, { type: "status" });
    assert.ok(reply !== null && reply.type === "status", `a status reply from port ${port}`);
    return reply as unknown as StatusReply;
  };

  /** Polls the scheduler until `used` reaches the expected value or the time is up; resolves to the last value. */
  readonly usedWithin = async (port: number, expected: number, ms: number): Promise<number> => {
    const deadline = Date.now() + ms;
    for (;;) {
      const used = (await this.statusOf(port)).used;
      if (used === expected || Date.now() > deadline) {
        return used;
      }
      await delay(250);
    }
  };

  /**
   * Polls the scheduler until `probe` finds what it looks for. The checks wait for states, not for fixed times: a
   * client can take seconds to start where the scripts are slow to load (WSL reading /mnt), and the scheduler a client
   * starts after a stop may not listen yet.
   */
  readonly waitFor = async <T>(
    port: number,
    what: string,
    probe: (status: StatusReply) => T | undefined,
    ms = 15000,
  ): Promise<T> => {
    const deadline = Date.now() + ms;
    for (;;) {
      const reply = await request(port, { type: "status" }).catch(() => null);
      const found = reply !== null && reply.type === "status" ? probe(reply as unknown as StatusReply) : undefined;
      if (found !== undefined) {
        return found;
      }
      assert.ok(Date.now() < deadline, `within ${ms} ms: ${what}`);
      await delay(100);
    }
  };

  /** Polls until a job with this command is known (running or waiting) and resolves to it. */
  readonly jobWith = (port: number, command: string): Promise<StatusJob> =>
    this.waitFor(port, `a job runs or waits: ${command}`, (s) =>
      s.running.concat(s.waiting).find((j) => j.command === command),
    );

  /** Polls until a job with this command runs and its client has reported the command's process. */
  readonly runningWith = (port: number, command: string): Promise<StatusJob> =>
    this.waitFor(port, `the command runs: ${command}`, (s) =>
      s.running.find((j) => j.command === command && j.commandPid !== null),
    );

  /** Polls until `count` jobs with this command run or wait. */
  readonly jobsWith = (port: number, command: string, count: number): Promise<StatusJob[]> =>
    this.waitFor(port, `${count} jobs run or wait: ${command}`, (s) => {
      const jobs = s.running.concat(s.waiting).filter((j) => j.command === command);
      return jobs.length >= count ? jobs : undefined;
    });

  /** Stops every client and scheduler the test started and removes the state directory. */
  async cleanup(): Promise<void> {
    this.children.forEach((child) => child.exitCode === null && stopChild(child));
    this.servers.forEach((server) => server.kill("SIGKILL"));
    for (const port of this.ports) {
      await request(port, { type: "stop" }).catch(() => null);
    }
    await delay(200);
    removeDirectory(this.dir);
  }
}
