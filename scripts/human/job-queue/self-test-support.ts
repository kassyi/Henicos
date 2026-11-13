// Shared support of the job queue self-test: test schedulers, polling of their status, raw registrations.

import * as assert from "node:assert";
import * as fs from "node:fs";
import { spawn, type ChildProcess } from "node:child_process";
import { EntryScript } from "./constants.ts";
import { connect, request } from "./client.ts";
import { type StatusJob, type StatusReply } from "./protocol.ts";
import { killProcessTree } from "./system.ts";

export const delay = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Stops a process the test started, with what it started: Windows ends the tree; on Unix a client stops its command's
 * process group on SIGTERM (the test does not start its children as group leaders, so a group kill would miss them).
 */
export function stopChild(child: ChildProcess): void {
  if (process.platform === "win32") {
    killProcessTree(child.pid);
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

/** Starts a scheduler on a private port and resolves once it accepts connections. */
export async function startTestScheduler(port: number, slots: number, env: NodeJS.ProcessEnv): Promise<ChildProcess> {
  const server = spawn(process.execPath, [EntryScript, "serve", "--port", String(port), "--slots", String(slots)], {
    stdio: "ignore",
    windowsHide: true,
    env,
  });
  for (let i = 0; i < 50; i++) {
    const probe = await connect(port);
    if (probe !== null) {
      probe.destroy();
      break;
    }
    await delay(100);
  }
  return server;
}

export async function statusOf(port: number): Promise<StatusReply> {
  const reply = await request(port, { type: "status" });
  assert.ok(reply !== null && reply.type === "status", `a status reply from port ${port}`);
  return reply as unknown as StatusReply;
}

/** Polls the scheduler until `used` reaches the expected value or the time is up; resolves to the last value. */
export async function usedWithin(port: number, expected: number, ms: number): Promise<number> {
  const deadline = Date.now() + ms;
  for (;;) {
    const used = (await statusOf(port)).used;
    if (used === expected || Date.now() > deadline) {
      return used;
    }
    await delay(250);
  }
}

/**
 * Polls the scheduler until `probe` finds what it looks for. The checks wait for states, not for fixed times: a client
 * can take seconds to start where the scripts are slow to load (WSL reading /mnt), and the scheduler a client starts
 * after a stop may not listen yet.
 */
export async function waitFor<T>(
  port: number,
  what: string,
  probe: (status: StatusReply) => T | undefined,
  ms = 15000,
): Promise<T> {
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
}

/** Polls until a job with this command is known (running or waiting) and resolves to it. */
export function jobWith(port: number, command: string): Promise<StatusJob> {
  return waitFor(port, `a job runs or waits: ${command}`, (s) =>
    s.running.concat(s.waiting).find((j) => j.command === command),
  );
}

/** Polls until a job with this command runs and its client has reported the command's process. */
export function runningWith(port: number, command: string): Promise<StatusJob> {
  return waitFor(port, `the command runs: ${command}`, (s) =>
    s.running.find((j) => j.command === command && j.commandPid !== null),
  );
}

/** Polls until `count` jobs with this command run or wait. */
export function jobsWith(port: number, command: string, count: number): Promise<StatusJob[]> {
  return waitFor(port, `${count} jobs run or wait: ${command}`, (s) => {
    const jobs = s.running.concat(s.waiting).filter((j) => j.command === command);
    return jobs.length >= count ? jobs : undefined;
  });
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

export type ClientFactory = (port: number, command: string, extra?: string[]) => Client;
