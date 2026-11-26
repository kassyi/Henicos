// Shared test doubles and factory helpers for remote CPU offload unit and offline E2E checks.

import type * as Assert from "node:assert";
import { spawnSync, type ChildProcess } from "node:child_process";
import { EventEmitter } from "node:events";
import * as fs from "node:fs";
import * as net from "node:net";
import * as os from "node:os";
import * as path from "node:path";
import type { RunHost } from "../client-run-host.ts";
import type { RunOptions } from "../client.ts";
import type { SchedulerJob } from "../scheduler.ts";
import { JobServer } from "../server.ts";
import type { ShellResult } from "../shell.ts";
import type { RemoteCommandPlan } from "./command-translator.ts";
import { type RemoteRunResult } from "./runner.ts";
import { removeReadOnlyTree } from "./artifact-collector.ts";
import type { RemoteSpec } from "./spec.ts";
import type { CaptureResult, RemoteTransport, StreamCallbacks } from "./transport.ts";

export type AssertModule = typeof Assert;

export function makeRemote(overrides: Partial<RemoteSpec> = {}): RemoteSpec {
  return {
    name: "ubuntu",
    host: "jobrunner@ubuntu-server.lan",
    workspace: "~/henicos",
    slots: 1,
    cpus: 4,
    memoryMb: 6144,
    mode: "prefer-remote",
    maxJobMinutes: 30,
    sshOptions: ["-o", "ConnectTimeout=5"],
    keepBuildServersWarm: true,
    probe: null,
    ...overrides,
  };
}

export function makeJob(id: number, overrides: Partial<SchedulerJob> = {}): SchedulerJob {
  return {
    id,
    weight: 1,
    gpu: false,
    queuedAt: 1000 + id,
    grantedAt: 0,
    estimate: { typicalMs: 10_000, longestMs: 20_000 },
    pin: null,
    before: null,
    after: null,
    memoryMb: 2880,
    offload: "auto",
    ...overrides,
  };
}

export function makeFrame(rel: string, content: string): Buffer {
  const body = Buffer.from(content, "utf8");
  return Buffer.concat([Buffer.from(`${rel}\0${body.length}\n`, "utf8"), body]);
}

export function baseRunOpts(command: string, overrides: Partial<RunOptions> = {}): RunOptions {
  return {
    port: 0,
    slots: 1,
    memoryMb: 4096,
    policy: "fifo",
    command,
    commandWords: null,
    weight: 1,
    gpu: false,
    shell: "pwsh",
    timeoutMinutes: 0,
    after: 0,
    keepIfFailed: false,
    ...overrides,
  };
}

/** Captures stderr output written via `process.stderr.write` while running `body`. */
export async function captureStderr(body: () => void | Promise<void>): Promise<string> {
  let captured = "";
  const original = process.stderr.write;
  process.stderr.write = ((chunk: string | Uint8Array) => {
    captured += typeof chunk === "string" ? chunk : Buffer.from(chunk).toString("utf8");
    return true;
  }) as typeof process.stderr.write;
  try {
    await body();
  } finally {
    process.stderr.write = original;
  }
  return captured;
}

/** In-memory `RemoteTransport` adapter for offline E2E verification without SSH. */
export class FakeRemoteTransport implements RemoteTransport {
  readonly spec: RemoteSpec;
  readonly execCalls: string[] = [];
  readonly uploadCalls: { remoteCommand: string; files: string[] }[] = [];
  readonly interactiveCalls: string[] = [];
  execHandler: (script: string, signal?: AbortSignal) => CaptureResult | Promise<CaptureResult> = () => ({
    code: 0,
    stdout: "",
    stderr: "",
  });
  binaryHandler: (script: string, signal?: AbortSignal) => Buffer | null | Promise<Buffer | null> = () => null;
  uploadHandler: (
    files: readonly string[],
    signal?: AbortSignal,
  ) => { code: number; stderr: string } | Promise<{ code: number; stderr: string }> = () => ({ code: 0, stderr: "" });
  interactiveHandler: (
    script: string,
    callbacks: StreamCallbacks,
    signal?: AbortSignal,
  ) => ShellResult | Promise<ShellResult> = () => ({
    code: 0,
    signaled: false,
  });

  constructor(spec: RemoteSpec) {
    this.spec = spec;
  }

  async exec(script: string, signal?: AbortSignal): Promise<CaptureResult> {
    this.execCalls.push(script);
    return await this.execHandler(script, signal);
  }

  async execBinary(script: string, _maxBytes: number, signal?: AbortSignal): Promise<Buffer | null> {
    return await this.binaryHandler(script, signal);
  }

  async streamUpload(
    remoteCommand: string,
    _headerChunks: readonly Buffer[],
    _worktree: string,
    files: readonly string[],
    signal?: AbortSignal,
  ): Promise<{ code: number; stderr: string }> {
    this.uploadCalls.push({ remoteCommand, files: [...files] });
    return await this.uploadHandler(files, signal);
  }

  async spawnInteractive(script: string, callbacks: StreamCallbacks, signal?: AbortSignal): Promise<ShellResult> {
    this.interactiveCalls.push(script);
    const result = await this.interactiveHandler(script, callbacks, signal);
    callbacks.onClose?.();
    return result;
  }
}

/** `RunHost` connected to an in-process `JobServer`, recording local and remote executions. */
export class ServerTestHost implements RunHost {
  readonly portOf: () => number;
  readonly commands: { command: string; env: NodeJS.ProcessEnv; end: (res: ShellResult) => void }[] = [];
  readonly remoteCalls: {
    remote: RemoteSpec;
    plan: RemoteCommandPlan;
    signal?: AbortSignal;
    end: (res: RemoteRunResult) => void;
  }[] = [];
  readonly exits: number[] = [];

  constructor(portOf: () => number) {
    this.portOf = portOf;
  }

  connect(): Promise<net.Socket> {
    const port = this.portOf();
    return new Promise((resolve, reject) => {
      const sock = net.connect(port, "127.0.0.1", () => resolve(sock));
      sock.on("error", reject);
    });
  }

  reportEnd(): Promise<void> {
    return Promise.resolve();
  }

  runCommand(command: string, env: NodeJS.ProcessEnv, onChild: (child: ChildProcess) => void): Promise<ShellResult> {
    const child = Object.assign(new EventEmitter(), {
      pid: 515151,
      exitCode: null,
      signalCode: null,
    }) as unknown as ChildProcess;
    onChild(child);
    return new Promise((resolve) => {
      this.commands.push({
        command,
        env,
        end: (res) => {
          Object.assign(child, { exitCode: res.code });
          child.emit("close", res.code, null);
          resolve(res);
        },
      });
    });
  }

  runRemoteCommand(
    remote: RemoteSpec,
    plan: RemoteCommandPlan,
    _onChild: (child: ChildProcess) => void,
    _remoteRules?: unknown,
    signal?: AbortSignal,
  ): Promise<RemoteRunResult> {
    return new Promise((resolve) => {
      let settled = false;
      const finish = (res: RemoteRunResult): void => {
        if (!settled) {
          settled = true;
          resolve(res);
        }
      };
      signal?.addEventListener("abort", () => finish({ code: 1, signaled: true }), { once: true });
      this.remoteCalls.push({ remote, plan, signal, end: finish });
    });
  }

  runUnderGpuLock(): Promise<ShellResult> {
    return Promise.resolve({ code: 0, signaled: false });
  }

  stop(): void {}

  exit(code: number): void {
    this.exits.push(code);
  }

  onSignal(): void {}
}

/** Starts an isolated `JobServer` with `remotes.local.json` and a temporary git worktree as `cwd`. */
export async function withRemoteWorkspaceServer(
  body: (ctx: { server: JobServer; port: number; wt: string; stateDir: string; configPath: string }) => Promise<void>,
): Promise<void> {
  const top = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-rem-srv-")));
  const wt = path.join(top, "wt");
  const stateDir = path.join(top, "state");
  fs.mkdirSync(path.join(wt, "scripts"), { recursive: true });
  fs.mkdirSync(stateDir, { recursive: true });
  fs.writeFileSync(path.join(wt, "scripts", "a.js"), "console.log('ok');\n");
  fs.writeFileSync(path.join(wt, "scripts", "holder.js"), "console.log('hold');\n");
  const gitInit = spawnSync("git", ["init", "-q"], { cwd: wt, windowsHide: true });
  if (gitInit.status !== 0) {
    throw new Error(`git init failed: ${gitInit.stderr?.toString() ?? ""}`);
  }
  const configPath = path.join(top, "job-queue.config.json");
  fs.writeFileSync(configPath, JSON.stringify({ version: 1, entry: "scripts/human/job-queue.js", plugins: [] }));
  fs.writeFileSync(
    path.join(stateDir, "remotes.local.json"),
    JSON.stringify({
      remotes: {
        ubuntu: {
          host: "jobrunner@ubuntu-server.lan",
          workspace: "~/henicos",
          slots: 1,
          cpus: 4,
          memoryMb: 6144,
          mode: "prefer-remote",
          keepBuildServersWarm: true,
        },
      },
    }),
  );
  const prevConfigEnv = process.env.JOB_QUEUE_CONFIG;
  const prevCwd = process.cwd();
  delete process.env.JOB_QUEUE_CONFIG;
  const server = new JobServer({
    slots: 1,
    memoryMb: 8192,
    policy: "fifo",
    port: 0,
    stateDir,
    configPath,
    log: () => {},
  });
  server.scheduler.holdUntil = 0;
  await new Promise<void>((resolve) => server.server.listen(0, "127.0.0.1", resolve));
  const port = (server.server.address() as net.AddressInfo).port;
  process.chdir(wt);
  try {
    await body({ server, port, wt, stateDir, configPath });
  } finally {
    process.chdir(prevCwd);
    if (prevConfigEnv === undefined) {
      delete process.env.JOB_QUEUE_CONFIG;
    } else {
      process.env.JOB_QUEUE_CONFIG = prevConfigEnv;
    }
    server.stopping = true;
    for (const job of server.records.values()) {
      job.connection?.socket.destroy();
    }
    server.server.close();
    removeReadOnlyTree(top);
  }
}
